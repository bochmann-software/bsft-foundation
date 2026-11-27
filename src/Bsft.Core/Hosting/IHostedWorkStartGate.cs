namespace Bsft.Core.Hosting;

/// <summary>
/// A precondition that every framework hosted loop waits for before its first pass: the database schema
/// has been migrated, a warm-up finished, a dependency is reachable.
/// </summary>
/// <remarks>
/// <para>
/// Hosted loops start with the host, in parallel with whatever else the host does at start. A worker
/// that drains an outbox in the same second the API applies its migrations reads tables in their old
/// shape, and the failure is a runtime error such as a missing column. A gate is the answer for that:
/// register one or more, and each hosted loop awaits all of them, in registration order, before it runs
/// anything.
/// </para>
/// <para>
/// A gate is awaited once per loop and is expected to stay completed afterwards, so it is cheap for
/// every loop to ask. It honors the token: a host that stops while a gate is pending stops the loop
/// cleanly and without an error log. A gate that throws any other exception faults the loop it was
/// awaited by, because running before a precondition holds is what the gate exists to prevent.
/// </para>
/// <para>
/// A gate that wants to be visible to a health check also implements
/// <see cref="IHostedWorkStartGateOverdueSignal"/>.
/// </para>
/// </remarks>
public interface IHostedWorkStartGate
{
    /// <summary>Completes when the precondition holds and hosted work may start.</summary>
    /// <param name="cancellationToken">A cancellation token that is cancelled when the host stops.</param>
    /// <returns>A task that completes when hosted work may start.</returns>
    Task WaitAsync(CancellationToken cancellationToken);
}
