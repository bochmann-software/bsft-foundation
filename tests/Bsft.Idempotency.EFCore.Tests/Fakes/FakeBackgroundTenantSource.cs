using Bsft.Core.Tenancy;

namespace Bsft.Idempotency.EFCore.Tests.Fakes;

public class FakeBackgroundTenantSource : IBackgroundTenantSource
{
    private readonly IReadOnlyList<string> _tenants;

    public FakeBackgroundTenantSource(IReadOnlyList<string> tenants)
    {
        _tenants = tenants;
    }

    public ValueTask<IReadOnlyList<string>> ListTenantsAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(_tenants);
    }
}
