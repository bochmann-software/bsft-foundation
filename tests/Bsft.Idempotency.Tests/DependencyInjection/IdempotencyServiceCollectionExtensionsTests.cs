using Bsft.Idempotency.Auditing;
using Bsft.Idempotency.DependencyInjection;
using Bsft.Idempotency.Diagnostics;
using Bsft.Idempotency.Fingerprinting;
using Bsft.Idempotency.Replay;
using Bsft.Idempotency.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Idempotency.Tests.DependencyInjection;

public class IdempotencyServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBsftIdempotency_RegistersTheWholeDecisionPath()
    {
        using var provider = Provider(services => services.AddBsftIdempotency());
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IIdempotencyStore>().Should().BeOfType<InMemoryIdempotencyStore>();
        scope.ServiceProvider.GetRequiredService<IIdempotencyAuditSink>()
            .Should().BeOfType<NullIdempotencyAuditSink>();
        scope.ServiceProvider.GetRequiredService<IIdempotencyFingerprinter>()
            .Should().BeOfType<IdempotencyFingerprinter>();
        scope.ServiceProvider.GetRequiredService<IIdempotencyGate>().Should().BeOfType<IdempotencyGate>();
        scope.ServiceProvider.GetRequiredService<IIdempotencyObserver>().Should().BeOfType<IdempotencyObserver>();
        scope.ServiceProvider.GetRequiredService<IIdempotencyTelemetry>().Should().BeOfType<IdempotencyTelemetry>();
        scope.ServiceProvider.GetRequiredService<IdempotencyRegistrationMarker>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<TimeProvider>().Should().BeSameAs(TimeProvider.System);
    }

    [Fact]
    public void AddBsftIdempotency_TheConfigureDelegate_ShapesTheOptions()
    {
        using var provider = Provider(services =>
            services.AddBsftIdempotency(options => options.ShouldRequireKey = true));

        provider.GetRequiredService<IOptions<BsftIdempotencyOptions>>().Value.ShouldRequireKey.Should().BeTrue();
    }

    [Fact]
    public void AddBsftIdempotency_WithoutAConfigureDelegate_LeavesTheDefaultsInPlace()
    {
        using var provider = Provider(services => services.AddBsftIdempotency());

        provider.GetRequiredService<IOptions<BsftIdempotencyOptions>>().Value.HeaderName
            .Should().Be(IdempotencyDefaults.HeaderName);
    }

    [Fact]
    public void AddBsftIdempotency_TheGateAndTheObserver_AreScopedSoAStoreCanWriteThroughAUnitOfWork()
    {
        var services = new ServiceCollection();

        services.AddBsftIdempotency();

        services.Single(descriptor => descriptor.ServiceType == typeof(IIdempotencyGate))
            .Lifetime.Should().Be(ServiceLifetime.Scoped);
        services.Single(descriptor => descriptor.ServiceType == typeof(IIdempotencyObserver))
            .Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddBsftIdempotency_FromConfiguration_BindsTheSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Idempotency:ShouldRequireKey"] = "true",
                ["Bsft:Idempotency:MaxKeyLength"] = "64",
                ["Bsft:Idempotency:Methods:0"] = "PATCH",
            })
            .Build();

        using var provider = Provider(services => services.AddBsftIdempotency(configuration));

        var options = provider.GetRequiredService<IOptions<BsftIdempotencyOptions>>().Value;

        options.ShouldRequireKey.Should().BeTrue();
        options.MaxKeyLength.Should().Be(64);
        options.Methods.Should().Equal("PATCH");
    }

    [Fact]
    public void AddBsftIdempotency_FromConfiguration_LetsCodeHaveTheLastWord()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Bsft:Idempotency:MaxKeyLength"] = "64" })
            .Build();

        using var provider = Provider(services =>
            services.AddBsftIdempotency(configuration, options => options.MaxKeyLength = 32));

        provider.GetRequiredService<IOptions<BsftIdempotencyOptions>>().Value.MaxKeyLength.Should().Be(32);
    }

    [Fact]
    public void AddBsftIdempotency_AConfigurationThatCannotBeUsed_FailsTheWiringLineRatherThanTheFirstRequest()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Bsft:Idempotency:MaxKeyLength"] = "0" })
            .Build();

        FluentActions.Invoking(() => new ServiceCollection().AddBsftIdempotency(configuration))
            .Should().Throw<InvalidOperationException>().WithMessage("*MaxKeyLength*");
    }

    [Fact]
    public void AddBsftIdempotency_OptionsThatCannotBeUsed_FailTheWiringLine() =>
        FluentActions
            .Invoking(() => new ServiceCollection().AddBsftIdempotency(options => options.HeaderName = " "))
            .Should().Throw<InvalidOperationException>().WithMessage("*HeaderName*");

    [Fact]
    public async Task AddBsftIdempotency_OptionsBrokenAfterTheWiringLine_StillFailTheHostAtStartup()
    {
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddBsftIdempotency();
                services.PostConfigure<BsftIdempotencyOptions>(options => options.MaxKeyLength = 0);
            })
            .Build();

        await FluentActions.Awaiting(() => host.StartAsync())
            .Should().ThrowAsync<OptionsValidationException>();
    }

    [Fact]
    public void AddBsftIdempotency_TheValidator_IsRegisteredOnlyOnceEvenWhenCalledTwice()
    {
        var services = new ServiceCollection();

        services.AddBsftIdempotency();
        services.AddBsftIdempotency();

        services.Count(descriptor =>
                descriptor.ServiceType == typeof(IValidateOptions<BsftIdempotencyOptions>))
            .Should().Be(1);
    }

    [Fact]
    public void AddBsftIdempotency_EverySeamAHostRegisteredFirst_IsLeftAlone()
    {
        var clock = new MutableTimeProvider();
        var sink = new RecordingAuditSink();
        var store = new InMemoryIdempotencyStore(clock);

        using var provider = Provider(services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<IIdempotencyAuditSink>(sink);
            services.AddSingleton<IIdempotencyStore>(store);
            services.AddBsftIdempotency();
        });

        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<TimeProvider>().Should().BeSameAs(clock);
        scope.ServiceProvider.GetRequiredService<IIdempotencyAuditSink>().Should().BeSameAs(sink);
        scope.ServiceProvider.GetRequiredService<IIdempotencyStore>().Should().BeSameAs(store);
    }

    [Fact]
    public void AddBsftIdempotency_NullServices_IsRefused() =>
        FluentActions.Invoking(() => IdempotencyServiceCollectionExtensions.AddBsftIdempotency(null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("services");

    [Fact]
    public void AddBsftIdempotency_FromConfiguration_NullServices_IsRefused() =>
        FluentActions
            .Invoking(() => IdempotencyServiceCollectionExtensions.AddBsftIdempotency(
                null!, new ConfigurationBuilder().Build()))
            .Should().Throw<ArgumentNullException>().WithParameterName("services");

    [Fact]
    public void AddBsftIdempotency_NullConfiguration_IsRefused() =>
        FluentActions
            .Invoking(() => new ServiceCollection().AddBsftIdempotency((IConfiguration)null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("configuration");

    private static ServiceProvider Provider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        configure(services);

        return services.BuildServiceProvider();
    }
}
