using FunicularSwitch.Generators.Generation.Semantic;

namespace FunicularSwitch.Generators.Transformer;

internal delegate string InvokeMethod(IReadOnlyList<TypeInfo> typeParameters, IReadOnlyList<Expression> parameters);
