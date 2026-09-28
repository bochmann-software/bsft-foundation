namespace Bsft.Mapping.Tests;

/// <summary>
/// Record with an object-typed ctor param Name.
/// Convention mapping: object.IsAssignableFrom(string) = true, so Name maps by convention.
/// In the expression tree, Expression.Property gives type 'string' but targetType is 'object' -> Convert fires.
/// </summary>
public record DestinationRecordWithObjectName(object Name);
