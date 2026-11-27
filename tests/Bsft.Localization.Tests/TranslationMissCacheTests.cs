using System.Collections.Concurrent;
using System.Threading.Tasks;
using Bsft.Localization.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Bsft.Localization.Tests;

/// <summary>
/// What the miss cache remembers, for how long, and how many: the bound counts live entries, and an
/// expired entry gives its place back on the next insert.
/// </summary>
public class TranslationMissCacheTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Start);

    [Fact]
    public void TryAdd_FullCacheOfLapsedEntries_AcceptsTheNewKey()
    {
        var cache = CreateCache(maxEntries: 3);
        cache.TryAdd(Key("a")).Should().BeTrue();
        cache.TryAdd(Key("b")).Should().BeTrue();
        cache.TryAdd(Key("c")).Should().BeTrue();

        _time.Advance(Lifetime);
        var accepted = cache.TryAdd(Key("d"));

        accepted.Should().BeTrue();
        cache.Count.Should().Be(1);
    }

    [Fact]
    public void TryAdd_FullCacheWithSomeLapsed_DropsOnlyTheLapsedEntries()
    {
        var cache = CreateCache(maxEntries: 2);
        cache.TryAdd(Key("a")).Should().BeTrue();

        _time.Advance(Lifetime / 2);
        cache.TryAdd(Key("b")).Should().BeTrue();
        cache.TryAdd(Key("c")).Should().BeFalse();

        _time.Advance(Lifetime / 2);
        var accepted = cache.TryAdd(Key("c"));

        accepted.Should().BeTrue();
        cache.Count.Should().Be(2);
        cache.Contains(Key("a")).Should().BeFalse();
        cache.Contains(Key("b")).Should().BeTrue();
        cache.Contains(Key("c")).Should().BeTrue();
    }

    [Fact]
    public void TryAdd_KeyWhoseEntryLapsed_RecordsItAfresh()
    {
        var cache = CreateCache();
        cache.TryAdd(Key("a")).Should().BeTrue();

        _time.Advance(Lifetime);
        cache.TryAdd(Key("a")).Should().BeTrue();

        _time.Advance(Lifetime - TimeSpan.FromTicks(1));
        cache.Contains(Key("a")).Should().BeTrue();
    }

    [Fact]
    public void TryAdd_KeyWhoseEntryLapsed_LeavesExactlyOneEntry()
    {
        var cache = CreateCache();
        cache.TryAdd(Key("a")).Should().BeTrue();

        _time.Advance(Lifetime);
        cache.TryAdd(Key("a")).Should().BeTrue();

        cache.Count.Should().Be(1);
    }

    [Fact]
    public void Clear_AfterMisses_EmptiesTheCache()
    {
        var cache = CreateCache();
        cache.TryAdd(Key("a"));
        cache.TryAdd(Key("b"));

        cache.Clear();

        cache.Count.Should().Be(0);
        cache.Contains(Key("a")).Should().BeFalse();
        cache.Contains(Key("b")).Should().BeFalse();
    }

    [Fact]
    public void TryAdd_ConcurrentWritersOverDistinctKeys_NeverExceedTheBoundAndReportOnlyWhatWasAdded()
    {
        const int maxEntries = 100;
        const int keyCount = 2_000;
        var cache = CreateCache(maxEntries);
        var accepted = new ConcurrentDictionary<int, bool>();

        Parallel.For(0, keyCount, index =>
        {
            if (cache.TryAdd(Key($"key-{index}")))
                accepted[index] = true;
        });

        cache.Count.Should().BeLessOrEqualTo(maxEntries);
        accepted.Count.Should().Be(maxEntries);

        for (var index = 0; index < keyCount; index++)
        {
            if (cache.Contains(Key($"key-{index}")))
                accepted.ContainsKey(index).Should().BeTrue($"key-{index} is reported present but TryAdd did not accept it");
        }
    }

    [Fact]
    public void TryAdd_FullCacheOfLiveMisses_RefusesANewKey()
    {
        var cache = CreateCache(maxEntries: 2);
        cache.TryAdd(Key("a")).Should().BeTrue();
        cache.TryAdd(Key("b")).Should().BeTrue();

        var accepted = cache.TryAdd(Key("c"));

        accepted.Should().BeFalse();
        cache.Contains(Key("c")).Should().BeFalse();
        cache.Count.Should().Be(2);
    }

    [Fact]
    public void TryAdd_FullCacheOfLiveMisses_StillReportsAKeyAlreadyPresent()
    {
        var cache = CreateCache(maxEntries: 2);
        cache.TryAdd(Key("a")).Should().BeTrue();
        cache.TryAdd(Key("b")).Should().BeTrue();

        var accepted = cache.TryAdd(Key("a"));

        accepted.Should().BeTrue();
        cache.Count.Should().Be(2);
    }

    [Fact]
    public void Contains_BeforeTheLifetime_IsTrue()
    {
        var cache = CreateCache();
        cache.TryAdd(Key("a"));

        _time.Advance(Lifetime - TimeSpan.FromTicks(1));

        cache.Contains(Key("a")).Should().BeTrue();
    }

    [Fact]
    public void Contains_ExactlyAtTheLifetime_IsFalse()
    {
        var cache = CreateCache();
        cache.TryAdd(Key("a"));

        _time.Advance(Lifetime);

        cache.Contains(Key("a")).Should().BeFalse();
    }

    [Fact]
    public void Contains_AKeyNeverAdded_IsFalse()
    {
        var cache = CreateCache();

        cache.Contains(Key("a")).Should().BeFalse();
    }

    [Fact]
    public void Contains_SameKeyUnderAnotherTenant_IsFalse()
    {
        var cache = CreateCache();
        cache.TryAdd(new TranslationCacheKey("t1", "en", "greeting"));

        cache.Contains(new TranslationCacheKey("t2", "en", "greeting")).Should().BeFalse();
        cache.Contains(new TranslationCacheKey(null, "en", "greeting")).Should().BeFalse();
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        var act = () => new TranslationMissCache(null!, _time);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullTimeProvider_ThrowsArgumentNullException()
    {
        var act = () => new TranslationMissCache(Options.Create(new BsftLocalizationOptions()), null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static TimeSpan Lifetime => TimeSpan.FromSeconds(30);

    private TranslationMissCache CreateCache(int maxEntries = 10_000)
    {
        var options = new BsftLocalizationOptions
        {
            MissCacheMaxEntries = maxEntries,
            MissCacheLifetime = Lifetime
        };

        return new TranslationMissCache(Options.Create(options), _time);
    }

    private static TranslationCacheKey Key(string key) => new(null, "en", key);
}
