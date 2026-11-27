using Bsft.Core.Tenancy;

namespace Bsft.Idempotency.EFCore.Tests.Fakes;

/// <summary>
/// Stands in for a service a background pass resolves: records the background tenant of each resolve, then
/// refuses, so one pass shows whom it worked for without a database.
/// </summary>
/// <typeparam name="TService">The service the pass resolves.</typeparam>
public sealed class TenantRecorder<TService>
    where TService : class
{
    private readonly List<string?> _resolvedFor = [];

    /// <summary>Gets the tenants the service was resolved for, in order.</summary>
    public IReadOnlyList<string?> ResolvedFor
    {
        get
        {
            lock (_resolvedFor)
                return [.. _resolvedFor];
        }
    }

    /// <summary>Records the current background tenant and refuses the resolve.</summary>
    /// <returns>Never returns.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public TService Record()
    {
        lock (_resolvedFor)
            _resolvedFor.Add(BackgroundTenant.Current);

        throw new InvalidOperationException("The recorder resolves nothing.");
    }
}
