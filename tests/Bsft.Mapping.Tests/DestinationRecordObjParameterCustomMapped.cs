namespace Bsft.Mapping.Tests;

/// <summary>
/// Dest record with an object-typed ctor param, mapped via custom mapping from a string expression.
/// When resolving the ctor param in ResolveParameterExpression, the custom mapping expression type
/// (string) != targetType (object), triggering : Expression.Convert.
/// </summary>
public record DestinationRecordObjParameterCustomMapped(object Name);
