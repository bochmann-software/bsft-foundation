using Bsft.Correlation;
using Bsft.EventAggregator.DependencyInjection;
using Bsft.EventAggregator.Saga.Correlation.DependencyInjection;
using Bsft.EventAggregator.Saga.DependencyInjection;
using Bsft.EventAggregator.Saga.Timeouts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.EventAggregator.Saga.Correlation.Tests;

/// <summary>
/// <see cref="ISagaEngine.StartSagaAsync"/> is the public way into a saga that no event triggers. The
/// engine is a singleton holding the root provider, so the trigger step used to run against the root
/// provider itself: resolving the scoped correlation accessor from there throws outright under the
/// scope validation an ASP.NET Core Development host switches on, and captures one process-lifetime
/// instance when it is off. These tests pin the direct-start path against both.
/// </summary>
public class DirectSagaStartCorrelationTests
{
    public record OrderPlaced(string OrderId);

    public record RemindMe(string OrderId);

    public class OrderState;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ADevelopmentHostCanStartASagaThatSchedulesATimeout(bool scopePerStep)
    {
        // ValidateScopes is what ASP.NET Core turns on in Development. Before the fix the default
        // setting could not start such a saga at all: capturing the correlation id resolved the
        // TryAddScoped accessor from the root provider and threw InvalidOperationException.
        using var provider = BuildProvider(scopePerStep);

        var act = async () => await StartAsync(provider, "order-1");

        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AScheduledTimeoutCarriesTheCallersAmbientCorrelationId(bool scopePerStep)
    {
        using var provider = BuildProvider(scopePerStep);

        // What SagaCorrelationMiddleware does for the duration of a request.
        using (Ambient(provider).Use("request-7"))
        {
            await StartAsync(provider, "order-2");
        }

        (await CorrelationIdOfScheduledTimeoutAsync(provider)).Should().Be("request-7");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EachCallerScopeStampsItsOwnCorrelationId(bool scopePerStep)
    {
        // The point of the finding: a single process-lifetime accessor would stamp every timeout
        // with whichever id happened to be ambient the first time one was captured.
        using var provider = BuildProvider(scopePerStep);

        using (Ambient(provider).Use("request-7"))
        {
            await StartAsync(provider, "order-3");
        }

        using (Ambient(provider).Use("request-8"))
        {
            await StartAsync(provider, "order-4");
        }

        var scheduled = await ScheduledAsync(provider);

        scheduled.Single(t => t.CorrelationKey == "order-3").CorrelationId.Should().Be("request-7");
        scheduled.Single(t => t.CorrelationKey == "order-4").CorrelationId.Should().Be("request-8");
    }

    private static async Task StartAsync(IServiceProvider provider, string orderId)
    {
        var factory = provider.GetRequiredService<ISagaBuilderFactory>();

        var definition = factory.WithState<OrderState>()
            .StartWith<OrderPlaced>(e => e.OrderId)
            .Then((ctx, e) =>
            {
                ctx.ScheduleTimeout("remind", TimeSpan.FromMinutes(5), new RemindMe(e.OrderId));
                return Task.CompletedTask;
            })
            // Build, not Complete: an auto-completing saga would finish on its trigger step and the
            // commit would cancel the very timer under test along with it.
            .Build();

        await provider.GetRequiredService<ISagaEngine>()
            .StartSagaAsync(definition, orderId, new OrderPlaced(orderId));
    }

    private static async Task<string?> CorrelationIdOfScheduledTimeoutAsync(IServiceProvider provider) =>
        (await ScheduledAsync(provider)).Single().CorrelationId;

    private static async Task<IReadOnlyList<SagaTimeout>> ScheduledAsync(IServiceProvider provider) =>
        await provider.GetRequiredService<ISagaTimeoutStore>()
            .ListDueAsync(DateTimeOffset.UtcNow.AddYears(1), batchSize: 100);

    private static ISagaCorrelationScope Ambient(IServiceProvider provider) =>
        provider.GetRequiredService<ISagaCorrelationScope>();

    private static ServiceProvider BuildProvider(bool scopePerStep)
    {
        var services = new ServiceCollection();
        services.AddBsftEventAggregator();

        // Mirrors AddBsftCorrelation: one scoped context per scope, minting its own id by default.
        services.AddScoped<CorrelationContext>();
        services.AddScoped<ICorrelationContext>(sp => sp.GetRequiredService<CorrelationContext>());

        services.AddBsftSaga(options => options.ShouldCreateScopePerStep = scopePerStep);
        services.AddBsftSagaCorrelation();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
