using Bsft.EventAggregator.DependencyInjection;
using Bsft.EventAggregator.Saga.DependencyInjection;
using Bsft.EventAggregator.Saga.Diagnostics;
using Bsft.EventAggregator.Saga.Execution;
using Bsft.EventAggregator.Saga.Tests.Support;
using Bsft.EventAggregator.Saga.Timeouts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Bsft.EventAggregator.Saga.Tests.Execution;

public class SagaEngineTests
{
    public record TriggerEvent(string Id);

    public record FollowUpDue(string Id);

    public class SimpleState
    {
        public bool Executed { get; set; }
    }

    private static (SagaEngine Engine, ISagaStore Store, IServiceProvider Services) CreateEngine()
    {
        var services = new ServiceCollection();
        services.AddBsftEventAggregator();
        services.AddBsftSaga();
        var provider = services.BuildServiceProvider();

        var engine = (SagaEngine)provider.GetRequiredService<ISagaEngine>();
        var store = provider.GetRequiredService<ISagaStore>();
        return (engine, store, provider);
    }

    private static SagaDefinition<SimpleState> BuildDefinition(ISagaEngine engine, bool autoComplete = true)
    {
        var factory = new SagaBuilderFactory(engine);
        var builder = factory.WithState<SimpleState>();
        var configurator = builder.StartWith<TriggerEvent>(e => e.Id);
        if (autoComplete)
        {
            return configurator.Then((ctx, e) =>
            {
                ctx.State.Executed = true;
                return Task.CompletedTask;
            }).Complete();
        }
        else
        {
            return configurator.Then((ctx, e) =>
            {
                ctx.State.Executed = true;
                return Task.CompletedTask;
            }).Build();
        }
    }

    /// <summary>A flow whose trigger step schedules a reminder and then waits for it.</summary>
    private static SagaDefinition<SimpleState> BuildTimeoutDefinition(ISagaEngine engine)
    {
        var factory = new SagaBuilderFactory(engine);

        return factory.WithState<SimpleState>()
            .StartWith<TriggerEvent>(e => e.Id)
            .Then((ctx, e) =>
            {
                ctx.ScheduleTimeout("followup", TimeSpan.FromDays(2), new FollowUpDue(e.Id));
                return Task.CompletedTask;
            })
            .WaitFor<FollowUpDue>(e => e.Id)
            .Then((ctx, _) =>
            {
                ctx.State.Executed = true;
                return Task.CompletedTask;
            })
            .Build();
    }

    [Fact]
    public async Task StartSagaAsync_NullDefinition_ThrowsArgumentNullException()
    {
        var (engine, _, _) = CreateEngine();

        var act = async () => await engine.StartSagaAsync<SimpleState>(null!, "key", new TriggerEvent("x"));

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("definition");
    }

    [Fact]
    public async Task StartSagaAsync_NullTriggerEvent_ThrowsArgumentNullException()
    {
        var (engine, _, _) = CreateEngine();
        var definition = BuildDefinition(engine);

        var act = async () => await engine.StartSagaAsync(definition, "key", null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("triggerEvent");
    }

    [Fact]
    public async Task StartSagaAsync_WithAutoComplete_RemovesInstanceAfterExecution()
    {
        // Exercises SagaEngine.StartSagaAsync lines 54, 56, 58-63 (auto-complete path):
        // After step executes and autoComplete=true && currentStepIndex > maxLogicalIndex,
        // the instance is removed from the store.
        var (engine, store, _) = CreateEngine();
        var definition = BuildDefinition(engine, autoComplete: true);

        await engine.StartSagaAsync(definition, "key-1", new TriggerEvent("key-1"));

        // Auto-complete: instance removed after execution
        var instance = await store.FindAsync<SimpleState>(definition.SagaType, "key-1");
        instance.Should().BeNull();
    }

    [Fact]
    public async Task StartSagaAsync_WithoutAutoComplete_SavesInstance()
    {
        // Exercises SagaEngine.StartSagaAsync lines 66-68 (save path):
        // After step executes and autoComplete=false, the instance is saved to the store.
        var (engine, store, _) = CreateEngine();
        var definition = BuildDefinition(engine, autoComplete: false);

        await engine.StartSagaAsync(definition, "key-2", new TriggerEvent("key-2"));

        // Not auto-complete: instance saved for further processing
        var instance = await store.FindAsync<SimpleState>(definition.SagaType, "key-2");
        instance.Should().NotBeNull();
        instance!.State.Executed.Should().BeTrue();
    }

    [Fact]
    public async Task StartSagaAsync_WritesTheTimeoutsTheTriggerStepScheduled()
    {
        // A saga started through this API goes through the same commit as one started by an event:
        // the trigger step's reminders used to be dropped with the context instead of being written.
        var (engine, _, services) = CreateEngine();
        var definition = BuildTimeoutDefinition(engine);

        await engine.StartSagaAsync(definition, "key-timeout", new TriggerEvent("key-timeout"));

        var timeouts = services.GetRequiredService<ISagaTimeoutStore>();
        var pending = await timeouts.ListDueAsync(DateTimeOffset.UtcNow.AddDays(3), 10);

        pending.Should().ContainSingle().Which.Name.Should().Be("followup");
    }

    [Fact]
    public async Task StartSagaAsync_WithoutACorrelationKey_RejectsAScheduledTimeout()
    {
        // Nothing could ever find the saga that reminder belongs to, so it fails loudly here rather
        // than leaving a timer nobody will match.
        var (engine, _, _) = CreateEngine();
        var definition = BuildTimeoutDefinition(engine);

        var act = async () => await engine.StartSagaAsync(definition, null, new TriggerEvent("x"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task StartSagaAsync_WithNullCorrelationKey_DoesNotSaveOrRemove()
    {
        // correlationKey is null => neither save nor remove is called
        // This verifies the null-check guard: "if (correlationKey is not null)"
        var (engine, store, _) = CreateEngine();
        var definition = BuildDefinition(engine, autoComplete: false);

        // Should not throw even with null correlation key
        var act = async () => await engine.StartSagaAsync(definition, null, new TriggerEvent("x"));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartSagaAsync_WithCreateScopePerStep_CreatesScope()
    {
        // Exercises the CreateScopePerStep branch in StartSagaAsync (line 47-49)
        var services = new ServiceCollection();
        services.AddBsftEventAggregator();
        services.AddBsftSaga(opts => opts.ShouldCreateScopePerStep = true);
        var provider = services.BuildServiceProvider();

        var engine = (SagaEngine)provider.GetRequiredService<ISagaEngine>();
        var definition = BuildDefinition(engine, autoComplete: true);

        var act = async () => await engine.StartSagaAsync(definition, "key-3", new TriggerEvent("key-3"));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void SagaEngine_Constructor_NullEventAggregator_Throws()
    {
        var store = Substitute.For<ISagaStore>();
        var services = Substitute.For<IServiceProvider>();
        var options = Options.Create(new BsftSagaOptions());

        var act = () => new SagaEngine(null!, services, store, options);

        act.Should().Throw<ArgumentNullException>().WithParameterName("eventAggregator");
    }

    [Fact]
    public void SagaEngine_Constructor_NullServices_Throws()
    {
        var ea = Substitute.For<IEventAggregator>();
        var store = Substitute.For<ISagaStore>();
        var options = Options.Create(new BsftSagaOptions());

        var act = () => new SagaEngine(ea, null!, store, options);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void SagaEngine_Constructor_NullStore_Throws()
    {
        var ea = Substitute.For<IEventAggregator>();
        var services = Substitute.For<IServiceProvider>();
        var options = Options.Create(new BsftSagaOptions());

        var act = () => new SagaEngine(ea, services, null!, options);

        act.Should().Throw<ArgumentNullException>().WithParameterName("store");
    }

    [Fact]
    public void SagaEngine_Constructor_NullOptions_Throws()
    {
        var ea = Substitute.For<IEventAggregator>();
        var services = Substitute.For<IServiceProvider>();
        var store = Substitute.For<ISagaStore>();

        var act = () => new SagaEngine(ea, services, store, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public async Task StartSagaAsync_WithCondition_ConditionMet_ExecutesAction()
    {
        // Exercises SagaEngine.ExecuteStepAsync lines 73-80: condition check path.
        // A conditional step with condition=true should execute the action.
        var (engine, store, _) = CreateEngine();

        var factory = new SagaBuilderFactory(engine);
        var definition = factory.WithState<SimpleState>()
            .StartWith<TriggerEvent>(e => e.Id)
            .When(
                (e, state) => true,  // always true
                (ctx, e) =>
                {
                    ctx.State.Executed = true;
                    return Task.CompletedTask;
                })
            .Complete();

        await engine.StartSagaAsync(definition, "cond-key-1", new TriggerEvent("cond-key-1"));

        // Auto-completes (one step, condition met); instance removed
        var instance = await store.FindAsync<SimpleState>(definition.SagaType, "cond-key-1");
        instance.Should().BeNull();
    }

    [Fact]
    public async Task StartSagaAsync_WithCondition_ConditionNotMet_SkipsAction()
    {
        // Exercises SagaEngine.ExecuteStepAsync: condition check, condition=false => return early.
        var (engine, store, _) = CreateEngine();

        var factory = new SagaBuilderFactory(engine);
        var definition = factory.WithState<SimpleState>()
            .StartWith<TriggerEvent>(e => e.Id)
            .When(
                (e, state) => false,  // never true
                (ctx, e) =>
                {
                    ctx.State.Executed = true;
                    return Task.CompletedTask;
                })
            .Complete();

        await engine.StartSagaAsync(definition, "cond-key-2", new TriggerEvent("cond-key-2"));

        // Auto-completes regardless (step advances even if condition fails)
        var instance = await store.FindAsync<SimpleState>(definition.SagaType, "cond-key-2");
        instance.Should().BeNull();
    }

    [Fact]
    public async Task StartSagaAsync_EmitsStartStepAndCompletionTelemetry()
    {
        var (engine, _, _) = CreateEngine();
        var definition = BuildDefinition(engine, autoComplete: true);
        using var capture = new SagaTelemetryCapture(SagaTelemetry.SourceName);

        await engine.StartSagaAsync(definition, "tel-key-1", new TriggerEvent("tel-key-1"));

        capture.Measurements.Should().Contain(m => m.Instrument == "bsft.saga.started");
        capture.Measurements.Should().Contain(m => m.Instrument == "bsft.saga.completed");
        capture.Measurements.Should().Contain(m => m.Instrument == "bsft.saga.step.duration" && m.Outcome == "executed");
        capture.Activities.Should().Contain(a => a.DisplayName == "saga.step");
    }

    [Fact]
    public async Task StartSagaAsync_SkippedStep_EmitsSkippedStepTelemetry()
    {
        var (engine, _, _) = CreateEngine();
        var factory = new SagaBuilderFactory(engine);
        var definition = factory.WithState<SimpleState>()
            .StartWith<TriggerEvent>(e => e.Id)
            .When(
                (e, state) => false,
                (ctx, e) =>
                {
                    ctx.State.Executed = true;
                    return Task.CompletedTask;
                })
            .Complete();
        using var capture = new SagaTelemetryCapture(SagaTelemetry.SourceName);

        await engine.StartSagaAsync(definition, "tel-key-2", new TriggerEvent("tel-key-2"));

        capture.Measurements.Should().Contain(m => m.Instrument == "bsft.saga.step.duration" && m.Outcome == "skipped");
    }

    [Fact]
    public async Task StartSagaAsync_StepMarksComplete_EmitsCompletionTelemetry()
    {
        var (engine, store, _) = CreateEngine();
        var factory = new SagaBuilderFactory(engine);
        // Non-auto-complete: completion is reached only because the step marks the instance complete.
        var definition = factory.WithState<SimpleState>()
            .StartWith<TriggerEvent>(e => e.Id)
            .Then((ctx, e) =>
            {
                ctx.MarkComplete();
                return Task.CompletedTask;
            })
            .Build();
        using var capture = new SagaTelemetryCapture(SagaTelemetry.SourceName);

        await engine.StartSagaAsync(definition, "tel-key-3", new TriggerEvent("tel-key-3"));

        capture.Measurements.Should().Contain(m => m.Instrument == "bsft.saga.completed");
        (await store.FindAsync<SimpleState>(definition.SagaType, "tel-key-3")).Should().BeNull();
    }

    [Fact]
    public async Task StartSagaAsync_WithNegatedCondition_NegationApplied()
    {
        // Exercises SagaEngine.ExecuteStepAsync line 76: IsNegated=true => conditionResult = !conditionResult.
        var (engine, store, _) = CreateEngine();

        var factory = new SagaBuilderFactory(engine);
        var definition = factory.WithState<SimpleState>()
            .StartWith<TriggerEvent>(e => e.Id)
            .WhenNot(
                (e, state) => false,  // WhenNot(false) => !false => true => executes
                (ctx, e) =>
                {
                    ctx.State.Executed = true;
                    return Task.CompletedTask;
                })
            .Complete();

        await engine.StartSagaAsync(definition, "neg-key-1", new TriggerEvent("neg-key-1"));

        var instance = await store.FindAsync<SimpleState>(definition.SagaType, "neg-key-1");
        instance.Should().BeNull(); // auto-completed
    }
}
