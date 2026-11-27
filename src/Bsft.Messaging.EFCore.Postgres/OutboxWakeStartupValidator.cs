using IStartupValidator = Bsft.Core.IStartupValidator;
using StartupValidationResult = Bsft.Core.StartupValidationResult;
using Bsft.Core.Tenancy;
using Microsoft.Extensions.Options;

namespace Bsft.Messaging.EFCore.Postgres;

/// <summary>
/// Warns at start-up when a host works per tenant but its outbox wake-up still listens on one database.
/// </summary>
/// <remarks>
/// A registered <see cref="IBackgroundTenantSource"/> says the tenants are worked one by one, which is what a host
/// whose tenants live in databases of their own (silo mode) does. A <c>LISTEN</c> session on one connection hears
/// one of those databases, so every other tenant's messages wait for the poll. That is slow rather than wrong,
/// which is why this is a warning and not a failure: <see cref="BsftMessagingPostgresOptions.IsInProcessWake"/> is
/// the setting such a host wants.
/// </remarks>
internal sealed class OutboxWakeStartupValidator : IStartupValidator
{
    private readonly bool _hasTenantSource;
    private readonly IOptions<BsftMessagingPostgresOptions> _options;

    /// <summary>Initializes a new instance of the <see cref="OutboxWakeStartupValidator"/> class.</summary>
    /// <param name="tenantSources">The registered tenant sources; empty in a host that works for no tenant in particular.</param>
    /// <param name="options">The options carrying the wake-up choice.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public OutboxWakeStartupValidator(
        IEnumerable<IBackgroundTenantSource> tenantSources, IOptions<BsftMessagingPostgresOptions> options)
    {
        ArgumentNullException.ThrowIfNull(tenantSources);

        _hasTenantSource = tenantSources.Any();
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public Task<StartupValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        if (_options.Value.IsInProcessWake || !_hasTenantSource)
            return Task.FromResult(StartupValidationResult.Success());

        return Task.FromResult(StartupValidationResult.Success(
            message: null,
            warnings:
            [
                $"An {nameof(IBackgroundTenantSource)} is registered, but the outbox wake-up listens with pg_notify on one database. In a host whose tenants have databases of their own, only that database's commits wake the dispatcher; set {nameof(BsftMessagingPostgresOptions)}.{nameof(BsftMessagingPostgresOptions.IsInProcessWake)} to true.",
            ]));
    }
}
