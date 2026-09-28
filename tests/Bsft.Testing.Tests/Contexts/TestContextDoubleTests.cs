using Bsft.Testing.Contexts;
using FluentAssertions;
using Xunit;

namespace Bsft.Testing.Tests.Contexts;

public class TestContextDoubleTests
{
    [Fact]
    public void Tenant_starts_resolved_to_the_default_tenant()
    {
        var tenant = new TestTenantContext();

        tenant.IsResolved.Should().BeTrue();
        tenant.TenantId.Should().Be(TestTenantContext.DefaultTenantId);
    }

    [Fact]
    public void Tenant_can_switch()
    {
        var tenant = new TestTenantContext("first");

        tenant.SetTenant("second");

        tenant.TenantId.Should().Be("second");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Tenant_rejects_a_blank_identifier(string? tenantId)
    {
        var resolve = () => new TestTenantContext(tenantId!);

        resolve.Should().Throw<ArgumentException>().WithParameterName("tenantId");
    }

    [Fact]
    public void Cleared_tenant_reports_unresolved()
    {
        var tenant = new TestTenantContext();

        tenant.Clear();

        tenant.IsResolved.Should().BeFalse();
        tenant.TenantId.Should().BeNull();
    }

    [Fact]
    public void Correlation_carries_a_fixed_default_id()
    {
        new TestCorrelationContext().CorrelationId.Should().Be(TestCorrelationContext.DefaultCorrelationId);
    }

    [Fact]
    public void Correlation_carries_the_given_id()
    {
        new TestCorrelationContext("abc-123").CorrelationId.Should().Be("abc-123");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Correlation_rejects_a_blank_id(string? correlationId)
    {
        var create = () => new TestCorrelationContext(correlationId!);

        create.Should().Throw<ArgumentException>().WithParameterName("correlationId");
    }
}
