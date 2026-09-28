namespace Bsft.Mapping.Tests;

/// <summary>
/// Dest record with two ctor params: Name (matched by custom mapping) and Other (not matched by anything).
/// Used to exercise ResolveParameterExpression (foreach closes without match for "Other").
/// </summary>
public record DestinationRecordTwoParametersOneUnresolvable(string Name, string Other);
