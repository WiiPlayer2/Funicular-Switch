using FunicularSwitch.Generators.Generation.Semantic;

namespace FunicularSwitch.Generators.Transformer;

internal delegate Expression InvokeMethod(IReadOnlyList<TypeInfo> typeParameters, IReadOnlyList<Expression> parameters);