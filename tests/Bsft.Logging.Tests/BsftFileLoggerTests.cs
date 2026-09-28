using Bsft.Logging.Tests.Fakes;
using Bsft.Logging.Tests.Support;
using FluentAssertions;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Bsft.Logging.Tests;

/// <summary>Covers what the logger writes: the line it formats and the scope it reads.</summary>
public class BsftFileLoggerTests : IDisposable
{
    private readonly LogFileFixture _log = new();

    /// <inheritdoc />
    public void Dispose() => _log.Dispose();

    [Fact]
    public void TextFormat_IncludesTimestampLevelCategoryMessage()
    {
        var options = LogFileFixture.Options();
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory);
        var logger = provider.CreateLogger("My.App.OrderService");

        logger.LogWarning("Order {OrderId} delayed", 42);
        provider.Flush();
        provider.Dispose();

        var content = File.ReadAllText(Directory.GetFiles(_log.LogDirectory, "app-*.log")[0]);
        content.Should().Contain(" WRN]");
        content.Should().Contain("OrderService:");
        content.Should().Contain("Order 42 delayed");
    }

    [Fact]
    public void JsonFormat_WritesValidJsonPerLine()
    {
        var options = LogFileFixture.Options();
        options.EnableJsonFormat = true;
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory);
        var logger = provider.CreateLogger("My.App.TestService");

        logger.LogInformation("json test message");
        provider.Flush();
        provider.Dispose();

        var content = File.ReadAllText(Directory.GetFiles(_log.LogDirectory, "app-*.log")[0]);
        var lines = content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(1);

        var json = System.Text.Json.JsonDocument.Parse(lines[0]);
        json.RootElement.GetProperty("level").GetString().Should().Be("Information");
        json.RootElement.GetProperty("category").GetString().Should().Be("My.App.TestService");
        json.RootElement.GetProperty("message").GetString().Should().Contain("json test message");
        json.RootElement.GetProperty("correlationId").GetString().Should().Be("-");
        json.RootElement.TryGetProperty("timestamp", out _).Should().BeTrue();
    }

    [Fact]
    public void JsonFormat_AfterAnOutsizedMessage_KeepsWritingWithoutHoldingTheBuffer()
    {
        // The JSON buffer is reused per thread, so one outsized message would otherwise leave a
        // buffer that big attached to its thread for the life of the process.
        var options = LogFileFixture.Options();
        options.EnableJsonFormat = true;
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory);
        var logger = provider.CreateLogger("My.App.BulkService");

        logger.LogInformation("{Payload}", new string('x', 128 * 1024));
        logger.LogInformation("back to a short line");
        provider.Flush();
        provider.Dispose();

        var content = File.ReadAllText(Directory.GetFiles(_log.LogDirectory, "app-*.log")[0]);
        var lines = content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(2);

        System.Text.Json.JsonDocument.Parse(lines[0]).RootElement
            .GetProperty("message").GetString().Should().HaveLength(128 * 1024);
        System.Text.Json.JsonDocument.Parse(lines[1]).RootElement
            .GetProperty("message").GetString().Should().Be("back to a short line");
    }

    [Fact]
    public void LogsExceptionDetails()
    {
        var options = LogFileFixture.Options();
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory);
        var logger = provider.CreateLogger("ExceptionTest");

        var exception = new InvalidOperationException("Something went wrong");
        logger.LogError(exception, "Operation failed");
        provider.Flush();
        provider.Dispose();

        var content = File.ReadAllText(Directory.GetFiles(_log.LogDirectory, "app-*.log")[0]);
        content.Should().Contain(" ERR]");
        content.Should().Contain("Operation failed");
        content.Should().Contain("InvalidOperationException");
        content.Should().Contain("Something went wrong");
    }

    [Fact]
    public void CorrelationId_AppearsInLogOutput()
    {
        var options = LogFileFixture.Options();
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory);
        var logger = provider.CreateLogger("CorrelationTest");

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = "test-corr-123" }))
        {
            logger.LogInformation("correlated message");
        }

        provider.Flush();
        provider.Dispose();

        var content = File.ReadAllText(Directory.GetFiles(_log.LogDirectory, "app-*.log")[0]);
        content.Should().Contain("[test-corr-123]");
    }

    [Fact]
    public void Critical_LogLevel_UsesCorrectAbbreviation()
    {
        var options = LogFileFixture.Options();
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory);
        var logger = provider.CreateLogger("CritTest");

        logger.LogCritical("critical message");
        provider.Flush();
        provider.Dispose();

        var content = File.ReadAllText(Directory.GetFiles(_log.LogDirectory, "app-*.log")[0]);
        content.Should().Contain(" CRT]");
    }

    [Fact]
    public void EmptyCategory_ReturnsEmptyCategoryName()
    {
        var options = LogFileFixture.Options();
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory);
        var logger = provider.CreateLogger(string.Empty); // empty category

        logger.LogInformation("empty cat message");
        provider.Flush();
        provider.Dispose();

        var content = File.ReadAllText(Directory.GetFiles(_log.LogDirectory, "app-*.log")[0]);
        content.Should().Contain("empty cat message");
    }

    [Fact]
    public void MinimumLevel_FiltersLowerLevels()
    {
        var options = LogFileFixture.Options();
        options.MinimumLevel = LogLevel.Warning;
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory);
        var logger = provider.CreateLogger("FilterTest");

        logger.LogInformation("should not appear");
        logger.LogWarning("should appear");
        provider.Flush();
        provider.Dispose();

        var files = Directory.GetFiles(_log.LogDirectory, "app-*.log");
        files.Should().HaveCount(1);

        var content = File.ReadAllText(files[0]);
        content.Should().NotContain("should not appear");
        content.Should().Contain("should appear");
    }

    [Fact]
    public void BeginScope_IReadOnlyListWithCorrelationId_AppearsInLogOutput()
    {
        var options = LogFileFixture.Options();
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory);
        var logger = provider.CreateLogger("ScopeListTest");

        // IReadOnlyList<KVP<string,object>> that IS IReadOnlyList (not just IEnumerable) WITH CorrelationId
        var kvpList = new List<KeyValuePair<string, object>>
        {
            new("CorrelationId", "readonly-list-corr-id")
        };

        using (logger.BeginScope(kvpList))
        {
            logger.LogInformation("message with IReadOnlyList scope");
        }

        provider.Flush();
        provider.Dispose();

        var content = File.ReadAllText(Directory.GetFiles(_log.LogDirectory, "app-*.log")[0]);
        content.Should().Contain("[readonly-list-corr-id]");
    }

    [Fact]
    public void BeginScope_IReadOnlyListWithoutCorrelationId_ReturnsNull()
    {
        // A scope the logger carries nothing from is not a scope it has to hold open.
        var options = LogFileFixture.Options();
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory);
        var logger = provider.CreateLogger("ScopeTest");

        // IReadOnlyList<KVP> without CorrelationId key
        var scope = logger.BeginScope(new List<KeyValuePair<string, object>>
        {
            new("SomeOtherKey", "value")
        });

        scope.Should().BeNull("no CorrelationId in list -> returns null");
        provider.Dispose();
    }

    [Fact]
    public void BeginScope_IEnumerableWithoutCorrelationId_ReturnsNull()
    {
        // The shape the logging framework passes is a list, but a caller may pass any enumerable of
        // pairs, and one without a correlation id opens no scope either.
        var options = LogFileFixture.Options();
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory);
        var logger = provider.CreateLogger("ScopeTest2");

        // Use a custom IEnumerable that is NOT IReadOnlyList
        var kvpEnumerable = new CustomKvpEnumerable(new[]
        {
            new KeyValuePair<string, object>("OtherKey", "value")
        });

        var scope = logger.BeginScope(kvpEnumerable);

        scope.Should().BeNull("no CorrelationId in enumerable -> returns null");
        provider.Dispose();
    }

    [Fact]
    public void BeginScope_NonKvpState_ReturnsNull()
    {
        // A scope state that is not a set of pairs carries no correlation id to read.
        var options = LogFileFixture.Options();
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory);
        var logger = provider.CreateLogger("ScopeTest3");

        // Pass a plain string - not a KVP collection at all
        var scope = logger.BeginScope("plain string state");

        scope.Should().BeNull("non-KVP state type -> returns null");
        provider.Dispose();
    }

    [Fact]
    public void Log_NullFormatter_ThrowsArgumentNullException()
    {
        var options = LogFileFixture.Options();
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory);
        var logger = provider.CreateLogger("NullFormatterTest");

        Func<string, Exception?, string> nullFormatter = null!;
        var act = () => logger.Log(LogLevel.Information, default, "state", null, nullFormatter);

        act.Should().Throw<ArgumentNullException>().WithParameterName("formatter");
        provider.Dispose();
    }

    [Fact]
    public void Log_FormatterReturnsNull_IsIgnored()
    {
        var options = LogFileFixture.Options();
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory);
        var logger = provider.CreateLogger("NullMessageTest");

        // Formatter returns null -> message is null -> early return, no file written
        var act = () => logger.Log<string>(LogLevel.Information, default, "state", null, (_, _) => null!);

        act.Should().NotThrow();
        provider.Flush();
        provider.Dispose();

        // No log file should be created since nothing was written
        var files = Directory.GetFiles(_log.LogDirectory, "app-*.log");
        // Either no files or the file is empty
        if (files.Length > 0)
            File.ReadAllText(files[0]).Should().BeEmpty();
    }

    [Fact]
    public void JsonFormat_WithException_IncludesExceptionField()
    {
        var options = LogFileFixture.Options();
        options.EnableJsonFormat = true;
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory);
        var logger = provider.CreateLogger("JsonExceptionTest");

        var exception = new InvalidOperationException("json exception detail");
        logger.LogError(exception, "error with json exception");
        provider.Flush();
        provider.Dispose();

        var content = File.ReadAllText(Directory.GetFiles(_log.LogDirectory, "app-*.log")[0]);
        var lines = content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(1);

        var json = System.Text.Json.JsonDocument.Parse(lines[0]);
        json.RootElement.TryGetProperty("exception", out var excProp).Should().BeTrue("exception field must be written");
        excProp.GetString().Should().Contain("InvalidOperationException");
        excProp.GetString().Should().Contain("json exception detail");
    }

    [Fact]
    public void Trace_LogLevel_UsesCorrectAbbreviation()
    {
        var options = LogFileFixture.Options();
        options.MinimumLevel = LogLevel.Trace;
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory);
        var logger = provider.CreateLogger("TraceTest");

        logger.LogTrace("trace message");
        provider.Flush();
        provider.Dispose();

        var content = File.ReadAllText(Directory.GetFiles(_log.LogDirectory, "app-*.log")[0]);
        content.Should().Contain(" TRC]");
    }

    [Fact]
    public void Debug_LogLevel_UsesCorrectAbbreviation()
    {
        var options = LogFileFixture.Options();
        options.MinimumLevel = LogLevel.Debug;
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory);
        var logger = provider.CreateLogger("DebugTest");

        logger.LogDebug("debug message");
        provider.Flush();
        provider.Dispose();

        var content = File.ReadAllText(Directory.GetFiles(_log.LogDirectory, "app-*.log")[0]);
        content.Should().Contain(" DBG]");
    }

    [Fact]
    public void UnknownLogLevel_GetLogLevelAbbreviation_ReturnsQuestionMarks()
    {
        var options = LogFileFixture.Options();
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory);
        var logger = provider.CreateLogger("UnknownLevelTest");

        // Invoke GetLogLevelAbbreviation via reflection with an unknown LogLevel value
        var method = logger.GetType()
            .GetMethod("GetLogLevelAbbreviation", BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull("GetLogLevelAbbreviation must exist");

        var result = (string?)method!.Invoke(null, [(LogLevel)999]);

        result.Should().Be("???");

        provider.Dispose();
    }

    [Fact]
    public void TextFormat_MessageWithCrlf_IsWrittenAsSingleLine()
    {
        var options = LogFileFixture.Options();
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory);
        var logger = provider.CreateLogger("Test");

        logger.LogInformation("legit\r\n[2026-01-01 00:00:00.000 ERR] [x] forged: injected");
        provider.Flush();
        provider.Dispose();

        var content = File.ReadAllText(Directory.GetFiles(_log.LogDirectory, "app-*.log")[0]);
        // The single log call must produce exactly one line (the trailing newline from WriteLine aside).
        content.TrimEnd('\r', '\n').Should().NotContain("\n");
        content.Should().Contain("legit [2026-01-01"); // CRLF replaced by a space, kept on one line
    }

    [Fact]
    public void Log_WithLogLevelNone_WritesNothing()
    {
        // Moved from Bsft.Hosting.Tests/FileLoggerTests.cs when the hosting file logger fork was removed.
        // LogLevel.None sorts above every real level, so only the explicit != None guard in
        // BsftFileLogger.IsEnabled keeps it out of the file.
        var options = LogFileFixture.Options();
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory);
        var logger = provider.CreateLogger("NoneTest");

        logger.Log(LogLevel.None, default, "state", null, (_, _) => "should not be written");
        provider.Flush();
        provider.Dispose();

        var files = Directory.GetFiles(_log.LogDirectory, "app-*.log");
        files.Should().BeEmpty("LogLevel.None is disabled, so nothing is written");
    }

    [Fact]
    public void UnknownLogLevel_IsWrittenWithQuestionMarks()
    {
        // Moved from Bsft.Hosting.Tests/FileLoggerTests.cs when the hosting file logger fork was removed.
        // Complements UnknownLogLevel_GetLogLevelAbbreviation_ReturnsQuestionMarks by going through a
        // real Log call instead of reflection, so the fallback is asserted on the written line.
        var options = LogFileFixture.Options();
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory);
        var logger = provider.CreateLogger("UnknownLevelWriteTest");

        logger.Log((LogLevel)99, default, "state", null, (_, _) => "unknown level message");
        provider.Flush();
        provider.Dispose();

        var files = Directory.GetFiles(_log.LogDirectory, "app-*.log");
        files.Should().HaveCount(1);
        File.ReadAllText(files[0]).Should().Contain(" ???]");
    }
}
