namespace Bsft.Mapping.Tests;

/// <summary>Destination with a read-only property the mapper may not assign.</summary>
public class DestinationWithReadOnlyProp
{
    public int Id { get; set; }
    // Read-only convention property that matches SourceEntity.FirstName
    public string FirstName => "readonly";
}
