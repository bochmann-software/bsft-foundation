using Bsft.Data.MultiTenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;

namespace Bsft.Data.Tests.MultiTenancy;

public class TenantMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_SetsTenantIdAndCallsNext()
    {
        var tenantContext = new TenantContext();
        var resolver = Substitute.For<ITenantResolver>();
        resolver.ResolveAsync(Arg.Any<HttpContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("tenant-middleware-test"));

        var nextCalled = false;
        var next = new RequestDelegate(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var middleware = new TenantMiddleware(next);

        await middleware.InvokeAsync(new DefaultHttpContext(), resolver, tenantContext);

        tenantContext.TenantId.Should().Be("tenant-middleware-test");
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public void Constructor_NullNext_ThrowsArgumentNullException()
    {
        var act = () => new TenantMiddleware(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("next");
    }

    [Fact]
    public async Task InvokeAsync_NullContext_ThrowsArgumentNullException()
    {
        var middleware = new TenantMiddleware(_ => Task.CompletedTask);

        var act = async () => await middleware.InvokeAsync(
            null!,
            Substitute.For<ITenantResolver>(),
            new TenantContext());

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("context");
    }

    [Fact]
    public async Task InvokeAsync_NullResolver_ThrowsArgumentNullException()
    {
        var middleware = new TenantMiddleware(_ => Task.CompletedTask);

        var act = async () => await middleware.InvokeAsync(
            new DefaultHttpContext(),
            null!,
            new TenantContext());

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("tenantResolver");
    }

    [Fact]
    public async Task InvokeAsync_NullTenantContext_ThrowsArgumentNullException()
    {
        var middleware = new TenantMiddleware(_ => Task.CompletedTask);

        var act = async () => await middleware.InvokeAsync(
            new DefaultHttpContext(),
            Substitute.For<ITenantResolver>(),
            null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("tenantContext");
    }
}
