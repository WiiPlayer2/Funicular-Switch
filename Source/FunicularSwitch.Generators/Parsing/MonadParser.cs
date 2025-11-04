using System.Collections.Immutable;
using FunicularSwitch.Generators.Common;
using FunicularSwitch.Generators.Generation.Semantic;
using FunicularSwitch.Generators.Transformer;
using Microsoft.CodeAnalysis;
using TypeInfo = FunicularSwitch.Generators.Transformer.TypeInfo;
using static FunicularSwitch.Generators.Generation.Semantic.Expressions;

namespace FunicularSwitch.Generators.Parsing;

internal static class MonadParser
{
    public static GenerationResult<MonadInfo> ResolveMonadDataFromMonadType(INamedTypeSymbol monadType,
        CancellationToken cancellationToken)
    {
        if (monadType.GetAttributes().Any(x =>
                x.AttributeClass?.FullTypeNameWithNamespace() == "FunicularSwitch.Generators.ResultTypeAttribute"))
            return ResolveMonadDataFromResultType(monadType);
        if (monadType.IsGenericType)
            return ResolveMonadDataFromGenericMonadType(monadType, cancellationToken);
        return ResolveMonadDataFromStaticMonadType(monadType);
    }

    private static MonadInfo CreateMonadData(INamedTypeSymbol? staticType, INamedTypeSymbol genericType,
        IMethodSymbol returnMethod, IMethodSymbol? bindMethod = default)
    {
        var genericTypeInfo = TypeInfo.From(genericType);
        var (returnMethodName, returnMethodInvoke) = GetReturnMethod();
        var (bindMethodName, bindMethodInvoke) = GetBindMethod();

        var returnMethodInfo = new MethodInfo(
            returnMethodName,
            returnMethodInvoke);
        var bindMethodInfo = new MethodInfo(
            bindMethodName,
            bindMethodInvoke);

        return new MonadInfo(
            genericTypeInfo.Construct,
            genericType.TypeParameters.Length - 1,
            returnMethodInfo,
            bindMethodInfo,
            ImplementsMonadInterface(genericType));

        (string Name, string FullName) GetMethodFullName(IMethodSymbol? method, string defaultName) =>
            method is not null
                ? (method.Name, $"global::{method.ContainingType.FullTypeNameWithNamespace()}.{method.Name}")
                : (defaultName, $"global::{staticType}.{defaultName}");

        (string Name, InvokeMethod Invoke) GetBindMethod()
        {
            if (staticType is not null)
            {
                var (name, fullName) = GetMethodFullName(bindMethod, "Bind");
                return (name, Invoke);

                Expression Invoke(IReadOnlyList<TypeInfo> typeParameters, IReadOnlyList<Expression> parameters) =>
                    Invocation(
                        genericTypeInfo.Construct([
                            ..typeParameters.Take(typeParameters.Count - 1),
                            typeParameters.Last()
                        ]),
                        t => Raw(t, fullName),
                        [..parameters]
                    );
            }
            else
            {
                const string name = "Bind";
                return (name, Invoke);

                Expression Invoke(IReadOnlyList<TypeInfo> typeParameters, IReadOnlyList<Expression> parameters) =>
                    Invocation(
                        genericTypeInfo.Construct([
                            ..typeParameters.Take(typeParameters.Count - 1),
                            typeParameters.Last()
                        ]),
                        t => Member(t, parameters[0], name),
                        parameters[1]
                    );
            }
        }

        (string Name, InvokeMethod Invoke) GetReturnMethod()
        {
            var name = returnMethod.Name;
            var func = returnMethod.ContainingType.IsGenericType
                ? new InvokeMethod(InvokeGeneric)
                : InvokeStatic;
            return (name, func);

            Expression InvokeGeneric(IReadOnlyList<TypeInfo> typeParameters, IReadOnlyList<Expression> parameters) =>
                Invocation(
                    genericTypeInfo.Construct(typeParameters),
                    t => Raw(t, $"{TypeInfo.From(returnMethod.ContainingType).Construct(typeParameters)}.{name}"),
                    [..parameters]
                );

            Expression InvokeStatic(IReadOnlyList<TypeInfo> typeParameters, IReadOnlyList<Expression> parameters) =>
                Invocation(
                    genericTypeInfo.Construct(typeParameters),
                    t => Raw(t,
                        $"{TypeInfo.From(returnMethod.ContainingType)}.{name}<{string.Join(", ", typeParameters)}>"),
                    [..parameters]
                );
        }
    }

    private static bool ImplementsMonadInterface(INamedTypeSymbol genericType) =>
        genericType.GetAttributes().Any(x =>
            x.AttributeClass?.FullTypeNameWithNamespace() == TransformMonadAttribute.ATTRIBUTE_NAME) ||
        genericType.OriginalDefinition.AllInterfaces.Any(x =>
            x.FullTypeNameWithNamespace() == "FunicularSwitch.Transformers.Monad");

    private static GenerationResult<MonadInfo> ResolveMonadDataFromGenericMonadType(INamedTypeSymbol genericMonadType,
        CancellationToken cancellationToken)
    {
        var transformMonadAttribute = genericMonadType
            .GetAttributes()
            .FirstOrDefault(x =>
                x.AttributeClass?.FullTypeNameWithNamespace() == "FunicularSwitch.Generators.TransformMonadAttribute");
        if (transformMonadAttribute is not null)
        {
            var transformedMonadData = Parser.GetTransformedMonadSchema(genericMonadType,
                TransformMonadAttribute.From(transformMonadAttribute), cancellationToken).Value!;
            var returnMethodInfo = transformedMonadData.Monad.ReturnMethod with
            {
                Invoke = (t, p) =>
                    Invocation(
                        transformedMonadData.FullGenericType(t),
                        t => Raw(t,
                            $"global::{transformedMonadData.FullTypeName}.{transformedMonadData.Monad.ReturnMethod.Name}"),
                        [..p]
                    )
            };
            var bindMethodInfo = transformedMonadData.Monad.BindMethod with
            {
                Invoke = (t, p) =>
                    Invocation(
                        transformedMonadData.FullGenericType(t),
                        t => Member(t, p[0], transformedMonadData.Monad.BindMethod.Name),
                        p[1]
                    )
            };
            return new MonadInfo(
                transformedMonadData.FullGenericType,
                genericMonadType.TypeParameters.Length - 1,
                returnMethodInfo,
                bindMethodInfo,
                true);
        }

        var returnMethod = genericMonadType.OriginalDefinition
            .GetMembers()
            .OfType<IMethodSymbol>()
            .FirstOrDefault(IsReturnMethod);

        if (returnMethod is null)
            return new DiagnosticInfo(Diagnostics.MissingReturnMethod(genericMonadType));

        var bindMethod = genericMonadType.OriginalDefinition
            .GetMembers()
            .OfType<IMethodSymbol>()
            .FirstOrDefault(IsBindMethod);

        if (bindMethod is null)
            return new DiagnosticInfo(Diagnostics.MissingBindMethod(genericMonadType));

        return CreateMonadData(default, genericMonadType, returnMethod);

        bool IsReturnMethod(IMethodSymbol method)
        {
            if (method.TypeParameters.Length != 0) return false;
            if (method.Parameters.Length != 1) return false;
            if (method.ReturnType is not INamedTypeSymbol { IsGenericType: true } genericReturnType) return false;
            if (!SymbolEqualityComparer.IncludeNullability.Equals(genericReturnType.ConstructUnboundGenericType(),
                    genericMonadType.ConstructUnboundGenericType())) return false;
            if (genericReturnType.TypeArguments[^1].Name !=
                genericMonadType.OriginalDefinition.TypeParameters[^1].Name) return false;
            return true;
        }

        bool IsBindMethod(IMethodSymbol method)
        {
            if (method.TypeParameters.Length != 1) return false;
            if (method.Parameters.Length != 1) return false;
            if (method.ReturnType is not INamedTypeSymbol { IsGenericType: true } genericReturnType) return false;
            if (!SymbolEqualityComparer.IncludeNullability.Equals(genericReturnType.ConstructUnboundGenericType(),
                    genericMonadType.ConstructUnboundGenericType())) return false;
            if (genericReturnType.TypeArguments[^1].Name ==
                genericMonadType.OriginalDefinition.TypeParameters[^1].Name) return false;
            return true;
        }
    }

    private static MonadInfo ResolveMonadDataFromResultType(INamedTypeSymbol resultType)
    {
        var typeInfo = TypeInfo.From(resultType);
        var returnMethod = new MethodInfo(
            "Ok",
            InvokeReturn);
        var bindMethod = new MethodInfo(
            "Bind",
            InvokeBind);
        return new MonadInfo(
            typeInfo.Construct,
            0,
            returnMethod,
            bindMethod);

        Expression InvokeReturn(IReadOnlyList<TypeInfo> typeParameters, IReadOnlyList<Expression> p) =>
            Invocation(
                typeInfo.Construct(typeParameters),
                t => Raw(t, $"{typeInfo.Construct(typeParameters)}.Ok"),
                [..p]
            );

        Expression InvokeBind(IReadOnlyList<TypeInfo> t, IReadOnlyList<Expression> p) =>
            Invocation(
                typeInfo.Construct([..t.Take(t.Count - 1), t.Last()]),
                t => Member(t, p[0], "Bind"),
                p[1]
            );
    }

    private static GenerationResult<MonadInfo> ResolveMonadDataFromStaticMonadType(INamedTypeSymbol staticMonadType)
    {
        var returnMethod = staticMonadType
            .GetMembers()
            .OfType<IMethodSymbol>()
            .FirstOrDefault(IsStaticReturnMethod);

        if (returnMethod is null)
            return new DiagnosticInfo(Diagnostics.MissingReturnMethod(staticMonadType));

        var genericMonadType = ((INamedTypeSymbol)returnMethod.ReturnType).ConstructUnboundGenericType();

        var bindMethod = staticMonadType
            .GetMembers()
            .OfType<IMethodSymbol>()
            .FirstOrDefault(x => IsStaticBindMethod(genericMonadType, x));

        if (bindMethod is null)
            return new DiagnosticInfo(Diagnostics.MissingBindMethod(staticMonadType));

        return CreateMonadData(staticMonadType, genericMonadType, returnMethod, bindMethod);

        bool IsStaticReturnMethod(IMethodSymbol method)
        {
            if (method.TypeParameters.Length == 0) return false;
            if (method.Parameters.Length != 1) return false;
            if (!IsGenericMonadType(method.ReturnType, method.TypeParameters)) return false;
            return method.Parameters[0].Type.Name == method.TypeParameters.Last().Name;
        }

        bool IsGenericMonadType(ITypeSymbol type, ImmutableArray<ITypeParameterSymbol> typeParameters)
        {
            if (type is not INamedTypeSymbol { IsGenericType: true } genericType) return false;
            if (!genericType.TypeArguments.SequenceEqual(typeParameters)) return false;
            return true;
        }

        static bool IsStaticBindMethod(INamedTypeSymbol genericMonadType, IMethodSymbol method)
        {
            if (method.TypeParameters.Length != genericMonadType.Arity + 1) return false;
            if (method.Parameters.Length != 2) return false;
            if (method.ReturnType is not INamedTypeSymbol { IsGenericType: true } genericReturnType) return false;
            if (!SymbolEqualityComparer.IncludeNullability.Equals(genericReturnType.ConstructUnboundGenericType(),
                    genericMonadType)) return false;
            if (genericReturnType.TypeArguments[^1].Name != method.TypeParameters[^1].Name) return false;
            return true;
        }
    }
}