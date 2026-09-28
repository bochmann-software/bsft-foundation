using Bsft.Messaging.EFCore.Postgres.Tests.Fakes;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Messaging.EFCore.Postgres.Tests;

public class OutboxNotificationWorkerTests
{
    private readonly RecordingLogger<OutboxNotificationWorker> _logger = new();

    private static IOptions<BsftMessagingPostgresOptions> Options(TimeSpan? reconnectDelay = null) =>
        Microsoft.Extensions.Options.Options.Create(new BsftMessagingPostgresOptions
        {
            ReconnectDelay = reconnectDelay ?? TimeSpan.FromMilliseconds(1),
        });

    private OutboxNotificationWorker Worker(
        FakeNotificationChannel channel, ScriptedOutboxProcessor processor, TimeSpan? reconnectDelay = null) =>
        new(channel, processor, Options(reconnectDelay), _logger);

    [Fact]
    public async Task RunLoopAsync_ANotification_DrainsUntilAPassDeliversNothing()
    {
        // One notification can stand behind more messages than a batch holds, and a pass claims at most
        // one batch. Stopping after the first pass would leave the rest to the very poll this exists to
        // get ahead of.
        using var stop = new CancellationTokenSource();

        var processor = new ScriptedOutboxProcessor(3, 2, 0);
        var channel = new FakeNotificationChannel(endOfSession: _ =>
        {
            stop.Cancel();

            return null;
        });

        await Worker(channel, processor).RunLoopAsync(stop.Token);

        processor.Passes.Should().Be(3);
    }

    [Fact]
    public async Task RunLoopAsync_ASessionThatDropped_IsReconnected()
    {
        // A listener that gave up on its first dropped connection would leave the host on polling for
        // the rest of its life, and nothing about the outbox would look wrong.
        using var stop = new CancellationTokenSource();

        var processor = new ScriptedOutboxProcessor();
        var channel = new FakeNotificationChannel(
            notificationsPerSession: 0,
            endOfSession: session =>
            {
                if (session < 3)
                    return new InvalidOperationException("the connection was closed by the server");

                stop.Cancel();

                return null;
            });

        await Worker(channel, processor).RunLoopAsync(stop.Token);

        channel.Sessions.Should().Be(3);
        _logger.Levels.Should().Contain(LogLevel.Error);
        _logger.Messages.Should().Contain(message => message.Contains("lost its session", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunLoopAsync_ASessionCancelledByNobodysStop_IsReconnectedRatherThanTakenForAShutdown()
    {
        // A command timeout on the listening connection cancels the session with a token of its own.
        // Reading that as a shutdown would leave the host running with no listener and no complaint.
        using var stop = new CancellationTokenSource();

        var processor = new ScriptedOutboxProcessor();
        var channel = new FakeNotificationChannel(
            notificationsPerSession: 0,
            endOfSession: session =>
            {
                if (session < 2)
                    return new OperationCanceledException();

                stop.Cancel();

                return null;
            });

        await Worker(channel, processor).RunLoopAsync(stop.Token);

        channel.Sessions.Should().Be(2);
        _logger.Messages.Should().Contain(message =>
            message.Contains("was canceled although the worker was not asked to stop", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheWorker_RunsAsAHostedServiceAndStopsWithTheHost()
    {
        var processor = new ScriptedOutboxProcessor();
        var channel = new FakeNotificationChannel(notificationsPerSession: 0);

        IHostedService worker = Worker(channel, processor, TimeSpan.FromMinutes(5));

        await worker.StartAsync(CancellationToken.None);
        // Since Microsoft.Extensions.Hosting 10, StartAsync runs the whole of ExecuteAsync on the
        // thread pool, so a stop right after the start can come before the first session. The test
        // waits for it rather than relying on the synchronous start of earlier versions.
        await channel.FirstSessionStarted.WaitAsync(TimeSpan.FromSeconds(10));
        await worker.StopAsync(CancellationToken.None);

        channel.Sessions.Should().BeGreaterThan(0);
        _logger.Messages.Should().Contain("Outbox notification listener stopping.");
    }

    [Fact]
    public void Constructor_RejectsMissingCollaborators()
    {
        var channel = new FakeNotificationChannel();
        var processor = new ScriptedOutboxProcessor();

        var missing = new List<Func<OutboxNotificationWorker>>
        {
            () => new OutboxNotificationWorker(null!, processor, Options(), _logger),
            () => new OutboxNotificationWorker(channel, null!, Options(), _logger),
            () => new OutboxNotificationWorker(channel, processor, null!, _logger),
            () => new OutboxNotificationWorker(channel, processor, Options(), null!),
        };

        foreach (var construct in missing)
            construct.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ListenAsync_RejectsAMissingCallback()
    {
        var act = async () => await new FakeNotificationChannel().ListenAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
