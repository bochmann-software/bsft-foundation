using Bsft.EventAggregator.Saga.Execution;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Bsft.EventAggregator.Saga.Tests;

public class SagaBuilderTests
{
    public record StartEvent(string Id);
    public record NextEvent(string Id);

    public class TestState
    {
        public int Counter { get; set; }
    }

    private readonly ISagaEngine _engine = Substitute.For<ISagaEngine>();

    private ISagaBuilderFactory CreateFactory()
    {
        return new SagaBuilderFactory(_engine);
    }

    [Fact]
    public void Complete_ReturnsDefinitionWithAutoComplete()
    {
        var factory = CreateFactory();
        var definition = factory.WithState<TestState>()
            .StartWith<StartEvent>(e => e.Id)
            .Then((ctx, e) => Task.CompletedTask)
            .Complete();

        definition.AutoComplete.Should().BeTrue();
        definition.Steps.Should().HaveCount(1);
        definition.InitialEventType.Should().Be(typeof(StartEvent));
    }

    [Fact]
    public void Build_ReturnsDefinitionWithoutAutoComplete()
    {
        var factory = CreateFactory();
        var definition = factory.WithState<TestState>()
            .StartWith<StartEvent>(e => e.Id)
            .Then((ctx, e) => Task.CompletedTask)
            .Build();

        definition.AutoComplete.Should().BeFalse();
    }

    [Fact]
    public void MultipleSteps_BuildsCorrectly()
    {
        var factory = CreateFactory();
        var builder = factory.WithState<TestState>();

        var definition = builder
            .StartWith<StartEvent>(e => e.Id)
            .Then((ctx, e) => Task.CompletedTask)
            .WaitFor<NextEvent>(e => e.Id)
            .Then((ctx, e) => Task.CompletedTask)
            .Complete();

        definition.Steps.Should().HaveCount(2);
        definition.Steps[0].EventType.Should().Be(typeof(StartEvent));
        definition.Steps[1].EventType.Should().Be(typeof(NextEvent));
    }

    [Fact]
    public void Then_KeepsTheDelegateItWasGivenAsTheStepsAction()
    {
        Func<ISagaContext<TestState>, StartEvent, Task> action = (ctx, e) => Task.CompletedTask;

        var definition = CreateFactory().WithState<TestState>()
            .StartWith<StartEvent>(e => e.Id)
            .Then(action)
            .Build();

        definition.Steps[0].Action.Should().BeSameAs(action);
    }

    [Fact]
    public void When_SetsConditionOnStep()
    {
        var factory = CreateFactory();
        var definition = factory.WithState<TestState>()
            .StartWith<StartEvent>(e => e.Id)
            .When((e, state) => state.Counter > 0, (ctx, e) => Task.CompletedTask)
            .Complete();

        definition.Steps[0].Condition.Should().NotBeNull();
        definition.Steps[0].Condition!.IsNegated.Should().BeFalse();
    }

    [Fact]
    public void WhenNot_SetsNegatedConditionOnStep()
    {
        var factory = CreateFactory();
        var definition = factory.WithState<TestState>()
            .StartWith<StartEvent>(e => e.Id)
            .WhenNot((e, state) => state.Counter > 0, (ctx, e) => Task.CompletedTask)
            .Complete();

        definition.Steps[0].Condition.Should().NotBeNull();
        definition.Steps[0].Condition!.IsNegated.Should().BeTrue();
    }

    [Fact]
    public void Stateless_CreatesBuilderWithEmptyState()
    {
        var factory = CreateFactory();
        var definition = factory.Stateless()
            .StartWith<StartEvent>(e => e.Id)
            .Then((ctx, e) => Task.CompletedTask)
            .Complete();

        definition.SagaType.Should().Contain(nameof(EmptyState));
    }

    [Fact]
    public void SagaType_ContainsStateAndEventNames()
    {
        var factory = CreateFactory();
        var definition = factory.WithState<TestState>()
            .StartWith<StartEvent>(e => e.Id)
            .Then((ctx, e) => Task.CompletedTask)
            .Complete();

        definition.SagaType.Should().Contain(nameof(TestState));
        definition.SagaType.Should().Contain(nameof(StartEvent));
    }

    [Fact]
    public void CorrelationKeySelector_StoredOnStep()
    {
        var factory = CreateFactory();
        var definition = factory.WithState<TestState>()
            .StartWith<StartEvent>(e => e.Id)
            .Then((ctx, e) => Task.CompletedTask)
            .Complete();

        definition.Steps[0].CorrelationKeySelector.Should().NotBeNull();
    }

    [Fact]
    public void StartWith_WithoutCorrelationKeySelector_StoresNull()
    {
        var factory = CreateFactory();
        var definition = factory.WithState<TestState>()
            .StartWith<StartEvent>()
            .Then((ctx, e) => Task.CompletedTask)
            .Complete();

        definition.Steps[0].CorrelationKeySelector.Should().BeNull();
    }

    [Fact]
    public void When_NullPredicate_ThrowsArgumentNullException()
    {
        var factory = CreateFactory();
        var configurator = factory.WithState<TestState>()
            .StartWith<StartEvent>(e => e.Id);

        var act = () => configurator.When(null!, (ctx, e) => Task.CompletedTask);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("predicate");
    }

    [Fact]
    public void When_NullAction_ThrowsArgumentNullException()
    {
        var factory = CreateFactory();
        var configurator = factory.WithState<TestState>()
            .StartWith<StartEvent>(e => e.Id);

        var act = () => configurator.When((e, state) => true, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("action");
    }

    [Fact]
    public void WhenNot_NullPredicate_ThrowsArgumentNullException()
    {
        var factory = CreateFactory();
        var configurator = factory.WithState<TestState>()
            .StartWith<StartEvent>(e => e.Id);

        var act = () => configurator.WhenNot(null!, (ctx, e) => Task.CompletedTask);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("predicate");
    }

    [Fact]
    public void WhenNot_NullAction_ThrowsArgumentNullException()
    {
        var factory = CreateFactory();
        var configurator = factory.WithState<TestState>()
            .StartWith<StartEvent>(e => e.Id);

        var act = () => configurator.WhenNot((e, state) => true, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("action");
    }

    [Fact]
    public void SagaStepConfigurator_Then_NullAction_ThrowsArgumentNullException()
    {
        var factory = CreateFactory();
        var configurator = factory.WithState<TestState>()
            .StartWith<StartEvent>(e => e.Id);

        var act = () => configurator.Then(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("action");
    }

    [Fact]
    public void BuildDefinition_WhenNotOnSameEventType_SharesLogicalStepIndex()
    {
        // Exercises SagaBuilder.BuildDefinition lines 120-121:
        // When/WhenNot on the same event type in consecutive steps share the same logical index.
        // Step 0: StartWith<StartEvent>.When(...) => StartEvent with condition, logical index 0
        // Step 1: WaitFor<StartEvent>.When(...) => StartEvent with condition, same event type => shares logical index 0
        var factory = CreateFactory();
        var definition = factory.WithState<TestState>()
            .StartWith<StartEvent>(e => e.Id)
            .When((e, state) => state.Counter > 0, (ctx, e) => Task.CompletedTask)           // step 0, EventType=StartEvent, condition, index 0
            .WaitFor<StartEvent>(e => e.Id)
            .When((e, state) => state.Counter == 0, (ctx, e) => Task.CompletedTask)          // step 1, EventType=StartEvent, condition => shares index 0
            .Complete();

        // Both steps share logical index 0 since they have same event type and are conditional branches
        definition.Steps.Should().HaveCount(2);
        definition.Steps[0].LogicalStepIndex.Should().Be(0);
        definition.Steps[1].LogicalStepIndex.Should().Be(0);
    }

    [Fact]
    public void BuildDefinition_DifferentEventTypes_IncrementLogicalIndex()
    {
        // Exercises SagaBuilder.BuildDefinition lines 124-125:
        // Different event types (without IsAlternative) increment the logical index.
        var factory = CreateFactory();
        var definition = factory.WithState<TestState>()
            .StartWith<StartEvent>(e => e.Id)
            .Then((ctx, e) => Task.CompletedTask)         // step 0, index 0
            .WaitFor<NextEvent>(e => e.Id)
            .Then((ctx, e) => Task.CompletedTask)          // step 1, different event type => index 1
            .Complete();

        definition.Steps[0].LogicalStepIndex.Should().Be(0);
        definition.Steps[1].LogicalStepIndex.Should().Be(1);
    }

    [Fact]
    public void Or_AddsAlternativeStep()
    {
        // Exercises SagaBuilder.Or lines 60-62: returns a SagaStepConfigurator with isAlternative=true.
        var factory = CreateFactory();
        var definition = factory.WithState<TestState>()
            .StartWith<StartEvent>(e => e.Id)
            .Then((ctx, e) => Task.CompletedTask)
            .WaitFor<NextEvent>(e => e.Id)
            .Then((ctx, e) => Task.CompletedTask)
            .Or<StartEvent>(e => e.Id)           // alternative branch - same logical step as WaitFor<NextEvent>
            .Then((ctx, e) => Task.CompletedTask)
            .Complete();

        definition.Steps.Should().HaveCount(3);
        definition.Steps[2].IsAlternative.Should().BeTrue();
    }

    [Fact]
    public void BuildDefinition_IsAlternative_SharesLogicalIndex()
    {
        // Exercises SagaBuilder.BuildDefinition lines 113-117:
        // IsAlternative=true steps share the same logical index as the previous step.
        var factory = CreateFactory();
        var definition = factory.WithState<TestState>()
            .StartWith<StartEvent>(e => e.Id)
            .Then((ctx, e) => Task.CompletedTask)          // step 0, index 0
            .WaitFor<NextEvent>(e => e.Id)
            .Then((ctx, e) => Task.CompletedTask)           // step 1, index 1
            .Or<StartEvent>(e => e.Id)                     // step 2, IsAlternative=true => shares index 1
            .Then((ctx, e) => Task.CompletedTask)
            .Complete();

        definition.Steps[0].LogicalStepIndex.Should().Be(0);
        definition.Steps[1].LogicalStepIndex.Should().Be(1);
        definition.Steps[2].LogicalStepIndex.Should().Be(1);
    }

    [Fact]
    public void BuildDefinition_WithoutStartWith_ThrowsInvalidOperationException()
    {
        // Exercises SagaBuilder.BuildDefinition line 100: _initialEventType is null.
        // Directly construct a SagaBuilder without calling StartWith, then call Build.
        var factory = new SagaBuilderFactory(_engine);
        var builder = new SagaBuilder<TestState>(_engine, factory);

        // WaitFor adds a step so _steps.Count > 0, but only StartWith sets _initialEventType.
        builder.WaitFor<StartEvent>(e => e.Id).Then((ctx, e) => Task.CompletedTask);

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*StartWith*");
    }

    [Fact]
    public void BuildDefinition_WithoutSteps_ThrowsInvalidOperationException()
    {
        // Exercises SagaBuilder.BuildDefinition line 103: _steps.Count == 0.
        // Call StartWith (sets _initialEventType) but never call Then/When/WhenNot to add a step.
        var factory = new SagaBuilderFactory(_engine);
        var builder = new SagaBuilder<TestState>(_engine, factory);

        // StartWith sets _initialEventType but returns a configurator - no step is added until .Then() is called.
        builder.StartWith<StartEvent>(e => e.Id);
        // Do NOT call Then/When/WhenNot, so _steps stays empty.

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*at least one step*");
    }

}
