using FunicularSwitch.Generators.Generation.Semantic;
using FunicularSwitch.Generators.Transformer;
using static FunicularSwitch.Generators.Generation.Semantic.Expressions;

namespace FunicularSwitch.Generators.Generation;

internal static class MonadMethods
{
    public static MethodGenerationInfo Create(
        int arity,
        string returnTypeParameter,
        ConstructType genericTypeName,
        IReadOnlyList<string> typeParameters,
        Func<IReadOnlyList<TypeInfo>, IReadOnlyList<ParameterGenerationInfo>> parameters,
        string name,
        Func<IReadOnlyList<TypeInfo>, IReadOnlyList<Expression>, Expression> invoke) =>
        Create(
            arity,
            returnTypeParameter,
            genericTypeName,
            typeParameters,
            parameters,
            name,
            t => invoke(t, parameters(t).Select(p => Raw(p.Type, p.Name)).ToList()).ToCode()
        );

    public static MethodGenerationInfo Create(
        int arity,
        string returnTypeParameter,
        ConstructType genericTypeName,
        IReadOnlyList<string> typeParameters,
        Func<IReadOnlyList<TypeInfo>, IReadOnlyList<ParameterGenerationInfo>> parameters,
        string name,
        Func<IReadOnlyList<TypeInfo>, string> invoke)
    {
        var extraTypeParameters = Enumerable.Range(0, arity)
            .Select(i => $"T{i}")
            .Select(TypeInfo.Parameter)
            .ToList();
        var allTypeParameters = extraTypeParameters
            .Select(x => x.ToString())
            .Concat(typeParameters).ToList();

        return new MethodGenerationInfo(
            genericTypeName([..extraTypeParameters, returnTypeParameter]),
            allTypeParameters,
            parameters(extraTypeParameters),
            name,
            invoke(extraTypeParameters));
    }

    public static IReadOnlyList<MethodGenerationInfo> CreateCoreMonadMethods(
        ConstructType genericTypeName,
        MonadInfo chainedMonad,
        MonadInfo outerMonad,
        MonadInfo innerMonad)
    {
        return
        [
            ..Lift(genericTypeName, chainedMonad, outerMonad, innerMonad),
        ];
    }

    public static IReadOnlyList<MethodGenerationInfo> CreateExtendMonadMethods(
        ConstructType genericTypeName,
        MonadInfo monad,
        IReadOnlyList<MethodGenerationInfo> existingMethods)
    {
        IReadOnlyList<MethodGenerationInfo> methodGenerationInfos =
        [
            ..Return(genericTypeName, monad),
            ..BindMethods(),
            ..MapMethods(),
            ..CombineMethods(),
            ..FlattenMethods(),
        ];
        return methodGenerationInfos
            .Distinct(MethodGenerationInfo.SignatureComparer.Instance)
            .Except(existingMethods, MethodGenerationInfo.SignatureComparer.Instance)
            .ToList();

        IEnumerable<MethodGenerationInfo> BindMethods() =>
        [
            ..Bind(monad.BindMethod.Name, genericTypeName, monad),
            ..Bind("SelectMany", genericTypeName, monad),
            ..Bind2("SelectMany", genericTypeName, monad),
        ];

        IEnumerable<MethodGenerationInfo> MapMethods() =>
        [
            ..Map("Map", genericTypeName, monad),
            ..Map("Select", genericTypeName, monad),
        ];

        IEnumerable<MethodGenerationInfo> CombineMethods() =>
            Combine(genericTypeName, monad, 12);

        IEnumerable<MethodGenerationInfo> FlattenMethods() =>
            Flatten("Flatten", genericTypeName, monad);
    }

    private static IEnumerable<MethodGenerationInfo> AsyncVariants(string parameterName, Func<string, MethodGenerationInfo> fn)
    {
        var sync = fn(parameterName);
        var parameter = sync.Parameters.First(x => x.Name == parameterName);
        var asyncBase = fn($"(await {parameterName})");

        return
        [
            sync,
            WithAsyncType(Types.Task),
            WithAsyncType(Types.ValueTask),
        ];

        MethodGenerationInfo WithAsyncType(Func<TypeInfo, TypeInfo> taskType) =>
            asyncBase with
            {
                ReturnType = taskType(sync.ReturnType),
                Parameters =
                [
                    parameter with {Type = taskType(parameter.Type)},
                    ..sync.Parameters.Skip(1),
                ],
                IsAsync = true,
            };
    }

    private static IEnumerable<MethodGenerationInfo> Bind(string name, ConstructType genericTypeName, MonadInfo chainedMonad)
    {
        return
        [
            ..ForFnType(t => genericTypeName([..t, "B"])),
            ..ForFnType(t => chainedMonad.GenericTypeName([..t, "B"])),
        ];

        IEnumerable<MethodGenerationInfo> ForFnType(Func<IReadOnlyList<TypeInfo>, TypeInfo> fnReturnType) =>
            AsyncVariants("ma", p => Create(
                chainedMonad.ExtraArity,
                "B",
                genericTypeName,
                ["A", "B"],
                t =>
                [
                    new ParameterGenerationInfo(genericTypeName([..t, "A"]), "ma", true),
                    new ParameterGenerationInfo(Types.Func("A", fnReturnType(t)), "fn"),
                ],
                name,
                (t, parameters) =>
                {
                    if (parameters is not [var maOriginal, var fn])
                        throw new InvalidOperationException();
                    var ma = Raw(maOriginal.Type, p);

                    return chainedMonad.BindMethod.Invoke.ToExpression(
                        genericTypeName([..t, "B"]),
                        [..t, "A", "B"],
                        Brackets(
                            Cast(
                                chainedMonad.GenericTypeName([..t, "A"]),
                                ma
                            )
                        ),
                        Lambda(
                            [("A", "a")],
                            a => Invocation(
                                genericTypeName([..t, "B"]),
                                fn,
                                a
                            )
                        )
                    );
                }));
    }

    private static IEnumerable<MethodGenerationInfo> Bind2(string name, ConstructType genericTypeName, MonadInfo chainedMonad)
    {
        return
        [
            ..ForFnType(t => genericTypeName([..t, "B"])),
            ..ForFnType(t => chainedMonad.GenericTypeName([..t, "B"])),
        ];

        IEnumerable<MethodGenerationInfo> ForFnType(Func<IReadOnlyList<TypeInfo>, TypeInfo> fnReturnType) =>
            AsyncVariants("ma", p => Create(
                chainedMonad.ExtraArity,
                "C",
                genericTypeName,
                ["A", "B", "C"],
                t =>
                [
                    new ParameterGenerationInfo(genericTypeName([..t, "A"]), "ma", true),
                    new ParameterGenerationInfo(Types.Func("A", fnReturnType(t)), "fn"),
                    new ParameterGenerationInfo(Types.Func("A", "B", "C"), "selector"),
                ],
                name,
                (t, parameters) =>
                {
                    if (parameters is not [var maOriginal, var fn, var selector])
                        throw new InvalidOperationException();
                    var ma = Raw(maOriginal.Type, p);

                    return Invocation(
                        genericTypeName([..t, "C"]),
                        Member(
                            Types.Func("A", genericTypeName([..t, "C"])),
                            ma,
                            "SelectMany"
                        ),
                        Lambda(
                            [("A", "a")],
                            a => Invocation(
                                genericTypeName([..t, "C"]),
                                Member(
                                    Types.Func("B", genericTypeName([..t, "C"])),
                                    Brackets(
                                        Cast(
                                            genericTypeName([..t, "B"]),
                                            Invocation(fnReturnType(t), fn, a)
                                        )
                                    ),
                                    "Map"
                                ),
                                Lambda(
                                    [("B", "b")],
                                    b => Invocation("C", selector, a, b)
                                )
                            )
                        )
                    );
                }));
    }

    private static IEnumerable<MethodGenerationInfo> Combine(ConstructType genericTypeName, MonadInfo chainedMonad, int maxCount)
    {
        var mapMethod = (IReadOnlyList<TypeInfo> typeParameters, Expression ma, Expression fn) =>
            Invocation(
                genericTypeName([..typeParameters.Take(typeParameters.Count - 2), typeParameters.Last()]),
                Member(
                    Types.Func(typeParameters[^2], typeParameters[^1]),
                    ma,
                    "Map",
                    [..typeParameters]
                ),
                fn
            );

        return Enumerable.Range(2, maxCount - 1)
            .SelectMany(ForCount);

        TypeInfo Tuple(int count) => TypeInfo.Tuple(Enumerable.Range(0, count).Select(i => TypeInfo.Parameter($"S{i}")).ToArray());

        IEnumerable<MethodGenerationInfo> ForCount(int count)
        {
            var typeParameters = Enumerable.Range(0, count).Select(x => $"S{x}").ToList();
            yield return Create(
                chainedMonad.ExtraArity,
                $"({string.Join(", ", typeParameters)})",
                genericTypeName,
                typeParameters,
                t => Enumerable.Range(0, count)
                    .Select(i => new ParameterGenerationInfo(
                        genericTypeName([..t, $"S{i}"]), $"s{i}"))
                    .ToList(),
                "Combine",
                count > 2 ? CombineTail : CombineHead
            );

            Expression CombineHead(IReadOnlyList<TypeInfo> t, IReadOnlyList<Expression> parameters)
            {
                if (parameters is not [var s0, var s1])
                    throw new InvalidOperationException();

                return chainedMonad.BindMethod.Invoke.ToExpression(
                    genericTypeName([..t, TypeInfo.Tuple("S0", "S1")]),
                    [..t, "S0", TypeInfo.Tuple("S0", "S1")],
                    s0,
                    Lambda(
                        [("S0", "v0")],
                        v0 => mapMethod(
                            [..t, "S1", TypeInfo.Tuple("S0", "S1")],
                            s1,
                            Lambda(
                                [("S1", "v1")],
                                v1 => Expressions.Tuple(v0, v1)
                            )
                        )
                    )
                );
            }

            Expression CombineTail(IReadOnlyList<TypeInfo> t, IReadOnlyList<Expression> parameters)
            {
                var fromTupleType = Tuple(count - 1);
                var toTupleType = Tuple(count);
                var lastType = $"S{count - 1}";
                var lastArg = parameters.Last();
                var mapFn = (Expression prev) => Lambda(
                    [(lastType, "last")],
                    last => Expressions.Tuple([
                        ..Enumerable.Range(1, count - 1)
                            .Select(i => Member($"S{i}", prev, $"Item{i}")),
                        last,
                    ])
                );

                return chainedMonad.BindMethod.Invoke.ToExpression(
                    genericTypeName([..t, toTupleType]),
                    [..t, fromTupleType, toTupleType],
                    Invocation(
                        genericTypeName([..t, Tuple(count - 1)]),
                        Raw(
                            Types.Func([
                                ..Enumerable.Range(0, count - 1)
                                    .Select(i => genericTypeName([..t, $"S{i}"])),
                                Tuple(count - 1),
                            ]),
                            "Combine"
                        ),
                        parameters.Take(parameters.Count - 1).ToArray()
                    ),
                    Lambda(
                        [(lastType, "prev")],
                        prev => mapMethod(
                            [..t, lastType, toTupleType],
                            lastArg,
                            mapFn(prev)
                        )
                    )
                );
            }
        }
    }

    private static IEnumerable<MethodGenerationInfo> Flatten(string name, ConstructType genericTypeName, MonadInfo monad) =>
        AsyncVariants("ma", p => Create(
            monad.ExtraArity,
            "A",
            genericTypeName,
            ["A"],
            t =>
            [
                new ParameterGenerationInfo(genericTypeName([..t, genericTypeName([..t, "A"])]), "ma", true),
            ],
            name,
            (t, parameters) =>
            {
                if (parameters is not [var maOriginal])
                    throw new InvalidOperationException();
                var ma = Raw(maOriginal.Type, p);

                return Invocation(
                    genericTypeName([..t, "A"]),
                    Member(
                        Types.Func(genericTypeName([..t, genericTypeName([..t, "A"])]), genericTypeName([..t, "A"])),
                        ma,
                        monad.BindMethod.Name
                    ),
                    Lambda(
                        [(genericTypeName([..t, "A"]), "a")],
                        a => a
                    )
                );
            }));

    private static IEnumerable<MethodGenerationInfo> Lift(ConstructType genericTypeName, MonadInfo chainedMonad, MonadInfo outerMonad, MonadInfo innerMonad) =>
        AsyncVariants("ma", p => Create(
            chainedMonad.ExtraArity,
            "A",
            genericTypeName,
            ["A"],
            t => [new ParameterGenerationInfo(outerMonad.GenericTypeName([..t.Take(outerMonad.ExtraArity), "A"]), "ma")],
            "Lift",
            t => $"{outerMonad.BindMethod.Invoke([..t.Take(outerMonad.ExtraArity), "A", $"{innerMonad.GenericTypeName([..t.Skip(outerMonad.ExtraArity), "A"])}"], [p, $"[{Constants.DebuggerStepThroughAttribute}](a) => {chainedMonad.ReturnMethod.Invoke([..t, "A"], ["a"])}"])}"
        ));

    private static IEnumerable<MethodGenerationInfo> Map(string name, ConstructType genericTypeName, MonadInfo monad) =>
        AsyncVariants("ma", p => Create(
            monad.ExtraArity,
            "B",
            genericTypeName,
            ["A", "B"],
            t =>
            [
                new ParameterGenerationInfo(genericTypeName([..t, "A"]), "ma", true),
                new ParameterGenerationInfo(Types.Func("A", "B"), "fn"),
            ],
            name,
            t => $"{p}.{monad.BindMethod.Name}([{Constants.DebuggerStepThroughAttribute}](a) => {monad.ReturnMethod.Invoke([..t, "B"], ["fn(a)"])})"
        ));

    private static IEnumerable<MethodGenerationInfo> Return(ConstructType genericTypeName, MonadInfo chainedMonad) =>
        AsyncVariants("a", p => Create(
            chainedMonad.ExtraArity,
            "A",
            genericTypeName,
            ["A"],
            _ => [new ParameterGenerationInfo("A", "a")],
            chainedMonad.ReturnMethod.Name,
            t => chainedMonad.ReturnMethod.Invoke([..t, "A"], [p])
        ));
}
