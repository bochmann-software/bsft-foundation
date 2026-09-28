using Bsft.Core;
using Bsft.Data.MultiTenancy;
using Bsft.Mediator;

namespace Bsft.Testing.Tests.Support;

public class WhichTenantHandler : IQueryHandler<WhichTenant, string?>
{
    private readonly ITenantContext _tenant;

    public WhichTenantHandler(ITenantContext tenant) => _tenant = tenant;

    public Task<string?> HandleAsync(WhichTenant query, CancellationToken ct = default) => Task.FromResult(_tenant.TenantId);
}
