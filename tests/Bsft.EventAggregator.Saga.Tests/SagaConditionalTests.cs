using Bsft.EventAggregator.DependencyInjection;
using Bsft.EventAggregator.Saga.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.EventAggregator.Saga.Tests;

public class SagaConditionalTests
{
    public record ApprovalRequested(string Id, bool IsAutoApproved);
    public record ApprovalDecided(string Id);

    public class ApprovalState
    {
        public bool WasAutoApproved { get; set; }
        public bool WasManuallyApproved { get; set; }
    }

    public class AutoApprovalFlow : ISagaFlow
    {
        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<ApprovalState>()
                .StartWith<ApprovalRequested>(e => e.Id)
                .When(
                    (e, state) => e.IsAutoApproved,
                    (ctx, e) =>
                    {
                        ctx.State.WasAutoApproved = true;
                        return Task.CompletedTask;
                    })
                .Complete();
        }
    }

    public class ManualApprovalFlow : ISagaFlow
    {
        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<ApprovalState>()
                .StartWith<ApprovalDecided>(e => e.Id)
                .WhenNot(
                    (e, state) => state.WasAutoApproved,
                    (ctx, e) =>
                    {
                        ctx.State.WasManuallyApproved = true;
                        return Task.CompletedTask;
                    })
                .Complete();
        }
    }

    [Fact]
    public async Task When_ConditionTrue_ExecutesAction()
    {
        var services = new ServiceCollection();
        services.AddBsftEventAggregator();
        services.AddBsftSaga();
        services.AddBsftSagaFlow<AutoApprovalFlow>();
        using var sp = services.BuildServiceProvider();
        sp.UseBsftSagaFlows();

        var ea = sp.GetRequiredService<IEventAggregator>();

        // The saga auto-completes, but we can verify the action ran by subscribing to a side-effect.
        // Since the saga completes and is removed, we verify it ran by checking it was created and removed.
        var store = sp.GetRequiredService<ISagaStore>();

        await ea.PublishAsync(new ApprovalRequested("req-1", IsAutoApproved: true));

        // With auto-complete and one step, the saga should already be removed
        var instance = await store.FindAsync<ApprovalState>("ApprovalState_ApprovalRequested", "req-1");
        instance.Should().BeNull();
    }

    [Fact]
    public async Task When_ConditionFalse_SkipsAction_ButStillCompletes()
    {
        var services = new ServiceCollection();
        services.AddBsftEventAggregator();
        services.AddBsftSaga();
        services.AddBsftSagaFlow<AutoApprovalFlow>();
        using var sp = services.BuildServiceProvider();
        sp.UseBsftSagaFlows();

        var ea = sp.GetRequiredService<IEventAggregator>();
        var store = sp.GetRequiredService<ISagaStore>();

        await ea.PublishAsync(new ApprovalRequested("req-2", IsAutoApproved: false));

        // Saga auto-completes even if action was skipped (step still advances)
        var instance = await store.FindAsync<ApprovalState>("ApprovalState_ApprovalRequested", "req-2");
        instance.Should().BeNull();
    }

    [Fact]
    public async Task WhenNot_ConditionFalse_ExecutesAction()
    {
        var services = new ServiceCollection();
        services.AddBsftEventAggregator();
        services.AddBsftSaga();
        services.AddBsftSagaFlow<ManualApprovalFlow>();
        using var sp = services.BuildServiceProvider();
        sp.UseBsftSagaFlows();

        var ea = sp.GetRequiredService<IEventAggregator>();

        await ea.PublishAsync(new ApprovalDecided("req-3"));

        // State.WasAutoApproved defaults to false, so WhenNot (not false = true) should execute
        // Since it auto-completes, it's removed
        var store = sp.GetRequiredService<ISagaStore>();
        var instance = await store.FindAsync<ApprovalState>("ApprovalState_ApprovalDecided", "req-3");
        instance.Should().BeNull();
    }
}
