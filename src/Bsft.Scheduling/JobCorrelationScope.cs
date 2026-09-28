namespace Bsft.Scheduling;

/// <summary>
/// Carries the correlation id of an executing job through the asynchronous flow that runs it, so that
/// components resolved from the run's own service scope pick it up instead of minting a fresh one.
/// </summary>
/// <remarks>
/// Internal on purpose: it is ambient process state, not part of the package's public surface. The
/// correlation bridge in <c>Bsft.Scheduling.Correlation</c> reaches it through
/// <c>InternalsVisibleTo</c>; everything else goes through <see cref="IJobCorrelationAccessor"/>.
/// </remarks>
internal static class JobCorrelationScope
{
    private static readonly AsyncLocal<string?> _current = new();

    /// <summary>Gets the correlation id of the job currently executing, if any.</summary>
    public static string? Current => _current.Value;

    /// <summary>
    /// Sets the ambient correlation id for the current asynchronous flow.
    /// </summary>
    /// <param name="correlationId">The correlation id to run under, or <c>null</c> for none.</param>
    /// <returns>A handle that restores the previous value when disposed.</returns>
    public static IDisposable Use(string? correlationId)
    {
        var previous = _current.Value;
        _current.Value = correlationId;
        return new Restorer(previous);
    }

    private sealed class Restorer : IDisposable
    {
        private readonly string? _previous;

        public Restorer(string? previous) => _previous = previous;

        /// <inheritdoc />
        public void Dispose() => _current.Value = _previous;
    }
}
