using System.Threading.RateLimiting;
using Bsft.Tests.Shared;
using FluentAssertions;

namespace Bsft.Hosting.Tests;

public sealed class ChainedRateLimiterTests
{
    [Fact]
    public void AttemptAcquire_EveryLinkGrants_Grants()
    {
        using var link = new PolicyLimiterLink(
            new PolicyLimiterOptions { Kind = RateLimiterKind.Concurrency, PermitLimit = 1, WindowSeconds = 600 },
            new BsftRateLimitingOptions());
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var limiter = new ChainedRateLimiter([link], ["a"], time);

        var lease = limiter.AttemptAcquire(1);

        lease.IsAcquired.Should().BeTrue();
        lease.Dispose();
    }

    [Fact]
    public void AttemptAcquire_SecondLinkRefuses_RefusesAndReturnsTheFirstLinksPermit()
    {
        var options1 = new PolicyLimiterOptions { Kind = RateLimiterKind.Concurrency, PermitLimit = 2, WindowSeconds = 600 };
        var options2 = new PolicyLimiterOptions { Kind = RateLimiterKind.Concurrency, PermitLimit = 1, WindowSeconds = 600 };
        using var linkA = new PolicyLimiterLink(options1, new BsftRateLimitingOptions());
        using var linkB = new PolicyLimiterLink(options2, new BsftRateLimitingOptions());
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var limiter = new ChainedRateLimiter([linkA, linkB], ["a", "b"], time);

        using var lease1 = limiter.AttemptAcquire(1);
        lease1.IsAcquired.Should().BeTrue();

        using var lease2 = limiter.AttemptAcquire(1);
        lease2.IsAcquired.Should().BeFalse();

        // lease1 still holds one of the first link's two permits; the refused attempt must not hold the other.
        using var lease3 = linkA.Limiter.AttemptAcquire("a");
        lease3.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public void AttemptAcquire_AfterTheGrantedLeaseIsDisposed_GrantsAgain()
    {
        using var link = new PolicyLimiterLink(
            new PolicyLimiterOptions { Kind = RateLimiterKind.Concurrency, PermitLimit = 1, WindowSeconds = 600 },
            new BsftRateLimitingOptions());
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var limiter = new ChainedRateLimiter([link], ["a"], time);

        var lease1 = limiter.AttemptAcquire(1);
        lease1.IsAcquired.Should().BeTrue();

        var lease2 = limiter.AttemptAcquire(1);
        lease2.IsAcquired.Should().BeFalse();

        lease1.Dispose();

        var lease3 = limiter.AttemptAcquire(1);
        lease3.IsAcquired.Should().BeTrue();
        lease3.Dispose();
    }

    [Fact]
    public void Dispose_CalledTwiceOnAGrantedLease_ReleasesOnce()
    {
        using var link = new PolicyLimiterLink(
            new PolicyLimiterOptions { Kind = RateLimiterKind.Concurrency, PermitLimit = 2, WindowSeconds = 600 },
            new BsftRateLimitingOptions());
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var limiter = new ChainedRateLimiter([link], ["a"], time);

        var l1 = limiter.AttemptAcquire(1);
        var l2 = limiter.AttemptAcquire(1);
        l1.IsAcquired.Should().BeTrue();
        l2.IsAcquired.Should().BeTrue();

        l1.Dispose();
        l1.Dispose();

        var l3 = limiter.AttemptAcquire(1);
        l3.IsAcquired.Should().BeTrue();

        var l4 = limiter.AttemptAcquire(1);
        l4.IsAcquired.Should().BeFalse();

        l3.Dispose();
        l2.Dispose();
    }

    [Fact]
    public void AttemptAcquire_RefusedByAConcurrencyLink_CarriesTheLinksRetryAfter()
    {
        using var link = new PolicyLimiterLink(
            new PolicyLimiterOptions { Kind = RateLimiterKind.Concurrency, PermitLimit = 1, WindowSeconds = 600, RetryAfterSeconds = 9 },
            new BsftRateLimitingOptions());
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var limiter = new ChainedRateLimiter([link], ["a"], time);

        var lease1 = limiter.AttemptAcquire(1);
        var lease2 = limiter.AttemptAcquire(1);

        lease2.IsAcquired.Should().BeFalse();
        lease2.TryGetMetadata(MetadataName.RetryAfter.Name, out var retryAfter).Should().BeTrue();
        retryAfter.Should().Be(TimeSpan.FromSeconds(9));

        lease1.Dispose();
        lease2.Dispose();
    }

    [Fact]
    public void AttemptAcquire_RefusedByALinkWithoutRetryAfter_CarriesTheOptionsFallback()
    {
        using var link = new PolicyLimiterLink(
            new PolicyLimiterOptions { Kind = RateLimiterKind.Concurrency, PermitLimit = 1, WindowSeconds = 600, RetryAfterSeconds = null },
            new BsftRateLimitingOptions { RetryAfterSeconds = 4 });
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var limiter = new ChainedRateLimiter([link], ["a"], time);

        var lease1 = limiter.AttemptAcquire(1);
        var lease2 = limiter.AttemptAcquire(1);

        lease2.IsAcquired.Should().BeFalse();
        lease2.TryGetMetadata(MetadataName.RetryAfter.Name, out var retryAfter).Should().BeTrue();
        retryAfter.Should().Be(TimeSpan.FromSeconds(4));

        lease1.Dispose();
        lease2.Dispose();
    }

    [Fact]
    public void AttemptAcquire_RefusedByAFixedWindow_CarriesTheWindowsEstimate()
    {
        using var link = new PolicyLimiterLink(
            new PolicyLimiterOptions { Kind = RateLimiterKind.FixedWindow, PermitLimit = 1, WindowSeconds = 600, RetryAfterSeconds = 9 },
            new BsftRateLimitingOptions());
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var limiter = new ChainedRateLimiter([link], ["a"], time);

        var lease1 = limiter.AttemptAcquire(1);
        var lease2 = limiter.AttemptAcquire(1);

        lease2.IsAcquired.Should().BeFalse();
        lease2.TryGetMetadata(MetadataName.RetryAfter.Name, out var retryAfter).Should().BeTrue();
        retryAfter.Should().Be(TimeSpan.FromSeconds(600));

        lease1.Dispose();
        lease2.Dispose();
    }

    [Fact]
    public void AttemptAcquire_Granted_CarriesNoRetryAfter()
    {
        using var link = new PolicyLimiterLink(
            new PolicyLimiterOptions { Kind = RateLimiterKind.Concurrency, PermitLimit = 1, WindowSeconds = 600 },
            new BsftRateLimitingOptions());
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var limiter = new ChainedRateLimiter([link], ["a"], time);

        var lease = limiter.AttemptAcquire(1);

        lease.TryGetMetadata(MetadataName.RetryAfter.Name, out _).Should().BeFalse();
        lease.TryGetMetadata("unknown", out var value).Should().BeFalse();
        value.Should().BeNull();

        lease.Dispose();
    }

    [Fact]
    public void IdleDuration_WithAndWithoutLeasesInFlight_MeasuresTimeSinceTheLastRelease()
    {
        using var link = new PolicyLimiterLink(
            new PolicyLimiterOptions { Kind = RateLimiterKind.Concurrency, PermitLimit = 1, WindowSeconds = 600 },
            new BsftRateLimitingOptions());
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var limiter = new ChainedRateLimiter([link], ["a"], time);

        limiter.IdleDuration.Should().Be(TimeSpan.Zero);

        time.Advance(TimeSpan.FromSeconds(5));
        limiter.IdleDuration.Should().Be(TimeSpan.FromSeconds(5));

        var lease = limiter.AttemptAcquire(1);
        limiter.IdleDuration.Should().BeNull();

        lease.Dispose();
        time.Advance(TimeSpan.FromSeconds(3));
        limiter.IdleDuration.Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void GetStatistics_AfterAGrantAndARefusal_AggregatesTheLinks()
    {
        var options1 = new PolicyLimiterOptions { Kind = RateLimiterKind.Concurrency, PermitLimit = 3, WindowSeconds = 600 };
        var options2 = new PolicyLimiterOptions { Kind = RateLimiterKind.FixedWindow, PermitLimit = 5, WindowSeconds = 600 };
        using var linkA = new PolicyLimiterLink(options1, new BsftRateLimitingOptions());
        using var linkB = new PolicyLimiterLink(options2, new BsftRateLimitingOptions());
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var limiter = new ChainedRateLimiter([linkA, linkB], ["a", "b"], time);

        using var granted = limiter.AttemptAcquire(1);
        granted.IsAcquired.Should().BeTrue();
        using var refused = limiter.AttemptAcquire(3);
        refused.IsAcquired.Should().BeFalse();

        var stats = limiter.GetStatistics();
        stats.Should().NotBeNull();
        stats!.CurrentAvailablePermits.Should().Be(2, "the concurrency link has 2 of 3 left, the window 4 of 5");
        stats.CurrentQueuedCount.Should().Be(0);
        stats.TotalSuccessfulLeases.Should().Be(1);
        stats.TotalFailedLeases.Should().Be(1);
    }

    [Fact]
    public async Task AcquireAsync_EveryLinkGrants_Grants()
    {
        using var link = new PolicyLimiterLink(
            new PolicyLimiterOptions { Kind = RateLimiterKind.Concurrency, PermitLimit = 1, WindowSeconds = 600 },
            new BsftRateLimitingOptions());
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var limiter = new ChainedRateLimiter([link], ["a"], time);

        var lease = await limiter.AcquireAsync(1);

        lease.IsAcquired.Should().BeTrue();
        lease.Dispose();
    }

    [Fact]
    public async Task AcquireAsync_NoQueueAndNoPermit_Refuses()
    {
        using var link = new PolicyLimiterLink(
            new PolicyLimiterOptions { Kind = RateLimiterKind.Concurrency, PermitLimit = 1, WindowSeconds = 600 },
            new BsftRateLimitingOptions());
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var limiter = new ChainedRateLimiter([link], ["a"], time);

        var held = await limiter.AcquireAsync(1);
        var refused = await limiter.AcquireAsync(1);

        refused.IsAcquired.Should().BeFalse();
        held.Dispose();
        refused.Dispose();
    }

    [Fact]
    public async Task AcquireAsync_WithAQueue_WaitsForThePermit()
    {
        var options = new PolicyLimiterOptions { Kind = RateLimiterKind.Concurrency, PermitLimit = 1, WindowSeconds = 600, QueueLimit = 1 };
        using var link = new PolicyLimiterLink(options, new BsftRateLimitingOptions());
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var limiter = new ChainedRateLimiter([link], ["a"], time);

        var held = await limiter.AcquireAsync(1);
        var pending = limiter.AcquireAsync(1).AsTask();

        pending.IsCompleted.Should().BeFalse();

        held.Dispose();

        var result = await pending;
        result.IsAcquired.Should().BeTrue();
        result.Dispose();
    }

    [Fact]
    public async Task AcquireAsync_CancelledWhileQueued_ReturnsTheEarlierLinksPermit()
    {
        var options1 = new PolicyLimiterOptions { Kind = RateLimiterKind.Concurrency, PermitLimit = 1, WindowSeconds = 600 };
        var options2 = new PolicyLimiterOptions { Kind = RateLimiterKind.Concurrency, PermitLimit = 1, WindowSeconds = 600, QueueLimit = 1 };
        using var linkA = new PolicyLimiterLink(options1, new BsftRateLimitingOptions());
        using var linkB = new PolicyLimiterLink(options2, new BsftRateLimitingOptions());
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var limiter = new ChainedRateLimiter([linkA, linkB], ["a", "b"], time);

        var held = linkB.Limiter.AttemptAcquire("b");
        held.IsAcquired.Should().BeTrue();

        using var cts = new CancellationTokenSource();
        var pending = limiter.AcquireAsync(1, cts.Token).AsTask();
        pending.IsCompleted.Should().BeFalse("the second link queues the request");
        linkA.Limiter.AttemptAcquire("a").IsAcquired.Should().BeFalse("the first link granted the queued request its only permit");

        cts.Cancel();
        await pending.Invoking(task => task).Should().ThrowAsync<OperationCanceledException>();

        using var afterCancel = linkA.Limiter.AttemptAcquire("a");
        afterCancel.IsAcquired.Should().BeTrue();

        held.Dispose();
    }
}
