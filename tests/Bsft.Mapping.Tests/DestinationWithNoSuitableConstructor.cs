namespace Bsft.Mapping.Tests;

/// <summary>
/// A destination class with no parameterless ctor AND no fully resolvable ctor,
/// to exercise the "no suitable constructor" path.
/// </summary>
public class DestinationWithNoSuitableConstructor
{
    private DestinationWithNoSuitableConstructor(string secret) { Secret = secret; }
    public string Secret { get; }
}
