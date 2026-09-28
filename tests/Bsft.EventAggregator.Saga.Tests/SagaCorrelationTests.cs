using Bsft.EventAggregator.DependencyInjection;
using Bsft.EventAggregator.Saga.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.EventAggregator.Saga.Tests;

public class SagaCorrelationTests
{
    public record TaskStarted(string TaskId, string AssignedTo);
    public record TaskCompleted(string TaskId);

    public class TaskSagaState
    {
        public string AssignedTo { get; set; } = string.Empty;
        public bool Done { get; set; }
    }

    public class TaskSagaFlow : ISagaFlow
    {
        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<TaskSagaState>()
                .StartWith<TaskStarted>(e => e.TaskId)
                .Then(async (ctx, e) =>
                {
                    ctx.State.AssignedTo = e.AssignedTo;
                    await Task.CompletedTask;
                })
                .WaitFor<TaskCompleted>(e => e.TaskId)
                .Then(async (ctx, e) =>
                {
                    ctx.State.Done = true;
                    await Task.CompletedTask;
                })
                .Complete();
        }
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddBsftEventAggregator();
        services.AddBsftSaga();
        services.AddBsftSagaFlow<TaskSagaFlow>();
        var sp = services.BuildServiceProvider();
        sp.UseBsftSagaFlows();
        return sp;
    }

    [Fact]
    public async Task MultipleSagaInstances_RunIndependently()
    {
        using var sp = BuildProvider();
        var ea = sp.GetRequiredService<IEventAggregator>();
        var store = sp.GetRequiredService<ISagaStore>();

        await ea.PublishAsync(new TaskStarted("task-A", "Alice"));
        await ea.PublishAsync(new TaskStarted("task-B", "Bob"));

        var instanceA = await store.FindAsync<TaskSagaState>("TaskSagaState_TaskStarted", "task-A");
        var instanceB = await store.FindAsync<TaskSagaState>("TaskSagaState_TaskStarted", "task-B");

        instanceA.Should().NotBeNull();
        instanceA!.State.AssignedTo.Should().Be("Alice");
        instanceB.Should().NotBeNull();
        instanceB!.State.AssignedTo.Should().Be("Bob");

        // Complete only task-A
        await ea.PublishAsync(new TaskCompleted("task-A"));

        instanceA = await store.FindAsync<TaskSagaState>("TaskSagaState_TaskStarted", "task-A");
        instanceB = await store.FindAsync<TaskSagaState>("TaskSagaState_TaskStarted", "task-B");

        // task-A should be removed (auto-complete)
        instanceA.Should().BeNull();
        // task-B should still be at step 1
        instanceB.Should().NotBeNull();
        instanceB!.CurrentStepIndex.Should().Be(1);
    }

    [Fact]
    public async Task WrongCorrelationKey_DoesNotAdvanceSaga()
    {
        using var sp = BuildProvider();
        var ea = sp.GetRequiredService<IEventAggregator>();
        var store = sp.GetRequiredService<ISagaStore>();

        await ea.PublishAsync(new TaskStarted("task-A", "Alice"));

        // Complete with a wrong correlation key
        await ea.PublishAsync(new TaskCompleted("task-X"));

        var instanceA = await store.FindAsync<TaskSagaState>("TaskSagaState_TaskStarted", "task-A");
        instanceA.Should().NotBeNull();
        instanceA!.CurrentStepIndex.Should().Be(1);
    }
}
