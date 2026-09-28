namespace Bsft.Mapping.Tests;

/// <summary>
/// Record with a parameter that has no matching source property.
/// </summary>
public record DestinationRecordWithUnresolvableParameter(int Id, string FirstName, string UnresolvableParam);
