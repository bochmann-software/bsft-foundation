namespace Bsft.Mapping.Tests;

/// <summary>
/// Mixed ctor/property record: Id comes via convention mapping after construction,
/// constructing with only FirstName + LastName.
/// </summary>
public record DestinationMixedConstructorAndProp(string FirstName, string LastName)
{
    // Id is a settable property, NOT a ctor param, to exercise the "set remaining props" path
    public int Id { get; set; }
}
