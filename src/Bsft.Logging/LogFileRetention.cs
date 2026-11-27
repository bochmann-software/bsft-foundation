namespace Bsft.Logging;

/// <summary>
/// Removes the oldest log files until the configured age, file-count and total-size limits hold.
/// </summary>
/// <remarks>
/// Only files whose name the <see cref="ILogFileNaming"/> recognises are considered, and they are ordered by
/// the date and roll index in the name, oldest first. The name is what the logger itself wrote, so the order
/// is the order the files were written in and does not move when a backup tool or a copy touches a file's
/// last-write time. A file whose name does not parse was not written by this logger and is never deleted.
/// The file being written is never deleted. A file that is locked, already gone or otherwise cannot be
/// removed is reported and left alone: cleanup runs inside the logging path and must not throw into it.
/// </remarks>
internal sealed class LogFileRetention
{
    private const long BytesPerMegabyte = 1024L * 1024L;

    private readonly BsftLoggingOptions _options;
    private readonly ILogFileNaming _fileNaming;
    private readonly TimeProvider _timeProvider;
    private readonly ILogFileStore _store;
    private readonly Action<string> _report;

    /// <summary>Initializes the retention.</summary>
    /// <param name="options">The limits to enforce.</param>
    /// <param name="fileNaming">Recognises the files the logger wrote.</param>
    /// <param name="timeProvider">The clock the age cutoff is read from.</param>
    /// <param name="store">The log directory.</param>
    /// <param name="report">Receives a line for every failure that was swallowed.</param>
    public LogFileRetention(
        BsftLoggingOptions options,
        ILogFileNaming fileNaming,
        TimeProvider timeProvider,
        ILogFileStore store,
        Action<string> report)
    {
        _options = options;
        _fileNaming = fileNaming;
        _timeProvider = timeProvider;
        _store = store;
        _report = report;
    }

    /// <summary>Deletes the oldest files until every configured limit holds.</summary>
    /// <param name="activePath">The file being written or about to be appended to, which is never deleted.</param>
    public void Apply(string? activePath)
    {
        try
        {
            var files = ListOldestFirst();
            DeleteExpired(files, activePath);
            LimitCount(files, activePath);
            LimitTotalSize(files, activePath);
        }
        catch (IOException ex)
        {
            _report($"Log file cleanup failed: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            _report($"Log file cleanup failed: {ex.Message}");
        }
    }

    private List<LogFileDescriptor> ListOldestFirst()
        => _store.List(_fileNaming.AllFilesPattern)
            .Select(_fileNaming.DescribeOrNull)
            .OfType<LogFileDescriptor>()
            .OrderBy(file => file.Date)
            .ThenBy(file => file.RollIndex)
            .ToList();

    private void DeleteExpired(List<LogFileDescriptor> files, string? activePath)
    {
        if (_options.RetainDays <= 0)
            return;

        var cutoff = DateOnly.FromDateTime(_timeProvider.GetLocalNow().Date.AddDays(-_options.RetainDays));
        TrimOldest(files, activePath, file => file.Date < cutoff);
    }

    private void LimitCount(List<LogFileDescriptor> files, string? activePath)
    {
        if (_options.MaxRetainedFiles is not > 0)
            return;

        var limit = _options.MaxRetainedFiles.Value;
        TrimOldest(files, activePath, _ => files.Count > limit);
    }

    private void LimitTotalSize(List<LogFileDescriptor> files, string? activePath)
    {
        if (_options.MaxTotalSizeMb is not > 0)
            return;

        var limit = _options.MaxTotalSizeMb.Value * BytesPerMegabyte;
        var sizes = files.ToDictionary(file => file.Path, file => LengthOf(file.Path));
        TrimOldest(files, activePath, _ => files.Sum(file => sizes[file.Path]) > limit);
    }

    /// <summary>Walks the files oldest first and deletes each one the predicate still asks for.</summary>
    private void TrimOldest(List<LogFileDescriptor> files, string? activePath, Func<LogFileDescriptor, bool> shouldDelete)
    {
        foreach (var file in files.ToArray())
        {
            if (!string.Equals(file.Path, activePath, StringComparison.Ordinal)
                && shouldDelete(file)
                && TryDelete(file.Path))
                files.Remove(file);
        }
    }

    private bool TryDelete(string path)
    {
        try
        {
            _store.Delete(path);
            return true;
        }
        catch (IOException ex)
        {
            ReportUndeletable(path, ex);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            ReportUndeletable(path, ex);
            return false;
        }
    }

    private void ReportUndeletable(string path, Exception ex)
        => _report($"Log file '{path}' could not be deleted: {ex.Message}");

    private long LengthOf(string path)
    {
        try
        {
            return _store.LengthOf(path);
        }
        catch (IOException ex)
        {
            return ReportUnmeasurable(path, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ReportUnmeasurable(path, ex);
        }
    }

    private long ReportUnmeasurable(string path, Exception ex)
    {
        _report($"Log file '{path}' could not be measured: {ex.Message}");
        return 0;
    }
}
