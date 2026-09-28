using Bsft.EventAggregator.DependencyInjection;
using Bsft.EventAggregator.Saga.DependencyInjection;
using Bsft.EventAggregator.Saga.Diagnostics;
using Bsft.EventAggregator.Saga.Execution;
using Bsft.EventAggregator.Saga.Persistence;
using Bsft.EventAggregator.Saga.Timeouts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.EventAggregator.Saga.Tests.Persistence;

/// <summary>
/// The one commit path both entry points into a saga step go through — the event-driven flow host and
/// the direct StartSagaAsync API. Its behaviour is exercised through those two; what is pinned here
/// is that it rejects the arguments it cannot act on.
/// </summary>
public class SagaStepCommitterTests
{
    public class QuoteState
    {
        public bool Accepted { get; set; }
    }

    [Fact]
    public void Constructor_RejectsEachNullCollaborator()
    {
        using var provider = BuildProvider();

        var store = provider.GetRequiredService<ISagaStore>();
        var telemetry = provider.GetRequiredService<ISagaTelemetry>();
        var timeouts = provider.GetRequiredService<ISagaTimeoutScheduler>();
        var persistence = provider.GetRequiredService<ISagaPersistenceWindow>();
        var instances = provider.GetRequiredService<ISagaInstanceRegistry>();

        FluentActions.Invoking(() => new SagaStepCommitter(null!, telemetry, timeouts, persistence, instances))
            .Should().Throw<ArgumentNullException>().WithParameterName("store");
        FluentActions.Invoking(() => new SagaStepCommitter(store, null!, timeouts, persistence, instances))
            .Should().Throw<ArgumentNullException>().WithParameterName("telemetry");
        FluentActions.Invoking(() => new SagaStepCommitter(store, telemetry, null!, persistence, instances))
            .Should().Throw<ArgumentNullException>().WithParameterName("timeouts");
        FluentActions.Invoking(() => new SagaStepCommitter(store, telemetry, timeouts, null!, instances))
            .Should().Throw<ArgumentNullException>().WithParameterName("persistence");
        FluentActions.Invoking(() => new SagaStepCommitter(store, telemetry, timeouts, persistence, null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("instances");
    }

    [Fact]
    public async Task CommitAsync_RejectsItsNullArguments()
    {
        using var provider = BuildProvider();
        var committer = Committer(provider);
        var definition = BuildDefinition(provider);
        var instance = new SagaInstance<QuoteState>(definition.SagaType, "q-1", new QuoteState());

        await FluentActions
            .Awaiting(() => committer.CommitAsync<QuoteState>(null!, instance, [], null))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("definition");

        await FluentActions
            .Awaiting(() => committer.CommitAsync(definition, null!, Array.Empty<PendingSagaTimeout>(), null))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("instance");

        await FluentActions
            .Awaiting(() => committer.CommitAsync(definition, instance, null!, null))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("timeouts");
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddBsftEventAggregator();
        services.AddBsftSaga();
        return services.BuildServiceProvider();
    }

    private static SagaDefinition<QuoteState> BuildDefinition(IServiceProvider provider)
    {
        var factory = new SagaBuilderFactory(provider.GetRequiredService<ISagaEngine>());

        return factory.WithState<QuoteState>()
            .StartWith<QuoteAccepted>(e => e.QuoteId)
            .Then((ctx, _) =>
            {
                ctx.State.Accepted = true;
                return Task.CompletedTask;
            })
            .Build();
    }

    public record QuoteAccepted(string QuoteId);

    /// <summary>Builds a committer from the collaborators the provider was set up with.</summary>
    /// <param name="provider">The container the saga package was registered into.</param>
    /// <returns>A committer wired exactly as the composition root wires it.</returns>
    private static SagaStepCommitter Committer(IServiceProvider provider) =>
        new(
            provider.GetRequiredService<ISagaStore>(),
            provider.GetRequiredService<ISagaTelemetry>(),
            provider.GetRequiredService<ISagaTimeoutScheduler>(),
            provider.GetRequiredService<ISagaPersistenceWindow>(),
            provider.GetRequiredService<ISagaInstanceRegistry>());
}
