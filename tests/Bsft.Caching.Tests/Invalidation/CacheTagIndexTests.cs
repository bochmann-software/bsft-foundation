using Bsft.Caching.Invalidation;
using FluentAssertions;
using Xunit;

namespace Bsft.Caching.Tests.Invalidation;

public class CacheTagIndexTests
{
    private readonly TestTimeProvider _time = new();

    private CacheTagIndex CreateIndex() => new(_time);

    private DateTimeOffset InAnHour() => _time.GetUtcNow() + TimeSpan.FromHours(1);

    [Fact]
    public void Constructor_NullTimeProvider_Throws()
    {
        var act = () => new CacheTagIndex(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Associate_NullKey_Throws()
    {
        var act = () => CreateIndex().Associate(null!, ["tag"], InAnHour());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Associate_NullTags_Throws()
    {
        var act = () => CreateIndex().Associate("key", null!, InAnHour());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Associate_NoTags_RecordsNothing()
    {
        var index = CreateIndex();

        index.Associate("key", [], InAnHour());

        index.ListKeys("anything").Should().BeEmpty();
    }

    [Fact]
    public void Associate_SameKeyTwice_KeepsOneAssociation()
    {
        var index = CreateIndex();

        index.Associate("key", ["orders"], InAnHour());
        index.Associate("key", ["orders"], InAnHour());

        index.ListKeys("orders").Should().ContainSingle();
    }

    [Fact]
    public void GetKeys_ReturnsEveryKeyCarryingTheTag()
    {
        var index = CreateIndex();
        index.Associate("key-1", ["orders"], InAnHour());
        index.Associate("key-2", ["orders", "customers"], InAnHour());

        index.ListKeys("orders").Should().BeEquivalentTo(["key-1", "key-2"]);
    }

    [Fact]
    public void GetKeys_UnknownTag_ReturnsEmpty()
    {
        CreateIndex().ListKeys("unknown").Should().BeEmpty();
    }

    [Fact]
    public void GetKeys_NullTag_Throws()
    {
        var act = () => CreateIndex().ListKeys(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetKeys_ExpiredAssociations_AreDropped()
    {
        var index = CreateIndex();
        index.Associate("key", ["orders"], _time.GetUtcNow() + TimeSpan.FromMinutes(5));

        _time.Advance(TimeSpan.FromMinutes(6));

        index.ListKeys("orders").Should().BeEmpty();
    }

    [Fact]
    public void GetKeys_KeepsAssociationsThatHaveNotExpired()
    {
        var index = CreateIndex();
        index.Associate("fresh", ["orders"], _time.GetUtcNow() + TimeSpan.FromMinutes(10));
        index.Associate("stale", ["orders"], _time.GetUtcNow() + TimeSpan.FromMinutes(1));

        _time.Advance(TimeSpan.FromMinutes(5));

        index.ListKeys("orders").Should().BeEquivalentTo(["fresh"]);
    }

    [Fact]
    public void Associate_PrunesExpiredAssociationsOfTheSameTag()
    {
        var index = CreateIndex();
        index.Associate("stale", ["orders"], _time.GetUtcNow() + TimeSpan.FromMinutes(1));

        _time.Advance(TimeSpan.FromMinutes(5));
        index.Associate("fresh", ["orders"], InAnHour());

        index.ListKeys("orders").Should().BeEquivalentTo(["fresh"]);
    }

    [Fact]
    public void Associate_SweepsTagsThatAreNeverReadAgain()
    {
        var index = CreateIndex();
        index.Associate("stale", ["untouched-tag"], _time.GetUtcNow() + TimeSpan.FromMinutes(1));

        // Past the sweep interval, associating any tag prunes the whole index.
        _time.Advance(TimeSpan.FromMinutes(30));
        index.Associate("fresh", ["other-tag"], InAnHour());

        index.TagCount.Should().Be(1);
    }

    [Fact]
    public void Associate_BeforeTheSweepIsDue_KeepsOtherTags()
    {
        var index = CreateIndex();
        index.Associate("first", ["tag-a"], InAnHour());

        index.Associate("second", ["tag-b"], InAnHour());

        index.TagCount.Should().Be(2);
    }

    [Fact]
    public void GetStamp_UnknownKey_ReturnsZero()
    {
        CreateIndex().GetStamp("key").Should().Be(0);
    }

    [Fact]
    public void GetStamp_NullKey_Throws()
    {
        var act = () => CreateIndex().GetStamp(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetStamp_KeyAssociatedTwice_ReturnsTheNewestAssociation()
    {
        var index = CreateIndex();
        index.Associate("key", ["orders"], InAnHour());
        var first = index.GetStamp("key");

        index.Associate("key", ["customers"], InAnHour());

        index.GetStamp("key").Should().BeGreaterThan(first);
    }

    [Fact]
    public void Forget_DropsTheKeyFromEveryTag()
    {
        var index = CreateIndex();
        index.Associate("key-1", ["orders", "customers"], InAnHour());
        index.Associate("key-2", ["orders"], InAnHour());

        index.Forget("key-1", index.GetStamp("key-1"));

        index.ListKeys("orders").Should().BeEquivalentTo(["key-2"]);
        index.ListKeys("customers").Should().BeEmpty();
    }

    [Fact]
    public void Forget_UnknownKey_LeavesTheIndexUntouched()
    {
        var index = CreateIndex();
        index.Associate("key-1", ["orders"], InAnHour());

        index.Forget("key-2", index.GetStamp("key-2"));

        index.ListKeys("orders").Should().BeEquivalentTo(["key-1"]);
    }

    [Fact]
    public void Forget_AssociationNewerThanTheStamp_IsKept()
    {
        // Regression: the caller observed the key, evicted its entry, and a query re-cached the key
        // before the forget landed. Dropping the fresh association would leave that entry in the
        // store with no tag pointing at it.
        var index = CreateIndex();
        index.Associate("key", ["orders"], InAnHour());
        var observed = index.GetStamp("key");

        index.Associate("key", ["orders"], InAnHour());
        index.Forget("key", observed);

        index.ListKeys("orders").Should().BeEquivalentTo(["key"]);
    }

    [Fact]
    public void Forget_NullKey_Throws()
    {
        var act = () => CreateIndex().Forget(null!, 0);

        act.Should().Throw<ArgumentNullException>();
    }
}
