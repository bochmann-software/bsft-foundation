namespace Bsft.Mapping;

/// <summary>The one source type and the one destination type a mapping is registered for.</summary>
/// <remarks>
/// The two types travel together everywhere a mapping is written down: they key the builders a
/// profile collects, they key the resolved model, and they name the pair in every refusal a caller
/// reads. Carried as one value rather than as two parameters because there is no point at which one
/// of them means anything without the other, and because a pair passed as two positional types is a
/// pair that can be passed the wrong way round without the compiler noticing.
/// </remarks>
/// <param name="Source">The type a map reads from.</param>
/// <param name="Destination">The type a map writes to.</param>
internal readonly record struct TypePair(Type Source, Type Destination);
