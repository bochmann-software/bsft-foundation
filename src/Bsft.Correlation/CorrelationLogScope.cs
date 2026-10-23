using Microsoft.Extensions.Logging;

namespace Bsft.Correlation;

/// <summary>The one place that decides what a correlation id is called inside a log scope.</summary>
/// <remarks>
/// Both middlewares in this package open the same scope, and the key in it is what a log query is
/// written against. The key is <see cref="LogScopeKeys.CorrelationId"/>, the constant the sinks read it by. Two literals would let the pair drift apart silently, and a deployment running
/// both would then produce two different field names for one value.
/// </remarks>
internal static class CorrelationLogScope
{
    /// <summary>Opens the scope every entry of the request is written inside.</summary>
    /// <param name="logger">The logger the scope belongs to.</param>
    /// <param name="correlationId">The id of the request in hand.</param>
    /// <returns>The scope, which the caller closes when the request is done.</returns>
    public static IDisposable? Begin(ILogger logger, string correlationId)
        => logger.BeginScope(new Dictionary<string, object> { [LogScopeKeys.CorrelationId] = correlationId });
}
