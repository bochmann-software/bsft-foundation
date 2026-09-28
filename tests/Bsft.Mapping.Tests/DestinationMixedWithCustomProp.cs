namespace Bsft.Mapping.Tests;

/// <summary>
/// A mixed type: ctor params (FirstName) + settable property (FullNameProp via custom mapping).
/// Used (custom mappings after ctor, not in ctor params).
/// </summary>
public class DestinationMixedWithCustomProp
{
    public DestinationMixedWithCustomProp(string firstName) { FirstName = firstName; }
    public string FirstName { get; }
    public string FullNameProp { get; set; } = string.Empty;
}
