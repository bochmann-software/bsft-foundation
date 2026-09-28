using Microsoft.Extensions.Options;

namespace Bsft.Data.MultiTenancy.Sandboxes;

/// <summary>The shipped provisioner: a free identifier, and a teardown that only touches one it gave out.</summary>
/// <remarks>
/// <para>
/// The identifier is the configured prefix and a random value, claimed through the registry rather
/// than assumed free. Two callers asking at the same moment are told apart by the registry's own
/// uniqueness, which is the only place they can be.
/// </para>
/// <para>
/// Teardown runs the cleanup handlers <em>before</em> the registry entry goes. A handler that throws
/// therefore leaves the sandbox listed and the teardown repeatable; forgetting it first would leave
/// rows behind under an identifier nothing names any more.
/// </para>
/// </remarks>
internal sealed class SandboxTenantProvisioner : ISandboxTenantProvisioner
{
    private readonly ISandboxTenantRegistry _registry;
    private readonly IEnumerable<ISandboxTenantCleanup> _cleanups;
    private readonly IOptionsMonitor<BsftSandboxTenantOptions> _options;
    private readonly TimeProvider _time;

    /// <summary>Initializes a new instance of the <see cref="SandboxTenantProvisioner"/> class.</summary>
    /// <param name="registry">Where the identifiers handed out are remembered.</param>
    /// <param name="cleanups">What each package removes on teardown.</param>
    /// <param name="options">How identifiers are shaped and how long a sandbox lasts.</param>
    /// <param name="timeProvider">The clock the timestamps are read from.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public SandboxTenantProvisioner(
        ISandboxTenantRegistry registry,
        IEnumerable<ISandboxTenantCleanup> cleanups,
        IOptionsMonitor<BsftSandboxTenantOptions> options,
        TimeProvider timeProvider)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _cleanups = cleanups ?? throw new ArgumentNullException(nameof(cleanups));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _time = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public async Task<SandboxTenant> ProvisionAsync(SandboxTenantSpecification spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var settings = _options.CurrentValue;

        RequireUsable(spec);

        var now = _time.GetUtcNow();
        var lifetime = spec.Lifetime ?? settings.DefaultLifetime;

        for (var attempt = 0; attempt < settings.MaxProvisionAttempts; attempt++)
        {
            var candidate = new SandboxTenant
            {
                TenantId = $"{settings.IdPrefix}-{Guid.NewGuid():n}",
                Label = spec.Label,
                RequestedBy = spec.RequestedBy,
                CreatedAt = now,
                ExpiresAt = now + lifetime,
            };

            if (await _registry.TryReserveAsync(candidate, cancellationToken).ConfigureAwait(false))
                return candidate;
        }

        var attempts = settings.MaxProvisionAttempts;

        throw new InvalidOperationException(
            $"""
            No free sandbox tenant identifier was found in {attempts} attempts. The registry refused every claim, which means either it is rejecting writes or its uniqueness is not what this seam assumes.
            """);
    }

    /// <inheritdoc />
    public async Task<SandboxTeardown> TearDownAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        var known = await _registry.FindAsync(tenantId, cancellationToken).ConfigureAwait(false);

        if (known is null)
        {
            // Not one of ours. Running the handlers here would be a way to delete a real tenant's data
            // by naming it, and answering rather than throwing is what makes a second teardown safe.
            return new SandboxTeardown { TenantId = tenantId, WasKnown = false, ClearedBy = [] };
        }

        var cleared = new List<string>();

        foreach (var cleanup in _cleanups)
        {
            await cleanup.ClearAsync(tenantId, cancellationToken).ConfigureAwait(false);

            cleared.Add(cleanup.GetType().FullName ?? cleanup.GetType().Name);
        }

        await _registry.RemoveAsync(tenantId, cancellationToken).ConfigureAwait(false);

        return new SandboxTeardown { TenantId = tenantId, WasKnown = true, ClearedBy = cleared };
    }

    private static void RequireUsable(SandboxTenantSpecification spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Label))
            throw new InvalidOperationException("A sandbox tenant has to say what it is for.");

        if (string.IsNullOrWhiteSpace(spec.RequestedBy))
            throw new InvalidOperationException("A sandbox tenant has to say who asked for it.");

        if (spec.Lifetime is { } lifetime && lifetime <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "A sandbox tenant's lifetime has to be greater than zero. Leave it unset for the "
                + "configured default rather than asking for one that has already expired.");
        }
    }
}
