using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Bsft.Caching.DependencyInjection;
using Bsft.Caching.Invalidation;
using Bsft.Caching.Keys;

namespace Bsft.Caching.Tests;

public class CachingRegistrationTests
{
    [Fact]
    public void AddBsftCaching_NullServices_Throws()
    {
        var act = () => CachingServiceCollectionExtensions.AddBsftCaching(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddBsftCaching_ByDefault_UsesTheMemoryStore()
    {
        using var provider = new ServiceCollection().AddBsftCaching().BuildServiceProvider();

        provider.GetRequiredService<ICacheStore>().Should().BeOfType<MemoryCacheStore>();
    }

    [Fact]
    public void AddBsftCaching_DistributedProvider_UsesTheDistributedStore()
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        services.AddSingleton<ILogger<DistributedCacheStore>>(NullLogger<DistributedCacheStore>.Instance);

        using var provider = services
            .AddBsftCaching(o => o.Provider = CacheProvider.Distributed)
            .BuildServiceProvider();

        provider.GetRequiredService<ICacheStore>().Should().BeOfType<DistributedCacheStore>();
    }

    [Fact]
    public void AddBsftCaching_UnknownProvider_Throws()
    {
        var act = () => new ServiceCollection().AddBsftCaching(o => o.Provider = (CacheProvider)99);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AddBsftCaching_NonPositiveDefaultLifetime_FailsTheHostAtStartup()
    {
        // Registration no longer throws: the rule moved into an IValidateOptions so that a value
        // bound from configuration is checked too, and so that every complaint arrives at once.
        using var provider = new ServiceCollection()
            .AddBsftCaching(o => o.DefaultTimeToLive = TimeSpan.Zero)
            .BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<BsftCachingOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*DefaultTimeToLive*");
    }

    [Fact]
    public void AddBsftCaching_EmptyKeyPrefix_FailsTheHostAtStartup()
    {
        using var provider = new ServiceCollection()
            .AddBsftCaching(o => o.KeyPrefix = "  ")
            .BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<BsftCachingOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*KeyPrefix*");
    }

    [Fact]
    public void AddBsftCaching_UnknownProvider_FailsTheHostAtStartup()
    {
        // The store registration throws on an unknown provider because which type is registered is a
        // container decision that cannot wait for IOptions. This covers the other half: a provider
        // that arrives through configuration after registration still has to be rejected.
        using var provider = new ServiceCollection()
            .AddBsftCaching()
            .PostConfigure<BsftCachingOptions>(o => o.Provider = (CacheProvider)42)
            .BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<BsftCachingOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*Provider*");
    }

    [Fact]
    public void AddBsftCaching_BindsTheConfigurationSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Caching:KeyPrefix"] = "from-config",
                ["Bsft:Caching:DefaultTimeToLive"] = "00:11:00",
                ["Bsft:Caching:Provider"] = nameof(CacheProvider.Distributed),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();

        using var provider = services
            .AddBsftCaching(configuration)
            .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<BsftCachingOptions>>().Value;

        options.KeyPrefix.Should().Be("from-config");
        options.DefaultTimeToLive.Should().Be(TimeSpan.FromMinutes(11));
        // The bound provider decides the store, which is why the overload reads the section twice.
        provider.GetRequiredService<ICacheStore>().Should().BeOfType<DistributedCacheStore>();
    }

    [Fact]
    public void AddBsftCaching_NullConfiguration_Throws()
    {
        var act = () => new ServiceCollection().AddBsftCaching((IConfiguration)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddBsftCaching_NullServicesWithConfiguration_Throws()
    {
        var act = () => CachingServiceCollectionExtensions.AddBsftCaching(
            null!,
            new ConfigurationBuilder().Build());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddBsftCaching_RegistersTheSupportingServices()
    {
        using var provider = new ServiceCollection().AddBsftCaching().BuildServiceProvider();

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICacheKeyBuilder>().Should().BeOfType<CacheKeyBuilder>();
        provider.GetRequiredService<ICacheTagIndex>().Should().BeOfType<CacheTagIndex>();
        provider.GetRequiredService<ICacheSerializer>().Should().BeOfType<JsonCacheSerializer>();
        provider.GetRequiredService<ICacheInvalidator>().Should().BeOfType<CacheInvalidator>();
        scope.ServiceProvider.GetRequiredService<ICacheKeyScopeProvider>().Should().BeOfType<GlobalCacheKeyScopeProvider>();
        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(TimeProvider.System);
    }

    [Fact]
    public void AddBsftCaching_KeepsAnApplicationSuppliedScopeProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICacheKeyScopeProvider>(new FixedCacheKeyScopeProvider("tenant-1"));

        using var provider = services.AddBsftCaching().BuildServiceProvider();

        provider.GetRequiredService<ICacheKeyScopeProvider>().GetScope()
            .Should().Be(CacheKeyScope.For("tenant-1"));
    }

    [Fact]
    public void AddBsftCaching_AcceptsAScopedScopeProvider_WithoutACaptiveDependency()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICacheKeyScopeProvider>(_ => new FixedCacheKeyScopeProvider("tenant-1"));
        services.AddBsftCaching();

        // Scope validation is what catches a singleton capturing a per-request dependency.
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<ICacheKeyBuilder>();

        act.Should().NotThrow();
    }

    [Fact]
    public void AddBsftCaching_KeepsAnApplicationSuppliedTagIndex()
    {
        // The default index is in-process and volatile, so tag invalidation only reaches what this
        // instance cached since it started. Replacing it is the documented way out on a distributed
        // cache, which only works as long as the registration stays a TryAdd.
        var index = new CacheTagIndex(TimeProvider.System);
        var services = new ServiceCollection();
        services.AddSingleton<ICacheTagIndex>(index);

        using var provider = services
            .AddBsftCaching(o => o.Provider = CacheProvider.Distributed)
            .BuildServiceProvider();

        provider.GetRequiredService<ICacheTagIndex>().Should().BeSameAs(index);
    }

    [Fact]
    public void AddBsftCaching_RegistersNoPipelineBehavior()
    {
        // The behavior lives in Bsft.Caching.Mediator: this package is referenced by hosts that
        // cache through ICacheStore and never dispatch anything, so it registers nothing mediator
        // shaped and takes no mediator reference to do so.
        var services = new ServiceCollection().AddBsftCaching();

        services.Should().NotContain(descriptor =>
            descriptor.ServiceType.FullName!.StartsWith("Bsft.Mediator", StringComparison.Ordinal));
    }

    [Fact]
    public void AddBsftCaching_IsIdempotent()
    {
        var services = new ServiceCollection();

        services.AddBsftCaching();
        services.AddBsftCaching(o => o.KeyPrefix = "ignored");

        using var provider = services.BuildServiceProvider();

        provider.GetServices<ICacheStore>().Should().ContainSingle();
        // The options themselves compose rather than being won by the first caller: that is the
        // Options pattern's semantics, and it is what replaced the previous singleton registration.
        provider.GetRequiredService<IOptions<BsftCachingOptions>>().Value.KeyPrefix.Should().Be("ignored");
    }

    [Fact]
    public void AddBsftCaching_AppliesTheConfiguredOptions()
    {
        using var provider = new ServiceCollection()
            .AddBsftCaching(o => o.KeyPrefix = "custom")
            .BuildServiceProvider();

        provider.GetRequiredService<IOptions<BsftCachingOptions>>().Value.KeyPrefix.Should().Be("custom");
    }

    [Fact]
    public void AddBsftCaching_AppliesTheConfigureDelegateExactlyOnce()
    {
        // The provider decides which ICacheStore is registered and therefore has to be read at
        // registration time. Running the caller's delegate a second time to fill IOptions would let a
        // delegate that is not purely deterministic hand the container one provider and IOptions
        // another, and the registered store would not be the one the application believes in.
        var invocations = 0;

        using var provider = new ServiceCollection()
            .AddBsftCaching(o =>
            {
                invocations++;
                o.KeyPrefix = $"run-{invocations}";
            })
            .BuildServiceProvider();

        provider.GetRequiredService<IOptions<BsftCachingOptions>>().Value.KeyPrefix.Should().Be("run-1");
        invocations.Should().Be(1);
    }

    [Fact]
    public void AddBsftCaching_TagIndexIsShared()
    {
        using var provider = new ServiceCollection().AddBsftCaching().BuildServiceProvider();

        provider.GetRequiredService<ICacheTagIndex>()
            .Should().BeSameAs(provider.GetRequiredService<ICacheTagIndex>());
    }
}
