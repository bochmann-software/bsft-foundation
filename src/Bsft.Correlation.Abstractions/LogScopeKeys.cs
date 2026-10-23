namespace Bsft.Correlation;

/// <summary>The scope keys that carry a fixed meaning between the code that opens a log scope and the sinks that persist it.</summary>
/// <remarks>
/// Lives in the dependency-free abstractions package so that the producer (<c>Bsft.Correlation</c>) and every
/// consumer (the file logger and reader in <c>Bsft.Logging</c>, the console formatter in <c>Bsft.Hosting</c>)
/// reference one constant without either of them taking the other, or a sink, as a dependency. A second literal
/// on either side would let the field name drift silently.
/// </remarks>
public static class LogScopeKeys
{
    /// <summary>The key of the correlation id in a log scope. It is the key the file logger writes to the line's dedicated correlation slot.</summary>
    public const string CorrelationId = "CorrelationId";
}
