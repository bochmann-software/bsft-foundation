using Bsft.Hosting.Logging;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Bsft.Hosting.Tests.Logging;

public class ConsoleFormatterTests
{
    private static BsftConsoleFormatter CreateFormatter(bool useUtc = false)
    {
        var options = Substitute.For<IOptionsMonitor<BsftConsoleFormatterOptions>>();
        var formatterOptions = new BsftConsoleFormatterOptions { UseUtcTimestamp = useUtc };
        options.CurrentValue.Returns(formatterOptions);
        options.OnChange(Arg.Any<Action<BsftConsoleFormatterOptions, string?>>()).Returns((IDisposable?)null);
        return new BsftConsoleFormatter(options);
    }

    [Fact]
    public void Write_WithNullMessage_WritesNothing()
    {
        // Exercises BsftConsoleFormatter.Write line 42-43: if (message is null) return.
        using var formatter = CreateFormatter();
        var writer = new StringWriter();

        var entry = new LogEntry<string>(
            LogLevel.Information,
            "TestCategory",
            new EventId(0),
            "state",
            null,
            (state, ex) => null!);

        formatter.Write(entry, null, writer);

        writer.ToString().Should().BeEmpty();
    }

    [Fact]
    public void Write_WithValidMessage_WritesFormattedLine()
    {
        // Exercises BsftConsoleFormatter.Write lines 41-64: full formatting path.
        using var formatter = CreateFormatter();
        var writer = new StringWriter();

        var entry = new LogEntry<string>(
            LogLevel.Information,
            "My.App.OrderService",
            new EventId(0),
            "state",
            null,
            (state, ex) => "test message");

        formatter.Write(entry, null, writer);

        var output = writer.ToString();
        output.Should().Contain(" INF]");
        output.Should().Contain("[-]");
        output.Should().Contain("OrderService: test message");
    }

    [Fact]
    public void Write_WithException_IncludesExceptionInOutput()
    {
        // Exercises BsftConsoleFormatter.Write lines 63-64: exception output.
        using var formatter = CreateFormatter();
        var writer = new StringWriter();
        var exception = new InvalidOperationException("boom");

        var entry = new LogEntry<string>(
            LogLevel.Error,
            "TestCategory",
            new EventId(0),
            "state",
            exception,
            (state, ex) => "error occurred");

        formatter.Write(entry, null, writer);

        var output = writer.ToString();
        output.Should().Contain("error occurred");
        output.Should().Contain("InvalidOperationException");
        output.Should().Contain("boom");
    }

    [Fact]
    public void Write_WithCorrelationIdInReadOnlyListScope_IncludesCorrelationId()
    {
        // Exercises BsftConsoleFormatter.GetCorrelationId lines 92-99: IReadOnlyList branch with CorrelationId.
        using var formatter = CreateFormatter();
        var writer = new StringWriter();

        // Use a List<KVP> which implements IReadOnlyList<KVP<string,object>>
        var kvpList = new List<KeyValuePair<string, object>>
        {
            new("CorrelationId", (object)"readonlylist-corr-id")
        };
        var scopeProvider = new TestScopeProvider(kvpList);

        var entry = new LogEntry<string>(
            LogLevel.Information,
            "TestCategory",
            new EventId(0),
            "state",
            null,
            (state, ex) => "message");

        formatter.Write(entry, scopeProvider, writer);

        writer.ToString().Should().Contain("readonlylist-corr-id");
    }

    [Fact]
    public void Write_WithReadOnlyListScopeNoCorrelationId_UsesDash()
    {
        // Exercises BsftConsoleFormatter.GetCorrelationId lines 101-102:
        // IReadOnlyList branch where loop completes without finding CorrelationId.
        using var formatter = CreateFormatter();
        var writer = new StringWriter();

        var kvpList = new List<KeyValuePair<string, object>>
        {
            new("OtherKey", (object)"some-value")
        };
        var scopeProvider = new TestScopeProvider(kvpList);

        var entry = new LogEntry<string>(
            LogLevel.Information,
            "TestCategory",
            new EventId(0),
            "state",
            null,
            (state, ex) => "message");

        formatter.Write(entry, scopeProvider, writer);

        writer.ToString().Should().Contain("[-]");
    }

    [Fact]
    public void Write_WithCorrelationIdInDictionaryScope_IncludesCorrelationId()
    {
        // Exercises BsftConsoleFormatter.GetCorrelationId IEnumerable branch: scope with CorrelationId.
        using var formatter = CreateFormatter();
        var writer = new StringWriter();

        // Dictionary<string, object> is IEnumerable<KVP> but NOT IReadOnlyList<KVP<string,object>>
        var scopeProvider = new TestScopeProvider(
            new Dictionary<string, object> { ["CorrelationId"] = "test-corr-id" });

        var entry = new LogEntry<string>(
            LogLevel.Information,
            "TestCategory",
            new EventId(0),
            "state",
            null,
            (state, ex) => "message");

        formatter.Write(entry, scopeProvider, writer);

        writer.ToString().Should().Contain("test-corr-id");
    }

    [Fact]
    public void Write_WithEnumerableScopeContainingCorrelationId_IncludesIt()
    {
        // Exercises BsftConsoleFormatter.GetCorrelationId IEnumerable branch (lines 103-113):
        // scope is IEnumerable<KVP> (not IReadOnlyList<KVP>) containing CorrelationId.
        using var formatter = CreateFormatter();
        var writer = new StringWriter();

        // EnumerableOnlyKvp is IEnumerable<KVP> but NOT IReadOnlyList<KVP>
        var scope = new EnumerableOnlyKvp("enum-corr-id");
        var scopeProvider = new TestScopeProvider(scope);

        var entry = new LogEntry<string>(
            LogLevel.Information,
            "TestCategory",
            new EventId(0),
            "state",
            null,
            (state, ex) => "message");

        formatter.Write(entry, scopeProvider, writer);

        writer.ToString().Should().Contain("enum-corr-id");
    }

    [Fact]
    public void Write_WithScopeNoCorrelationId_UsesDefaultDash()
    {
        // Exercises BsftConsoleFormatter.GetCorrelationId lines 116-117:
        // When no scope contains CorrelationId, returns "-" (the StrongBox<string> default).
        // Also exercises StrongBox<T> constructor (lines 128, 130).
        using var formatter = CreateFormatter();
        var writer = new StringWriter();

        var scopeProvider = new TestScopeProvider(
            new Dictionary<string, object> { ["OtherKey"] = "something" });

        var entry = new LogEntry<string>(
            LogLevel.Information,
            "TestCategory",
            new EventId(0),
            "state",
            null,
            (state, ex) => "message");

        formatter.Write(entry, scopeProvider, writer);

        writer.ToString().Should().Contain("[-]");
    }

    [Fact]
    public void Write_EmptyCategory_UsesEmptyCategory()
    {
        // Exercises BsftConsoleFormatter.GetShortCategory lines 120-122:
        // empty category returns as-is (the IsNullOrEmpty branch).
        using var formatter = CreateFormatter();
        var writer = new StringWriter();

        var entry = new LogEntry<string>(
            LogLevel.Information,
            string.Empty,
            new EventId(0),
            "state",
            null,
            (state, ex) => "empty cat message");

        formatter.Write(entry, null, writer);

        writer.ToString().Should().Contain("empty cat message");
    }

    [Fact]
    public void Write_CategoryWithoutDot_UsesCategoryAsIs()
    {
        // Exercises BsftConsoleFormatter.GetShortCategory lines 124-125:
        // category without a dot - lastDot is -1, returns category unchanged.
        using var formatter = CreateFormatter();
        var writer = new StringWriter();

        var entry = new LogEntry<string>(
            LogLevel.Information,
            "NoDotCategory",
            new EventId(0),
            "state",
            null,
            (state, ex) => "no dot message");

        formatter.Write(entry, null, writer);

        writer.ToString().Should().Contain("NoDotCategory: no dot message");
    }

    [Fact]
    public void Write_UseUtcTimestamp_WritesUtcTime()
    {
        // Exercises BsftConsoleFormatter.Write line 45: UseUtcTimestamp = true branch.
        using var formatter = CreateFormatter(useUtc: true);
        var writer = new StringWriter();

        var entry = new LogEntry<string>(
            LogLevel.Information,
            "TestCategory",
            new EventId(0),
            "state",
            null,
            (state, ex) => "utc message");

        formatter.Write(entry, null, writer);

        writer.ToString().Should().Contain("utc message");
    }

    [Fact]
    public void Write_TraceLevel_WritesTRC()
    {
        // Exercises BsftConsoleFormatter.GetLogLevelAbbreviation line 75: LogLevel.Trace => "TRC".
        using var formatter = CreateFormatter();
        var writer = new StringWriter();

        var entry = new LogEntry<string>(
            LogLevel.Trace,
            "TestCategory",
            new EventId(0),
            "state",
            null,
            (state, ex) => "trace message");

        formatter.Write(entry, null, writer);

        writer.ToString().Should().Contain(" TRC]");
    }

    [Fact]
    public void Write_DebugLevel_WritesDBG()
    {
        // Exercises BsftConsoleFormatter.GetLogLevelAbbreviation line 76: LogLevel.Debug => "DBG".
        using var formatter = CreateFormatter();
        var writer = new StringWriter();

        var entry = new LogEntry<string>(
            LogLevel.Debug,
            "TestCategory",
            new EventId(0),
            "state",
            null,
            (state, ex) => "debug message");

        formatter.Write(entry, null, writer);

        writer.ToString().Should().Contain(" DBG]");
    }

    [Fact]
    public void Write_WarningLevel_WritesWRN()
    {
        // Exercises BsftConsoleFormatter.GetLogLevelAbbreviation line 78: LogLevel.Warning => "WRN".
        using var formatter = CreateFormatter();
        var writer = new StringWriter();

        var entry = new LogEntry<string>(
            LogLevel.Warning,
            "TestCategory",
            new EventId(0),
            "state",
            null,
            (state, ex) => "warning message");

        formatter.Write(entry, null, writer);

        writer.ToString().Should().Contain(" WRN]");
    }

    [Fact]
    public void Write_CriticalLevel_WritesCRT()
    {
        // Exercises BsftConsoleFormatter.GetLogLevelAbbreviation line 80: LogLevel.Critical => "CRT".
        using var formatter = CreateFormatter();
        var writer = new StringWriter();

        var entry = new LogEntry<string>(
            LogLevel.Critical,
            "TestCategory",
            new EventId(0),
            "state",
            null,
            (state, ex) => "critical message");

        formatter.Write(entry, null, writer);

        writer.ToString().Should().Contain(" CRT]");
    }

    [Fact]
    public void Write_UnknownLevel_WritesQuestionMarks()
    {
        // Exercises BsftConsoleFormatter.GetLogLevelAbbreviation line 81: _ => "???".
        using var formatter = CreateFormatter();
        var writer = new StringWriter();

        var entry = new LogEntry<string>(
            (LogLevel)99,
            "TestCategory",
            new EventId(0),
            "state",
            null,
            (state, ex) => "unknown level message");

        formatter.Write(entry, null, writer);

        writer.ToString().Should().Contain(" ???]");
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        // Exercises BsftConsoleFormatter.Dispose line 70.
        var formatter = CreateFormatter();

        var act = () => formatter.Dispose();

        act.Should().NotThrow();
    }

    /// <summary>
    /// A test scope provider that provides one scope object.
    /// </summary>
    private sealed class TestScopeProvider : IExternalScopeProvider
    {
        private readonly object _scopeState;

        public TestScopeProvider(object scopeState)
        {
            _scopeState = scopeState;
        }

        public void ForEachScope<TState>(Action<object?, TState> callback, TState state)
        {
            callback(_scopeState, state);
        }

        public IDisposable Push(object? state) => throw new NotSupportedException();
    }

    /// <summary>
    /// An IEnumerable KVP collection that is NOT an IReadOnlyList, to exercise
    /// the IEnumerable branch in GetCorrelationId.
    /// </summary>
    private sealed class EnumerableOnlyKvp : IEnumerable<KeyValuePair<string, object>>
    {
        private readonly string _correlationId;

        public EnumerableOnlyKvp(string correlationId)
        {
            _correlationId = correlationId;
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            yield return new KeyValuePair<string, object>("CorrelationId", _correlationId);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
