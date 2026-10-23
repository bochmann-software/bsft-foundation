using Bsft.Correlation;
using Bsft.Logging.DependencyInjection;
using Bsft.Logging.LogFiles;
using Bsft.Logging.Sinks;
using Bsft.Logging.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Logging.Tests;

/// <summary>Covers forged input, deferred maps, equality, the scope lifetime and the registered options of the scope properties.</summary>
public sealed class BsftFileLoggerScopeHardeningTests : IDisposable
{
    private const string Hostile = "evil] {RunId=victim} X\\";

    private static readonly ScopeLineEncoder Encoder = new();

    private readonly LogFileFixture _log = new();

    /// <inheritdoc />
    public void Dispose() => _log.Dispose();

    [Fact]
    public void EscapeSlot_WhenTheIdNeedsNoChange_ReturnsItUnchanged()
    {
        var id = "0HN7-abc.def_1";

        Encoder.EscapeSlot(id).Should().BeSameAs(id);
    }

    [Fact]
    public void EscapeSlot_WhenTheIdHoldsBracketBackslashOrLineBreaks_EscapesAndFlattensIt()
    {
        Encoder.EscapeSlot("a]b\\c\r\nd").Should().Be("a\\]b\\\\c d");
    }

    [Fact]
    public void EscapeSlot_WhenNull_Throws()
    {
        var act = () => Encoder.EscapeSlot(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Block_WhenNullOrEmpty_ThrowsOrWritesNothing()
    {
        var act = () => Encoder.ToBlock(null!);

        act.Should().Throw<ArgumentNullException>();
        Encoder.ToBlock([]).Should().BeEmpty();
    }

    [Fact]
    public void ToBlock_WithProperties_EscapesKeysAndValues()
    {
        Encoder.ToBlock([new("A;B", "x=y}"), new("C", "1")]).Should().Be(" {A\\;B=x\\=y\\};C=1}");
    }

    [Fact]
    public void DescribeKeyProblem_ForUsableAndUnusableLists_NamesTheRuleThatFails()
    {
        Encoder.DescribeKeyProblem(["CorrelationId", "RunId"]).Should().BeNull();
        Encoder.DescribeKeyProblem(null).Should().Contain("must be a list");
        Encoder.DescribeKeyProblem(["RunId", " "]).Should().Contain("blank key");
        Encoder.DescribeKeyProblem(["~truncated"]).Should().Contain("reserved truncation marker");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RoundTrip_WithAHostileCorrelationId_CannotForgeAScopeProperty(bool json)
    {
        var options = Options(json);
        Write(options, logger =>
        {
            using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = Hostile }))
                logger.LogInformation("hello");
        });
        var reader = new LogFileReader(options, _log.Clock);

        var all = await reader.ReadAsync(new LogReadRequest(10));
        var forged = await reader.ReadAsync(
            new LogReadRequest(10) { ScopeFilter = new Dictionary<string, string> { ["RunId"] = "victim" } });

        var entry = all.Entries.Single();
        entry.CorrelationId.Should().Be(Hostile);
        entry.Category.Should().Be("Cat");
        entry.ScopeProperties.Should().BeEquivalentTo(new Dictionary<string, string> { ["CorrelationId"] = Hostile });
        forged.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_WhenAHandWrittenLineStatesCorrelationIdAsAnExtraProperty_DoesNotMatchTheCorrelationFilter()
    {
        Directory.CreateDirectory(_log.LogDirectory);
        File.WriteAllText(
            Path.Combine(_log.LogDirectory, "app-2026-06-15.log"),
            "[2026-06-15 09:00:00.000 INF] [-] {CorrelationId=x} Cat: text\n"
            + "{\"timestamp\":\"2026-06-15T10:00:00.000+00:00\",\"level\":\"Information\",\"correlationId\":\"-\","
            + "\"scope\":{\"CorrelationId\":\"x\"},\"category\":\"C\",\"message\":\"json\"}\n");

        var page = await CreateReader().ReadAsync(new LogReadRequest(10));
        var filtered = await CreateReader().ReadAsync(new LogReadRequest(10) { CorrelationId = "x" });

        page.Entries.Select(entry => entry.CorrelationId).Should().AllBeEquivalentTo((string?)null);
        page.Entries.Select(entry => entry.ScopeProperties.Count).Should().AllBeEquivalentTo(0);
        filtered.Entries.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RoundTrip_WhenPropertiesAreDropped_FlagsTheEntryAndKeepsTheMarkerOutOfTheMap(bool json)
    {
        var options = Options(json, "A", "B");
        options.MaxScopePropertyChars = 4;
        Write(options, logger =>
        {
            using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = "c1", ["A"] = "1", ["B"] = "long-value" }))
                logger.LogInformation("hello");
        });

        var entry = (await new LogFileReader(options, _log.Clock).ReadAsync(new LogReadRequest(10))).Entries.Single();
        var exactCut = await new LogFileReader(options, _log.Clock).ReadAsync(
            new LogReadRequest(10) { ScopeFilter = new Dictionary<string, string> { ["B"] = "long" } });

        entry.IsScopeTruncated.Should().BeTrue();
        entry.ScopeProperties.Should().BeEquivalentTo(new Dictionary<string, string> { ["CorrelationId"] = "c1", ["A"] = "1" });
        exactCut.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_WhenOnlyTheMarkerIsPresent_FlagsTheEntryAndKeepsTheLazyMap()
    {
        Directory.CreateDirectory(_log.LogDirectory);
        File.WriteAllText(
            Path.Combine(_log.LogDirectory, "app-2026-06-15.log"),
            "[2026-06-15 09:00:00.000 INF] [c1] {~truncated=1} Cat: with id\n"
            + "[2026-06-15 10:00:00.000 INF] [-] {~truncated=1} Cat: without id\n");

        var page = await CreateReader().ReadAsync(new LogReadRequest(10));

        page.Entries.Select(entry => entry.IsScopeTruncated).Should().AllBeEquivalentTo(true);
        page.Entries[1].ScopeProperties.Should().BeEquivalentTo(new Dictionary<string, string> { ["CorrelationId"] = "c1" });
        page.Entries[0].ScopeProperties.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_ForALineWithoutExtraProperties_DefersTheMapUntilItIsRead()
    {
        Directory.CreateDirectory(_log.LogDirectory);
        File.WriteAllText(
            Path.Combine(_log.LogDirectory, "app-2026-06-15.log"),
            "[2026-06-15 09:00:00.000 INF] [c1] Cat: one\n[2026-06-15 10:00:00.000 INF] [-] Cat: none\n");

        var page = await CreateReader().ReadAsync(new LogReadRequest(10)
        {
            CorrelationId = "c1",
            ScopeFilter = new Dictionary<string, string> { ["CorrelationId"] = "c1" }
        });
        var other = await CreateReader().ReadAsync(
            new LogReadRequest(10) { ScopeFilter = new Dictionary<string, string> { ["RunId"] = "r" } });
        var everything = await CreateReader().ReadAsync(new LogReadRequest(10));

        page.Entries.Single().AreScopePropertiesMaterialized.Should().BeFalse();
        other.Entries.Should().BeEmpty();
        everything.Entries.Should().OnlyContain(entry => !entry.AreScopePropertiesMaterialized);
        everything.Entries[1].ScopeProperties.Should().ContainKey("CorrelationId");
        everything.Entries[1].AreScopePropertiesMaterialized.Should().BeTrue();
    }

    [Fact]
    public async Task LogFileEntry_WhenDecodedTwiceFromTheSameLine_IsEqualByValue()
    {
        Directory.CreateDirectory(_log.LogDirectory);
        File.WriteAllText(
            Path.Combine(_log.LogDirectory, "app-2026-06-15.log"),
            "[2026-06-15 09:00:00.000 INF] [c1] {RunId=r1} Cat: one\n[2026-06-15 10:00:00.000 INF] [c1] {RunId=r2} Cat: one\n");

        var first = (await CreateReader().ReadAsync(new LogReadRequest(10))).Entries;
        var second = (await CreateReader().ReadAsync(new LogReadRequest(10))).Entries;

        first[0].Should().Be(second[0]);
        first[0].GetHashCode().Should().Be(second[0].GetHashCode());
        first[0].Equals((LogFileEntry?)null).Should().BeFalse();
        first[0].Should().NotBe(first[1]);
        (first[0] with { ScopeProperties = new Dictionary<string, string> { ["CorrelationId"] = "c1", ["RunId"] = "other" } })
            .Should().NotBe(first[0]);
        (first[0] with { ScopeProperties = new Dictionary<string, string> { ["CorrelationId"] = "c1" } })
            .Should().NotBe(first[0]);
        (first[0] with { IsScopeTruncated = true }).Should().NotBe(first[0]);
        (first[0] with { RawText = "x" }).Should().NotBe(first[0]);
        (first[0] with { Message = "x" }).Should().NotBe(first[0]);
        (first[0] with { Category = "x" }).Should().NotBe(first[0]);
        (first[0] with { Exception = "x" }).Should().NotBe(first[0]);
        (first[0] with { CorrelationId = "x" }).Should().NotBe(first[0]);
        (first[0] with { Level = LogLevel.Error }).Should().NotBe(first[0]);
        (first[0] with { Timestamp = null }).Should().NotBe(first[0]);
        (first[0] with { Sequence = 1 }).Should().NotBe(first[0]);
        (first[0] with { IsMalformed = true }).Should().NotBe(first[0]);
        (first[0] with { IsTruncated = true }).Should().NotBe(first[0]);
    }

    [Fact]
    public void ScopeDispose_WhenCalledTwice_DoesNotUndoAScopeOpenedSince()
    {
        var lines = Write(Options(false, "RunId"), logger =>
        {
            var first = logger.BeginScope(new Dictionary<string, object> { ["RunId"] = "a" });
            first!.Dispose();
            using (logger.BeginScope(new Dictionary<string, object> { ["RunId"] = "b" }))
            {
                first.Dispose();
                logger.LogInformation("hello");
            }
        });

        lines.Single().Should().Contain("{RunId=b}");
    }

    [Fact]
    public void Scopes_WhenOpenedFromAMessageTemplate_PersistTheTemplateValues()
    {
        var lines = Write(Options(false, "RunId"), logger =>
        {
            using (logger.BeginScope("Run {RunId}", "r-7"))
                logger.LogInformation("hello");
        });

        lines.Single().Should().Contain("{RunId=r-7}");
    }

    [Fact]
    public async Task Scopes_AcrossAnAwaitAndInASiblingTask_StayVisibleAndDoNotLeakIntoTheParent()
    {
        var provider = new BsftFileLoggerProvider(Options(false, "RunId"), _log.LogDirectory, _log.Clock);
        var logger = provider.CreateLogger("Cat");
        using (logger.BeginScope(new Dictionary<string, object> { ["RunId"] = "parent" }))
        {
            await Task.Run(async () =>
            {
                using (logger.BeginScope(new Dictionary<string, object> { ["RunId"] = "child" }))
                {
                    await Task.Delay(5);
                    logger.LogInformation("child");
                }
            });
            await Task.Yield();
            logger.LogInformation("parent");
        }

        provider.Flush();
        provider.Dispose();

        var lines = File.ReadAllLines(Directory.GetFiles(_log.LogDirectory, "app-*.log").Single());
        lines.Should().Contain(line => line.EndsWith("{RunId=child} Cat: child", StringComparison.Ordinal));
        lines.Should().Contain(line => line.EndsWith("{RunId=parent} Cat: parent", StringComparison.Ordinal));
    }

    [Fact]
    public void Options_WhenTheTruncationMarkerIsConfiguredAsAKey_FailValidation()
    {
        var options = new BsftLoggingOptions();
        options.PersistedScopeKeys.Add(ScopeLineEncoder.TruncatedKey);

        var act = options.ValidateFileRules;

        act.Should().Throw<InvalidOperationException>().WithMessage("*PersistedScopeKeys*~truncated*");
    }

    [Fact]
    public void AddBsftLogging_WhenTheSectionNamesKeys_AddsThemToTheCorrelationIdDefault()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Bsft:Logging:PersistedScopeKeys:0"] = "RunId" })
            .Build();
        var services = new ServiceCollection();
        services.AddBsftLogging(configuration);

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<BsftLoggingOptions>>().Value;

        options.PersistedScopeKeys.Should().Equal(LogScopeKeys.CorrelationId, "RunId");
    }

    private BsftLoggingOptions Options(bool json, params string[] keys)
    {
        var options = new BsftLoggingOptions { EnableJsonFormat = json, LogDirectory = _log.LogDirectory };
        foreach (var key in keys)
            options.PersistedScopeKeys.Add(key);

        return options;
    }

    private BsftLoggingOptions Options(bool json) => Options(json, "RunId");

    private string[] Write(BsftLoggingOptions options, Action<ILogger> act)
    {
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory, _log.Clock);
        act(provider.CreateLogger("Cat"));
        provider.Flush();
        provider.Dispose();

        return File.ReadAllText(Directory.GetFiles(_log.LogDirectory, "app-*.log").Single())
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    private LogFileReader CreateReader()
    {
        var options = LogFileFixture.Options();
        options.LogDirectory = _log.LogDirectory;
        return new LogFileReader(options, TimeProvider.System);
    }
}
