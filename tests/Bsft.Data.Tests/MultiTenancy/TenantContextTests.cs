using Bsft.Data.MultiTenancy;
using FluentAssertions;
using Xunit;

namespace Bsft.Data.Tests.MultiTenancy;

public class TenantContextTests
{
    [Fact]
    public void TenantContext_InitialState_IsNotResolved()
    {
        var context = new TenantContext();

        context.IsResolved.Should().BeFalse();
        context.TenantId.Should().BeNull();
    }

    [Fact]
    public void TenantContext_AfterSet_IsResolved()
    {
        var context = new TenantContext();

        context.SetTenantId("tenant-1");

        context.IsResolved.Should().BeTrue();
        context.TenantId.Should().Be("tenant-1");
    }

    [Fact]
    public void TenantContext_SetNull_IsResolvedButNull()
    {
        var context = new TenantContext();

        context.SetTenantId(null);

        context.IsResolved.Should().BeTrue();
        context.TenantId.Should().BeNull();
    }
}
