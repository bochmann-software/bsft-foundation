using System.Text;
using Bsft.Logging.LogFiles;
using Bsft.Logging.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace Bsft.Logging.Tests.LogFiles;

public sealed class LogFileReaderTests : IDisposable
{
    private readonly LogFileFixture _fixture = new();

    [Fact]
    public async Task ReadAsync_WhenFilesContainTextAndJson_ReturnsNewestEntriesFirst()
    {
        Write("app-2026-06-14.log", "[2026-06-14 10:00:00.000 INF] [text-correlation] TextCategory: text message\n");
        Write("app-2026-06-15.log", "{\"timestamp\":\"2026-06-15T11:00:00.000+00:00\",\"level\":\"Warning\",\"correlationId\":\"json-correlation\",\"category\":\"Json.Category\",\"message\":\"json message\"}\n");
        var reader = CreateReader();

        var page = await reader.ReadAsync(new LogReadRequest(10));

        page.Entries.Should().HaveCount(2);
        page.Entries[0].Message.Should().Be("json message");
        page.Entries[0].Level.Should().Be(LogLevel.Warning);
        page.Entries[0].Category.Should().Be("Json.Category");
        page.Entries[0].CorrelationId.Should().Be("json-correlation");
        page.Entries[1].Message.Should().Be("text message");
        page.Entries[1].Level.Should().Be(LogLevel.Information);
        page.Entries[1].Category.Should().Be("TextCategory");
        page.Entries[1].CorrelationId.Should().Be("text-correlation");
        page.Entries.Should().BeInDescendingOrder(entry => entry.Sequence);
        page.NextBefore.Should().BeNull();
    }

    [Fact]
    public async Task ReadAsync_WhenTextEntryHasMultilineException_KeepsExceptionWithEntry()
    {
        Write("app-2026-06-15.log", "[2026-06-15 11:00:00.000 ERR] [corr] Category: failed\nSystem.InvalidOperationException: broken\n   at Example.Run()\n");

        var page = await CreateReader().ReadAsync(new LogReadRequest(10));

        page.Entries.Should().ContainSingle();
        page.Entries[0].Exception.Should().Be("System.InvalidOperationException: broken" + Environment.NewLine + "   at Example.Run()");
        page.Entries[0].RawText.Should().EndWith("   at Example.Run()");
        page.Entries[0].IsMalformed.Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_WhenLineIsMalformed_ReturnsFallbackEntry()
    {
        Write("app-2026-06-15.log", "not a supported log record\n");

        var page = await CreateReader().ReadAsync(new LogReadRequest(10));

        page.Entries.Should().ContainSingle();
        page.Entries[0].Message.Should().Be("not a supported log record");
        page.Entries[0].RawText.Should().Be("not a supported log record");
        page.Entries[0].IsMalformed.Should().BeTrue();
        page.Entries[0].Level.Should().Be(LogLevel.None);
    }

    [Fact]
    public async Task ReadAsync_WhenAStrayLineFollowsAJsonEntry_ReturnsItAsItsOwnMalformedEntry()
    {
        Write(
            "app-2026-06-15.log",
            "{\"timestamp\":\"2026-06-15T11:00:00.000+00:00\",\"level\":\"Warning\",\"category\":\"Json.Category\",\"message\":\"json message\"}\n"
            + "stray output\n");

        var page = await CreateReader().ReadAsync(new LogReadRequest(10));

        page.Entries.Should().HaveCount(2);
        page.Entries[0].IsMalformed.Should().BeTrue();
        page.Entries[0].RawText.Should().Be("stray output");
        page.Entries[1].Message.Should().Be("json message");
        page.Entries[1].IsMalformed.Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_WhenTheFileIsEmpty_ReturnsEmptyPage()
    {
        Write("app-2026-06-15.log", string.Empty);

        (await CreateReader().ReadAsync(new LogReadRequest(10))).Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_WhenPageEnds_ReturnsCursorForNextOlderPage()
    {
        Write("app-2026-06-15.log", "[2026-06-15 09:00:00.000 INF] [-] Category: first\n[2026-06-15 10:00:00.000 INF] [-] Category: second\n[2026-06-15 11:00:00.000 INF] [-] Category: third\n");
        var reader = CreateReader();

        var first = await reader.ReadAsync(new LogReadRequest(2));
        var second = await reader.ReadAsync(new LogReadRequest(2, first.NextBefore));

        first.Entries.Select(entry => entry.Message).Should().Equal("third", "second");
        first.NextBefore.Should().NotBeNull();
        second.Entries.Select(entry => entry.Message).Should().Equal("first");
        second.NextBefore.Should().BeNull();
    }

    [Fact]
    public async Task ReadAsync_WhenPageCrossesRolledFiles_ContinuesWithOlderFile()
    {
        Write("app-2026-06-15.log", "[2026-06-15 09:00:00.000 INF] [-] Category: base\n");
        Write("app-2026-06-15_001.log", "[2026-06-15 10:00:00.000 WRN] [-] Category: rolled\n");
        var reader = CreateReader();

        var first = await reader.ReadAsync(new LogReadRequest(1));
        var second = await reader.ReadAsync(new LogReadRequest(1, first.NextBefore));

        first.Entries.Select(entry => entry.Message).Should().Equal("rolled");
        second.Entries.Select(entry => entry.Message).Should().Equal("base");
    }

    [Fact]
    public async Task ReadAsync_WhenFiltersArePresent_AppliesLevelAndCaseInsensitiveSearch()
    {
        Write(
            "app-2026-06-15.log",
            "[2026-06-15 09:00:00.000 INF] [-] Category: needle in information\n"
            + "[2026-06-15 10:00:00.000 WRN] [-] Category: no match\n"
            + "[2026-06-15 11:00:00.000 ERR] [-] Category: failed\nNEEDLE exception\n");

        var page = await CreateReader().ReadAsync(new LogReadRequest(
            MinimumLevel: LogLevel.Warning,
            IncludedLevels: new HashSet<LogLevel> { LogLevel.Error },
            SearchText: "needle"));

        page.Entries.Should().ContainSingle();
        page.Entries[0].Level.Should().Be(LogLevel.Error);
        page.Entries[0].Exception.Should().Contain("NEEDLE");
    }

    [Fact]
    public async Task ReadAsync_WhenDirectoryIsMissingOrEmpty_ReturnsEmptyPage()
    {
        var reader = CreateReader();

        (await reader.ReadAsync(new LogReadRequest())).Entries.Should().BeEmpty();
        Directory.CreateDirectory(_fixture.LogDirectory);
        (await reader.ReadAsync(new LogReadRequest())).Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_WhenReverseReadIsCancelled_CancelsTheAsynchronousIo()
    {
        var access = new SingleStreamLogFileAccess(new CancellationBlockingReadStream());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var act = () => CreateReader(access).ReadAsync(new LogReadRequest(), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ReadAsync_WhenEntryCrossesReverseChunkBoundary_ParsesWithoutLoadingWholeFile()
    {
        var message = new string('x', 70_000);
        Write("app-2026-06-15.log", $"[2026-06-15 11:00:00.000 INF] [-] Category: {message}\n");

        var page = await CreateReader().ReadAsync(new LogReadRequest(1));

        page.Entries.Should().ContainSingle();
        page.Entries[0].Message.Should().Be(message);
    }

    [Fact]
    public async Task ReadAsync_WhenEntryExceedsConfiguredLimit_ReturnsBoundedTruncatedEntry()
    {
        const int limit = 128;
        Write("app-2026-06-15.log", $"[2026-06-15 11:00:00.000 INF] [-] Category: {new string('x', 500)}\n");
        var options = LogFileFixture.Options();
        options.LogDirectory = _fixture.LogDirectory;
        options.MaxEntryBytes = limit;

        var page = await new LogFileReader(options, _fixture.Clock).ReadAsync(new LogReadRequest(1));

        page.Entries.Should().ContainSingle();
        page.Entries[0].IsMalformed.Should().BeFalse();
        page.Entries[0].IsTruncated.Should().BeTrue();
        Encoding.UTF8.GetByteCount(page.Entries[0].RawText).Should().BeLessThanOrEqualTo(limit);
    }

    [Theory]
    [InlineData("äö", 1)]
    [InlineData("😀x", 3)]
    public async Task ReadAsync_WhenPhysicalLineLimitSplitsAMultibyteCharacter_TruncatesAtAValidUtf8Boundary(
        string message,
        int partialByteCount)
    {
        const string header = "[2026-06-15 11:00:00.000 INF] [-] Category: ";
        var options = LogFileFixture.Options();
        options.LogDirectory = _fixture.LogDirectory;
        options.MaxEntryBytes = Encoding.UTF8.GetByteCount(header) + partialByteCount;
        Write("app-2026-06-15.log", $"{header}{message}\n");

        var page = await new LogFileReader(options, _fixture.Clock).ReadAsync(new LogReadRequest(1));

        page.Entries.Should().ContainSingle();
        page.Entries[0].RawText.Should().Be(header);
        page.Entries[0].RawText.Should().NotContain("�");
        page.Entries[0].IsTruncated.Should().BeTrue();
    }

    [Theory]
    [InlineData("one", 0)]
    [InlineData("abcdefghijklmnopqrst", 5)]
    public async Task ReadAsync_WhenExceptionExceedsRemainingEntryBudget_BoundsCombinedEntry(
        string continuation,
        int continuationBudget)
    {
        const string header = "[2026-06-15 11:00:00.000 ERR] [-] Category: failed";
        var newlineBytes = Encoding.UTF8.GetByteCount(Environment.NewLine);
        var limit = Encoding.UTF8.GetByteCount(header) + (continuationBudget == 0
            ? 0
            : newlineBytes + continuationBudget);
        Write("app-2026-06-15.log", $"{header}\n{continuation}\n");
        var options = LogFileFixture.Options();
        options.LogDirectory = _fixture.LogDirectory;
        options.MaxEntryBytes = limit;

        var page = await new LogFileReader(options, _fixture.Clock).ReadAsync(new LogReadRequest(1));

        page.Entries.Should().ContainSingle();
        page.Entries[0].IsMalformed.Should().BeFalse();
        page.Entries[0].Message.Should().Be("failed");
        page.Entries[0].IsTruncated.Should().BeTrue();
        Encoding.UTF8.GetByteCount(page.Entries[0].RawText).Should().BeLessThanOrEqualTo(limit);
    }

    [Fact]
    public async Task ReadAsync_WhenExceptionBudgetCannotFitNextSeparator_StopsBeforeSeparator()
    {
        const string header = "[2026-06-15 11:00:00.000 ERR] [-] Category: failed with padded message";
        var separatorBytes = Encoding.UTF8.GetByteCount(Environment.NewLine);
        var limit = Encoding.UTF8.GetByteCount(header) + (2 * separatorBytes);
        Write("app-2026-06-15.log", $"{header}\nx\nsecond\n");
        var options = LogFileFixture.Options();
        options.LogDirectory = _fixture.LogDirectory;
        options.MaxEntryBytes = limit;

        var page = await new LogFileReader(options, _fixture.Clock).ReadAsync(new LogReadRequest(1));

        page.Entries.Should().ContainSingle();
        page.Entries[0].Exception.Should().Be("x");
        page.Entries[0].IsTruncated.Should().BeTrue();
        Encoding.UTF8.GetByteCount(page.Entries[0].RawText).Should().BeLessThanOrEqualTo(limit);
    }

    [Fact]
    public async Task ReadAsync_WhenExceptionBudgetOnlyFitsSeparator_StopsBeforeSeparator()
    {
        const string header = "[2026-06-15 11:00:00.000 ERR] [-] Category: failed with padded message";
        var separatorBytes = Encoding.UTF8.GetByteCount(Environment.NewLine);
        var limit = Encoding.UTF8.GetByteCount(header) + (2 * separatorBytes) + 1;
        Write("app-2026-06-15.log", $"{header}\nx\nsecond\n");
        var options = LogFileFixture.Options();
        options.LogDirectory = _fixture.LogDirectory;
        options.MaxEntryBytes = limit;

        var page = await new LogFileReader(options, _fixture.Clock).ReadAsync(new LogReadRequest(1));

        page.Entries.Should().ContainSingle();
        page.Entries[0].Exception.Should().Be("x");
        page.Entries[0].IsTruncated.Should().BeTrue();
        Encoding.UTF8.GetByteCount(page.Entries[0].RawText).Should().BeLessThanOrEqualTo(limit);
    }

    [Fact]
    public async Task ReadAsync_WhenManyEmptyContinuationsExceedBufferBudget_MarksEntryTruncated()
    {
        const int limit = 128;
        Write(
            "app-2026-06-15.log",
            "[2026-06-15 11:00:00.000 ERR] [-] Category: failed\n" + new string('\n', 10));
        var options = LogFileFixture.Options();
        options.LogDirectory = _fixture.LogDirectory;
        options.MaxEntryBytes = limit;

        var page = await new LogFileReader(options, _fixture.Clock).ReadAsync(new LogReadRequest(1));

        page.Entries.Should().ContainSingle();
        page.Entries[0].IsTruncated.Should().BeTrue();
    }

    [Fact]
    public async Task ReadAsync_WhenWriterRestarts_AppendedEntryRemainsNewestAcrossRolls()
    {
        Write("app-2026-06-15.log", "[2026-06-15 09:00:00.000 INF] [-] Category: base\n");
        Write("app-2026-06-15_001.log", "[2026-06-15 10:00:00.000 INF] [-] Category: rolled\n");
        var options = LogFileFixture.Options();
        options.LogDirectory = _fixture.LogDirectory;
        using (var provider = new BsftFileLoggerProvider(options, _fixture.Clock))
        {
            provider.CreateLogger("Category").LogInformation("after restart");
            provider.Flush();
        }

        var page = await CreateReader().ReadAsync(new LogReadRequest(10));

        page.Entries.Select(entry => entry.Message).Should().Equal("after restart", "rolled", "base");
    }

    [Fact]
    public async Task TailAsync_WhenEntryIsAppendedAfterCursor_YieldsIt()
    {
        Write("app-2026-06-15.log", "[2026-06-15 10:00:00.000 INF] [-] Category: existing\n");
        var options = LogFileFixture.Options();
        options.LogDirectory = _fixture.LogDirectory;
        options.LogReaderPollInterval = TimeSpan.FromMilliseconds(10);
        var reader = new LogFileReader(options, TimeProvider.System);
        var current = await reader.ReadAsync(new LogReadRequest(1));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var tail = reader.TailAsync(
                new LogReadRequest(Before: current.Entries[0].Sequence),
                cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        var pending = tail.MoveNextAsync().AsTask();
        await Task.Delay(30, cancellation.Token);
        File.AppendAllText(
            Path.Combine(_fixture.LogDirectory, "app-2026-06-15.log"),
            "[2026-06-15 11:00:00.000 INF] [-] Category: appended\n",
            new UTF8Encoding(false));

        (await pending).Should().BeTrue();
        tail.Current.Message.Should().Be("appended");
        tail.Current.Sequence.Should().BeGreaterThan(current.Entries[0].Sequence);
    }

    [Fact]
    public async Task TailAsync_WithoutACursor_StartsAfterTheNewestExistingEntry()
    {
        Write("app-2026-06-15.log", "[2026-06-15 10:00:00.000 INF] [-] Category: existing\n");
        var options = LogFileFixture.Options();
        options.LogDirectory = _fixture.LogDirectory;
        options.LogReaderPollInterval = TimeSpan.FromMilliseconds(10);
        var reader = new LogFileReader(options, TimeProvider.System);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var tail = reader.TailAsync(new LogReadRequest(), cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        var pending = tail.MoveNextAsync().AsTask();
        await Task.Delay(30, cancellation.Token);
        File.AppendAllText(
            Path.Combine(_fixture.LogDirectory, "app-2026-06-15.log"),
            "[2026-06-15 11:00:00.000 INF] [-] Category: appended\n",
            new UTF8Encoding(false));

        (await pending).Should().BeTrue();
        tail.Current.Message.Should().Be("appended");
    }

    [Fact]
    public async Task TailAsync_WhenFileIsDeletedAndRolledFileAppears_ContinuesWithMatchingEntry()
    {
        const string baseName = "app-2026-06-15.log";
        Write(baseName, "[2026-06-15 10:00:00.000 INF] [-] Category: existing\n");
        var options = LogFileFixture.Options();
        options.LogDirectory = _fixture.LogDirectory;
        options.LogReaderPollInterval = TimeSpan.FromMilliseconds(10);
        var reader = new LogFileReader(options, TimeProvider.System);
        var current = await reader.ReadAsync(new LogReadRequest(1));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var tail = reader.TailAsync(
                new LogReadRequest(
                    Before: current.Entries[0].Sequence,
                    SearchText: "wanted"),
                cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        var pending = tail.MoveNextAsync().AsTask();
        File.Delete(Path.Combine(_fixture.LogDirectory, baseName));
        Write("app-2026-06-15_001.log", "[2026-06-15 11:00:00.000 WRN] [-] Category: wanted\n");

        (await pending).Should().BeTrue();
        tail.Current.Message.Should().Be("wanted");
    }

    [Fact]
    public async Task TailAsync_WhenCancelled_StopsPolling()
    {
        var options = LogFileFixture.Options();
        options.LogDirectory = _fixture.LogDirectory;
        options.LogReaderPollInterval = TimeSpan.FromSeconds(1);
        var reader = new LogFileReader(options, TimeProvider.System);
        using var cancellation = new CancellationTokenSource();
        await using var tail = reader.TailAsync(new LogReadRequest(), cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        var pending = tail.MoveNextAsync().AsTask();
        cancellation.Cancel();

        var act = async () => await pending;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task TailAsync_WhenBurstIsAvailable_YieldsStrictlyAscendingSequences()
    {
        Write(
            "app-2026-06-15.log",
            "[2026-06-15 09:00:00.000 INF] [-] Category: first\n"
            + "[2026-06-15 10:00:00.000 INF] [-] Category: second\n"
            + "[2026-06-15 11:00:00.000 INF] [-] Category: third\n");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var tail = CreateReader().TailAsync(new LogReadRequest(Before: 0), cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);
        var entries = new List<LogFileEntry>();

        for (var index = 0; index < 3; index++)
        {
            (await tail.MoveNextAsync()).Should().BeTrue();
            entries.Add(tail.Current);
        }

        entries.Select(entry => entry.Message).Should().Equal("first", "second", "third");
        entries.Should().BeInAscendingOrder(entry => entry.Sequence);
        entries.Select(entry => entry.Sequence).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task TailAsync_WhenEntryExceedsConfiguredLimit_YieldsBoundedTruncatedEntry()
    {
        const int limit = 128;
        Write("app-2026-06-15.log", $"[2026-06-15 11:00:00.000 INF] [-] Category: {new string('x', 500)}\n");
        var options = LogFileFixture.Options();
        options.LogDirectory = _fixture.LogDirectory;
        options.MaxEntryBytes = limit;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var tail = new LogFileReader(options, _fixture.Clock)
            .TailAsync(new LogReadRequest(Before: 0), cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        (await tail.MoveNextAsync()).Should().BeTrue();
        tail.Current.IsTruncated.Should().BeTrue();
        Encoding.UTF8.GetByteCount(tail.Current.RawText).Should().BeLessThanOrEqualTo(limit);
    }

    [Fact]
    public async Task TailAsync_WhenContinuationExceedsConfiguredLimit_YieldsBoundedTruncatedEntry()
    {
        const string header = "[2026-06-15 11:00:00.000 ERR] [-] Category: failed";
        var limit = Encoding.UTF8.GetByteCount(header) + 32;
        Write("app-2026-06-15.log", $"{header}\n{new string('x', 500)}\n");
        var options = LogFileFixture.Options();
        options.LogDirectory = _fixture.LogDirectory;
        options.MaxEntryBytes = limit;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var tail = new LogFileReader(options, _fixture.Clock)
            .TailAsync(new LogReadRequest(Before: 0), cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        (await tail.MoveNextAsync()).Should().BeTrue();
        tail.Current.Message.Should().Be("failed");
        tail.Current.IsTruncated.Should().BeTrue();
        Encoding.UTF8.GetByteCount(tail.Current.RawText).Should().BeLessThanOrEqualTo(limit);
    }

    [Fact]
    public async Task ReadAsync_WhenLastLineIsStillBeingWritten_IgnoresTheIncompleteFragment()
    {
        Write(
            "app-2026-06-15.log",
            "[2026-06-15 10:00:00.000 INF] [-] Category: complete\n"
            + "[2026-06-15 11:00:00.000 INF] [-] Category: incomplete");

        var page = await CreateReader().ReadAsync(new LogReadRequest(10));

        page.Entries.Select(entry => entry.Message).Should().Equal("complete");
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task ReadAsync_WhenLineUsesExplicitTerminator_DoesNotIncludeTerminator(string terminator)
    {
        const string line = "[2026-06-15 10:00:00.000 INF] [-] Category: message";
        WriteBytes("app-2026-06-15.log", Encoding.UTF8.GetBytes(line + terminator));

        var page = await CreateReader().ReadAsync(new LogReadRequest(1));

        page.Entries.Should().ContainSingle();
        page.Entries[0].RawText.Should().Be(line);
        page.Entries[0].Message.Should().Be("message");
    }

    [Theory]
    [InlineData("TRC", LogLevel.Trace)]
    [InlineData("DBG", LogLevel.Debug)]
    [InlineData("WRN", LogLevel.Warning)]
    [InlineData("CRT", LogLevel.Critical)]
    public async Task ReadAsync_WhenTextUsesLevelAbbreviation_MapsLevel(string abbreviation, LogLevel expected)
    {
        Write("app-2026-06-15.log", $"[2026-06-15 11:00:00.000 {abbreviation}] [-] Category: message\n");

        var page = await CreateReader().ReadAsync(new LogReadRequest(1));

        page.Entries[0].Level.Should().Be(expected);
    }

    [Fact]
    public async Task ReadAsync_WhenJsonShapeIsIncomplete_ReturnsMalformedFallback()
    {
        Write("app-2026-06-15.log", "{\"message\":\"missing metadata\"}\n");

        var page = await CreateReader().ReadAsync(new LogReadRequest(1));

        page.Entries[0].IsMalformed.Should().BeTrue();
    }

    [Theory]
    [InlineData("123", "\"message\"")]
    [InlineData("{}", "\"message\"")]
    [InlineData("null", "\"message\"")]
    [InlineData("\"Information\"", "123")]
    [InlineData("\"Information\"", "{}")]
    [InlineData("\"Information\"", "null")]
    public async Task ReadAsync_WhenJsonLevelOrMessageHasWrongKind_ReturnsMalformedFallback(
        string level,
        string message)
    {
        Write(
            "app-2026-06-15.log",
            $"{{\"timestamp\":\"2026-06-15T11:00:00Z\",\"level\":{level},\"message\":{message}}}\n");

        var page = await CreateReader().ReadAsync(new LogReadRequest(1));

        page.Entries.Should().ContainSingle();
        page.Entries[0].IsMalformed.Should().BeTrue();
    }

    [Fact]
    public async Task ReadAsync_WhenDiscoveredFileDisappears_ReturnsEmptyPage()
    {
        var access = new ThrowingLogFileAccess(new FileNotFoundException());

        var page = await CreateReader(access).ReadAsync(new LogReadRequest());

        page.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_WhenDiscoveredDirectoryDisappears_ReturnsEmptyPage()
    {
        var access = new ThrowingLogFileAccess(new DirectoryNotFoundException());

        var page = await CreateReader(access).ReadAsync(new LogReadRequest());

        page.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_WhenDirectoryDisappearsDuringEnumeration_ReturnsEmptyPage()
    {
        var access = new DisappearingDirectoryLogFileAccess();

        var page = await CreateReader(access).ReadAsync(new LogReadRequest());

        page.Entries.Should().BeEmpty();
    }

    [Fact]
    public void LogFileDescriptor_WithSameValues_HasValueEquality()
    {
        var date = new DateOnly(2026, 6, 15);

        new LogFileDescriptor("path", date, 7)
            .Should().Be(new LogFileDescriptor("path", date, 7));
    }

    [Fact]
    public void LogFileSequence_AtMaximumFields_RemainsPositiveAndOrdered()
    {
        var date = DateOnly.MaxValue;
        var beforeRoll = LogFileSequence.Of(
            new LogFileDescriptor("path", date, LogFileSequence.MaximumRollIndex - 1),
            LogFileSequence.MaximumOffset);
        var maximum = LogFileSequence.Of(
            new LogFileDescriptor("path", date, LogFileSequence.MaximumRollIndex),
            LogFileSequence.MaximumOffset);

        maximum.Should().BePositive();
        maximum.Should().BeGreaterThan(beforeRoll);
    }

    [Fact]
    public void LogFileSequence_BetweenMaximumRollAndNextDay_PreservesOrdering()
    {
        var day = new DateOnly(2026, 6, 15);
        var endOfDay = LogFileSequence.Of(
            new LogFileDescriptor("path", day, LogFileSequence.MaximumRollIndex),
            LogFileSequence.MaximumOffset);
        var nextDay = LogFileSequence.Of(
            new LogFileDescriptor("path", day.AddDays(1), 0),
            0);

        nextDay.Should().BeGreaterThan(endOfDay);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2048)]
    public void LogFileSequence_WhenRollIndexIsOutsideLayout_ThrowsInvalidDataException(int rollIndex)
    {
        var descriptor = new LogFileDescriptor("path", new DateOnly(2026, 6, 15), rollIndex);

        var act = () => LogFileSequence.Of(descriptor, 0);

        act.Should().Throw<InvalidDataException>().WithMessage("*roll index*");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1073741824)]
    public void LogFileSequence_WhenOffsetIsOutsideLayout_ThrowsInvalidDataException(long offset)
    {
        var descriptor = new LogFileDescriptor("path", new DateOnly(2026, 6, 15), 0);

        var act = () => LogFileSequence.Of(descriptor, offset);

        act.Should().Throw<InvalidDataException>().WithMessage("*byte offset*");
    }

    [Fact]
    public async Task ReadAsync_WhenFileNamesDoNotCarryValidDateAndRoll_IgnoresThem()
    {
        Write("app-invalid.log", "ignored\n");
        Write("app-2026-06-15_invalid.log", "ignored\n");
        Write("app-2026-06-15.log", "[2026-06-15 11:00:00.000 INF] [-] Category: included\n");

        var page = await CreateReader().ReadAsync(new LogReadRequest(10));

        page.Entries.Select(entry => entry.Message).Should().Equal("included");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public async Task ReadAsync_WhenPageSizeIsOutsideBounds_ThrowsArgumentOutOfRangeException(int pageSize)
    {
        var act = () => CreateReader().ReadAsync(new LogReadRequest(pageSize));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    private LogFileReader CreateReader(ILogFileAccess? fileAccess = null)
    {
        var options = LogFileFixture.Options();
        options.LogDirectory = _fixture.LogDirectory;
        return new LogFileReader(options, _fixture.Clock, fileAccess);
    }

    private void Write(string fileName, string content)
    {
        Directory.CreateDirectory(_fixture.LogDirectory);
        File.WriteAllText(Path.Combine(_fixture.LogDirectory, fileName), content, new UTF8Encoding(false));
    }

    private void WriteBytes(string fileName, byte[] content)
    {
        Directory.CreateDirectory(_fixture.LogDirectory);
        File.WriteAllBytes(Path.Combine(_fixture.LogDirectory, fileName), content);
    }

    public void Dispose() => _fixture.Dispose();

    private sealed class ThrowingLogFileAccess(Exception exception) : ILogFileAccess
    {
        public bool IsDirectoryPresent(string path) => true;

        public IEnumerable<string> EnumerateFiles(string path, string searchPattern)
            => [Path.Combine(path, "app-2026-06-15.log")];

        public Stream OpenRead(string path) => throw exception;
    }

    private sealed class SingleStreamLogFileAccess(Stream stream) : ILogFileAccess
    {
        public bool IsDirectoryPresent(string path) => true;

        public IEnumerable<string> EnumerateFiles(string path, string searchPattern)
            => [Path.Combine(path, "app-2026-06-15.log")];

        public Stream OpenRead(string path) => stream;
    }

    private sealed class DisappearingDirectoryLogFileAccess : ILogFileAccess
    {
        public bool IsDirectoryPresent(string path) => true;

        public IEnumerable<string> EnumerateFiles(string path, string searchPattern)
            => throw new DirectoryNotFoundException();

        public Stream OpenRead(string path) => throw new NotSupportedException();
    }

    private sealed class CancellationBlockingReadStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => 1;

        public override long Position { get; set; }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
            => throw new InvalidOperationException("Reverse reads must not use synchronous I/O.");

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
            => new(WaitForCancellationAsync(cancellationToken));

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private static async Task<int> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
