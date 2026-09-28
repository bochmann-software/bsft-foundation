namespace Bsft.Mapping.Tests;

/// <summary>
/// A record with only Id (convention-mapped) and an extra param with a default value
/// but no source convention match.
/// </summary>
public record DestinationRecordOnlyIdWithDefault(int Id, string ExtraWithDefault = "fallback");
