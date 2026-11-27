using FluentAssertions;
using Xunit;
using Bsft.Core.Tenancy;

namespace Bsft.Core.Tests.Tenancy;

public class BackgroundTenantTests
{
    [Fact]
    public void Current_ByDefault_IsNull()
    {
        BackgroundTenant.Current.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithATenant_ExposesItsTenantId()
    {
        using var tenant = new BackgroundTenant("t1");

        tenant.TenantId.Should().Be("t1");
    }

    [Fact]
    public void Constructor_SetsCurrentTenantId()
    {
        using var _ = new BackgroundTenant("t1");

        BackgroundTenant.Current.Should().Be("t1");
    }

    [Fact]
    public void Constructor_DisposingSpan_RestoresNullCurrent()
    {
        var span = new BackgroundTenant("t1");
        BackgroundTenant.Current.Should().Be("t1");

        span.Dispose();
        BackgroundTenant.Current.Should().BeNull();
    }

    [Fact]
    public void Constructor_NestedCalls_RestorePreviousValueOnDispose()
    {
        using var outer = new BackgroundTenant("t1");
        BackgroundTenant.Current.Should().Be("t1");

        using var inner = new BackgroundTenant("t2");
        BackgroundTenant.Current.Should().Be("t2");

        inner.Dispose();
        BackgroundTenant.Current.Should().Be("t1");

        outer.Dispose();
        BackgroundTenant.Current.Should().BeNull();
    }

    [Fact]
    public void Constructor_DisposingTwice_IsIdempotent()
    {
        var span = new BackgroundTenant("t1");
        BackgroundTenant.Current.Should().Be("t1");

        span.Dispose();
        BackgroundTenant.Current.Should().BeNull();

        span.Dispose();
        BackgroundTenant.Current.Should().BeNull();
    }

    [Fact]
    public void Constructor_DisposingTwice_DoesNotClobberLaterValue()
    {
        var span1 = new BackgroundTenant("t1");
        span1.Dispose();
        BackgroundTenant.Current.Should().BeNull();

        span1.Dispose(); // Second dispose should not affect the next Enter

        using var span2 = new BackgroundTenant("t2");
        BackgroundTenant.Current.Should().Be("t2");
    }

    [Fact]
    public void Constructor_WithNullTenantId_ThrowsArgumentException()
    {
        var act = () => new BackgroundTenant(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithEmptyString_ThrowsArgumentException()
    {
        var act = () => new BackgroundTenant("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithWhitespaceOnlyString_ThrowsArgumentException()
    {
        var act = () => new BackgroundTenant("   ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Constructor_FlowsIntoAwaitedTask()
    {
        string? innerTenant = null;

        using var _ = new BackgroundTenant("t1");

        await Task.Run(() =>
        {
            innerTenant = BackgroundTenant.Current;
        });

        innerTenant.Should().Be("t1");
    }

    [Fact]
    public async Task Constructor_DoesNotLeakBackFromAwaitedTask()
    {
        using var outer = new BackgroundTenant("t1");

        await Task.Run(() =>
        {
            new BackgroundTenant("t2").Dispose();
        });

        BackgroundTenant.Current.Should().Be("t1");
    }

    [Fact]
    public async Task Constructor_AsyncLocalSemantics_ChildSetDoesNotAffectCaller()
    {
        using var _ = new BackgroundTenant("t1");

        var childSpan = await Task.Run(async () =>
        {
            var span = new BackgroundTenant("t2");
            await Task.Yield();
            return span;
        });

        BackgroundTenant.Current.Should().Be("t1");
        childSpan.Dispose();
        BackgroundTenant.Current.Should().Be("t1");
    }
}
