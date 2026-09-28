using Bsft.Testing.Contexts;

namespace Bsft.Testing.Tests.Support;

/// <summary>The same application, booted with doubles other than the defaults.</summary>
internal sealed class OtherContextsTestAppFactory : TestAppFactory
{
    protected override TestTenantContext CreateTenantContext() => new("other-tenant");

    protected override TestCorrelationContext CreateCorrelationContext() => new("other-correlation");
}
