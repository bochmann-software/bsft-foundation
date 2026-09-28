using Bsft.Idempotency.DependencyInjection;
using Bsft.Idempotency.EFCore.DependencyInjection;
using Bsft.Idempotency.EFCore.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Bsft.Idempotency.EFCore.Tests.DependencyInjection;

public sealed class IdempotencyEFCoreServiceCollectionExtensionsTests
{
    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TestIdempotencyDbContext>(options => options.UseSqlite("DataSource=:memory:"));

        return services;
    }

    private static IIdempotencyStore StoreOf(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();

        return scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
    }

    [Fact]
    public void The_shared_store_replaces_the_in_process_default_however_the_calls_are_ordered()
    {
        // Order-independence is the point of replacing rather than adding: which store is in effect must
        // not depend on the order two lines of start-up code happen to be in, because the wrong answer
        // is a host whose replicas each keep their own memory and let duplicates through.
        var before = Services();
        before.AddBsftIdempotencyEFCore<TestIdempotencyDbContext>();
        before.AddBsftIdempotency();

        var after = Services();
        after.AddBsftIdempotency();
        after.AddBsftIdempotencyEFCore<TestIdempotencyDbContext>();

        using var beforeProvider = before.BuildServiceProvider();
        using var afterProvider = after.BuildServiceProvider();

        StoreOf(beforeProvider).Should().BeOfType<EFCoreIdempotencyStore>();
        StoreOf(afterProvider).Should().BeOfType<EFCoreIdempotencyStore>();
    }

    [Fact]
    public void The_in_process_default_is_what_stands_without_the_shared_store()
    {
        // The precondition of the test above: the replacement is doing something, rather than the shared
        // store happening to be what was registered anyway.
        var services = Services();
        services.AddBsftIdempotency();

        using var provider = services.BuildServiceProvider();

        StoreOf(provider).Should().BeOfType<InMemoryIdempotencyStore>();
    }

    [Fact]
    public void The_shared_store_is_not_captive_on_the_scoped_context()
    {
        // The store writes through the application's own scoped context, so it must be scoped itself.
        // Registered as a singleton it would be resolvable in a test and hold a root-scoped context in a
        // host, which is the defect scope validation exists to catch.
        var services = Services();
        services.AddBsftIdempotency();
        services.AddBsftIdempotencyEFCore<TestIdempotencyDbContext>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
        });

        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IIdempotencyStore>()
            .Should().BeOfType<EFCoreIdempotencyStore>()
            .And.BeSameAs(scope.ServiceProvider.GetRequiredService<IIdempotencyStore>());
    }

    [Fact]
    public void Two_scopes_get_two_stores()
    {
        var services = Services();
        services.AddBsftIdempotency();
        services.AddBsftIdempotencyEFCore<TestIdempotencyDbContext>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
        });

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        first.ServiceProvider.GetRequiredService<IIdempotencyStore>()
            .Should().NotBeSameAs(second.ServiceProvider.GetRequiredService<IIdempotencyStore>());
    }

    [Fact]
    public void The_context_resolution_and_the_pruner_are_registered()
    {
        var services = Services();
        services.AddBsftIdempotencyEFCore<TestIdempotencyDbContext>();

        using var provider = services.BuildServiceProvider();

        provider.GetServices<IHostedService>().OfType<IdempotencyRecordSweeper>().Should().ContainSingle();

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IIdempotencyDbContext>()
            .Should().BeOfType<TestIdempotencyDbContext>();
    }

    [Fact]
    public void A_clock_is_registered_for_hosts_that_wire_nothing_else()
    {
        // The store and the pruner both judge expiry against it, and the shared store may be registered
        // without the in-process package's own call having run.
        var services = Services();
        services.AddBsftIdempotencyEFCore<TestIdempotencyDbContext>();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(TimeProvider.System);
    }

    [Fact]
    public void A_host_s_own_clock_survives_the_registration()
    {
        var services = Services();
        var clock = new MutableTimeProvider(DateTimeOffset.UnixEpoch);
        services.AddSingleton<TimeProvider>(clock);

        services.AddBsftIdempotencyEFCore<TestIdempotencyDbContext>();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(clock);
    }

    [Fact]
    public void The_registration_is_chainable() =>
        Services().AddBsftIdempotencyEFCore<TestIdempotencyDbContext>()
            .Should().BeAssignableTo<IServiceCollection>();

    [Fact]
    public void A_missing_service_collection_is_rejected()
    {
        var act = () => ((IServiceCollection)null!).AddBsftIdempotencyEFCore<TestIdempotencyDbContext>();

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }
}
