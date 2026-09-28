using Bsft.Correlation;
using Bsft.Correlation.DependencyInjection;
using Bsft.Messaging.Correlation.DependencyInjection;
using Bsft.Messaging.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Messaging.Correlation.Tests.DependencyInjection;

public class MessagingCorrelationServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBsftMessagingCorrelationRejectsANullServiceCollection()
    {
        var act = () => MessagingCorrelationServiceCollectionExtensions.AddBsftMessagingCorrelation(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddBsftMessagingCorrelationReturnsTheSameCollectionForChaining()
    {
        var services = new ServiceCollection();

        services.AddBsftMessagingCorrelation().Should().BeSameAs(services);
    }

    [Fact]
    public void TheAccessorIsRegisteredScopedBecauseTheCorrelationContextIs()
    {
        var services = new ServiceCollection();

        services.AddBsftMessagingCorrelation();

        var descriptor = services.Single(d => d.ServiceType == typeof(IMessageCorrelationAccessor));
        descriptor.ImplementationType.Should().Be<CorrelationContextMessageAccessor>();
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void TheCorrelationAccessorWinsWhenMessagingWasRegisteredFirst()
    {
        var services = new ServiceCollection();
        services.AddBsftCorrelation();
        services.AddBsftMessaging();
        services.AddBsftMessagingCorrelation();

        ResolveAccessor(services).Should().BeOfType<CorrelationContextMessageAccessor>();
    }

    [Fact]
    public void TheCorrelationAccessorWinsWhenItWasRegisteredFirst()
    {
        var services = new ServiceCollection();
        services.AddBsftCorrelation();
        services.AddBsftMessagingCorrelation();
        services.AddBsftMessaging();

        // AddBsftMessaging only registers its ambient-only default with TryAdd, so it must not
        // displace the richer accessor.
        ResolveAccessor(services).Should().BeOfType<CorrelationContextMessageAccessor>();
    }

    [Fact]
    public void AResolvedAccessorReadsTheCorrelationIdOfItsOwnScope()
    {
        var services = new ServiceCollection();
        services.AddBsftCorrelation();
        services.AddBsftMessaging();
        services.AddBsftMessagingCorrelation();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<CorrelationContext>().CorrelationId = "request-1";

        scope.ServiceProvider.GetRequiredService<IMessageCorrelationAccessor>().CorrelationId.Should().Be("request-1");
    }

    [Fact]
    public void AResolvedAccessorPrefersTheIdOfTheMessageBeingHandled()
    {
        var services = new ServiceCollection();
        services.AddBsftCorrelation();
        services.AddBsftMessaging();
        services.AddBsftMessagingCorrelation();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        using (provider.GetRequiredService<IMessageCorrelationScope>().Use("from-the-message"))
        {
            scope.ServiceProvider.GetRequiredService<IMessageCorrelationAccessor>()
                .CorrelationId.Should().Be("from-the-message");
        }
    }

    private static IMessageCorrelationAccessor ResolveAccessor(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IMessageCorrelationAccessor>();
    }
}
