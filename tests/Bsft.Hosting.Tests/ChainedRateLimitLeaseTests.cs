using System.Threading.RateLimiting;
using FluentAssertions;

namespace Bsft.Hosting.Tests;

public sealed class ChainedRateLimitLeaseTests
{
    [Fact]
    public void TryGetMetadata_TwoLeasesWithTheSameName_ReturnsTheFirst()
    {
        var lease1 = new FakeLease(true, new Dictionary<string, object?> { { "key1", "value1" } });
        var lease2 = new FakeLease(true, new Dictionary<string, object?> { { "key1", "value2" }, { "key2", "value2" } });

        var chainedLease = new ChainedRateLimitLease(isAcquired: true, [lease1, lease2], retryAfterFallback: null, onRelease: null);

        chainedLease.TryGetMetadata("key1", out var value).Should().BeTrue();
        value.Should().Be("value1");

        chainedLease.TryGetMetadata("key2", out var value2).Should().BeTrue();
        value2.Should().Be("value2");
    }

    [Fact]
    public void MetadataNames_RefusedWithAFallback_IsTheDistinctUnionWithRetryAfter()
    {
        var lease1 = new FakeLease(true, new Dictionary<string, object?> { { "name1", 1 }, { "name2", 2 } });
        var lease2 = new FakeLease(true, new Dictionary<string, object?> { { "name2", 3 }, { "name3", 4 } });

        var chainedLease = new ChainedRateLimitLease(isAcquired: false, [lease1, lease2], retryAfterFallback: TimeSpan.FromSeconds(5), onRelease: null);

        var names = chainedLease.MetadataNames.ToList();
        names.Should().Contain("name1");
        names.Should().Contain("name2");
        names.Should().Contain("name3");
        names.Should().Contain(MetadataName.RetryAfter.Name);
        names.Should().HaveCount(4);
    }

    [Fact]
    public void TryGetMetadata_GrantedWithAFallback_IgnoresTheFallback()
    {
        var lease = new FakeLease(true, new Dictionary<string, object?>());

        var chainedLease = new ChainedRateLimitLease(isAcquired: true, [lease], retryAfterFallback: TimeSpan.FromSeconds(5), onRelease: null);

        chainedLease.TryGetMetadata(MetadataName.RetryAfter.Name, out _).Should().BeFalse();
    }

    [Fact]
    public void Dispose_CalledTwice_DisposesInReverseOrderAndReleasesOnce()
    {
        var disposeOrder = new List<string>();
        var lease1 = new FakeLease(true, new Dictionary<string, object?>());
        var lease2 = new FakeLease(true, new Dictionary<string, object?>());
        var lease3 = new FakeLease(true, new Dictionary<string, object?>());

        lease1.OnDispose = () => disposeOrder.Add("lease1");
        lease2.OnDispose = () => disposeOrder.Add("lease2");
        lease3.OnDispose = () => disposeOrder.Add("lease3");

        var onReleaseCallCount = 0;
        Action onRelease = () => onReleaseCallCount++;

        var chainedLease = new ChainedRateLimitLease(isAcquired: true, [lease1, lease2, lease3], retryAfterFallback: null, onRelease);

        chainedLease.Dispose();

        disposeOrder.Should().Equal(["lease3", "lease2", "lease1"]);
        onReleaseCallCount.Should().Be(1);

        chainedLease.Dispose();
        onReleaseCallCount.Should().Be(1);
    }

    private sealed class FakeLease : RateLimitLease
    {
        private readonly Dictionary<string, object?> _metadata;

        public FakeLease(bool isAcquired, Dictionary<string, object?> metadata)
        {
            IsAcquired = isAcquired;
            _metadata = metadata;
        }

        public override bool IsAcquired { get; }

        public override IEnumerable<string> MetadataNames => _metadata.Keys;

        public Action? OnDispose { get; set; }

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (_metadata.TryGetValue(metadataName, out var value))
            {
                metadata = value;
                return true;
            }

            metadata = null;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            OnDispose?.Invoke();
        }
    }
}
