using Bsft.Logging.LogFiles;
using Bsft.Logging.Tests.Support;
using FluentAssertions;

namespace Bsft.Logging.Tests.LogFiles;

/// <summary>Covers how a decoded entry keeps its scope properties across copies, comparisons and old or hand-written lines.</summary>
public sealed class LogFileEntryScopeBehaviorTests : IDisposable
{
    private readonly LogFileFixture _fixture = new();

    /// <inheritdoc />
    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task With_CopiesTakenBeforeAndAfterTheFirstRead_AreEqualAndFollowTheirOwnCorrelationId()
    {
        var original = await ReadSingle("[2026-06-15 09:00:00.000 INF] [c1] Cat: m\n");

        var before = original with { CorrelationId = "c2" };
        _ = original.ScopeProperties;
        var after = original with { CorrelationId = "c2" };

        after.Should().Be(before);
        after.ScopeProperties["CorrelationId"].Should().Be("c2");
        before.ScopeProperties["CorrelationId"].Should().Be("c2");
        after.HasScopeValue("CorrelationId", "c2").Should().BeTrue();
        original.ScopeProperties["CorrelationId"].Should().Be("c1");
    }

    [Fact]
    public async Task With_AMapThatWasSupplied_IsKeptByTheCopy()
    {
        var original = await ReadSingle("[2026-06-15 09:00:00.000 INF] [c1] {RunId=r1} Cat: m\n");

        var copy = original with { Message = "other" };

        copy.ScopeProperties.Should().BeEquivalentTo(original.ScopeProperties);
        copy.IsScopeTruncated.Should().BeFalse();
    }

    [Fact]
    public async Task Equals_WhenASuppliedMapUsesAnotherComparer_AgreesInBothDirections()
    {
        var entry = await ReadSingle("[2026-06-15 09:00:00.000 INF] [c1] Cat: m\n");
        var ignoringCase = entry with
        {
            ScopeProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["correlationid"] = "c1" }
        };
        var exact = entry with { ScopeProperties = new Dictionary<string, string> { ["CorrelationId"] = "c1" } };

        ignoringCase.Equals(exact).Should().BeFalse();
        exact.Equals(ignoringCase).Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_WhenANonCorrelationFilterNamesTheCorrelationValue_DoesNotMatchALazyEntry()
    {
        Write("[2026-06-15 09:00:00.000 INF] [c1] Cat: one\n");

        var page = await CreateReader().ReadAsync(
            new LogReadRequest(10) { ScopeFilter = new Dictionary<string, string> { ["RunId"] = "c1" } });

        page.Entries.Should().BeEmpty();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("no")]
    [InlineData("")]
    public async Task ReadAsync_WhenTheMarkerHasAnotherValueThanOne_DoesNotFlagTheEntryAndKeepsItOutOfTheMap(string value)
    {
        Write($"[2026-06-15 09:00:00.000 INF] [c1] {{RunId=r;~truncated={value}}} Cat: m\n");

        var entry = (await CreateReader().ReadAsync(new LogReadRequest(10))).Entries.Single();

        entry.IsScopeTruncated.Should().BeFalse();
        entry.ScopeProperties.Should().BeEquivalentTo(new Dictionary<string, string> { ["CorrelationId"] = "c1", ["RunId"] = "r" });
    }

    [Fact]
    public async Task ReadAsync_ForLinesWrittenBeforeTheSlotWasEscaped_ReadsBackslashesAsEscapes()
    {
        // Pins the documented consequence for files within retention: an id with a backslash in a 2.0.2 line
        // is read through the new escape rule.
        Write(
            "[2026-06-15 09:00:00.000 INF] [a\\] Cat: trailing backslash\n"
            + "[2026-06-15 10:00:00.000 INF] [a\\b] Cat: inner backslash\n");

        var page = await CreateReader().ReadAsync(new LogReadRequest(10));

        page.Entries.Single(entry => entry.IsMalformed).Message.Should().Contain("trailing backslash");
        page.Entries.Single(entry => !entry.IsMalformed).CorrelationId.Should().Be("ab");
    }

    private async Task<LogFileEntry> ReadSingle(string content)
    {
        Write(content);
        return (await CreateReader().ReadAsync(new LogReadRequest(10))).Entries.Single();
    }

    private LogFileReader CreateReader()
    {
        var options = LogFileFixture.Options();
        options.LogDirectory = _fixture.LogDirectory;
        return new LogFileReader(options, TimeProvider.System);
    }

    private void Write(string content)
    {
        Directory.CreateDirectory(_fixture.LogDirectory);
        File.WriteAllText(Path.Combine(_fixture.LogDirectory, "app-2026-06-15.log"), content);
    }
}
