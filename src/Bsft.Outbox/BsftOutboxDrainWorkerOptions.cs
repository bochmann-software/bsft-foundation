namespace Bsft.Outbox;

/// <summary>
/// What the hosted drain worker of one outbox adds to the bounds of <see cref="BsftOutboxOptions"/>:
/// how long a pass in flight may continue once the host stops.
/// </summary>
/// <remarks>
/// Resolved per outbox as named options, the same way <see cref="BsftOutboxOptions"/> is. The interval
/// the worker idles for and the batch it claims stay with <see cref="BsftOutboxOptions.PollInterval"/>
/// and <see cref="BsftOutboxOptions.BatchSize"/>, so one outbox has one spelling of each. The parent
/// section is <see cref="SectionName"/> and the outbox's name is the sub-key underneath it.
/// </remarks>
public sealed class BsftOutboxDrainWorkerOptions
{
    private static readonly TimeSpan DefaultShutdownGrace = TimeSpan.FromSeconds(5);

    /// <summary>The parent section one outbox's worker options are bound from, under its own name.</summary>
    public const string SectionName = "Bsft:Outbox:DrainWorkers";

    /// <summary>
    /// Gets or sets how long a pass in flight may continue after the host asked it to stop, before the
    /// pass is cancelled. Defaults to five seconds. A row whose side effect is under way, a mail being
    /// sent for example, then still reaches its processed mark instead of being abandoned after its
    /// effect and applied again by the next instance. It must not be negative and has to fit into a
    /// timer; keep it well under the host's shutdown timeout, which ends the process regardless.
    /// </summary>
    /// <remarks>
    /// <see cref="TimeSpan.Zero"/> cancels a pass the moment the host stops, which is what draining
    /// directly from the host's stopping token does.
    /// </remarks>
    public TimeSpan ShutdownGrace { get; set; } = DefaultShutdownGrace;
}
