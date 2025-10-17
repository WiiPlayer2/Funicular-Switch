using FunicularSwitch.Generators.Transformer;

namespace FunicularSwitch.Generators.Generation.Semantic;

internal abstract record Expression(TypeInfo Type)
{
    public record Brackets(Expression Expression) : Expression(Expression.Type);

    public record Cast(TypeInfo Type, Expression Expression) : Expression(Type);

    public record Invocation(TypeInfo Type, Expression Method, IReadOnlyList<Expression> Arguments) : Expression(Type);

    public record Lambda(IReadOnlyList<(TypeInfo Type, string Name)> Parameters, Expression Body) : Expression(Types.Func([..Parameters.Select(x => x.Type), Body.Type]));

    public record Member(TypeInfo Type, Expression Expression, string Name) : Expression(Type);

    public record Raw(TypeInfo Type, string Code) : Expression(Type);
}

internal static class Expressions
{
    public static Expression.Brackets Brackets(Expression expression) => new(expression);

    public static Expression.Cast Cast(TypeInfo type, Expression expression) => new(type, expression);

    public static Expression.Invocation Invocation(TypeInfo type, Expression expression, params Expression[] arguments) => new(type, expression, arguments);

    public static Expression.Lambda Lambda(IReadOnlyList<(TypeInfo Type, string Name)> parameters, Expression expression) => new(parameters, expression);

    public static Expression.Lambda Lambda(IReadOnlyList<(TypeInfo Type, string Name)> parameters, Func<Expression, Expression> expressionFn) => new(parameters, expressionFn(Raw(parameters[0].Type, parameters[0].Name)));

    public static Expression.Member Member(TypeInfo type, Expression expression, string name) => new(type, expression, name);

    public static Expression.Raw Raw(TypeInfo type, string code) => new(type, code);

    public static string ToCode(this Expression expression) => expression switch
    {
        Expression.Raw raw => raw.ToCode(),
        Expression.Cast cast => cast.ToCode(),
        Expression.Invocation invocation => invocation.ToCode(),
        Expression.Lambda lambda => lambda.ToCode(),
        Expression.Brackets brackets => brackets.ToCode(),
        Expression.Member member => member.ToCode(),
        _ => throw new ArgumentOutOfRangeException(),
    };

    public static string ToCode(this Expression.Raw raw) => raw.Code;

    public static string ToCode(this Expression.Cast cast) => $"({cast.Type}){cast.Expression.ToCode()}";

    public static string ToCode(this Expression.Invocation invocation) =>
        $"{invocation.Method.ToCode()}({string.Join(", ", invocation.Arguments.Select(x => x.ToCode()))})";

    public static string ToCode(this Expression.Lambda lambda) =>
        $"[{Constants.DebuggerStepThroughAttribute}]({string.Join(", ", lambda.Parameters.Select(x => x.Name))}) => {lambda.Body.ToCode()}";

    public static string ToCode(this Expression.Brackets brackets) => $"({brackets.Expression.ToCode()})";

    public static string ToCode(this Expression.Member member) => $"{member.Expression.ToCode()}.{member.Name}";
}
