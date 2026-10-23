using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Bsft.Logging.LogFiles;

internal static class LogFileEntryDecoder
{
    private const int InitialExceptionCapacity = 4096;

    private static readonly Regex TextEntryPattern = new(
        @"^\[(?<timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}) (?<level>TRC|DBG|INF|WRN|ERR|CRT)\] \[(?<correlation>(?:[^\]\\]|\\.)*)\] (?:\{(?<scope>(?:[^\\=;}]|\\.)+=(?:[^\\;}]|\\.)*(?:;(?:[^\\=;}]|\\.)+=(?:[^\\;}]|\\.)*)*)\} )?(?<category>[^:]+): (?<message>.*)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly IReadOnlyDictionary<string, LogLevel> Levels =
        new Dictionary<string, LogLevel>(StringComparer.Ordinal)
        {
            ["TRC"] = LogLevel.Trace,
            ["DBG"] = LogLevel.Debug,
            ["INF"] = LogLevel.Information,
            ["WRN"] = LogLevel.Warning,
            ["ERR"] = LogLevel.Error,
            ["CRT"] = LogLevel.Critical
        };

    public static LogFileEntry? ParseTextOrNull(
        LogFileDescriptor file,
        PhysicalLogInput line,
        LogEntryContinuation continuation,
        TimeZoneInfo localTimeZone)
    {
        var match = TextEntryPattern.Match(line.Text);
        if (!match.Success
            || !DateTime.TryParseExact(
                match.Groups["timestamp"].Value,
                "yyyy-MM-dd HH:mm:ss.fff",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var localTimestamp))
            return null;

        var exception = BuildException(
            continuation.Lines,
            continuation.MaximumEntryBytes - line.ByteCount - Encoding.UTF8.GetByteCount(Environment.NewLine),
            out var exceptionWasTruncated);
        var rawText = exception is null ? line.Text : $"{line.Text}{Environment.NewLine}{exception}";
        var offset = localTimeZone.GetUtcOffset(localTimestamp);
        var correlationId = CorrelationIdOrNull(ScopePropertyEncoding.Unescape(match.Groups["correlation"].Value));
        var entry = new LogFileEntry(
            LogFileSequence.Of(file, line.Offset),
            new DateTimeOffset(localTimestamp, offset),
            Levels[match.Groups["level"].Value],
            correlationId,
            match.Groups["category"].Value,
            match.Groups["message"].Value,
            exception,
            rawText,
            IsMalformed: false)
        {
            IsTruncated = line.IsTruncated || continuation.WasTruncated || exceptionWasTruncated
        };
        return LogEntryScope.WithScope(
            entry,
            match.Groups["scope"].Success ? ScopePropertyEncoding.Parse(match.Groups["scope"].Value) : null);
    }

    public static LogFileEntry? ParseJsonOrNull(
        LogFileDescriptor file,
        PhysicalLogInput line)
    {
        try
        {
            using var document = JsonDocument.Parse(line.Text);
            var root = document.RootElement;
            if (!TryReadRequired(root, out var timestamp, out var level, out var message))
                return null;

            var correlationId = CorrelationIdOrNull(StringProperty(root, "correlationId"));
            var entry = new LogFileEntry(
                LogFileSequence.Of(file, line.Offset), timestamp, level,
                correlationId, StringProperty(root, "category"),
                message, StringProperty(root, "exception"), line.Text, IsMalformed: false)
            {
                IsTruncated = line.IsTruncated
            };
            return LogEntryScope.WithScope(entry, LogEntryScope.FromJson(root));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads the writer's "outside any scope" marker and an empty field as no correlation id.</summary>
    private static string? CorrelationIdOrNull(string? value)
        => string.IsNullOrEmpty(value) || string.Equals(value, BsftFileLogger.NoCorrelationId, StringComparison.Ordinal)
            ? null
            : value;

    private static bool TryReadRequired(
        JsonElement root,
        out DateTimeOffset timestamp,
        out LogLevel level,
        out string message)
    {
        timestamp = default;
        level = default;
        message = string.Empty;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("timestamp", out var timestampElement)
            || timestampElement.ValueKind != JsonValueKind.String
            || !timestampElement.TryGetDateTimeOffset(out timestamp)
            || !root.TryGetProperty("level", out var levelElement)
            || levelElement.ValueKind != JsonValueKind.String
            || !Enum.TryParse(levelElement.GetString(), ignoreCase: true, out level)
            || !root.TryGetProperty("message", out var messageElement)
            || messageElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        message = messageElement.GetString() ?? string.Empty;
        return true;
    }

    private static string? BuildException(
        Queue<PhysicalLogInput> continuation,
        int availableBytes,
        out bool wasTruncated)
    {
        wasTruncated = false;
        if (continuation.Count == 0)
            return null;

        if (availableBytes <= 0)
        {
            wasTruncated = true;
            return null;
        }

        var builder = new StringBuilder(Math.Min(availableBytes, InitialExceptionCapacity));
        var separator = Environment.NewLine;
        var separatorBytes = Encoding.UTF8.GetByteCount(separator);
        foreach (var item in continuation.Reverse())
        {
            if (builder.Length > 0)
            {
                if (availableBytes <= separatorBytes)
                {
                    wasTruncated = true;
                    break;
                }

                builder.Append(separator);
                availableBytes -= separatorBytes;
            }

            var text = PrefixWithinBytes(item.Text, availableBytes, out var complete);
            builder.Append(text);
            availableBytes -= Encoding.UTF8.GetByteCount(text);
            if (!complete || item.IsTruncated)
            {
                wasTruncated = true;
                break;
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static string PrefixWithinBytes(string value, int maximumBytes, out bool complete)
    {
        if (Encoding.UTF8.GetByteCount(value) <= maximumBytes)
        {
            complete = true;
            return value;
        }

        var bytes = new byte[maximumBytes];
        Encoding.UTF8.GetEncoder().Convert(
            value.AsSpan(),
            bytes,
            flush: true,
            out _,
            out var bytesUsed,
            out complete);
        return Encoding.UTF8.GetString(bytes, 0, bytesUsed);
    }

    private static string? StringProperty(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
