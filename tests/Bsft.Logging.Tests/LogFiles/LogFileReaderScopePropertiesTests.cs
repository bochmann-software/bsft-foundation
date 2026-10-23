using Bsft.Logging.LogFiles;
using Bsft.Logging.Tests.Support;
using FluentAssertions;

namespace Bsft.Logging.Tests.LogFiles;

/// <summary>Covers decoding scope properties, lines from before they existed, and the generic scope filter.</summary>
public sealed class LogFileReaderScopePropertiesTests : IDisposable
{
    private const string Text = "app-2026-06-15.log";

    private readonly LogFileFixture _fixture = new();

    /// <inheritdoc />
    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task ReadAsync_WhenLinesPredateScopeProperties_DecodesThemWithOnlyTheCorrelationId()
    {
        Write(
            Text,
            "[2026-06-15 09:00:00.000 INF] [abc] Category: old text\n"
            + "[2026-06-15 10:00:00.000 INF] [-] Category: old uncorrelated\n"
            + "{\"timestamp\":\"2026-06-15T11:00:00.000+00:00\",\"level\":\"Information\",\"correlationId\":\"j1\",\"category\":\"C\",\"message\":\"old json\"}\n"
            + "{\"timestamp\":\"2026-06-15T12:00:00.000+00:00\",\"level\":\"Information\",\"correlationId\":\"-\",\"category\":\"C\",\"message\":\"old json none\"}\n");

        var page = await CreateReader().ReadAsync(new LogReadRequest(10));

        page.Entries.Select(entry => entry.IsMalformed).Should().AllBeEquivalentTo(false);
        page.Entries[3].ScopeProperties.Should().BeEquivalentTo(new Dictionary<string, string> { ["CorrelationId"] = "abc" });
        page.Entries[2].ScopeProperties.Should().BeEmpty();
        page.Entries[1].ScopeProperties.Should().BeEquivalentTo(new Dictionary<string, string> { ["CorrelationId"] = "j1" });
        page.Entries[0].ScopeProperties.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_WhenATextLineCarriesAPropertyBlock_DecodesEscapesAndKeepsCategoryAndMessage()
    {
        Write(
            Text,
            "[2026-06-15 09:00:00.000 INF] [c1] {A=1;B=x\\;y\\=z\\}\\\\;Empty=} Category: msg {not=scope}\n");

        var entry = (await CreateReader().ReadAsync(new LogReadRequest(10))).Entries.Single();

        entry.Category.Should().Be("Category");
        entry.Message.Should().Be("msg {not=scope}");
        entry.ScopeProperties.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["CorrelationId"] = "c1",
            ["A"] = "1",
            ["B"] = "x;y=z}\\",
            ["Empty"] = string.Empty
        });
    }

    [Fact]
    public async Task ReadAsync_WhenABraceCategoryIsNoPropertyBlock_KeepsItAsTheCategory()
    {
        Write(Text, "[2026-06-15 09:00:00.000 INF] [-] {Generic}: msg\n");

        var entry = (await CreateReader().ReadAsync(new LogReadRequest(10))).Entries.Single();

        entry.Category.Should().Be("{Generic}");
        entry.ScopeProperties.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_WhenAJsonLineCarriesAScopeObject_DecodesStringMembersOnly()
    {
        Write(
            Text,
            "{\"timestamp\":\"2026-06-15T11:00:00.000+00:00\",\"level\":\"Information\",\"correlationId\":\"j1\","
            + "\"scope\":{\"RunId\":\"r1\",\"Count\":3},\"category\":\"C\",\"message\":\"m\"}\n"
            + "{\"timestamp\":\"2026-06-15T12:00:00.000+00:00\",\"level\":\"Information\",\"scope\":\"nope\",\"category\":\"C\",\"message\":\"n\"}\n");

        var page = await CreateReader().ReadAsync(new LogReadRequest(10));

        page.Entries[1].ScopeProperties.Should().BeEquivalentTo(
            new Dictionary<string, string> { ["CorrelationId"] = "j1", ["RunId"] = "r1" });
        page.Entries[0].ScopeProperties.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_WhenAScopeFilterHasSeveralPairs_RequiresAllOfThemExactly()
    {
        Write(
            Text,
            "[2026-06-15 09:00:00.000 INF] [c1] {A=1;B=2} Category: both\n"
            + "[2026-06-15 10:00:00.000 INF] [c1] {A=1;B=3} Category: wrong b\n"
            + "[2026-06-15 11:00:00.000 INF] [c1] {A=1} Category: no b\n"
            + "[2026-06-15 12:00:00.000 INF] [c1] {A=10;B=2} Category: wrong a\n"
            + "[2026-06-15 13:00:00.000 INF] [c1] Category: none\n");

        var page = await CreateReader().ReadAsync(new LogReadRequest(10)
        {
            ScopeFilter = new Dictionary<string, string> { ["A"] = "1", ["B"] = "2" }
        });

        page.Entries.Select(entry => entry.Message).Should().Equal("both");
    }

    [Fact]
    public async Task ReadAsync_WhenTheScopeFilterIsNullOrEmpty_AppliesNoFilter()
    {
        Write(Text, "[2026-06-15 09:00:00.000 INF] [c1] {A=1} Category: one\n[2026-06-15 10:00:00.000 INF] [-] Category: two\n");

        var withNull = await CreateReader().ReadAsync(new LogReadRequest(10) { ScopeFilter = null });
        var withEmpty = await CreateReader().ReadAsync(
            new LogReadRequest(10) { ScopeFilter = new Dictionary<string, string>() });

        withNull.Entries.Should().HaveCount(2);
        withEmpty.Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task ReadAsync_WhenCorrelationIdAndTheScopeFilterDisagree_ReturnsNothing()
    {
        Write(Text, "[2026-06-15 09:00:00.000 INF] [c1] Category: one\n");

        var page = await CreateReader().ReadAsync(new LogReadRequest(10)
        {
            CorrelationId = "c1",
            ScopeFilter = new Dictionary<string, string> { ["CorrelationId"] = "c2" }
        });

        page.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task TailAsync_WhenAScopeFilterIsSet_YieldsOnlyMatchingNewEntries()
    {
        Write(Text, "[2026-06-15 10:00:00.000 INF] [-] Category: existing\n");
        var options = LogFileFixture.Options();
        options.LogDirectory = _fixture.LogDirectory;
        options.LogReaderPollInterval = TimeSpan.FromMilliseconds(10);
        var reader = new LogFileReader(options, TimeProvider.System);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var tail = reader.TailAsync(
                new LogReadRequest { ScopeFilter = new Dictionary<string, string> { ["RunId"] = "mine" } },
                cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        var pending = tail.MoveNextAsync().AsTask();
        await Task.Delay(50, cancellation.Token);
        File.AppendAllText(
            Path.Combine(_fixture.LogDirectory, Text),
            "[2026-06-15 11:00:00.000 INF] [-] {RunId=theirs} Category: foreign\n"
            + "[2026-06-15 12:00:00.000 INF] [-] {RunId=mine} Category: appended\n");

        (await pending).Should().BeTrue();
        tail.Current.Message.Should().Be("appended");
    }

    private LogFileReader CreateReader()
    {
        var options = LogFileFixture.Options();
        options.LogDirectory = _fixture.LogDirectory;
        return new LogFileReader(options, TimeProvider.System);
    }

    private void Write(string name, string content)
    {
        Directory.CreateDirectory(_fixture.LogDirectory);
        File.WriteAllText(Path.Combine(_fixture.LogDirectory, name), content);
    }
}
