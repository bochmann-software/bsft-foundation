using System.Security.Claims;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace Bsft.Hosting.Tests;

public sealed class ChainedRateLimiterPolicyTests
{
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void GetPartition_SameKeysInEveryLink_YieldsOnePartitionKey()
    {
        using var principal = Link(RateLimitPartitionKind.Principal);
        using var global = Link(RateLimitPartitionKind.Global);
        var policy = new ChainedRateLimiterPolicy([principal, global], _time);

        var first = policy.GetPartition(Guest("guest-1"));
        var second = policy.GetPartition(Guest("guest-1"));

        second.PartitionKey.Should().Be(first.PartitionKey);
        first.PartitionKey.Should().Contain("principal:guest-1").And.Contain("global");
    }

    [Fact]
    public void GetPartition_DifferentPrincipals_YieldDifferentPartitionKeys()
    {
        using var principal = Link(RateLimitPartitionKind.Principal);
        using var global = Link(RateLimitPartitionKind.Global);
        var policy = new ChainedRateLimiterPolicy([principal, global], _time);

        var first = policy.GetPartition(Guest("guest-1"));
        var second = policy.GetPartition(Guest("guest-2"));

        second.PartitionKey.Should().NotBe(first.PartitionKey);
    }

    [Fact]
    public void GetPartition_Factory_BuildsAChainOverTheLinksKeys()
    {
        using var principal = Link(RateLimitPartitionKind.Principal);
        var policy = new ChainedRateLimiterPolicy([principal], _time);

        var partition = policy.GetPartition(Guest("guest-1"));
        using var limiter = partition.Factory(partition.PartitionKey);

        limiter.Should().BeOfType<ChainedRateLimiter>();
        using var lease = limiter.AttemptAcquire();
        lease.IsAcquired.Should().BeTrue();
        principal.Limiter.AttemptAcquire("principal:guest-1").IsAcquired.Should().BeFalse(
            "the chain holds the only permit of the guest's partition in the link");
    }

    [Fact]
    public void OnRejected_Always_IsNull()
    {
        using var link = Link(RateLimitPartitionKind.Global);
        var policy = new ChainedRateLimiterPolicy([link], _time);

        policy.OnRejected.Should().BeNull();
    }

    private static PolicyLimiterLink Link(RateLimitPartitionKind partitionBy)
        => new(
            new PolicyLimiterOptions { Kind = RateLimiterKind.Concurrency, PartitionBy = partitionBy, PermitLimit = 1 },
            new BsftRateLimitingOptions());

    private static DefaultHttpContext Guest(string id)
        => new()
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], authenticationType: "test"))
        };
}
