using Bsft.Instrumentation;
using System.Diagnostics.Metrics;

namespace Bsft.Idempotency.Diagnostics;

/// <summary>
/// The three counters this package publishes, on the shared instrumentation source.
/// </summary>
internal sealed class IdempotencyTelemetry : IIdempotencyTelemetry
{
    /// <summary>The activity source and meter name, matching the source-name convention.</summary>
    internal const string SourceName = "Bsft.Idempotency";

    /// <summary>Replays served instead of running the request again.</summary>
    private const string ReplayedMetric = "bsft.idempotency.replayed";

    /// <summary>Keys presented with a request other than the one they name.</summary>
    private const string ConflictedMetric = "bsft.idempotency.conflicted";

    /// <summary>Requests refused because the first request under their key was still running.</summary>
    private const string InProgressMetric = "bsft.idempotency.in_progress";

    private readonly Counter<long> _replayed;
    private readonly Counter<long> _conflicted;
    private readonly Counter<long> _inProgress;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyTelemetry"/> class.
    /// </summary>
    /// <param name="sources">The provider the meter is taken from.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sources"/> is null.</exception>
    public IdempotencyTelemetry(IInstrumentationSourceProvider sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var source = sources.Get(SourceName);

        _replayed = source.GetOrCreateCounter(ReplayedMetric);
        _conflicted = source.GetOrCreateCounter(ConflictedMetric);
        _inProgress = source.GetOrCreateCounter(InProgressMetric);
    }

    /// <inheritdoc />
    public void Record(IdempotencyAuditAction action)
    {
        var counter = action switch
        {
            IdempotencyAuditAction.Replayed => _replayed,
            IdempotencyAuditAction.KeyReused => _conflicted,
            _ => _inProgress,
        };

        counter.Add(1);
    }
}
