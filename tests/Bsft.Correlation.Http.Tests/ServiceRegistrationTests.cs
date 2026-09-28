using Bsft.Correlation.Http.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Bsft.Correlation.Http.Tests;

/// <summary>
/// What <c>AddBsftCorrelationHttp</c> puts into the container, and what it deliberately leaves out.
/// </summary>
public sealed class ServiceRegistrationTests
{
    [Fact]
    public void AddBsftCorrelationHttp_RegistersTheHandler()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ICorrelationContext>());

        services.AddBsftCorrelationHttp();

        using var provider = services.BuildServiceProvider();

        provider.GetService<CorrelationIdDelegatingHandler>().Should().NotBeNull();
    }

    [Fact]
    public void AddBsftCorrelationHttp_ReturnsTheSameCollection()
    {
        var services = new ServiceCollection();

        services.AddBsftCorrelationHttp().Should().BeSameAs(services);
    }

    [Fact]
    public void AddBsftCorrelationHttp_RegistersTheHandlerAsTransient()
    {
        // Transient rather than scoped: IHttpClientFactory owns the handler lifetime and rotates the
        // chain on its own schedule, so a scoped registration would tie a pooled handler to a request.
        var services = new ServiceCollection();

        services.AddBsftCorrelationHttp();

        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(CorrelationIdDelegatingHandler))
            .Which.Lifetime.Should().Be(ServiceLifetime.Transient);
    }

    [Fact]
    public void AddBsftCorrelationHttp_CalledTwice_RegistersTheHandlerOnce()
    {
        // The host-facing promise of the split: AddBsftCorrelation() calls this, and a host that also
        // calls it itself — or a resilient client registration that does — must not end up with the
        // header attached twice.
        var services = new ServiceCollection();

        services.AddBsftCorrelationHttp();
        services.AddBsftCorrelationHttp();

        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(CorrelationIdDelegatingHandler));
    }

    [Fact]
    public void AddBsftCorrelationHttp_RegistersNoCorrelationContext()
    {
        // Where the id comes from is the host's decision: a server gets it from AddBsftCorrelation() and
        // the inbound middleware, a client supplies its own. Registering a default here would give a
        // client a silently useless one.
        var services = new ServiceCollection();

        services.AddBsftCorrelationHttp();

        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(ICorrelationContext));
    }

    [Fact]
    public void AddBsftCorrelationHttp_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftCorrelationHttp();

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }
}
