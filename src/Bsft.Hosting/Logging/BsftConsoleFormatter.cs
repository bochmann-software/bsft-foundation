using System.Collections.Concurrent;
using System.Globalization;
using Bsft.Correlation;
using Bsft.Logging.Sinks;
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
    private readonly IScopeLineEncoder _encoder;
    private BsftConsoleFormatterOptions _options;
    private string[] _scopeKeys;

    /// <summary>
    /// Initializes a new instance of the <see cref="BsftConsoleFormatter"/> class.
    /// </summary>
    /// <param name="options">The monitor providing formatter options.</param>
    /// <param name="timeProvider">The time provider used for timestamps.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public BsftConsoleFormatter(IOptionsMonitor<BsftConsoleFormatterOptions> options, TimeProvider? timeProvider = null)
        : this(options, new ScopeLineEncoder(), timeProvider)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BsftConsoleFormatter"/> class with the encoder that writes the scope parts of the line.
    /// </summary>
    /// <param name="options">The monitor providing formatter options.</param>
    /// <param name="encoder">The encoder of the correlation slot and the scope property block, shared with the file logger's format.</param>
    /// <param name="timeProvider">The time provider used for timestamps.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> or <paramref name="encoder"/> is <see langword="null"/>.</exception>
    public BsftConsoleFormatter(
        IOptionsMonitor<BsftConsoleFormatterOptions> options,
        IScopeLineEncoder encoder,
        TimeProvider? timeProvider = null)
        : base(HostingDefaults.ConsoleFormatterName)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(encoder);

        _encoder = encoder;
        _options = options.CurrentValue;
        _scopeKeys = DistinctKeys(_options);
        _optionsReloadToken = options.OnChange(updated =>
        {
            _options = updated;
            _scopeKeys = DistinctKeys(updated);
        });
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
        var scopes = ReadScopes(scopeProvider, _scopeKeys);
        var categoryShort = GetShortCategory(logEntry.Category);

        textWriter.Write('[');
        textWriter.Write(timestamp);
        textWriter.Write(' ');
        textWriter.Write(levelAbbreviation);
        textWriter.Write("] [");
        textWriter.Write(_encoder.EscapeSlot(scopes.CorrelationId));
        textWriter.Write(']');
        textWriter.Write(_encoder.ToBlock(scopes.Properties));
        textWriter.Write(' ');
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
    /// Reads the persisted scope values among the scopes in force, the same keys and the same encoding the file logger writes.
    /// </summary>
    /// <param name="scopeProvider">The scopes, or <see langword="null"/> when none are captured.</param>
    /// <param name="persistedKeys">The keys to read, the correlation id among them or not.</param>
    /// <returns>The correlation id (<c>-</c> when no scope carries one) and the other values found, in key order.</returns>
    /// <remarks>
    /// The innermost scope carrying a key wins: <c>ForEachScope</c> walks outermost first, and each match
    /// overwrites the last. Unlike the file, the console applies no size budget, since nothing reads it back.
    /// </remarks>
    private static ScopeReading ReadScopes(IExternalScopeProvider? scopeProvider, string[] persistedKeys)
    {
        var reading = new ScopeReading(persistedKeys);
        scopeProvider?.ForEachScope(static (scope, state) => state.Read(scope), reading);
        reading.Complete();
        return reading;
    }

    /// <summary>
    /// Lists the persisted keys once per options instance: distinct, in configured order, cut to one line like the
    /// file logger's keys. A missing list reads as the correlation id alone.
    /// </summary>
    /// <param name="options">The formatter options.</param>
    /// <returns>The keys.</returns>
    private static string[] DistinctKeys(BsftConsoleFormatterOptions options)
        => (options.PersistedScopeKeys ?? [LogScopeKeys.CorrelationId])
            .Select(key => key.ReplaceLineEndings(" "))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

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

    /// <summary>The persisted values of the scopes of one line, collected while the scopes are walked.</summary>
    private sealed class ScopeReading
    {
        private readonly string[] _keys;
        private readonly string?[] _values;

        private List<KeyValuePair<string, string>>? _properties;

        public ScopeReading(string[] keys)
        {
            _keys = keys;
            _values = new string?[keys.Length];
        }

        public string CorrelationId { get; private set; } = "-";

        public IReadOnlyList<KeyValuePair<string, string>> Properties
            => _properties ?? (IReadOnlyList<KeyValuePair<string, string>>)Array.Empty<KeyValuePair<string, string>>();

        /// <summary>Takes the persisted values of one scope, which is whatever the caller passed to <c>BeginScope</c>.</summary>
        /// <param name="scope">The scope object.</param>
        /// <remarks>
        /// The list case is checked first and indexed rather than enumerated, because the scope the correlation
        /// middleware opens is a list and this runs for every scope of every log line.
        /// </remarks>
        public void Read(object? scope)
        {
            if (scope is IReadOnlyList<KeyValuePair<string, object>> list)
            {
                for (var index = 0; index < list.Count; index++)
                    Take(list[index]);
            }
            else if (scope is IEnumerable<KeyValuePair<string, object>> pairs)
            {
                foreach (var pair in pairs)
                    Take(pair);
            }
        }

        /// <summary>Splits what was read into the correlation id and the other properties.</summary>
        public void Complete()
        {
            for (var index = 0; index < _keys.Length; index++)
            {
                if (_values[index] is not { } value)
                    continue;

                if (string.Equals(_keys[index], LogScopeKeys.CorrelationId, StringComparison.Ordinal))
                    CorrelationId = value;
                else
                    (_properties ??= []).Add(new KeyValuePair<string, string>(_keys[index], value.ReplaceLineEndings(" ")));
            }
        }

        private void Take(KeyValuePair<string, object> pair)
        {
            for (var index = 0; index < _keys.Length; index++)
            {
                if (!string.Equals(pair.Key, _keys[index], StringComparison.Ordinal))
                    continue;

                var value = pair.Value?.ToString();
                _values[index] = value ?? (string.Equals(pair.Key, LogScopeKeys.CorrelationId, StringComparison.Ordinal) ? "-" : string.Empty);
                return;
            }
        }
    }
}
