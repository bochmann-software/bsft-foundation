using Bsft.Hosting.Logging;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Bsft.Hosting.Tests.Logging;

public class ConsoleFormatterTests
{
    private static BsftConsoleFormatter CreateFormatter(bool useUtc = false, params string[] extraKeys)
    {
        var options = Substitute.For<IOptionsMonitor<BsftConsoleFormatterOptions>>();
        var formatterOptions = new BsftConsoleFormatterOptions { UseUtcTimestamp = useUtc };
        foreach (var key in extraKeys)
            formatterOptions.PersistedScopeKeys.Add(key);

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
    [Fact]
    public void Write_WithConfiguredKeys_ShowsTheInnermostValuesInTheSameBlockAsTheFile()
    {
        using var formatter = CreateFormatter(false, "RunId", "Tenant");
        var writer = new StringWriter();
        var scopes = new ScopeList(
            new Dictionary<string, object> { ["CorrelationId"] = "req-1", ["RunId"] = "outer", ["Tenant"] = "a;b\r\nc" },
            new List<KeyValuePair<string, object>> { new("RunId", "inner"), new("Other", "ignored"), new("Nothing", null!) });

        formatter.Write(Entry(), scopes, writer);

        writer.ToString().Should().Contain("] [req-1] {RunId=inner;Tenant=a\\;b c} Cat: message");
    }

    [Fact]
    public void Write_WithAHostileCorrelationId_EscapesTheSlot()
    {
        using var formatter = CreateFormatter(false, "RunId");
        var writer = new StringWriter();
        var scopes = new ScopeList(new Dictionary<string, object> { ["CorrelationId"] = "evil] {RunId=victim}" });

        formatter.Write(Entry(), scopes, writer);

        writer.ToString().Should().Contain("[evil\\] {RunId=victim}] Cat: message");
    }

    [Fact]
    public void Write_WithNullValuesAndOnlyTheCorrelationKey_FallsBackToTheMarkerAndNoBlock()
    {
        using var formatter = CreateFormatter();
        var writer = new StringWriter();
        var scopes = new ScopeList(new Dictionary<string, object> { ["CorrelationId"] = null! });

        formatter.Write(Entry(), scopes, writer);

        writer.ToString().Should().Contain("] [-] Cat: message");
    }

    [Fact]
    public void Write_WithAConfiguredKeyWhoseValueIsNull_ShowsAnEmptyValue()
    {
        using var formatter = CreateFormatter(false, "Nothing");
        var writer = new StringWriter();
        var scopes = new ScopeList(new List<KeyValuePair<string, object>> { new("Nothing", null!) });

        formatter.Write(Entry(), scopes, writer);

        writer.ToString().Should().Contain("] [-] {Nothing=} Cat: message");
    }

    [Fact]
    public void Write_WithAKeyHoldingLineBreaks_MatchesTheFlattenedKeyAndNeverBreaksTheLine()
    {
        using var formatter = CreateFormatter(false, "Run\r\nId");
        var writer = new StringWriter();
        var scopes = new ScopeList(new Dictionary<string, object> { ["Run Id"] = "r1" });

        formatter.Write(Entry(), scopes, writer);

        writer.ToString().Should().Contain("{Run Id=r1}");
    }

    [Fact]
    public void Write_WhenTheKeyListIsMissing_ShowsTheCorrelationIdOnly()
    {
        var monitor = Substitute.For<IOptionsMonitor<BsftConsoleFormatterOptions>>();
        monitor.CurrentValue.Returns(new BsftConsoleFormatterOptions { PersistedScopeKeys = null! });
        using var formatter = new BsftConsoleFormatter(monitor);
        var writer = new StringWriter();
        var scopes = new ScopeList(new Dictionary<string, object> { ["CorrelationId"] = "c1", ["RunId"] = "r" });

        formatter.Write(Entry(), scopes, writer);

        writer.ToString().Should().Contain("] [c1] Cat: message");
    }

    [Fact]
    public void Write_AfterTheOptionsChange_UsesTheNewKeys()
    {
        Action<BsftConsoleFormatterOptions, string?>? listener = null;
        var monitor = Substitute.For<IOptionsMonitor<BsftConsoleFormatterOptions>>();
        monitor.CurrentValue.Returns(new BsftConsoleFormatterOptions());
        monitor.OnChange(Arg.Do<Action<BsftConsoleFormatterOptions, string?>>(action => listener = action)).Returns((IDisposable?)null);
        using var formatter = new BsftConsoleFormatter(monitor, new Bsft.Logging.Sinks.ScopeLineEncoder());
        var changed = new BsftConsoleFormatterOptions();
        changed.PersistedScopeKeys.Add("RunId");
        listener!(changed, null);
        var writer = new StringWriter();

        formatter.Write(Entry(), new ScopeList(new Dictionary<string, object> { ["RunId"] = "r9" }), writer);

        writer.ToString().Should().Contain("{RunId=r9}");
    }

    [Fact]
    public void Constructor_WithoutAnEncoder_Throws()
    {
        var monitor = Substitute.For<IOptionsMonitor<BsftConsoleFormatterOptions>>();

        var act = () => new BsftConsoleFormatter(monitor, (Bsft.Logging.Sinks.IScopeLineEncoder)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static LogEntry<string> Entry()
        => new(LogLevel.Information, "Cat", new EventId(0), "state", null, (state, ex) => "message");

    private sealed class ScopeList : IExternalScopeProvider
    {
        private readonly object[] _scopes;

        public ScopeList(params object[] scopes)
        {
            _scopes = scopes;
        }

        public void ForEachScope<TState>(Action<object?, TState> callback, TState state)
        {
            foreach (var scope in _scopes)
                callback(scope, state);
        }

        public IDisposable Push(object? state) => throw new NotSupportedException();
    }

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
