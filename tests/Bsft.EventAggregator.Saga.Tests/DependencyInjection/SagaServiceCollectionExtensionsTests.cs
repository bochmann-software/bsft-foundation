using Bsft.EventAggregator.DependencyInjection;
using Bsft.EventAggregator.Saga.DependencyInjection;
using Bsft.EventAggregator.Saga.Execution;
using Bsft.EventAggregator.Saga.Timeouts;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Bsft.EventAggregator.Saga.Tests.DependencyInjection;

public class SagaServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBsftSaga_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftSaga();

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("services");
    }

    [Fact]
    public void AddBsftSaga_RegistersRequiredServices()
    {
        var services = new ServiceCollection();
        services.AddBsftEventAggregator();
        services.AddBsftSaga();

        var provider = services.BuildServiceProvider();

        provider.GetService<ISagaEngine>().Should().NotBeNull().And.BeOfType<SagaEngine>();
        provider.GetService<ISagaStore>().Should().NotBeNull().And.BeOfType<InMemorySagaStore>();
        provider.GetService<ISagaBuilderFactory>().Should().NotBeNull().And.BeOfType<SagaBuilderFactory>();
        provider.GetService<SagaFlowHost>().Should().NotBeNull();
    }

    [Fact]
    public void AddBsftSaga_ServesOneDispatcherBehindTheDrainerAndTheHostedService()
    {
        var services = new ServiceCollection();
        services.AddBsftEventAggregator();
        services.AddBsftSaga();

        var provider = services.BuildServiceProvider();

        // A test that drains has to pump the very dispatcher the host polls, otherwise it exercises
        // a second instance over the same stores and the two can drift apart.
        var dispatcher = provider.GetRequiredService<SagaTimeoutDispatcher>();
        provider.GetRequiredService<ISagaTimeoutDrainer>().Should().BeSameAs(dispatcher);
        provider.GetServices<IHostedService>().Should().Contain(dispatcher);
    }

    [Fact]
    public void AddBsftSaga_WithApplicationSuppliedStore_KeepsTheApplicationStore()
    {
        var customStore = Substitute.For<ISagaStore>();

        var services = new ServiceCollection();
        services.AddBsftEventAggregator();
        services.AddSingleton(customStore);
        services.AddBsftSaga();

        var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<ISagaStore>();

        // Every store the engine brings is registered with TryAdd, which is what replaced the removed
        // BsftSagaOptions.StoreFactory: the choice of store is a registration, not an options value.
        store.Should().BeSameAs(customStore);
    }

    [Fact]
    public void AddBsftSaga_WithApplicationSuppliedTimeoutStore_KeepsTheApplicationStore()
    {
        var customStore = Substitute.For<ISagaTimeoutStore>();

        var services = new ServiceCollection();
        services.AddBsftEventAggregator();
        services.AddSingleton(customStore);
        services.AddBsftSaga();

        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ISagaTimeoutStore>().Should().BeSameAs(customStore);
    }

    [Fact]
    public void AddBsftSaga_WithConfiguration_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftSaga(new ConfigurationBuilder().Build());

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddBsftSaga_WithConfiguration_NullConfiguration_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftSaga((IConfiguration)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void AddBsftSaga_WithConfiguration_BindsTheSectionAndRegistersTheEngine()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{BsftSagaOptions.SectionName}:Timeouts:BatchSize"] = "7",
                [$"{BsftSagaOptions.SectionName}:Timeouts:CatchUpPolicy"] = nameof(SagaTimeoutCatchUpPolicy.FireLatestPerSaga),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddBsftEventAggregator();
        services.AddBsftSaga(configuration);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<BsftSagaOptions>>().Value;

        options.Timeouts.BatchSize.Should().Be(7);
        options.Timeouts.CatchUpPolicy.Should().Be(SagaTimeoutCatchUpPolicy.FireLatestPerSaga);
        provider.GetService<ISagaEngine>().Should().NotBeNull();
    }

    [Fact]
    public void AddBsftSaga_ReturnsServiceCollectionForChaining()
    {
        var services = new ServiceCollection();
        services.AddBsftEventAggregator();

        var result = services.AddBsftSaga();

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddBsftSagaFlow_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftSagaFlow<TestSagaFlow>();

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("services");
    }

    [Fact]
    public void AddBsftSagaFlow_RegistersFlowAsTransient()
    {
        var services = new ServiceCollection();
        services.AddBsftSagaFlow<TestSagaFlow>();

        var provider = services.BuildServiceProvider();
        var flows = provider.GetServices<ISagaFlow>().ToList();

        flows.Should().ContainSingle().Which.Should().BeOfType<TestSagaFlow>();
    }

    [Fact]
    public void AddBsftSaga_RegistersTheInMemoryStoresByDefault()
    {
        var services = new ServiceCollection();
        services.AddBsftEventAggregator();
        services.AddBsftSaga();

        var provider = services.BuildServiceProvider();

        // The in-memory pair is the default a host gets without a persistence package; the removed
        // UseInMemoryStore() no longer has to be called for it.
        provider.GetRequiredService<ISagaStore>().Should().BeOfType<InMemorySagaStore>();
        provider.GetRequiredService<ISagaTimeoutStore>().Should().BeOfType<InMemorySagaTimeoutStore>();
    }

    [Fact]
    public void BsftSagaOptions_ShouldCreateScopePerStep_DefaultsToFalse()
    {
        var options = new BsftSagaOptions();

        options.ShouldCreateScopePerStep.Should().BeFalse();
    }

    [Fact]
    public void BsftSagaOptions_ShouldCreateScopePerStep_CanBeSetToTrue()
    {
        var options = new BsftSagaOptions();
        options.ShouldCreateScopePerStep = true;

        options.ShouldCreateScopePerStep.Should().BeTrue();
    }

    private sealed class TestSagaFlow : ISagaFlow
    {
        public void Configure(ISagaBuilderFactory factory) { }
    }
}
