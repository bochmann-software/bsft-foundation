using Bsft.Core.Polling;

namespace Bsft.Core.Tenancy;

/// <summary>
/// Turns a background poll pass into one that runs once per tenant of an <see cref="IBackgroundTenantSource"/>.
/// </summary>
/// <remarks>
/// <para>
/// The services that poll a store — outbox and audit outbox dispatchers, the inbox sweeper, the job and work
/// dispatchers — hand their pass to <see cref="PerTenant"/> before giving it to <see cref="PollLoop"/>. Without
/// a source the pass is returned unchanged. With one, each pass lists the tenants and runs the inner pass for
/// each of them in turn, with <see cref="BackgroundTenant.Current"/> set, so that the scopes the inner pass
/// opens resolve that tenant.
/// </para>
/// <para>
/// Tenants are served one after another, never concurrently: a pass that runs for two tenants at once would
/// double the load on a shared server at exactly the moment a backlog is being drained, and the tenants of one
/// pass share the service's batch settings anyway.
/// </para>
/// <para>
/// One tenant's failure does not starve the others: the pass goes on with the next tenant and reports the
/// failures once all tenants had their turn, as the one exception thrown, or an <see cref="AggregateException"/>
/// when several tenants failed. The poll loop reports it and runs the next pass after its interval.
/// Cancellation is not a failure of a tenant and ends the pass at once.
/// </para>
/// <para>
/// The combined outcome is <see cref="PollPassOutcome.PollAgain"/> when any tenant's pass asked to poll again, so
/// a backlog of one tenant keeps the loop draining while the others are idle.
/// </para>
/// </remarks>
public static class BackgroundTenantPasses
{
    /// <summary>Wraps a poll pass so that it runs once per tenant of <paramref name="tenants"/>.</summary>
    /// <param name="tenants">The tenants to serve, or <see langword="null"/> to serve none in particular.</param>
    /// <param name="pass">The pass for one tenant, or for the whole store when there is no source.</param>
    /// <returns>The pass to give to the poll loop.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pass"/> is null.</exception>
    public static Func<CancellationToken, Task<PollPassOutcome>> PerTenant(
        this IBackgroundTenantSource? tenants, Func<CancellationToken, Task<PollPassOutcome>> pass)
    {
        ArgumentNullException.ThrowIfNull(pass);

        if (tenants is null)
            return pass;

        return cancellationToken => RunAsync(tenants, pass, cancellationToken);
    }

    /// <summary>
    /// Runs a count-returning pass, such as a sweep or a drain, once per tenant of <paramref name="tenants"/>, or
    /// once outside any tenant when there is no source.
    /// </summary>
    /// <param name="tenants">The tenants to serve, or <see langword="null"/> to serve none in particular.</param>
    /// <param name="pass">The pass for one tenant, returning how many items it handled.</param>
    /// <param name="cancellationToken">A token to observe.</param>
    /// <returns>The number of items handled over all tenants.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pass"/> is null.</exception>
    public static async Task<int> CountPerTenantAsync(
        this IBackgroundTenantSource? tenants, Func<CancellationToken, Task<int>> pass, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pass);

        if (tenants is null)
            return await pass(cancellationToken).ConfigureAwait(false);

        var total = 0;

        await ForEachTenantAsync(
            tenants,
            async ct => total += await pass(ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        return total;
    }

    private static async Task<PollPassOutcome> RunAsync(
        IBackgroundTenantSource tenants, Func<CancellationToken, Task<PollPassOutcome>> pass, CancellationToken cancellationToken)
    {
        var outcome = PollPassOutcome.WaitForInterval;

        await ForEachTenantAsync(
            tenants,
            async ct =>
            {
                if (await pass(ct).ConfigureAwait(false) == PollPassOutcome.PollAgain)
                    outcome = PollPassOutcome.PollAgain;
            },
            cancellationToken).ConfigureAwait(false);

        return outcome;
    }

    private static async Task ForEachTenantAsync(
        IBackgroundTenantSource tenants, Func<CancellationToken, Task> pass, CancellationToken cancellationToken)
    {
        var tenantIds = await tenants.ListTenantsAsync(cancellationToken).ConfigureAwait(false);
        List<Exception>? failures = null;

        foreach (var tenantId in tenantIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var tenant = new BackgroundTenant(tenantId);

            // One tenant's broken database must not keep the others' outboxes from draining, so the pass is
            // awaited without rethrowing and its outcome read from the task: a fault is kept for the end, and so
            // is a cancellation that did not come from this pass's token. Only that token's cancellation can
            // leave the task cancelled, and it ends the pass at once.
            var run = RunForTenantAsync(pass, cancellationToken);
            await ((Task)run).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            if (run.IsCanceled)
                throw new OperationCanceledException(cancellationToken);

            if (run.IsFaulted)
                (failures ??= []).AddRange(run.Exception.InnerExceptions);
            else if (await run.ConfigureAwait(false) is { } foreignCancellation)
                (failures ??= []).Add(foreignCancellation);
        }

        if (failures is { Count: 1 })
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();

        if (failures is { Count: > 1 })
            throw new AggregateException("The background pass failed for several tenants.", failures);
    }

    /// <summary>
    /// Runs one tenant's pass so that an exception it throws before returning a task faults the task too, and
    /// hands back a cancellation the pass's token did not ask for instead of cancelling the task with it.
    /// </summary>
    private static async Task<OperationCanceledException?> RunForTenantAsync(
        Func<CancellationToken, Task> pass, CancellationToken cancellationToken)
    {
        try
        {
            await pass(cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException canceled) when (!cancellationToken.IsCancellationRequested)
        {
            return canceled;
        }
    }
}
