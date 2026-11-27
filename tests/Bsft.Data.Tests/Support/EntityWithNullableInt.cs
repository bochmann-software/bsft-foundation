namespace Bsft.Data.Tests.Support;

/// <summary>
/// A test entity with a nullable int field for testing In operator on nullable types.
/// </summary>
public sealed class EntityWithNullableInt
{
    public int Id { get; set; }

    public int? Size { get; set; }
}
