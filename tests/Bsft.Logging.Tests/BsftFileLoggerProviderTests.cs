using Bsft.Logging.Tests.Fakes;
using Bsft.Logging.Tests.Support;
using FluentAssertions;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Bsft.Logging.Tests;

public class BsftFileLoggerProviderTests : IDisposable
{
    private readonly LogFileFixture _log = new();

    /// <inheritdoc />
    public void Dispose() => _log.Dispose();

    [Fact]
    public void CreatesLogDirectory()
    {
        var options = LogFileFixture.Options();

        using var provider = new BsftFileLoggerProvider(options, _log.LogDirectory);

        Directory.Exists(_log.LogDirectory).Should().BeTrue();
    }

    [Fact]
    public void WritesLogFileInCorrectDirectory()
    {
        var options = LogFileFixture.Options();
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory);
        var logger = provider.CreateLogger("My.Namespace.TestCategory");

        logger.LogInformation("Hello from test");
        provider.Flush();
        provider.Dispose();

        var files = Directory.GetFiles(_log.LogDirectory, "app-*.log");
        files.Should().HaveCount(1);

        var content = File.ReadAllText(files[0]);
        content.Should().Contain(" INF]");
        content.Should().Contain("TestCategory: Hello from test");
        content.Should().Contain("[-]");
    }

    [Fact]
    public void DailyRotation_FileContainsTodaysDate()
    {
        var options = LogFileFixture.Options();
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory, _log.Clock);
        var logger = provider.CreateLogger("Test");

        logger.LogInformation("date check");
        provider.Flush();
        provider.Dispose();

        var today = _log.Clock.GetLocalNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var files = Directory.GetFiles(_log.LogDirectory, $"app-{today}.log");
        files.Should().HaveCount(1);
    }

    [Fact]
    public void CleanupOldFiles_DeletesExpiredFiles()
    {
        Directory.CreateDirectory(_log.LogDirectory);

        var oldDate = _log.Clock.GetLocalNow().AddDays(-60).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var oldFile = Path.Combine(_log.LogDirectory, $"app-{oldDate}.log");
        File.WriteAllText(oldFile, "old log entry");

        var recentDate = _log.Clock.GetLocalNow().AddDays(-5).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var recentFile = Path.Combine(_log.LogDirectory, $"app-{recentDate}.log");
        File.WriteAllText(recentFile, "recent log entry");

        var options = LogFileFixture.Options();
        options.RetainDays = 30;

        using var provider = new BsftFileLoggerProvider(options, _log.LogDirectory, _log.Clock);

        File.Exists(oldFile).Should().BeFalse("files older than RetainDays should be deleted");
        File.Exists(recentFile).Should().BeTrue("recent files should be retained");
    }

    [Fact]
    public void RollsOnSize_CreatesNumberedFiles()
    {
        var options = LogFileFixture.Options();
        options.MaxFileSizeMb = 1;

        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory);
        var logger = provider.CreateLogger("Test");

        var largeMessage = new string('X', 100_000);
        for (var i = 0; i < 15; i++)
            logger.LogInformation(largeMessage);

        provider.Flush();
        provider.Dispose();

        var files = Directory.GetFiles(_log.LogDirectory, "app-*.log");
        files.Should().HaveCountGreaterThan(1, "writing beyond MaxFileSizeMb should create rolled files");

        var rolledFiles = files.Where(f => Path.GetFileName(f).Contains("_001")).ToArray();
        rolledFiles.Should().HaveCount(1, "the first rolled file should have suffix _001");
    }

    [Fact]
    public void RollingOntoAFileFromAnEarlierRun_DoesNotLetItGrowPastTheLimit()
    {
        // After a restart the roll index starts at zero again and walks back over the files of the
        // earlier run. Assuming a rolled file is empty made the writer append a whole further limit's
        // worth to a file that was already full, so files grew without bound across restarts.
        var options = LogFileFixture.Options();
        options.MaxFileSizeMb = 1;
        var limit = 1024L * 1024L;
        var date = LogFileFixture.FixedNow.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        Directory.CreateDirectory(_log.LogDirectory);
        File.WriteAllText(Path.Combine(_log.LogDirectory, $"app-{date}.log"), new string('X', (int)limit + 1_000));
        File.WriteAllText(Path.Combine(_log.LogDirectory, $"app-{date}_001.log"), new string('X', (int)limit + 1_000));

        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory, _log.Clock);
        var logger = provider.CreateLogger("Test");

        for (var i = 0; i < 5; i++)
            logger.LogInformation("a line that lands after the restart");

        provider.Flush();
        provider.Dispose();

        // Both pre-existing files are full, so the writer has to walk past them to a fresh one
        // instead of piling onto _001.
        File.Exists(Path.Combine(_log.LogDirectory, $"app-{date}_002.log"))
            .Should().BeTrue("a rolled file that is already at the limit must not be appended to");
        new FileInfo(Path.Combine(_log.LogDirectory, $"app-{date}_001.log")).Length
            .Should().BeLessThan(limit * 2, "the file from the earlier run must not absorb another limit's worth");
    }

    [Fact]
    public async Task ThreadSafety_MultipleConcurrentWrites()
    {
        var options = LogFileFixture.Options();
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory);

        const int ThreadCount = 10;
        const int MessagesPerThread = 100;
        var tasks = new Task[ThreadCount];

        for (var t = 0; t < ThreadCount; t++)
        {
            var threadIndex = t;
            tasks[t] = Task.Run(() =>
            {
                var logger = provider.CreateLogger($"Thread{threadIndex}");
                for (var i = 0; i < MessagesPerThread; i++)
                    logger.LogInformation("Message {Index} from thread {ThreadIndex}", i, threadIndex);
            });
        }

        await Task.WhenAll(tasks);
        provider.Flush();
        provider.Dispose();

        var files = Directory.GetFiles(_log.LogDirectory, "app-*.log");
        var allContent = string.Join(Environment.NewLine, files.Select(File.ReadAllText));
        var lineCount = allContent.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length;

        lineCount.Should().Be(ThreadCount * MessagesPerThread,
            "all messages from all threads should be written without loss");
    }

    [Fact]
    public void WriteEntry_AfterDispose_IsIgnored()
    {
        var options = LogFileFixture.Options();
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory);
        var logger = provider.CreateLogger("DisposeTest");

        provider.Dispose();

        var act = () => logger.LogInformation("message after dispose");

        act.Should().NotThrow("a logger outliving its provider must not fail the caller's call");
        Directory.GetFiles(_log.LogDirectory, "app-*.log")
            .SelectMany(File.ReadAllLines)
            .Should().NotContain(line => line.Contains("message after dispose", StringComparison.Ordinal));
    }

    [Fact]
    public void EnsureWriter_DateChanged_ClosesOldWriterAndOpensNew()
    {
        // A writer left open on yesterday's file has to be closed and reopened on today's, or a
        // process that runs past midnight keeps writing into the previous day's file.
        var options = LogFileFixture.Options();
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory, _log.Clock);
        var logger = provider.CreateLogger("DateTest");

        var firstDay = _log.Clock.GetLocalNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        logger.LogInformation("an entry on the first day");
        provider.Flush();

        _log.Clock.MoveTo(LogFileFixture.FixedNow.AddDays(1));
        var secondDay = _log.Clock.GetLocalNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        logger.LogInformation("an entry on the day after");
        provider.Flush();
        provider.Dispose();

        File.ReadAllText(Path.Combine(_log.LogDirectory, $"app-{firstDay}.log"))
            .Should().Contain("an entry on the first day").And.NotContain("the day after");
        File.ReadAllText(Path.Combine(_log.LogDirectory, $"app-{secondDay}.log"))
            .Should().Contain("an entry on the day after");
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var options = LogFileFixture.Options();
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory);

        provider.Dispose();

        var act = () => provider.Dispose();

        act.Should().NotThrow("double-dispose should be a no-op");
    }

    [Fact]
    public void CleanupOldFiles_RetainDaysZero_SkipsCleanup()
    {
        // A retention of zero is "keep everything", not "delete everything": the cleanup is skipped
        // rather than run with a cutoff of today.
        Directory.CreateDirectory(_log.LogDirectory);

        var oldDate = _log.Clock.GetLocalNow().AddDays(-60).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var oldFile = Path.Combine(_log.LogDirectory, $"app-{oldDate}.log");
        File.WriteAllText(oldFile, "old entry");

        var options = LogFileFixture.Options();
        options.RetainDays = 0; // <= 0 -> CleanupOldFiles returns immediately at line 171

        using var provider = new BsftFileLoggerProvider(options, _log.LogDirectory, _log.Clock);

        // Old file should NOT be deleted because RetainDays = 0 causes early return
        File.Exists(oldFile).Should().BeTrue("with RetainDays=0, cleanup is skipped entirely");
    }

    [Fact]
    public void CleanupOldFiles_LockedFile_SkipsWithoutException()
    {
        // Another process holding a file open must not fail the cleanup, and with it the
        // provider's construction.
        Directory.CreateDirectory(_log.LogDirectory);

        var oldDate = _log.Clock.GetLocalNow().AddDays(-60).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var lockedFile = Path.Combine(_log.LogDirectory, $"app-{oldDate}.log");
        File.WriteAllText(lockedFile, "old entry");

        // Open the file with exclusive lock so File.Delete throws IOException
        using var fileStream = new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None);

        var options = LogFileFixture.Options();
        options.RetainDays = 30;

        // The provider constructor calls CleanupOldFiles; the locked file should be skipped silently
        var act = () =>
        {
            using var provider = new BsftFileLoggerProvider(options, _log.LogDirectory, _log.Clock);
        };

        act.Should().NotThrow("locked files during cleanup should be silently skipped");
    }

    [Fact]
    public void Dispose_WhenTheWriteLockIsHeldByADrain_WaitsForItBeforeDisposingTheWriter()
    {
        // Dispose waits at most ShutdownFlushTimeout for the flush thread. When that Join times out,
        // the flush thread is still inside DrainQueue holding the write lock, and disposing the
        // StreamWriter next pulled it out from under a live WriteLine: the ObjectDisposedException
        // escaped the flush thread's entry point and terminated the process during shutdown.
        //
        // Deterministic here: the test itself holds the provider's write lock, which is exactly the
        // state a mid-drain flush thread is in, and asserts that Dispose blocks on it instead of
        // disposing the writer, then completes and writes the backlog once the lock is released.
        // Best-effort: the timing of a genuinely timed-out Join cannot be forced, so what is proven
        // is the mutual exclusion the fix rests on, not a particular scheduler interleaving.
        var options = LogFileFixture.Options();
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory, _log.Clock);
        var logger = provider.CreateLogger("DisposeRace");
        var writeLock = GetWriteLock(provider);

        Exception? disposeFailure = null;
        var disposeThread = new Thread(() =>
        {
            try
            {
                provider.Dispose();
            }
            catch (Exception ex)
            {
                disposeFailure = ex;
            }
        });

        Monitor.Enter(writeLock);
        try
        {
            logger.LogInformation("queued while the writer is busy");
            disposeThread.Start();

            disposeThread.Join(TimeSpan.FromMilliseconds(300))
                .Should().BeFalse("Dispose must not touch the writer while another drain holds the write lock");
        }
        finally
        {
            Monitor.Exit(writeLock);
        }

        disposeThread.Join(TimeSpan.FromSeconds(30))
            .Should().BeTrue("Dispose must complete once the write lock is free");
        disposeFailure.Should().BeNull("disposing while a drain is in flight must not throw");

        var content = File.ReadAllText(Directory.GetFiles(_log.LogDirectory, "app-*.log")[0]);
        content.Should().Contain("queued while the writer is busy", "the final drain still flushes the backlog");
    }

    [Fact]
    public void DrainSafely_WhenTheWriterWasDisposedByARace_DropsTheLineInsteadOfThrowing()
    {
        // A lost dispose race leaves the flush thread writing to a disposed StreamWriter. Before the
        // fix that ObjectDisposedException escaped FlushLoop and killed the process; now the line is
        // dropped. Deterministic: the disposed writer is installed directly, no timing involved.
        using var disposedWriter = new StreamWriter(new MemoryStream());
        disposedWriter.Dispose();

        DrainSafely_WithBrokenWriter_SwallowsTheFailure(disposedWriter);
    }

    [Fact]
    public void DrainSafely_WhenTheWriterFailsWithIoException_DropsTheLineInsteadOfThrowing()
    {
        var writer = new StreamWriter(new ThrowingStream(new IOException("disk gone")));

        DrainSafely_WithBrokenWriter_SwallowsTheFailure(writer);
    }

    [Fact]
    public void DrainSafely_WhenTheWriterFailsWithUnauthorizedAccess_DropsTheLineInsteadOfThrowing()
    {
        var writer = new StreamWriter(new ThrowingStream(new UnauthorizedAccessException("denied")));

        DrainSafely_WithBrokenWriter_SwallowsTheFailure(writer);
    }

    [Fact]
    public void TryWaitForSignal_WhenTheSignalIsAlreadyDisposed_EndsTheFlushLoopQuietly()
    {
        // The flush thread waits on the signal outside every lock, so a Dispose that wins the race
        // disposes it underneath the wait. That must end the loop, not throw off the thread.
        var options = LogFileFixture.Options();
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory, _log.Clock);
        provider.Dispose(); // disposes the signal

        var waitForSignal = typeof(BsftFileLoggerProvider)
            .GetMethod("TryWaitForSignal", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var keepLooping = waitForSignal.Invoke(provider, null);

        keepLooping.Should().Be(false, "a disposed signal must end the flush loop without an exception");
    }

    [Fact]
    public void Dispose_WhenTheWriteLockStaysHeldPastTheBudget_SkipsTheDrainInsteadOfHanging()
    {
        // Finding 1: Dispose used to re-acquire the write lock with a blocking lock, so a flush thread
        // wedged inside DrainQueue turned the bounded five-second shutdown into an unbounded stall.
        // Deterministic here: the test holds the write lock and never releases it while Dispose runs,
        // exactly the state a genuinely stuck drain leaves behind. Dispose must spend only the bounded
        // budget, then skip the drain and the writer/signal disposal and return instead of hanging.
        var options = LogFileFixture.Options();
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory, _log.Clock);
        var writeLock = GetWriteLock(provider);
        var signal = GetField<ManualResetEventSlim>(provider, "_signal");

        // An abandoned drain is not a confirmed loss: the flush thread still holds the write lock and
        // may still write these entries. So the abandoned-drain signal, not DroppedEntryCount, must
        // reflect the queue depth as an upper bound. Seed more than one entry under the held write lock
        // so the flush thread cannot drain them before Dispose abandons the drain.
        const int backlog = 7;

        Exception? disposeFailure = null;
        var completed = new ManualResetEventSlim(false);
        var disposeThread = new Thread(() =>
        {
            try
            {
                provider.Dispose();
            }
            catch (Exception ex)
            {
                disposeFailure = ex;
            }
            finally
            {
                completed.Set();
            }
        });

        Monitor.Enter(writeLock);
        try
        {
            var queue = GetQueue(provider);
            for (var i = 0; i < backlog; i++)
                queue.Enqueue($"queued but never drained {i}");

            disposeThread.Start();

            completed.Wait(TimeSpan.FromSeconds(30))
                .Should().BeTrue("Dispose must return within the bounded budget even while the write lock is never released");
        }
        finally
        {
            Monitor.Exit(writeLock);
        }

        disposeThread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
        disposeFailure.Should().BeNull("an abandoned drain must not throw");
        provider.AbandonedShutdownDrains.Should().Be(1, "the abandoned shutdown drain must be recorded on its own signal");
        provider.LastAbandonedQueueDepth.Should().Be(backlog, "the abandoned-drain signal must report the queue depth as an upper bound on what may be unflushed");
        provider.DroppedEntryCount.Should().Be(0, "an abandoned drain is not a confirmed loss and must not over-count DroppedEntryCount");

        var readSignal = () => _ = signal.IsSet;
        readSignal.Should().NotThrow("a skipped drain must leave the signal undisposed for finalization instead of disposing it under the held lock");

        // The skip-drain path leaves the writer open for finalization, so its file handle outlives
        // Dispose. Let the flush thread reach its terminal state, then release the handle so the temp
        // directory can be cleaned up.
        GetField<Thread>(provider, "_flushThread").Join(TimeSpan.FromSeconds(5));
        GetField<StreamWriter?>(provider, "_writer")?.Dispose();
        SetField(provider, "_writer", null);
    }

    [Fact]
    public void DrainSafely_WhenTheWriterFails_RecordsAndMirrorsTheDropToConsoleError()
    {
        // Finding 2: dropped lines were reported only through Debug.WriteLine, a no-op outside DEBUG
        // builds. A drop must now increment the internal counter and reach Console.Error so it stays
        // observable in a Release deployment.
        var options = LogFileFixture.Options();
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory, _log.Clock);
        var writeLock = GetWriteLock(provider);
        var today = _log.Clock.GetLocalNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        // Not disposed on purpose: the ThrowingStream fails on Flush, so disposing the writer would
        // throw. The underlying stream holds no OS handle, so leaving it to GC is safe.
        var brokenWriter = new StreamWriter(new ThrowingStream(new IOException("disk gone")));

        var originalError = Console.Error;
        using var captured = new StringWriter();
        Console.SetError(captured);
        try
        {
            Monitor.Enter(writeLock);
            try
            {
                SetField(provider, "_currentFileName", Path.Combine(_log.LogDirectory, $"app-{today}.log"));
                SetField(provider, "_writer", brokenWriter);
                GetQueue(provider).Enqueue("a line lost to the broken writer");

                InvokeDrainSafely(provider);
            }
            finally
            {
                SetField(provider, "_writer", null);
                Monitor.Exit(writeLock);
            }
        }
        finally
        {
            Console.SetError(originalError);
        }

        provider.DroppedEntryCount.Should().Be(1, "the dropped line must be counted on the release-observable channel");
        captured.ToString().Should().Contain("dropped a log entry", "the drop must be mirrored to Console.Error for Release visibility");

        provider.Dispose();
    }

    [Fact]
    public void RecordAbandonedShutdownDrain_WhenInvoked_ReportsAnUpperBoundWithoutTouchingDroppedEntryCount()
    {
        // An abandoned shutdown drain is not a confirmed loss: the in-flight drain may still write the
        // queued entries. The signal must be its own honestly-named channel (count plus last queue
        // depth as an upper bound), mirrored to Console.Error for Release visibility, and must never
        // be added to the precise per-line DroppedEntryCount.
        var options = LogFileFixture.Options();
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory, _log.Clock);
        var recordAbandoned = typeof(BsftFileLoggerProvider)
            .GetMethod("RecordAbandonedShutdownDrain", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var originalError = Console.Error;
        using var captured = new StringWriter();
        Console.SetError(captured);
        try
        {
            recordAbandoned.Invoke(provider, new object[] { 3 });
        }
        finally
        {
            Console.SetError(originalError);
        }

        provider.AbandonedShutdownDrains.Should().Be(1, "the abandoned drain must be counted on its own signal");
        provider.LastAbandonedQueueDepth.Should().Be(3, "the abandoned-drain signal must report the queue depth as an upper bound");
        provider.DroppedEntryCount.Should().Be(0, "an abandoned drain is not a confirmed loss and must not touch DroppedEntryCount");
        captured.ToString().Should().Contain("abandoned a shutdown drain", "the abandoned drain must be mirrored to Console.Error for Release visibility");
        captured.ToString().Should().Contain("may be unflushed", "the message must be honest that the entries are an upper bound, not confirmed drops");

        provider.Dispose();
    }

    // Installs a writer that fails on every write while the flush thread is locked out, then drains
    // on the test thread so the failing write is guaranteed to happen inside DrainSafely.
    private void DrainSafely_WithBrokenWriter_SwallowsTheFailure(StreamWriter brokenWriter)
    {
        var options = LogFileFixture.Options();
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory, _log.Clock);
        var writeLock = GetWriteLock(provider);
        var today = _log.Clock.GetLocalNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        Monitor.Enter(writeLock);
        try
        {
            SetField(provider, "_currentFileName", Path.Combine(_log.LogDirectory, $"app-{today}.log"));
            SetField(
                provider,
                "_currentFileDate",
                DateOnly.ParseExact(today, "yyyy-MM-dd", CultureInfo.InvariantCulture));
            SetField(provider, "_writer", brokenWriter);
            GetQueue(provider).Enqueue("a line lost to the broken writer");

            var act = () => InvokeDrainSafely(provider);

            act.Should().NotThrow("a failing writer must never take the flush thread down");
            GetQueue(provider).Should().BeEmpty("the entry is dequeued and dropped, not retried forever");
        }
        finally
        {
            SetField(provider, "_writer", null);
            Monitor.Exit(writeLock);
        }

        provider.Dispose();
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        var act = () => new BsftFileLoggerProvider(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_OptionsNamingNoDirectory_ThrowsNamingTheMember(string directory)
    {
        // Left to Path.GetFullPath this answers a host that named no directory with an exception
        // about an argument the host never passed, which says nothing about what to change.
        var options = LogFileFixture.Options();
        options.LogDirectory = directory;

        var act = () => new BsftFileLoggerProvider(options);

        act.Should().Throw<InvalidOperationException>().WithMessage("*LogDirectory*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1025)]
    public void Constructor_UnusableFileSize_ThrowsNamingTheMember(int megabytes)
    {
        // The provider is the second entry into the sink, reached by AddBsftFileLogger without a
        // registration call having validated anything. A size of zero rolls on every single line.
        var options = LogFileFixture.Options();
        options.LogDirectory = _log.LogDirectory;
        options.MaxFileSizeMb = megabytes;

        var act = () => new BsftFileLoggerProvider(options);

        act.Should().Throw<InvalidOperationException>().WithMessage("*MaxFileSizeMb*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3651)]
    public void Constructor_UnusableRetention_ThrowsNamingTheMember(int days)
    {
        // A retention past the limit reaches the cleanup's date arithmetic and throws an
        // ArgumentOutOfRangeException out of the constructor, which names nothing a host can act on.
        var options = LogFileFixture.Options();
        options.LogDirectory = _log.LogDirectory;
        options.RetainDays = days;

        var act = () => new BsftFileLoggerProvider(options);

        act.Should().Throw<InvalidOperationException>().WithMessage("*RetainDays*");
    }

    [Fact]
    public void Constructor_FileLoggingOff_StillChecksTheFileRules()
    {
        // Constructing the provider is itself the decision to write to a file, so the flag the
        // registration call reads says nothing about whether these values have to be usable.
        var options = LogFileFixture.Options();
        options.EnableFileLogging = false;
        options.LogDirectory = string.Empty;

        var act = () => new BsftFileLoggerProvider(options);

        act.Should().Throw<InvalidOperationException>().WithMessage("*LogDirectory*");
    }

    [Fact]
    public void DayRollover_PrunesWhatThePreviousDayLeftPastTheRetentionWindow()
    {
        // Without this the retention window only ever applies at start-up, and a service that runs
        // for months keeps every file it has ever written.
        var options = LogFileFixture.Options();
        options.RetainDays = 30;
        Directory.CreateDirectory(_log.LogDirectory);

        using var provider = new BsftFileLoggerProvider(options, _log.LogDirectory, _log.Clock);
        var logger = provider.CreateLogger("Retention");

        logger.LogInformation("the first day");
        provider.Flush();

        // Placed after the start-up cleanup ran, so only the rollover can still remove it.
        var expiredDate = _log.Clock.GetLocalNow().AddDays(-60).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var expired = Path.Combine(_log.LogDirectory, $"app-{expiredDate}.log");
        File.WriteAllText(expired, "a line from two months ago");

        _log.Clock.MoveTo(LogFileFixture.FixedNow.AddDays(1));
        logger.LogInformation("the day after");
        provider.Flush();

        File.Exists(expired).Should().BeFalse("the day rollover is where a running process purges");
        Directory.GetFiles(_log.LogDirectory, "app-*.log").Should().HaveCount(2, "both days are still within the window");
    }

    private static void InvokeDrainSafely(BsftFileLoggerProvider provider)
    {
        var drainSafely = typeof(BsftFileLoggerProvider)
            .GetMethod("DrainSafely", BindingFlags.NonPublic | BindingFlags.Instance)!;

        try
        {
            drainSafely.Invoke(provider, null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static object GetWriteLock(BsftFileLoggerProvider provider)
        => GetField<object>(provider, "_writeLock");

    private static ConcurrentQueue<string> GetQueue(BsftFileLoggerProvider provider)
        => GetField<ConcurrentQueue<string>>(provider, "_queue");

    private static T GetField<T>(BsftFileLoggerProvider provider, string name)
        => (T)typeof(BsftFileLoggerProvider)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(provider)!;

    private static void SetField(BsftFileLoggerProvider provider, string name, object? value)
        => typeof(BsftFileLoggerProvider)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(provider, value);
}
