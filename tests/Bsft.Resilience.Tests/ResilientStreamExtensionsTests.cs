using System.Runtime.CompilerServices;
using Bsft.Resilience.Tests.Fakes;
using Bsft.Resilience.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Resilience.Tests;

public class ResilientStreamExtensionsTests
{
    private static readonly InvalidOperationException Boom = new("the stream broke");

    [Fact]
    public async Task WithRetry_StreamCompletes_YieldsEveryItemOnce()
    {
        var invocations = 0;

        var items = await ResilientStreamExtensions
            .WithRetry<int>(ct =>
            {
                invocations++;
                return Streams.Range(0, 3, ct);
            })
            .Collect();

        items.Should().Equal(0, 1, 2);
        invocations.Should().Be(1);
    }

    [Fact]
    public async Task WithRetry_FailsBeforeFirstItem_RetriesAndSucceeds()
    {
        var invocations = 0;

        var items = await ResilientStreamExtensions
            .WithRetry<int>(ct =>
            {
                invocations++;
                return invocations == 1 ? Streams.ThrowAfter(0, Boom, ct) : Streams.Range(0, 2, ct);
            }, NoWait)
            .Collect();

        items.Should().Equal(0, 1);
        invocations.Should().Be(2);
    }

    [Fact]
    public async Task WithRetry_FailsMidStream_RethrowsUnderTheDefaultPolicy()
    {
        var invocations = 0;
        var items = new List<int>();

        var act = async () =>
        {
            await foreach (var item in ResilientStreamExtensions.WithRetry<int>(ct =>
            {
                invocations++;
                return Streams.ThrowAfter(2, Boom, ct);
            }, NoWait))
            {
                items.Add(item);
            }
        };

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(Boom.Message);
        items.Should().Equal(0, 1);
        invocations.Should().Be(1);
    }

    [Fact]
    public async Task WithRetry_FailsMidStream_RestartFromStartRepeatsTheItemsAlreadySeen()
    {
        var invocations = 0;

        var items = await ResilientStreamExtensions
            .WithRetry<int>(
                ct =>
                {
                    invocations++;
                    return invocations == 1 ? Streams.ThrowAfter(2, Boom, ct) : Streams.Range(0, 3, ct);
                },
                options =>
                {
                    NoWait(options);
                    options.PartialStreamPolicy = PartialStreamPolicy.RestartFromStart;
                })
            .Collect();

        items.Should().Equal(0, 1, 0, 1, 2);
        invocations.Should().Be(2);
    }

    [Fact]
    public async Task WithRetry_FailsMidStream_FactoryResumesFromTheOffset()
    {
        var contexts = new List<StreamRetryContext>();

        var items = await ResilientStreamExtensions
            .WithRetry<int>(
                (context, ct) =>
                {
                    contexts.Add(context);
                    return context.AttemptNumber == 0
                        ? Streams.ThrowAfter(2, Boom, ct)
                        : Streams.Range((int)context.ItemsYielded, 3, ct);
                },
                options =>
                {
                    NoWait(options);
                    options.PartialStreamPolicy = PartialStreamPolicy.RestartFromStart;
                })
            .Collect();

        items.Should().Equal(0, 1, 2, 3, 4);
        contexts.Should().Equal(new StreamRetryContext(0, 0), new StreamRetryContext(1, 2));
    }

    [Fact]
    public async Task WithRetry_RetriesExhausted_ThrowsTheLastFailure()
    {
        var invocations = 0;

        var act = async () => await ResilientStreamExtensions
            .WithRetry<int>(
                ct =>
                {
                    invocations++;
                    return Streams.ThrowAfter(0, Boom, ct);
                },
                options =>
                {
                    NoWait(options);
                    options.MaxRetryAttempts = 2;
                })
            .Collect();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(Boom.Message);
        invocations.Should().Be(3);
    }

    [Fact]
    public async Task WithRetry_ClassifierRejectsTheFailure_ThrowsWithoutRetrying()
    {
        var invocations = 0;

        var act = async () => await ResilientStreamExtensions
            .WithRetry<int>(
                ct =>
                {
                    invocations++;
                    return Streams.ThrowAfter(0, Boom, ct);
                },
                options =>
                {
                    NoWait(options);
                    options.ShouldRetry = failure => failure is TimeoutException;
                })
            .Collect();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(Boom.Message);
        invocations.Should().Be(1);
    }

    [Fact]
    public async Task WithRetry_SourceIsCancelled_PropagatesAndDisposesTheEnumerator()
    {
        using var cts = new CancellationTokenSource();
        var disposals = 0;

        var act = async () =>
        {
            await foreach (var item in ResilientStreamExtensions.WithRetry(
                ct => Counted(cts.Token, () => disposals++), NoWait, cts.Token))
            {
                if (item == 2)
                    await cts.CancelAsync();
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
        disposals.Should().Be(1);
    }

    [Fact]
    public async Task WithRetry_ConsumerStopsEarly_DisposesTheEnumerator()
    {
        var disposals = 0;

        await foreach (var item in ResilientStreamExtensions.WithRetry(
            ct => Counted(ct, () => disposals++), NoWait))
        {
            if (item == 1)
                break;
        }

        disposals.Should().Be(1);
    }

    [Fact]
    public async Task WithRetry_CancelledBetweenAttempts_Throws()
    {
        using var cts = new CancellationTokenSource();

        var act = async () => await ResilientStreamExtensions
            .WithRetry<int>(
                ct =>
                {
                    cts.Cancel();
                    return Streams.ThrowAfter(0, Boom, CancellationToken.None);
                },
                NoWait,
                cts.Token)
            .Collect();

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task WithRetry_BacksOffExponentiallyOnTheTimeProvider()
    {
        var time = new RecordingTimeProvider();

        var act = async () => await ResilientStreamExtensions
            .WithRetry<int>(
                ct => Streams.ThrowAfter(0, Boom, ct),
                options =>
                {
                    options.TimeProvider = time;
                    options.UseJitter = false;
                    options.BaseDelay = TimeSpan.FromMilliseconds(200);
                    options.MaxRetryAttempts = 3;
                })
            .Collect();

        await act.Should().ThrowAsync<InvalidOperationException>();
        time.Delays.Should().Equal(
            TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(800));
    }

    [Fact]
    public async Task WithRetry_BackoffIsCappedByMaxDelay()
    {
        var time = new RecordingTimeProvider();

        var act = async () => await ResilientStreamExtensions
            .WithRetry<int>(
                ct => Streams.ThrowAfter(0, Boom, ct),
                options =>
                {
                    options.TimeProvider = time;
                    options.UseJitter = false;
                    options.BaseDelay = TimeSpan.FromSeconds(1);
                    options.MaxDelay = TimeSpan.FromMilliseconds(1500);
                    options.MaxRetryAttempts = 3;
                })
            .Collect();

        await act.Should().ThrowAsync<InvalidOperationException>();
        time.Delays.Should().Equal(
            TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1500), TimeSpan.FromMilliseconds(1500));
    }

    [Fact]
    public async Task WithRetry_JitterKeepsTheDelayWithinHalfTheComputedOne()
    {
        var time = new RecordingTimeProvider();

        var act = async () => await ResilientStreamExtensions
            .WithRetry<int>(
                ct => Streams.ThrowAfter(0, Boom, ct),
                options =>
                {
                    options.TimeProvider = time;
                    options.BaseDelay = TimeSpan.FromSeconds(1);
                    options.MaxRetryAttempts = 1;
                })
            .Collect();

        await act.Should().ThrowAsync<InvalidOperationException>();
        time.Delays.Should().ContainSingle().Which.Should()
            .BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(500))
            .And.BeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task WithRetry_ZeroBaseDelay_WaitsNotAtAll()
    {
        var time = new RecordingTimeProvider();

        var act = async () => await ResilientStreamExtensions
            .WithRetry<int>(
                ct => Streams.ThrowAfter(0, Boom, ct),
                options =>
                {
                    options.TimeProvider = time;
                    options.BaseDelay = TimeSpan.Zero;
                    options.MaxRetryAttempts = 2;
                })
            .Collect();

        await act.Should().ThrowAsync<InvalidOperationException>();
        time.Delays.Should().BeEmpty();
    }

    [Fact]
    public async Task WithRetry_TeardownFaultsOnTopOfTheBreak_KeepsTheFailureThatStoppedTheStream()
    {
        // Its own exception instance rather than the shared one: the suppressed failure is attached to
        // the thrown exception's Data, and mutating an instance shared with the other tests would leak.
        var broke = new InvalidOperationException("this stream broke");
        var invocations = 0;

        var act = async () => await ResilientStreamExtensions
            .WithRetry<int>(
                ct =>
                {
                    invocations++;
                    return new FaultingOnDisposeStream(
                        Streams.ThrowAfter(0, broke, ct), new TimeoutException("the teardown broke too"));
                },
                options =>
                {
                    NoWait(options);
                    options.MaxRetryAttempts = 1;
                })
            .Collect();

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(broke.Message);
        invocations.Should().Be(2);
        thrown.Which.Data[ResilienceDefaults.SuppressedFailureDataKey].Should()
            .BeOfType<TimeoutException>().Which.Message.Should().Be("the teardown broke too");
    }

    [Fact]
    public async Task WithRetry_TeardownCancelsOnTopOfTheBreak_KeepsTheFailureThatStoppedTheStream()
    {
        // A cancellation ends the stream when it is the only thing that happened. On top of a recorded
        // failure it is a teardown fault like any other, and letting it out would throw away the
        // exception that says why the stream broke, along with the retry that failure might have earned.
        var broke = new InvalidOperationException("this stream broke");
        var invocations = 0;

        var act = async () => await ResilientStreamExtensions
            .WithRetry<int>(
                ct =>
                {
                    invocations++;
                    return new FaultingOnDisposeStream(
                        Streams.ThrowAfter(0, broke, ct), new OperationCanceledException("torn down"));
                },
                options =>
                {
                    NoWait(options);
                    options.MaxRetryAttempts = 1;
                })
            .Collect();

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(broke.Message);
        invocations.Should().Be(2);
        thrown.Which.Data[ResilienceDefaults.SuppressedFailureDataKey].Should()
            .BeOfType<OperationCanceledException>();
    }

    [Fact]
    public async Task WithRetry_TeardownCancelsAfterACleanStream_EndsTheStream()
    {
        var act = async () => await ResilientStreamExtensions
            .WithRetry<int>(
                ct => new FaultingOnDisposeStream(
                    Streams.Range(1, 2, ct), new OperationCanceledException("torn down")),
                NoWait)
            .Collect();

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task WithRetry_TeardownFaultsAfterACleanStream_IsClassifiedLikeAnyOtherFailure()
    {
        var cleanup = new TimeoutException("the teardown broke");
        var items = new List<int>();

        var act = async () =>
        {
            await foreach (var item in ResilientStreamExtensions.WithRetry<int>(
                ct => new FaultingOnDisposeStream(Streams.Range(0, 2, ct), cleanup), NoWait))
            {
                items.Add(item);
            }
        };

        await act.Should().ThrowAsync<TimeoutException>().WithMessage(cleanup.Message);
        items.Should().Equal(0, 1);
    }

    [Fact]
    public void WithRetry_NullFactory_Throws()
    {
        var act = () => ResilientStreamExtensions.WithRetry((Func<CancellationToken, IAsyncEnumerable<int>>)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("factory");
    }

    [Fact]
    public void WithRetry_NullResumingFactory_Throws()
    {
        var act = () => ResilientStreamExtensions.WithRetry(
            (Func<StreamRetryContext, CancellationToken, IAsyncEnumerable<int>>)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("factory");
    }

    [Fact]
    public void WithRetry_UnusableOptions_ThrowsBeforeAnythingIsEnumerated()
    {
        var invocations = 0;

        var act = () => ResilientStreamExtensions.WithRetry<int>(
            ct =>
            {
                invocations++;
                return Streams.Range(0, 1, ct);
            },
            options => options.MaxRetryAttempts = -1);

        act.Should().Throw<InvalidOperationException>();
        invocations.Should().Be(0);
    }

    /// <summary>Never waits: the retry tests that are not about timing must not spend real time.</summary>
    /// <param name="options">The options to flatten.</param>
    private static void NoWait(BsftResilienceStreamOptions options) => options.BaseDelay = TimeSpan.Zero;

    /// <summary>An endless stream that reports its disposal.</summary>
    /// <param name="cancellationToken">Stops the enumeration.</param>
    /// <param name="onDispose">Called once the enumerator is disposed.</param>
    /// <returns>The stream.</returns>
    private static async IAsyncEnumerable<int> Counted(
        [EnumeratorCancellation] CancellationToken cancellationToken, Action onDispose)
    {
        try
        {
            for (var i = 0; ; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return i;
            }
        }
        finally
        {
            onDispose();
        }
    }
}
