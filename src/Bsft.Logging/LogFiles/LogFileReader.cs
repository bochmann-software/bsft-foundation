using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Bsft.Logging.LogFiles;

/// <summary>Reads the rolling text or JSON files produced by the BSFT file logger.</summary>
public sealed class LogFileReader : ILogFileReader
{
    private const int MaximumPageSize = 1000;

    private readonly string _directory;
    private readonly ILogFileAccess _files;
    private readonly int _maximumEntryBytes;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _pollInterval;
    private readonly ILogFileNaming _fileNaming;

    /// <summary>Initializes a reader from the same options used by the file logger.</summary>
    /// <param name="options">The file location and tail polling options.</param>
    /// <param name="timeProvider">The clock used for cancellable polling.</param>
    /// <param name="fileAccess">The replaceable local-file access, or the production implementation.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public LogFileReader(
        BsftLoggingOptions options,
        TimeProvider? timeProvider = null,
        ILogFileAccess? fileAccess = null)
        : this(options, timeProvider, fileAccess ?? new LogFileAccess(), new LogFileNaming())
    {
    }

    /// <summary>Initializes the DI-owned reader with its replaceable file access and shared naming service.</summary>
    internal LogFileReader(
        BsftLoggingOptions options,
        TimeProvider? timeProvider,
        ILogFileAccess fileAccess,
        ILogFileNaming fileNaming)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ValidateFileRules();
        options.ValidateReaderRules();
        _directory = Path.GetFullPath(options.LogDirectory);
        _pollInterval = options.LogReaderPollInterval;
        _maximumEntryBytes = options.MaxEntryBytes;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _files = fileAccess ?? throw new ArgumentNullException(nameof(fileAccess));
        _fileNaming = fileNaming ?? throw new ArgumentNullException(nameof(fileNaming));
    }

    /// <inheritdoc />
    public async Task<LogReadPageResult> ReadAsync(
        LogReadRequest query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.PageSize is < 1 or > MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(query), query.PageSize, $"Page size must be between 1 and {MaximumPageSize}.");

        cancellationToken.ThrowIfCancellationRequested();
        var candidates = new List<LogFileEntry>(query.PageSize + 1);
        await foreach (var entry in EnumerateNewestAsync(query, cancellationToken).ConfigureAwait(false))
        {
            candidates.Add(entry);
            if (candidates.Count > query.PageSize)
                break;
        }

        var hasMore = candidates.Count > query.PageSize;
        if (hasMore)
            candidates.RemoveAt(candidates.Count - 1);

        long? nextBefore = hasMore ? candidates[^1].Sequence : null;
        return new LogReadPageResult(candidates, nextBefore);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<LogFileEntry> TailAsync(
        LogReadRequest query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var initialQuery = query with { Before = null };
        var lastSequence = query.Before
            ?? await NewestSequenceOrZeroAsync(initialQuery, cancellationToken).ConfigureAwait(false);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var burst = new List<LogFileEntry>();
            await foreach (var entry in EnumerateNewestAsync(initialQuery, cancellationToken).ConfigureAwait(false))
            {
                if (entry.Sequence <= lastSequence)
                    break;

                burst.Add(entry);
            }

            for (var index = burst.Count - 1; index >= 0; index--)
            {
                var entry = burst[index];
                lastSequence = entry.Sequence;
                yield return entry;
            }

            await Task.Delay(_pollInterval, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<long> NewestSequenceOrZeroAsync(
        LogReadRequest query,
        CancellationToken cancellationToken)
    {
        await foreach (var entry in EnumerateNewestAsync(query, cancellationToken).ConfigureAwait(false))
            return entry.Sequence;

        return 0;
    }

    private async IAsyncEnumerable<LogFileEntry> EnumerateNewestAsync(
        LogReadRequest query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var file in LocateFilesNewest())
        {
            await using var entries = ReadFileNewestAsync(file, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            while (await IsMoveNextAvailableAsync(entries).ConfigureAwait(false))
            {
                var entry = entries.Current;
                if ((query.Before is null || entry.Sequence < query.Before.Value)
                    && IsMatch(entry, query))
                {
                    yield return entry;
                }
            }
        }
    }

    private IReadOnlyList<LogFileDescriptor> LocateFilesNewest()
    {
        try
        {
            if (!_files.IsDirectoryPresent(_directory))
                return [];

            return _files.EnumerateFiles(_directory, _fileNaming.AllFilesPattern)
                .Select(_fileNaming.DescribeOrNull)
                .Where(file => file is not null)
                .Cast<LogFileDescriptor>()
                .OrderByDescending(file => file.Date)
                .ThenByDescending(file => file.RollIndex)
                .ToList();
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
    }

    private static async ValueTask<bool> IsMoveNextAvailableAsync(IAsyncEnumerator<LogFileEntry> entries)
    {
        try
        {
            return await entries.MoveNextAsync().ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static bool IsMatch(LogFileEntry entry, LogReadRequest query)
    {
        if (query.MinimumLevel is { } minimum && entry.Level < minimum)
            return false;

        if (query.IncludedLevels is { } included && !included.Contains(entry.Level))
            return false;

        if (string.IsNullOrWhiteSpace(query.SearchText))
            return true;

        return Contains(entry.Category, query.SearchText)
            || Contains(entry.Message, query.SearchText)
            || Contains(entry.Exception, query.SearchText);
    }

    private static bool Contains(string? value, string searchText)
        => value?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true;

    private async IAsyncEnumerable<LogFileEntry> ReadFileNewestAsync(
        LogFileDescriptor file,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var continuation = new LogEntryContinuation(_maximumEntryBytes);
        await foreach (var line in ReverseLogFileScanner.ReadLinesNewestAsync(
                           _files,
                           file.Path,
                           _maximumEntryBytes,
                           cancellationToken)
                           .ConfigureAwait(false))
        {
            var jsonEntry = LogFileEntryDecoder.ParseJsonOrNull(file, line);
            if (jsonEntry is not null)
            {
                foreach (var malformed in FlushMalformed(file, continuation.Lines))
                    yield return malformed;

                continuation.Clear();
                yield return jsonEntry;
                continue;
            }

            var textEntry = LogFileEntryDecoder.ParseTextOrNull(
                file,
                line,
                continuation,
                _timeProvider.LocalTimeZone);
            if (textEntry is not null)
            {
                continuation.Clear();
                yield return textEntry;
                continue;
            }

            continuation.Add(line);
        }

        foreach (var malformed in FlushMalformed(file, continuation.Lines))
            yield return malformed;
    }

    private static IEnumerable<LogFileEntry> FlushMalformed(
        LogFileDescriptor file,
        Queue<PhysicalLogInput> lines)
    {
        foreach (var line in lines)
        {
            yield return new LogFileEntry(
                LogFileSequence.Of(file, line.Offset), null, LogLevel.None, null, null,
                line.Text, null, line.Text, IsMalformed: true)
            {
                IsTruncated = line.IsTruncated
            };
        }

        lines.Clear();
    }
}
