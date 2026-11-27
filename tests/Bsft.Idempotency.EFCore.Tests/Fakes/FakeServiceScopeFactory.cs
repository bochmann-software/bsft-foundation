using Bsft.Core.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Idempotency.EFCore.Tests.Fakes;

/// <summary>
/// Records the background tenant every scope is opened for, then refuses the scope, so one pass of a sweeper
/// shows whom it worked for without needing a database.
/// </summary>
public sealed class FakeServiceScopeFactory : IServiceScopeFactory
{
    private readonly List<string?> _openedFor = [];

    /// <summary>Gets the tenants the scopes were opened for, in order.</summary>
    public IReadOnlyList<string?> OpenedFor
    {
        get
        {
            lock (_openedFor)
                return [.. _openedFor];
        }
    }

    /// <inheritdoc />
    public IServiceScope CreateScope()
    {
        lock (_openedFor)
            _openedFor.Add(BackgroundTenant.Current);

        throw new InvalidOperationException("The fake opens no scope.");
    }
}
