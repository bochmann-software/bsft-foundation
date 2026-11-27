using Bsft.Core.Tenancy;
using Bsft.Messaging;
using Bsft.Outbox;

namespace Bsft.Messaging.EFCore.Tests.Fakes;

/// <summary>A drainer that finds nothing and records the background tenant of every drain.</summary>
public sealed class TenantRecordingOutboxDrainer : IOutboxDrainer<IntegrationEventMessage>
{
    private readonly List<string?> _drainedFor = [];

    /// <summary>Gets the tenants the drains ran for, in order.</summary>
    public IReadOnlyList<string?> DrainedFor
    {
        get
        {
            lock (_drainedFor)
                return [.. _drainedFor];
        }
    }

    /// <inheritdoc />
    public Task<int> DrainOnceAsync(CancellationToken cancellationToken = default)
    {
        lock (_drainedFor)
            _drainedFor.Add(BackgroundTenant.Current);

        return Task.FromResult(0);
    }
}
