using Bsft.Core.Observers;
using Bsft.Hosting.DependencyInjection;
using Bsft.Hosting.Logging;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Hosting.Tests.DependencyInjection;

public sealed class ObserverServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBsftObservers_RegistersGuardReporterAndComposite()
    {
        using var provider = new ServiceCollection().AddLogging().AddBsftObservers().BuildServiceProvider();

        provider.GetRequiredService<IObserverFailureReporter>().Should().BeOfType<LoggingObserverFailureReporter>();
        provider.GetRequiredService<IObserverGuard>().Should().BeOfType<ObserverGuard>();
        provider.GetRequiredService<ObserverComposite<IProbe>>().Observers.Should().BeEmpty();
    }

    [Fact]
    public void AddBsftObservers_KeepsARegistrationTheHostMadeFirst()
    {
        var reporter = new HostReporter();
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IObserverFailureReporter>(reporter)
            .AddBsftObservers()
            .BuildServiceProvider();

        provider.GetRequiredService<IObserverFailureReporter>().Should().BeSameAs(reporter);
    }

    [Fact]
    public void AddBsftObserver_RegistersObserversInOrderAndOnlyOncePerImplementation()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddBsftObserver<IProbe, FirstProbe>();
        services.AddBsftObserver<IProbe, SecondProbe>();
        services.AddBsftObserver<IProbe, FirstProbe>();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ObserverComposite<IProbe>>().Observers
            .Select(observer => observer.GetType()).Should().Equal(typeof(FirstProbe), typeof(SecondProbe));
    }

    [Fact]
    public void AddBsftObservers_AppliesTheLoggingOptions()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddBsftObservers(options => options.IncludeExceptionDetails = true)
            .BuildServiceProvider();

        provider.GetRequiredService<IOptions<ObserverFailureLoggingOptions>>().Value.IncludeExceptionDetails.Should().BeTrue();
    }

    [Fact]
    public void AddBsftObservers_WithoutADelegate_DoesNotLogExceptionDetails()
    {
        using var provider = new ServiceCollection().AddLogging().AddBsftObservers().BuildServiceProvider();

        provider.GetRequiredService<IOptions<ObserverFailureLoggingOptions>>().Value.IncludeExceptionDetails.Should().BeFalse();
    }

    [Theory]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public void AddBsftObserver_WhenTheContractIsAlreadyRegisteredWithAShorterLifetime_Throws(ServiceLifetime lifetime)
    {
        IServiceCollection services = new ServiceCollection();
        services.Add(ServiceDescriptor.Describe(typeof(IProbe), typeof(FirstProbe), lifetime));

        FluentActions.Invoking(() => services.AddBsftObserver<IProbe, SecondProbe>())
            .Should().Throw<InvalidOperationException>().WithMessage($"*{nameof(IProbe)}*{lifetime}*singleton*");
    }

    [Fact]
    public void AddBsftObserver_WhenOnlyAKeyedScopedServiceIsRegistered_Succeeds()
    {
        var services = new ServiceCollection().AddLogging().AddKeyedScoped<IProbe, FirstProbe>("other");

        services.AddBsftObserver<IProbe, SecondProbe>();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ObserverComposite<IProbe>>().Observers.Should().ContainSingle();
    }

    [Fact]
    public void AddBsftObserver_WhenTheContractIsAlreadyRegisteredAsASingleton_Succeeds()
    {
        var services = new ServiceCollection().AddLogging().AddSingleton<IProbe, FirstProbe>();

        services.AddBsftObserver<IProbe, SecondProbe>();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ObserverComposite<IProbe>>().Observers.Should().HaveCount(2);
    }

    [Fact]
    public void Arguments_AreValidated()
    {
        FluentActions.Invoking(() => ((IServiceCollection)null!).AddBsftObservers()).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => ((IServiceCollection)null!).AddBsftObserver<IProbe, FirstProbe>()).Should().Throw<ArgumentNullException>();
    }

    public interface IProbe;

    private sealed class FirstProbe : IProbe;

    private sealed class SecondProbe : IProbe;

    private sealed class HostReporter : IObserverFailureReporter
    {
        public void ReportObserverFailed(object observer, Exception failure)
        {
        }
    }
}
