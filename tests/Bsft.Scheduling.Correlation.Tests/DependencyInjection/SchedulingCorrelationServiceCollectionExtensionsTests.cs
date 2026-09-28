using Bsft.Correlation;
using Bsft.Correlation.DependencyInjection;
using Bsft.Scheduling.Correlation.DependencyInjection;
using Bsft.Scheduling.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Scheduling.Correlation.Tests.DependencyInjection;

public class SchedulingCorrelationServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBsftSchedulingCorrelationRejectsANullServiceCollection()
    {
        var act = () => SchedulingCorrelationServiceCollectionExtensions.AddBsftSchedulingCorrelation(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddBsftSchedulingCorrelationReturnsTheSameCollectionForChaining()
    {
        var services = new ServiceCollection();

        services.AddBsftSchedulingCorrelation().Should().BeSameAs(services);
    }

    [Fact]
    public void TheAccessorIsRegisteredScopedBecauseTheCorrelationContextIs()
    {
        var services = new ServiceCollection();

        services.AddBsftSchedulingCorrelation();

        var descriptor = services.Single(d => d.ServiceType == typeof(IJobCorrelationAccessor));
        descriptor.ImplementationType.Should().Be<CorrelationContextJobAccessor>();
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void TheCorrelationAccessorWinsWhenTheSchedulerWasRegisteredFirst()
    {
        var services = new ServiceCollection();
        services.AddBsftCorrelation();
        services.AddBsftScheduling();
        services.AddBsftSchedulingCorrelation();

        ResolveAccessor(services).Should().BeOfType<CorrelationContextJobAccessor>();
    }

    [Fact]
    public void TheCorrelationAccessorWinsWhenItWasRegisteredFirst()
    {
        var services = new ServiceCollection();
        services.AddBsftCorrelation();
        services.AddBsftSchedulingCorrelation();
        services.AddBsftScheduling();

        // AddBsftScheduling only registers its ambient-only default with TryAdd, so it must not
        // displace the richer accessor.
        ResolveAccessor(services).Should().BeOfType<CorrelationContextJobAccessor>();
    }

    [Fact]
    public void AResolvedAccessorReadsTheCorrelationIdOfItsOwnScope()
    {
        var services = new ServiceCollection();
        services.AddBsftCorrelation();
        services.AddBsftScheduling();
        services.AddBsftSchedulingCorrelation();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<CorrelationContext>().CorrelationId = "request-1";

        scope.ServiceProvider.GetRequiredService<IJobCorrelationAccessor>().CorrelationId.Should().Be("request-1");
    }

    private static IJobCorrelationAccessor ResolveAccessor(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IJobCorrelationAccessor>();
    }
}
