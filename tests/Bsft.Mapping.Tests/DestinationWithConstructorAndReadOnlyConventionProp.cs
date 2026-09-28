namespace Bsft.Mapping.Tests;

/// <summary>
/// Destination with a ctor for FirstName, a settable Id property, and a
/// read-only LastName property that is convention-mapped but NOT a ctor param.
/// Used (!destProp.CanWrite -> continue) in CreateAndPopulate.
/// </summary>
public class DestinationWithConstructorAndReadOnlyConventionProp
{
    public DestinationWithConstructorAndReadOnlyConventionProp(string firstName) { FirstName = firstName; }
    public string FirstName { get; }
    // Read-only non-ctor property that convention-maps from SourceEntity.LastName -> triggers
    public string LastName => "readonly";
    public int Id { get; set; }
}
