using Bsft.Messaging.EFCore.Postgres;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Messaging.EFCore.Postgres.Tests;

public class InProcessOutboxWakeTests
{
    private InProcessOutboxWake CreateWake() => new();

    [Fact]
    public async Task ListenAsync_ImmediatelyOnStart_CallsOnNotified()
    {
        var calls = 0;
        var wake = CreateWake();
        using var cts = new CancellationTokenSource();

        var task = Task.Run(async () =>
        {
            await wake.ListenAsync(async ct =>
            {
                calls++;
                if (calls >= 1)
                    cts.Cancel();
            }, cts.Token);
        });

        var act = async () => await task;
        await act.Should().ThrowAsync<OperationCanceledException>();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task ListenAsync_AfterNotify_CallsOnNotified()
    {
        var calls = 0;
        var wake = CreateWake();
        using var cts = new CancellationTokenSource();

        var task = Task.Run(async () =>
        {
            await wake.ListenAsync(async ct =>
            {
                calls++;
                if (calls >= 2)
                    cts.Cancel();
            }, cts.Token);
        });

        // The wake queues a notification that arrives before the listener has subscribed, so no wait is needed.
        var context = new DbContextOptionsBuilder().Options;
        var dbContext = new DbContext(context);
        wake.Notify(dbContext);

        var act = async () => await task;
        await act.Should().ThrowAsync<OperationCanceledException>();
        calls.Should().Be(2);
    }

    [Fact]
    public async Task ListenAsync_WithMultipleNotifyCalls_Coalesces()
    {
        var calls = 0;
        var wake = CreateWake();
        using var cts = new CancellationTokenSource();

        var task = Task.Run(async () =>
        {
            await wake.ListenAsync(async ct =>
            {
                calls++;
                if (calls >= 2)
                    cts.Cancel();
            }, cts.Token);
        });

        // The wake queues a notification that arrives before the listener has subscribed, so no wait is needed.
        var context = new DbContextOptionsBuilder().Options;
        var dbContext = new DbContext(context);

        wake.Notify(dbContext);
        wake.Notify(dbContext);
        wake.Notify(dbContext);

        var act = async () => await task;
        await act.Should().ThrowAsync<OperationCanceledException>();
        calls.Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task NotifyAsync_Always_ReturnsCompletedTask()
    {
        var calls = 0;
        var wake = CreateWake();
        using var cts = new CancellationTokenSource();

        var task = Task.Run(async () =>
        {
            await wake.ListenAsync(async ct =>
            {
                calls++;
                if (calls >= 2)
                    cts.Cancel();
            }, cts.Token);
        });

        // The wake queues a notification that arrives before the listener has subscribed, so no wait is needed.
        var context = new DbContextOptionsBuilder().Options;
        var dbContext = new DbContext(context);

        var notifyTask = wake.NotifyAsync(dbContext, CancellationToken.None);
        notifyTask.Status.Should().Be(TaskStatus.RanToCompletion);

        var act = async () => await task;
        await act.Should().ThrowAsync<OperationCanceledException>();
        calls.Should().Be(2);
    }

    [Fact]
    public void Notify_WithNullContext_ThrowsArgumentNullException()
    {
        var wake = CreateWake();

        var act = () => wake.Notify(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("context");
    }

    [Fact]
    public async Task NotifyAsync_WithNullContext_ThrowsArgumentNullException()
    {
        var wake = CreateWake();

        var act = async () => await wake.NotifyAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithMessage("*context*");
    }

    [Fact]
    public async Task ListenAsync_WithNullOnNotified_ThrowsArgumentNullException()
    {
        var wake = CreateWake();

        var act = async () => await wake.ListenAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithMessage("*onNotified*");
    }

    [Fact]
    public async Task ListenAsync_WhenCancelled_EndsWithOperationCanceledException()
    {
        var wake = CreateWake();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await wake.ListenAsync(async ct => { }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
