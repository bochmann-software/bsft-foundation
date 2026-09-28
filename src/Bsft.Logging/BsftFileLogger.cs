using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Bsft.Logging;

/// <summary>
/// A logger that formats log entries using either the BSFT structured text format or JSON
/// and writes them to the <see cref="BsftFileLoggerProvider"/>. The text output format is:
/// <c>[2026-03-24 14:30:00.123 INF] [abc-123-def] OrdersController: Order ORD-42 created</c>
/// </summary>
internal sealed class BsftFileLogger : ILogger
{
    /// <summary>The one scope value this logger carries onto the lines written inside the scope.</summary>
    private const string CorrelationIdKey = "CorrelationId";

    /// <summary>What stands in for the correlation id on a line logged outside any scope.</summary>
    private const string NoCorrelationId = "-";

    /// <summary>
    /// How large the reused JSON buffer may stay between lines. One outsized message would otherwise
    /// leave a buffer that big attached to its thread for the life of the process.
    /// </summary>
    private const int MaxRetainedJsonBuffer = 64 * 1024;

    private static readonly AsyncLocal<string?> CorrelationIdCurrent = new();

    // One buffer and one writer per thread, reused across lines: a log line is written, copied into a
    // string and forgotten, so allocating both per line allocates twice for every line the process
    // ever logs. Thread-static rather than pooled because a logger is shared across threads and this
    // is the whole of the sharing.
    [ThreadStatic]
    private static ArrayBufferWriter<byte>? JsonBuffer;

    [ThreadStatic]
    private static Utf8JsonWriter? JsonWriter;

    private readonly BsftFileLoggerProvider _provider;
    private readonly string _categoryName;
    private readonly string _categoryShort;
    private readonly BsftLoggingOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="BsftFileLogger"/> class.
    /// </summary>
    /// <param name="provider">The owning file logger provider.</param>
    /// <param name="categoryName">The full category name for this logger.</param>
    /// <param name="options">The logging options.</param>
    /// <param name="timeProvider">The time provider used for timestamps.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public BsftFileLogger(BsftFileLoggerProvider provider, string categoryName, BsftLoggingOptions options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(categoryName);
        ArgumentNullException.ThrowIfNull(options);

        _provider = provider;
        _categoryName = categoryName;
        _categoryShort = GetShortCategory(categoryName);
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        // Indexed for a list, which is the shape the logging framework passes, so the common case
        // walks the scope's values without allocating an enumerator for them.
        if (state is IReadOnlyList<KeyValuePair<string, object>> pairs)
        {
            for (var index = 0; index < pairs.Count; index++)
            {
                if (IsCorrelationId(pairs[index]))
                    return EnterCorrelationScope(pairs[index].Value);
            }

            return null;
        }

        if (state is IEnumerable<KeyValuePair<string, object>> enumerable)
        {
            foreach (var pair in enumerable)
            {
                if (IsCorrelationId(pair))
                    return EnterCorrelationScope(pair.Value);
            }
        }

        return null;
    }

    /// <summary>Reports whether a scope value is the correlation id.</summary>
    /// <param name="pair">The scope value.</param>
    /// <returns><see langword="true"/> when it is the correlation id.</returns>
    private static bool IsCorrelationId(KeyValuePair<string, object> pair)
        => string.Equals(pair.Key, CorrelationIdKey, StringComparison.Ordinal);

    /// <summary>States the correlation id of everything logged until the scope is disposed.</summary>
    /// <param name="value">The correlation id, as the scope carried it.</param>
    /// <returns>The scope, which puts the previous correlation id back.</returns>
    private static IDisposable EnterCorrelationScope(object? value)
    {
        var previous = CorrelationIdCurrent.Value;
        CorrelationIdCurrent.Value = value?.ToString();

        return new CorrelationScope(previous);
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= _options.MinimumLevel;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId _,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        if (formatter is null)
            throw new ArgumentNullException(nameof(formatter));

        var message = formatter(state, exception);
        if (message is null)
            return;

        var correlationId = CorrelationIdCurrent.Value ?? NoCorrelationId;

        var line = _options.EnableJsonFormat
            ? FormatJson(logLevel, correlationId, message, exception)
            : FormatText(logLevel, correlationId, message, exception);

        _provider.WriteEntry(line);
    }

    private string FormatText(LogLevel logLevel, string correlationId, string message, Exception? exception)
    {
        var timestamp = _timeProvider.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var level = GetLogLevelAbbreviation(logLevel);

        // Strip line breaks from the variable fields so a CRLF in the message or correlation id cannot
        // forge an additional log line. The exception block is appended deliberately as multi-line.
        var line = $"[{timestamp} {level}] [{StripLineBreaks(correlationId)}] {_categoryShort}: {StripLineBreaks(message)}";
        if (exception is not null)
            line += Environment.NewLine + exception;

        return line;
    }

    private static string StripLineBreaks(string value) => value.ReplaceLineEndings(" ");

    private string FormatJson(LogLevel logLevel, string correlationId, string message, Exception? exception)
    {
        var buffer = JsonBuffer ??= new ArrayBufferWriter<byte>();
        var writer = JsonWriter ??= new Utf8JsonWriter(buffer);

        buffer.ResetWrittenCount();
        writer.Reset(buffer);

        writer.WriteStartObject();
        writer.WriteString("timestamp", _timeProvider.GetLocalNow().ToString("yyyy-MM-ddTHH:mm:ss.fffK", CultureInfo.InvariantCulture));
        writer.WriteString("level", logLevel.ToString());
        writer.WriteString("correlationId", correlationId);
        writer.WriteString("category", _categoryName);
        writer.WriteString("message", message);

        if (exception is not null)
            writer.WriteString("exception", exception.ToString());

        writer.WriteEndObject();
        writer.Flush();

        var line = Encoding.UTF8.GetString(buffer.WrittenSpan);

        if (buffer.Capacity > MaxRetainedJsonBuffer)
        {
            JsonWriter = null;
            JsonBuffer = null;
        }

        return line;
    }

    private static string GetLogLevelAbbreviation(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???"
    };

    private static string GetShortCategory(string category)
    {
        if (string.IsNullOrEmpty(category))
            return category;

        var lastDot = category.LastIndexOf('.');
        return lastDot >= 0 ? category[(lastDot + 1)..] : category;
    }

    private sealed class CorrelationScope : IDisposable
    {
        private readonly string? _previousValue;

        public CorrelationScope(string? previousValue)
        {
            _previousValue = previousValue;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            CorrelationIdCurrent.Value = _previousValue;
        }
    }
}
