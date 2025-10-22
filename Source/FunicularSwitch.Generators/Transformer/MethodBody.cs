using FunicularSwitch.Generators.Generation.Semantic;

namespace FunicularSwitch.Generators.Transformer;

internal readonly record struct MethodBody(Expression Expression)
{
    public static implicit operator MethodBody(Expression expression) => new(expression);

    public override string ToString() => Expression.ToCode();
}
