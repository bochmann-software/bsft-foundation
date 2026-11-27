namespace Bsft.Data.Tests.Support;

/// <summary>
/// An entity with dynamic/extensible fields stored as a dictionary.
/// </summary>
public sealed class ExtensibleEntity
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Dynamic fields keyed by name, values are arbitrary objects or null.
    /// </summary>
    public Dictionary<string, object?> Custom { get; set; } = new();
}
