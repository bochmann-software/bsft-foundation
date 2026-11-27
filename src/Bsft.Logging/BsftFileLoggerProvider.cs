using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Bsft.Logging;

/// <summary>
/// A file-based logger provider that writes structured log lines with daily rotation
/// and size-based rolling. Log files are named <c>app-{yyyy-MM-dd}.log</c>, with rolled
/// files using <c>app-{yyyy-MM-dd}_001.log</c>, <c>_002.log</c>, etc. Uses a background
/// flush thread with a <see cref="ConcurrentQueue{T}"/> for thread-safe writing.
/// </summary>
public sealed class BsftFileLoggerProvider : ILoggerProvider, IDisposable
{
    /// <summary>How long <see cref="Dispose"/> waits for the flush thread to drain before giving up.</summary>
    private static readonly TimeSpan ShutdownFlushTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Longest the flush thread waits before draining the queue without being signalled.</summary>
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>Bytes per megabyte, used to turn the configured size limit into a byte threshold.</summary>
    private const long BytesPerMegabyte = 1024L * 1024L;

    private readonly BsftLoggingOptions _options;
    private readonly string _basePath;
    private readonly TimeProvider _timeProvider;
    private readonly LogScopeChain _scopes;
    private readonly ILogFileNaming _fileNaming;
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly ManualResetEventSlim _signal = new(false);

    // The write lock is a semaphore rather than a Monitor so that Dispose can wait for it under a
    // cancellable shutdown budget on the injected clock: Monitor.TryEnter times out on the machine clock only.
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ManualResetEventSlim _flushThreadExited = new(false);
    private readonly ITimer _flushTimer;
    private readonly Thread _flushThread;
    private StreamWriter? _writer;
    private string _currentFileName = string.Empty;
    private DateOnly _currentFileDate;
    private long _currentFileSize;
    private int _currentRollIndex;
    private long _droppedEntryCount;
    private long _abandonedShutdownDrains;
    private long _lastAbandonedQueueDepth;
    private volatile bool _disposed;

    /// <summary>
    /// The number of log entries the provider has genuinely, confirmedly lost instead of writing:
    /// a lost dispose race in <see cref="SignalWriter"/> and a transient writer failure caught in
    /// <see cref="DrainSafely"/>. Every one of these entries really did fail to reach the file.
    /// Exposed so a Release deployment can observe a loss that <see cref="System.Diagnostics.Debug"/>
    /// output alone would hide. A shutdown drain that outran the shutdown budget is deliberately not
    /// counted here — see <see cref="AbandonedShutdownDrains"/> — because its queued entries are
    /// frequently still written by the in-flight drain and are not a confirmed loss.
    /// </summary>
    internal long DroppedEntryCount => Interlocked.Read(ref _droppedEntryCount);

    /// <summary>
    /// The number of shutdown drains the provider abandoned because the flush thread did not release
    /// the write lock within the shutdown budget. This is not a count of lost lines: an abandoned
    /// drain means only that <see cref="Dispose"/> stopped waiting, while the flush thread very likely
    /// remained inside <see cref="DrainQueue"/> and kept writing. Exposed alongside
    /// <see cref="LastAbandonedQueueDepth"/> so the abandoned-shutdown case stays observable in a
    /// Release deployment as its own honest signal, distinct from <see cref="DroppedEntryCount"/>.
    /// </summary>
    internal long AbandonedShutdownDrains => Interlocked.Read(ref _abandonedShutdownDrains);

    /// <summary>
    /// The queue depth observed when the most recent shutdown drain was abandoned. This is an
    /// <em>upper bound</em> on the entries that may be unflushed, not a confirmed drop count: the
    /// in-flight drain still holds the write lock and may write these entries a moment later.
    /// Reported so the potential loss stays observable in Release without over-counting
    /// <see cref="DroppedEntryCount"/>.
    /// </summary>
    internal long LastAbandonedQueueDepth => Interlocked.Read(ref _lastAbandonedQueueDepth);

    /// <summary>
    /// Initializes a new instance of the <see cref="BsftFileLoggerProvider"/> class.
    /// </summary>
    /// <param name="options">The logging options controlling file behavior.</param>
    /// <param name="timeProvider">The time provider used for date-based file naming, the flush interval and the shutdown budget.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> or <paramref name="timeProvider"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a configured value cannot be used, naming the member and the value.
    /// </exception>
    public BsftFileLoggerProvider(BsftLoggingOptions options, TimeProvider timeProvider)
        : this(options, BasePathOf(options), timeProvider, new LogFileNaming(), new LogScopeChain(options))
    {
    }

    /// <summary>Reads the absolute log directory out of the options.</summary>
    /// <param name="options">The options to read.</param>
    /// <returns>The absolute path to write log files to.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a configured value cannot be used, naming the member and the value.
    /// </exception>
    /// <remarks>
    /// This constructor is public and is what <see cref="DependencyInjection.LoggingBuilderExtensions.AddBsftFileLogger(ILoggingBuilder, Action{BsftLoggingOptions})"/>
    /// reaches, so it is a second entry into the sink and cannot assume a registration call validated
    /// anything. Left unchecked, a size of zero rolls the file on every single line, a blank directory
    /// is answered with an exception about an argument the host never passed, and a retention past the
    /// limit throws an unreadable <see cref="ArgumentOutOfRangeException"/> out of the cleanup's date
    /// arithmetic.
    /// </remarks>
    private static string BasePathOf(BsftLoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ValidateFileRules();

        return Path.GetFullPath(options.LogDirectory);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BsftFileLoggerProvider"/> class
    /// using an absolute base path for log files.
    /// </summary>
    /// <param name="options">The logging options controlling file behavior.</param>
    /// <param name="basePath">The absolute path to the log directory.</param>
    /// <param name="timeProvider">The time provider used for date-based file naming.</param>
    internal BsftFileLoggerProvider(BsftLoggingOptions options, string basePath, TimeProvider timeProvider)
        : this(options, basePath, timeProvider, new LogFileNaming(), new LogScopeChain(options))
    {
    }

    /// <summary>Initializes the DI-owned provider after validating its public options entry point.</summary>
    internal BsftFileLoggerProvider(
        BsftLoggingOptions options,
        TimeProvider timeProvider,
        ILogFileNaming fileNaming)
        : this(options, BasePathOf(options), timeProvider, fileNaming, new LogScopeChain(options))
    {
    }

    /// <summary>Initializes the DI-owned provider with the naming service shared by writer and reader and the scope chain of its loggers.</summary>
    internal BsftFileLoggerProvider(
        BsftLoggingOptions options,
        string basePath,
        TimeProvider timeProvider,
        ILogFileNaming fileNaming,
        LogScopeChain scopes)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _fileNaming = fileNaming ?? throw new ArgumentNullException(nameof(fileNaming));
        Directory.CreateDirectory(_basePath);
        ApplyRetention(FindLatestFile(DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime)).Path);

        // The flush interval is a timer on the injected clock that wakes the flush thread, so the thread itself
        // waits on its signal alone and a test clock decides when the interval has elapsed.
        _flushTimer = _timeProvider.CreateTimer(_ => SignalWriter(), null, FlushInterval, FlushInterval);
        _flushThread = new Thread(FlushLoop)
        {
            IsBackground = true,
            Name = "BsftFileLogger-Flush"
        };
        _flushThread.Start();
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new BsftFileLogger(
        this,
        categoryName,
        _options,
        _scopes,
        _timeProvider);

    /// <summary>
    /// Enqueues a formatted log entry for asynchronous writing to the current log file.
    /// This method is thread-safe.
    /// </summary>
    /// <param name="message">The fully formatted log line to write.</param>
    internal void WriteEntry(string message)
    {
        if (_disposed)
            return;

        _queue.Enqueue(message);
        SignalWriter();
    }

    // Kept lock-free for the hot logging path. The Set can race with Dispose disposing the signal;
    // swallowing the resulting ObjectDisposedException avoids crashing the caller's logging call.
    [ExcludeFromCodeCoverage] // dispose race, not deterministically reproducible on the CI OS
    private void SignalWriter()
    {
        try
        {
            _signal.Set();
        }
        catch (ObjectDisposedException ex)
        {
            // Provider disposed concurrently; the entry remains queued and is dropped on shutdown.
            RecordDroppedEntry(ex.Message);
        }
    }

    /// <summary>
    /// Records a genuinely lost log entry on the drop counter and mirrors it to
    /// <see cref="Console.Error"/>, so the loss stays observable in a Release deployment where
    /// <see cref="System.Diagnostics.Debug"/> output is compiled out. Only confirmed per-line losses
    /// are recorded here; an abandoned shutdown drain uses <see cref="RecordAbandonedShutdownDrain"/>.
    /// </summary>
    /// <param name="reason">A human-readable description of why the entry was dropped.</param>
    private void RecordDroppedEntry(string reason)
    {
        Interlocked.Increment(ref _droppedEntryCount);
        Console.Error.WriteLine($"BsftFileLoggerProvider dropped a log entry: {reason}");
    }

    /// <summary>
    /// Records that a shutdown drain was abandoned with <paramref name="queuedDepth"/> entries still
    /// queued, and mirrors it to <see cref="Console.Error"/> in one line. This is a distinct signal
    /// from <see cref="RecordDroppedEntry"/>: an abandoned drain is not a confirmed loss, because the
    /// flush thread still holds the write lock and may write these entries a moment later. The depth
    /// is reported as an upper bound on what may be unflushed, never added to
    /// <see cref="DroppedEntryCount"/>.
    /// </summary>
    /// <param name="queuedDepth">The queue depth observed when the drain was abandoned; an upper bound.</param>
    private void RecordAbandonedShutdownDrain(int queuedDepth)
    {
        Interlocked.Increment(ref _abandonedShutdownDrains);
        Interlocked.Exchange(ref _lastAbandonedQueueDepth, queuedDepth);
        Console.Error.WriteLine(
            $"BsftFileLoggerProvider abandoned a shutdown drain after {ShutdownFlushTimeout}; up to {queuedDepth} queued entries may be unflushed (upper bound; the in-flight drain may still write them).");
    }

    /// <summary>
    /// Flushes all pending log entries to disk synchronously. Intended for testing and shutdown scenarios.
    /// </summary>
    internal void Flush()
    {
        DrainQueue();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _flushTimer.Dispose();
        _signal.Set();

        // One shutdown budget on the injected clock covers both waits below.
        using var budget = new CancellationTokenSource(ShutdownFlushTimeout, _timeProvider);
        var flushThreadFinished = HasFlushThreadFinished(budget.Token);
        System.Diagnostics.Debug.WriteLineIf(!flushThreadFinished, "BsftFileLoggerProvider: the flush thread did not finish within the shutdown timeout.");

        // The wait can end with the flush thread still inside DrainQueue, holding the write gate.
        // Draining and disposing the writer outside that gate would pull the StreamWriter out from
        // under a live WriteLine, and the resulting ObjectDisposedException would escape the flush
        // thread's entry point and tear the process down during shutdown, so the final drain and the
        // disposal take the same gate. A plain wait, though, would wait for that gate without bound:
        // a flush thread genuinely stuck on an unresponsive file-share write never releases it, which
        // would turn the bounded five-second shutdown budget into an unbounded host-shutdown stall.
        // Only the time left in that budget is spent trying to take the gate; if it cannot be taken,
        // the final drain and the writer/signal disposal are skipped and left to finalization rather
        // than hang, and the abandoned drain is recorded on its own observable signal.
        using var hold = _writeGate.HoldWithin(budget.Token);
        if (hold is null)
        {
            // A budget that ran out only means the drain outran the shutdown budget, not that it is
            // wedged: the flush thread still holds the write gate and is very likely still inside
            // DrainQueue writing these very entries, so they are NOT confirmed dropped. Report the
            // abandoned drain and its queue depth as an upper bound on what may be unflushed, kept
            // separate from the precise per-line DroppedEntryCount so that metric stays honest.
            RecordAbandonedShutdownDrain(_queue.Count);
            return;
        }

        DrainGuarded(DrainQueueHoldingGate);

        _writer?.Dispose();
        _writer = null;
        _signal.Dispose();
    }

    /// <summary>Waits for the flush thread to leave its loop, for as long as the shutdown budget lasts.</summary>
    /// <param name="budget">Cancelled when the shutdown budget has run out.</param>
    /// <returns><see langword="true"/> when the thread finished within the budget.</returns>
    private bool HasFlushThreadFinished(CancellationToken budget)
    {
        try
        {
            _flushThreadExited.Wait(budget);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private void FlushLoop()
    {
        try
        {
            // The loop ends on the dispose flag, or on a signal that Dispose has already taken away.
            while (!_disposed && TryWaitForSignal())
                DrainSafely();
        }
        finally
        {
            _flushThreadExited.Set();
        }
    }

    /// <summary>
    /// Waits for the next flush signal, which a write or the flush-interval timer gives.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> to keep looping; <see langword="false"/> when the signal has been
    /// disposed by a concurrent <see cref="Dispose"/>, in which case the loop must end quietly.
    /// </returns>
    private bool TryWaitForSignal()
    {
        try
        {
            _signal.Wait();
            _signal.Reset();
            return true;
        }
        catch (ObjectDisposedException ex)
        {
            // Dispose won the race for the signal; the queue is drained by Dispose under _writeGate.
            System.Diagnostics.Debug.WriteLine(ex.Message);
            return false;
        }
    }

    // Guards the background thread: a transient file IO failure (e.g. the writer cannot be opened)
    // must not let the exception escape and silently kill the flush thread, which would drop all
    // subsequent log entries. ObjectDisposedException is defense-in-depth rather than an active race:
    // now that the write lock mutually excludes the writer's disposal from every DrainQueue write, a
    // live write can no longer meet a disposed writer through the class's own paths. It stays caught
    // because Dispose's bounded skip-drain path leaves the writer undisposed for finalization and a
    // later touch could still land on a disposed writer, and because a future refactor could
    // reopen the window. In every case the line is dropped and recorded on the observable channel
    // instead of taking the process down from a background thread.
    private void DrainSafely() => DrainGuarded(DrainQueue);

    private void DrainGuarded(Action drain)
    {
        try
        {
            drain();
        }
        catch (IOException ex)
        {
            RecordDroppedEntry(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            RecordDroppedEntry(ex.Message);
        }
        catch (ObjectDisposedException ex)
        {
            RecordDroppedEntry(ex.Message);
        }
    }

    /// <summary>
    /// Writes everything queued to the current file, rolling it whenever it grows past the limit.
    /// </summary>
    /// <remarks>
    /// Flushed once per drain rather than once per line: the queue is what makes a burst of logging
    /// cheap for the thread that did it, and flushing inside the loop would hand every one of those
    /// lines its own write to the file anyway. A drain runs at least every half second, and a roll
    /// disposes the writer, which flushes what it holds — so nothing waits longer than that to land.
    /// </remarks>
    private void DrainQueue()
    {
        using var hold = _writeGate.Hold();
        DrainQueueHoldingGate();
    }

    /// <summary>The drain itself, for a caller that already holds the write gate, which is not reentrant.</summary>
    private void DrainQueueHoldingGate()
    {
        var written = false;

        while (_queue.TryDequeue(out var entry))
        {
            EnsureWriter();
            _writer!.WriteLine(entry);
            written = true;
            _currentFileSize += Encoding.UTF8.GetByteCount(entry) + Environment.NewLine.Length;

            if (_currentFileSize >= _options.MaxFileSizeMb * BytesPerMegabyte)
            {
                RollFile();
                written = false;
            }
        }

        if (written)
            _writer!.Flush();
    }

    /// <remarks>
    /// Runs once per line, so the day the open file belongs to is held rather than read back off its
    /// name: on every call but the one a day that turns the date over, this compares two day values
    /// and does nothing else.
    /// </remarks>
    private void EnsureWriter()
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);

        if (_writer is not null && _currentFileDate == today)
            return;

        if (_writer is not null)
        {
            _writer.Dispose();
            _writer = null;
            _currentRollIndex = 0;
        }

        var current = FindLatestFile(today);
        _currentRollIndex = current.RollIndex;
        OpenWriterFor(current.Path, today);

        // The day just turned over, which is the one moment a running process can prune without
        // polling for it. Without this the limits only ever apply at start-up, and a service that
        // runs for months keeps every file it has ever written. It runs once the new file is open so
        // that the file being written is known and kept.
        ApplyRetention(_currentFileName);
    }

    /// <summary>Finds the last roll already written for a day so restart appends stay monotonic.</summary>
    /// <param name="date">The file-name date.</param>
    /// <returns>The path and roll index to append to.</returns>
    private (string Path, int RollIndex) FindLatestFile(DateOnly date)
    {
        var latestIndex = 0;
        var latestPath = _fileNaming.PathFor(_basePath, date, latestIndex);

        foreach (var path in Directory.EnumerateFiles(_basePath, _fileNaming.FilesForDatePattern(date)))
        {
            var file = _fileNaming.DescribeOrNull(path);
            if (file is not null && file.Date == date && file.RollIndex > latestIndex)
            {
                latestIndex = file.RollIndex;
                latestPath = path;
            }
        }

        return (latestPath, latestIndex);
    }

    /// <summary>
    /// Opens the writer on one file and takes the file's current size as the roll counter's start.
    /// </summary>
    /// <param name="filePath">The file to write to.</param>
    /// <param name="fileDate">The day the file belongs to, held for the next rollover check.</param>
    /// <remarks>
    /// The stream appends, so the counter has to start from what is already in the file. After a
    /// restart the roll index starts at zero again and walks back over the files of an earlier run:
    /// assuming they were empty would let them grow without bound, which is the one thing
    /// <see cref="BsftLoggingOptions.MaxFileSizeMb"/> exists to prevent.
    /// </remarks>
    private void OpenWriterFor(string filePath, DateOnly fileDate)
    {
        _currentFileName = filePath;
        _currentFileDate = fileDate;

        var existing = new FileInfo(_currentFileName);
        _currentFileSize = existing.Exists ? existing.Length : 0;

        var stream = new FileStream(
            _currentFileName,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private void RollFile()
    {
        _writer?.Dispose();
        _writer = null;

        _currentRollIndex++;
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);

        OpenWriterFor(_fileNaming.PathFor(_basePath, today, _currentRollIndex), today);

        // A roll adds a file, so the count and size limits are checked here as well.
        ApplyRetention(_currentFileName);
    }

    /// <summary>Deletes the files that exceed the configured limits, sparing the one being written.</summary>
    /// <remarks>The count and size limits assume this provider is the only writer in the directory.</remarks>
    /// <param name="activePath">The file being written or about to be appended to.</param>
    private void ApplyRetention(string activePath)
        => new LogFileRetention(_options, _fileNaming, _timeProvider, new PhysicalLogFileStore(_basePath), ReportCleanupProblem).Apply(activePath);

    /// <summary>Mirrors a swallowed cleanup failure to <see cref="Console.Error"/>, where a Release host can see it.</summary>
    /// <param name="message">What failed.</param>
    internal static void ReportCleanupProblem(string message)
        => Console.Error.WriteLine($"BsftFileLoggerProvider: {message}");
}
