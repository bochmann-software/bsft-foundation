namespace Bsft.Logging.Sinks;

/// <summary>The default <see cref="IScopeLineEncoder"/>, which is the encoding of the file logger.</summary>
public sealed class ScopeLineEncoder : IScopeLineEncoder
{
    /// <summary>The reserved key of the marker property a line carries when scope properties were dropped.</summary>
    public const string TruncatedKey = ScopePropertyEncoding.TruncatedKey;

    /// <inheritdoc />
    public string EscapeSlot(string correlationId) => ScopePropertyEncoding.EscapeSlot(correlationId);

    /// <inheritdoc />
    public string ToBlock(IReadOnlyList<KeyValuePair<string, string>> properties) => ScopePropertyEncoding.ToBlock(properties);

    /// <inheritdoc />
    public string? DescribeKeyProblem(IEnumerable<string>? keys)
        => ScopePropertyEncoding.TryFindKeyProblem(keys, out var problem, out var value) ? $"{problem}, but it is {value}" : null;
}
