using Bsft.Tests.Shared;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Bsft.Outbox.Postgres.Tests;

/// <summary>
/// Tests the listening session against a real server: the guards need none, the rest runs on the server
/// <c>BSFT_TEST_POSTGRES</c> names and is skipped without one.
/// </summary>
public sealed class PostgresListenSessionTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly PostgresListenSession _session = new();

    [Fact]
    public async Task ListenAsync_WithoutAConnectionString_Throws()
    {
        var act = () => _session.ListenAsync(null!, new PostgresNotificationChannel("orders_outbox"), _ => Task.CompletedTask);

        (await act.Should().ThrowAsync<ArgumentNullException>()).WithParameterName("connectionString");
    }

    [Fact]
    public async Task ListenAsync_WithTheDefaultChannel_Throws()
    {
        var act = () => _session.ListenAsync("Host=localhost", default, _ => Task.CompletedTask);

        (await act.Should().ThrowAsync<ArgumentException>()).WithParameterName("channel");
    }

    [Fact]
    public async Task ListenAsync_WithoutACallback_Throws()
    {
        var act = () => _session.ListenAsync("Host=localhost", new PostgresNotificationChannel("orders_outbox"), null!);

        (await act.Should().ThrowAsync<ArgumentNullException>()).WithParameterName("onNotified");
    }

    [PostgresFact]
    public async Task ListenAsync_WhenTheSubscriptionIsInPlace_CallsBackOnceForWhatWasWrittenBefore()
    {
        using var stop = new CancellationTokenSource();
        var calls = new Calls();
        var listening = _session.ListenAsync(ConnectionString(), new PostgresNotificationChannel("bsft_outbox_it_first"), calls.Record, stop.Token);

        await calls.Reached(1);

        calls.Count.Should().Be(1);

        await EndAsync(listening, stop);
    }

    [PostgresFact]
    public async Task ListenAsync_WhenANotificationArrives_CallsBackAgain()
    {
        const string channel = "bsft_outbox_it_notify";
        using var stop = new CancellationTokenSource();
        var calls = new Calls();
        var listening = _session.ListenAsync(ConnectionString(), new PostgresNotificationChannel(channel), calls.Record, stop.Token);
        await calls.Reached(1);

        await NotifyAsync(channel);
        await calls.Reached(2);

        await EndAsync(listening, stop);
    }

    [PostgresFact]
    public async Task ListenAsync_WhenAnotherChannelIsAnnounced_DoesNotCallBack()
    {
        using var stop = new CancellationTokenSource();
        var calls = new Calls();
        var listening = _session.ListenAsync(ConnectionString(), new PostgresNotificationChannel("bsft_outbox_it_mine"), calls.Record, stop.Token);
        await calls.Reached(1);

        await NotifyAsync("bsft_outbox_it_other");
        await NotifyAsync("bsft_outbox_it_mine");
        await calls.Reached(2);

        calls.Count.Should().Be(2);

        await EndAsync(listening, stop);
    }

    [PostgresFact]
    public async Task ListenAsync_WhenAWriteLandsDuringTheCatchUpCall_IsAnsweredByAnotherCall()
    {
        // The handler is attached before the LISTEN and the catch-up call, so a commit that lands while
        // the first call runs is not cleared unread.
        const string channel = "bsft_outbox_it_race";
        using var stop = new CancellationTokenSource();
        var calls = new Calls(onFirst: () => NotifyAsync(channel));
        var listening = _session.ListenAsync(ConnectionString(), new PostgresNotificationChannel(channel), calls.Record, stop.Token);

        await calls.Reached(2);

        await EndAsync(listening, stop);
    }

    [PostgresFact]
    public async Task ListenAsync_WhenTheCallbackStopsTheSession_ReturnsWithoutFailing()
    {
        const string channel = "bsft_outbox_it_stop";
        using var stop = new CancellationTokenSource();
        var calls = new Calls(onCall: count => count == 2 ? stop.CancelAsync() : Task.CompletedTask);
        var listening = _session.ListenAsync(ConnectionString(), new PostgresNotificationChannel(channel), calls.Record, stop.Token);
        await calls.Reached(1);

        await NotifyAsync(channel);

        await listening.WaitAsync(Patience);
    }

    [PostgresFact]
    public async Task ListenAsync_WhenTheChannelHasDigitsAndUnderscores_StillReceivesItsNotifications()
    {
        // The server quotes the name it was given as a value, so the channel is the one pg_notify names.
        const string channel = "bsft_outbox_it_9_x";
        using var stop = new CancellationTokenSource();
        var calls = new Calls();
        var listening = _session.ListenAsync(ConnectionString(), new PostgresNotificationChannel(channel), calls.Record, stop.Token);
        await calls.Reached(1);

        await NotifyAsync(channel);
        await calls.Reached(2);

        await EndAsync(listening, stop);
    }

    [PostgresFact]
    public async Task ListenAsync_WhenTheServerIsNotReachable_Fails()
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionString()) { Port = 1, Timeout = 3 };

        var act = () => _session.ListenAsync(builder.ConnectionString, new PostgresNotificationChannel("bsft_outbox_it_down"), _ => Task.CompletedTask);

        await act.Should().ThrowAsync<Exception>();
    }

    private static string ConnectionString() =>
        Environment.GetEnvironmentVariable(PostgresTestServer.ConnectionStringVariable)!;

    private static async Task NotifyAsync(string channel)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        await using var notify = new NpgsqlCommand("SELECT pg_notify(@channel, '')", connection);
        notify.Parameters.AddWithValue("channel", channel);
        await notify.ExecuteNonQueryAsync();
    }

    private static async Task EndAsync(Task listening, CancellationTokenSource stop)
    {
        await stop.CancelAsync();

        var act = () => listening.WaitAsync(Patience);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>Counts the callbacks of a session and lets a test wait for the n-th.</summary>
    private sealed class Calls
    {
        private readonly Func<int, Task>? _onCall;
        private readonly List<(int Count, TaskCompletionSource Reached)> _waiters = [];
        private int _count;

        public Calls(Func<Task>? onFirst = null, Func<int, Task>? onCall = null) =>
            _onCall = onFirst is null ? onCall : count => count == 1 ? onFirst() : Task.CompletedTask;

        public int Count => Volatile.Read(ref _count);

        public async Task Record(CancellationToken cancellationToken)
        {
            var count = Interlocked.Increment(ref _count);

            if (_onCall is not null)
                await _onCall(count);

            lock (_waiters)
            {
                foreach (var waiter in _waiters.Where(waiter => waiter.Count <= count))
                    waiter.Reached.TrySetResult();
            }
        }

        public Task Reached(int count)
        {
            var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_waiters)
            {
                if (Count >= count)
                    reached.TrySetResult();
                else
                    _waiters.Add((count, reached));
            }

            return reached.Task.WaitAsync(Patience);
        }
    }
}
