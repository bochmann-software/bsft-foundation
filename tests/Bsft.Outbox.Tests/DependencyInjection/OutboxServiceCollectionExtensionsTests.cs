using Bsft.Outbox.DependencyInjection;
using Bsft.Outbox.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Outbox.Tests.DependencyInjection;

/// <summary>Tests what registering one outbox on the shared kernel gives a host.</summary>
public class OutboxServiceCollectionExtensionsTests
{
    [Fact]
    public void The_drainer_is_resolvable_once_the_outbox_supplies_its_two_seams()
    {
        using var provider = Provider(services => services.AddBsftOutbox<string>("primary"));

        provider.GetRequiredService<IOutboxDrainer<string>>().Should().NotBeNull();
    }

    /// <summary>The bounds are named options, so a second outbox is a second name.</summary>
    [Fact]
    public async Task Each_outbox_is_bounded_by_the_options_registered_under_its_own_name()
    {
        using var provider = Provider(services => services
            .AddBsftOutbox<string>("primary", options => options.BatchSize = 3)
            .AddBsftOutbox<int>("secondary", options => options.BatchSize = 9));
        var store = (RecordingOutboxStore)provider.GetRequiredService<IOutboxStore<string>>();

        await provider.GetRequiredService<IOutboxDrainer<string>>().DrainOnceAsync();
        await provider.GetRequiredService<IOutboxDrainer<int>>().DrainOnceAsync();

        store.Leases.Should().ContainSingle().Which.BatchSize.Should().Be(3);
    }

    /// <summary>The schedule is shared, and a host that replaces it keeps its replacement.</summary>
    [Fact]
    public void A_registered_schedule_is_not_replaced()
    {
        var replacement = new StubRetrySchedule();

        using var provider = Provider(services =>
        {
            services.AddSingleton<IOutboxRetrySchedule>(replacement);
            services.AddBsftOutbox<string>("primary");
        });

        provider.GetRequiredService<IOutboxRetrySchedule>().Should().BeSameAs(replacement);
    }

    /// <summary>The defaults have to stand on their own, because most outboxes never configure them.</summary>
    [Fact]
    public void An_outbox_that_configures_nothing_still_gets_the_shared_schedule()
    {
        using var provider = Provider(services => services.AddBsftOutbox<string>("primary"));

        provider.GetRequiredService<IOutboxRetrySchedule>().Should().NotBeNull();
    }

    [Fact]
    public void A_missing_argument_is_rejected()
    {
        var withoutServices = () => OutboxServiceCollectionExtensions.AddBsftOutbox<string>(null!, "primary");
        var withoutName = () => new ServiceCollection().AddBsftOutbox<string>(null!);

        withoutServices.Should().Throw<ArgumentNullException>().WithParameterName("services");
        withoutName.Should().Throw<ArgumentNullException>().WithParameterName("name");
    }

    /// <summary>The section carries the outbox's own name, so two outboxes read two sub-keys.</summary>
    [Fact]
    public async Task An_outbox_is_bounded_by_the_section_named_after_it()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{BsftOutboxOptions.SectionName}:primary:BatchSize"] = "7",
            })
            .Build();

        using var provider = Provider(services => services.AddBsftOutbox<string>("primary", configuration));
        var store = (RecordingOutboxStore)provider.GetRequiredService<IOutboxStore<string>>();

        await provider.GetRequiredService<IOutboxDrainer<string>>().DrainOnceAsync();

        store.Leases.Should().ContainSingle().Which.BatchSize.Should().Be(7);
    }

    /// <summary>The action runs after the section, so a host can still override what it read.</summary>
    [Fact]
    public async Task What_the_host_configures_in_code_wins_over_the_section()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{BsftOutboxOptions.SectionName}:primary:BatchSize"] = "7",
            })
            .Build();

        using var provider = Provider(services => services
            .AddBsftOutbox<string>("primary", configuration, options => options.BatchSize = 2));
        var store = (RecordingOutboxStore)provider.GetRequiredService<IOutboxStore<string>>();

        await provider.GetRequiredService<IOutboxDrainer<string>>().DrainOnceAsync();

        store.Leases.Should().ContainSingle().Which.BatchSize.Should().Be(2);
    }

    [Fact]
    public void A_missing_argument_of_the_configuration_overload_is_rejected()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();

        var withoutServices = () =>
            OutboxServiceCollectionExtensions.AddBsftOutbox<string>(null!, "primary", configuration);
        var withoutName = () => new ServiceCollection().AddBsftOutbox<string>(null!, configuration);
        var withoutConfiguration = () => new ServiceCollection().AddBsftOutbox<string>("primary", (IConfiguration)null!);

        withoutServices.Should().Throw<ArgumentNullException>().WithParameterName("services");
        withoutName.Should().Throw<ArgumentNullException>().WithParameterName("name");
        withoutConfiguration.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    /// <summary>A bound that no drain pass could work within must not reach one.</summary>
    [Fact]
    public void An_outbox_bounded_by_nothing_workable_is_refused_before_the_host_starts()
    {
        using var provider = Provider(services =>
            services.AddBsftOutbox<string>("primary", options => options.BatchSize = 0));

        var act = () => provider.GetRequiredService<IOptionsMonitor<BsftOutboxOptions>>().Get("primary");

        act.Should().Throw<OptionsValidationException>()
            .WithMessage($"*{nameof(BsftOutboxOptions.BatchSize)}*");
    }

    private static ServiceProvider Provider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<IOutboxStore<string>>(new RecordingOutboxStore());
        services.AddSingleton<IOutboxSideEffect<string>>(new ScriptedSideEffect());
        services.AddSingleton<IOutboxStore<int>>(new EmptyOutboxStore());
        services.AddSingleton<IOutboxSideEffect<int>>(new CountingSideEffect());

        configure(services);

        return services.BuildServiceProvider();
    }

    /// <summary>A schedule a host registered itself, which the registration must leave alone.</summary>
    private sealed class StubRetrySchedule : IOutboxRetrySchedule
    {
        public TimeSpan NextDelay(int attemptNumber, TimeSpan backoffBase, TimeSpan maxDelay, TimeSpan? retryAfter)
            => TimeSpan.Zero;

        public TimeSpan? RequestedRetryAfter(Exception failure) => null;
    }

    /// <summary>A second outbox's store, present only so a second drainer can be resolved.</summary>
    private sealed class EmptyOutboxStore : IOutboxStore<int>
    {
        public Task<IReadOnlyList<LeasedOutboxRow<int>>> LeaseAsync(
            DateTimeOffset now, TimeSpan leaseDuration, int batchSize, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LeasedOutboxRow<int>>>([]);

        public Task<bool> TryMarkProcessedAsync(
            long id, Guid leaseToken, DateTimeOffset processedAt, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task AbandonAsync(
            long id, Guid leaseToken, DateTimeOffset nextAttemptAt, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeadLetterAsync(
            long id, Guid leaseToken, DateTimeOffset deadLetteredAt, string reason, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    /// <summary>A second outbox's side effect.</summary>
    private sealed class CountingSideEffect : IOutboxSideEffect<int>
    {
        public Task ApplyAsync(int payload, CancellationToken ct = default) => Task.CompletedTask;

        public string Describe(int payload) => $"number {payload}";
    }
}
