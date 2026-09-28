using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Bsft.Hosting.Logging;

/// <summary>
/// A custom console formatter that outputs structured text lines including the correlation ID
/// extracted from the logger scope.
/// </summary>
/// <remarks>
/// Output format:
/// <c>[2026-03-24 14:30:00.123 INF] [abc-123-def] OrdersController: Order ORD-42 created</c>
/// </remarks>
public sealed class BsftConsoleFormatter : ConsoleFormatter, IDisposable
{
    /// <summary>
    /// The short name of each category seen so far.
    /// </summary>
    /// <remarks>
    /// A category is fixed per logger, so the answer is the same for every line one logger writes
    /// and the substring is worth keeping. The map is bounded by the number of distinct categories
    /// the process logs under, which is the number of types that take an <c>ILogger&lt;T&gt;</c>.
    /// </remarks>
    private static readonly ConcurrentDictionary<string, string> ShortCategories = new(StringComparer.Ordinal);

    private readonly IDisposable? _optionsReloadToken;
    private readonly TimeProvider _timeProvider;
    private BsftConsoleFormatterOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="BsftConsoleFormatter"/> class.
    /// </summary>
    /// <param name="options">The monitor providing formatter options.</param>
    /// <param name="timeProvider">The time provider used for timestamps.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public BsftConsoleFormatter(IOptionsMonitor<BsftConsoleFormatterOptions> options, TimeProvider? timeProvider = null)
        : base(HostingDefaults.ConsoleFormatterName)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.CurrentValue;
        _optionsReloadToken = options.OnChange(updated => _options = updated);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
        if (message is null)
            return;

        var now = _timeProvider.GetUtcNow();
        var timestamp = (_options.UseUtcTimestamp ? now : now.ToLocalTime())
            .ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

        var levelAbbreviation = GetLogLevelAbbreviation(logEntry.LogLevel);
        var correlationId = GetCorrelationId(scopeProvider);
        var categoryShort = GetShortCategory(logEntry.Category);

        textWriter.Write('[');
        textWriter.Write(timestamp);
        textWriter.Write(' ');
        textWriter.Write(levelAbbreviation);
        textWriter.Write("] [");
        textWriter.Write(correlationId);
        textWriter.Write("] ");
        textWriter.Write(categoryShort);
        textWriter.Write(": ");
        textWriter.WriteLine(message);

        if (logEntry.Exception is not null)
            textWriter.WriteLine(logEntry.Exception.ToString());
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _optionsReloadToken?.Dispose();
    }

    /// <summary>
    /// Abbreviates a level to the three characters the line reserves for it.
    /// </summary>
    /// <param name="logLevel">The level of the entry being written.</param>
    /// <returns>The abbreviation.</returns>
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

    /// <summary>
    /// Finds the correlation id among the scopes in force.
    /// </summary>
    /// <param name="scopeProvider">The scopes, or <see langword="null"/> when none are captured.</param>
    /// <returns>The correlation id, or <c>-</c> when no scope carries one.</returns>
    /// <remarks>
    /// The innermost scope carrying one wins: <c>ForEachScope</c> walks outermost first, and each
    /// match overwrites the last.
    /// </remarks>
    private static string GetCorrelationId(IExternalScopeProvider? scopeProvider)
    {
        if (scopeProvider is null)
            return "-";

        var box = new StrongBox<string>("-");
        scopeProvider.ForEachScope(static (scope, state) =>
        {
            var found = TryExtractCorrelationId(scope, out var value);
            if (found)
                state.Value = value ?? "-";
        }, box);

        // The box starts at "-" and is only ever assigned a non-null value; the coalesce is what the
        // BCL's nullable Value field asks for rather than a case that can arise.
        return box.Value ?? "-";
    }

    /// <summary>
    /// Reads the correlation id out of one scope.
    /// </summary>
    /// <param name="scope">The scope object, whatever the caller passed to <c>BeginScope</c>.</param>
    /// <param name="value">The id it carries, when it carries one.</param>
    /// <returns><see langword="true"/> when the scope named a correlation id.</returns>
    /// <remarks>
    /// The list case is checked first and indexed rather than enumerated, because the scope the
    /// correlation middleware opens is a list and this runs for every scope of every log line.
    /// </remarks>
    private static bool TryExtractCorrelationId(object? scope, out string? value)
    {
        value = null;
        if (scope is IReadOnlyList<KeyValuePair<string, object>> kvpList)
        {
            for (var i = 0; i < kvpList.Count; i++)
            {
                if (!string.Equals(kvpList[i].Key, "CorrelationId", StringComparison.Ordinal))
                    continue;
                value = kvpList[i].Value?.ToString();
                return true;
            }
        }
        else if (scope is IEnumerable<KeyValuePair<string, object>> kvpEnumerable)
        {
            foreach (var kvp in kvpEnumerable)
            {
                if (!string.Equals(kvp.Key, "CorrelationId", StringComparison.Ordinal))
                    continue;
                value = kvp.Value?.ToString();
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Shortens a category to its last segment, which is the type name.
    /// </summary>
    /// <param name="category">The full category the entry was logged under.</param>
    /// <returns>The segment after the last dot, or the category itself when it has none.</returns>
    private static string GetShortCategory(string category)
    {
        if (string.IsNullOrEmpty(category))
            return category;

        return ShortCategories.GetOrAdd(category, static full =>
        {
            var lastDot = full.LastIndexOf('.');
            return lastDot >= 0 ? full[(lastDot + 1)..] : full;
        });
    }
}
