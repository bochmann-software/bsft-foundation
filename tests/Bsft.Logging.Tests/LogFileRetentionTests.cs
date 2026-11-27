using Bsft.Logging.Tests.Support;
using System.Runtime.Versioning;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace Bsft.Logging.Tests;

public sealed class LogFileRetentionTests : IDisposable
{
    private const int BytesPerMegabyte = 1024 * 1024;

    private readonly LogFileFixture _log = new();
    private readonly List<string> _reports = [];
    private readonly LogFileNaming _naming = new();

    public LogFileRetentionTests() => Directory.CreateDirectory(_log.LogDirectory);

    /// <inheritdoc />
    public void Dispose() => _log.Dispose();

    [Fact]
    public void Apply_CountLimit_DeletesOldestFirstByDateAndRollIndex()
    {
        var oldest = Create(daysAgo: 3, roll: 0);
        var rolled1 = Create(daysAgo: 2, roll: 1);
        var rolled2 = Create(daysAgo: 2, roll: 2);
        var newest = Create(daysAgo: 1, roll: 0);

        Retention(new BsftLoggingOptions { MaxRetainedFiles = 2 }).Apply(newest);

        Existing().Should().BeEquivalentTo([rolled2, newest]);
        File.Exists(oldest).Should().BeFalse();
        File.Exists(rolled1).Should().BeFalse();
    }

    [Fact]
    public void Apply_CountLimit_OrdersByNameNotByLastWriteTime()
    {
        var older = Create(daysAgo: 5, roll: 0);
        var newer = Create(daysAgo: 1, roll: 0);
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow);
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow.AddYears(-1));

        Retention(new BsftLoggingOptions { MaxRetainedFiles = 1 }).Apply(activePath: null);

        Existing().Should().BeEquivalentTo([newer]);
    }

    [Fact]
    public void Apply_TotalSizeLimit_DeletesOldestUntilTheTotalFits()
    {
        var oldest = Create(daysAgo: 3, roll: 0, bytes: BytesPerMegabyte);
        var middle = Create(daysAgo: 2, roll: 0, bytes: BytesPerMegabyte);
        var newest = Create(daysAgo: 1, roll: 0, bytes: BytesPerMegabyte);

        Retention(new BsftLoggingOptions { MaxFileSizeMb = 1, MaxTotalSizeMb = 2 }).Apply(newest);

        Existing().Should().BeEquivalentTo([middle, newest]);
        File.Exists(oldest).Should().BeFalse();
    }

    [Fact]
    public void Apply_RetainDaysAndCountLimit_BothApply()
    {
        var expired = Create(daysAgo: 40, roll: 0);
        var a = Create(daysAgo: 3, roll: 0);
        var b = Create(daysAgo: 2, roll: 0);
        var c = Create(daysAgo: 1, roll: 0);

        Retention(new BsftLoggingOptions { RetainDays = 30, MaxRetainedFiles = 2 }).Apply(c);

        Existing().Should().BeEquivalentTo([b, c]);
        File.Exists(expired).Should().BeFalse();
        File.Exists(a).Should().BeFalse();
    }

    [Fact]
    public void Apply_RetainDaysOnly_KeepsFilesWithinTheWindowWhateverTheirNumber()
    {
        var expired = Create(daysAgo: 40, roll: 0);
        var kept = new[] { Create(daysAgo: 3, roll: 0), Create(daysAgo: 2, roll: 0), Create(daysAgo: 1, roll: 0) };

        Retention(new BsftLoggingOptions { RetainDays = 30 }).Apply(activePath: null);

        Existing().Should().BeEquivalentTo(kept);
        File.Exists(expired).Should().BeFalse();
    }

    [Fact]
    public void Apply_ActiveFile_IsKeptEvenWhenItIsTheOldest()
    {
        var active = Create(daysAgo: 40, roll: 0, bytes: BytesPerMegabyte);
        var other = Create(daysAgo: 1, roll: 0, bytes: BytesPerMegabyte);

        Retention(new BsftLoggingOptions
        {
            RetainDays = 30,
            MaxRetainedFiles = 1,
            MaxFileSizeMb = 1,
            MaxTotalSizeMb = 1,
        }).Apply(active);

        Existing().Should().BeEquivalentTo([active]);
        File.Exists(other).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void Apply_LimitsNullOrZero_DeleteNothingBeyondTheAge(int? limit)
    {
        var files = new[] { Create(daysAgo: 3, roll: 0), Create(daysAgo: 2, roll: 0), Create(daysAgo: 1, roll: 0) };

        Retention(new BsftLoggingOptions { MaxRetainedFiles = limit, MaxTotalSizeMb = limit }).Apply(files[2]);

        Existing().Should().BeEquivalentTo(files);
    }

    [Fact]
    public void Apply_FilesTheLoggerDidNotWrite_AreNeverDeleted()
    {
        var foreign = Path.Combine(_log.LogDirectory, "app-notes.log");
        File.WriteAllText(foreign, "x");
        var a = Create(daysAgo: 2, roll: 0);
        var b = Create(daysAgo: 1, roll: 0);

        Retention(new BsftLoggingOptions { MaxRetainedFiles = 1 }).Apply(b);

        Existing().Should().BeEquivalentTo([foreign, b]);
        File.Exists(a).Should().BeFalse();
    }

    // Independent of file permissions, so it also runs where the process bypasses them (root on CI),
    // which skips the permission-based tests below.
    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void Apply_ListingFails_ReportsInsteadOfThrowing(Type failure)
    {
        var kept = Create(daysAgo: 1, roll: 0);
        var retention = Retention(
            new BsftLoggingOptions { MaxRetainedFiles = 1 },
            list: _ => throw (Exception)Activator.CreateInstance(failure, "listing denied")!);

        var act = () => retention.Apply(kept);

        act.Should().NotThrow();
        _reports.Should().ContainSingle().Which.Should().Contain("cleanup failed").And.Contain("listing denied");
        Existing().Should().BeEquivalentTo([kept]);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void Apply_DeleteFails_ReportsAndTriesTheNextFile(Type failure)
    {
        var locked = Create(daysAgo: 3, roll: 0);
        var next = Create(daysAgo: 2, roll: 0);
        var newest = Create(daysAgo: 1, roll: 0);
        var retention = Retention(
            new BsftLoggingOptions { MaxRetainedFiles = 2 },
            delete: path =>
            {
                if (path == locked)
                    throw (Exception)Activator.CreateInstance(failure, "locked")!;
                File.Delete(path);
            });

        var act = () => retention.Apply(newest);

        act.Should().NotThrow();
        Existing().Should().BeEquivalentTo([locked, newest]);
        _reports.Should().ContainSingle().Which.Should().Contain("could not be deleted").And.Contain("locked");
    }

    [Theory]
    [InlineData(typeof(FileNotFoundException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void Apply_FileVanishesBeforeItIsMeasured_CountsItAsEmptyAndKeepsTheRest(Type failure)
    {
        var gone = Create(daysAgo: 3, roll: 0, bytes: BytesPerMegabyte);
        var newest = Create(daysAgo: 1, roll: 0, bytes: BytesPerMegabyte);
        var retention = Retention(
            new BsftLoggingOptions { MaxFileSizeMb = 1, MaxTotalSizeMb = 1 },
            length: path => path == gone ? throw (Exception)Activator.CreateInstance(failure, "gone")! : new FileInfo(path).Length);

        var act = () => retention.Apply(newest);

        act.Should().NotThrow();
        _reports.Should().ContainSingle().Which.Should().Contain("could not be measured");
        Existing().Should().BeEquivalentTo([gone, newest]);
    }

    [Fact]
    public void Apply_DirectoryMissing_ReportsInsteadOfThrowing()
    {
        var retention = new LogFileRetention(
            new BsftLoggingOptions(),
            _naming,
            _log.Clock,
            new PhysicalLogFileStore(Path.Combine(_log.LogDirectory, "missing")),
            _reports.Add);

        var act = () => retention.Apply(activePath: null);

        act.Should().NotThrow();
        _reports.Should().ContainSingle().Which.Should().Contain("cleanup failed");
    }

    [EnforcedPermissionsFact]
    [UnsupportedOSPlatform("windows")]
    public void Apply_DirectoryNotReadable_ReportsInsteadOfThrowing()
    {
        var directoryMode = File.GetUnixFileMode(_log.LogDirectory);
        File.SetUnixFileMode(_log.LogDirectory, UnixFileMode.None);

        try
        {
            var act = () => Retention(new BsftLoggingOptions()).Apply(activePath: null);

            act.Should().NotThrow();
            _reports.Should().ContainSingle().Which.Should().Contain("cleanup failed");
        }
        finally
        {
            File.SetUnixFileMode(_log.LogDirectory, directoryMode);
        }
    }

    [Fact]
    public void Provider_StartUp_AppliesTheCountLimitAndKeepsTodaysFile()
    {
        var old1 = Create(daysAgo: 3, roll: 0);
        var old2 = Create(daysAgo: 2, roll: 0);
        var today = Create(daysAgo: 0, roll: 0);
        var options = LogFileFixture.Options();
        options.MaxRetainedFiles = 2;

        using var provider = new BsftFileLoggerProvider(options, _log.LogDirectory, _log.Clock);

        Existing().Should().BeEquivalentTo([old2, today]);
        File.Exists(old1).Should().BeFalse();
    }

    [Fact]
    public void Provider_RollOver_AppliesTheCountLimit()
    {
        var options = LogFileFixture.Options();
        options.MaxFileSizeMb = 1;
        options.MaxRetainedFiles = 2;
        using var provider = new BsftFileLoggerProvider(options, _log.LogDirectory, _log.Clock);
        var logger = provider.CreateLogger("Test");
        var line = new string('x', BytesPerMegabyte);

        for (var i = 0; i < 4; i++)
        {
            logger.LogInformation("{Line}", line);
            provider.Flush();
        }

        var names = Existing().Select(Path.GetFileName).Order().ToArray();
        names.Should().HaveCount(2);
        names[^1].Should().EndWith("_004.log");
    }

    [Fact]
    public void Provider_DayRollOver_AppliesTheCountLimit()
    {
        var options = LogFileFixture.Options();
        options.MaxRetainedFiles = 2;
        using var provider = new BsftFileLoggerProvider(options, _log.LogDirectory, _log.Clock);
        var logger = provider.CreateLogger("Test");

        for (var i = 0; i < 4; i++)
        {
            logger.LogInformation("day {Day}", i);
            provider.Flush();
            _log.Clock.MoveTo(LogFileFixture.FixedNow.AddDays(i + 1));
        }

        var today = DateOnly.FromDateTime(LogFileFixture.FixedNow.Date);
        Existing().Select(Path.GetFileName).Order().Should().Equal(
            Path.GetFileName(_naming.PathFor(_log.LogDirectory, today.AddDays(2), 0)),
            Path.GetFileName(_naming.PathFor(_log.LogDirectory, today.AddDays(3), 0)));
    }

    [EnforcedPermissionsFact]
    [UnsupportedOSPlatform("windows")]
    public void Provider_StartUpWithAnUndeletableFile_StillStarts()
    {
        var old1 = Create(daysAgo: 3, roll: 0);
        var options = LogFileFixture.Options();
        options.RetainDays = 1;
        var directoryMode = File.GetUnixFileMode(_log.LogDirectory);
        File.SetUnixFileMode(_log.LogDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            var act = () => new BsftFileLoggerProvider(options, _log.LogDirectory, _log.Clock).Dispose();

            act.Should().NotThrow();
            File.Exists(old1).Should().BeTrue();
        }
        finally
        {
            File.SetUnixFileMode(_log.LogDirectory, directoryMode);
        }
    }

    private string Create(int daysAgo, int roll, int bytes = 10)
    {
        var date = DateOnly.FromDateTime(LogFileFixture.FixedNow.Date.AddDays(-daysAgo));
        var path = _naming.PathFor(_log.LogDirectory, date, roll);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    private string[] Existing() => Directory.GetFiles(_log.LogDirectory);

    private LogFileRetention Retention(
        BsftLoggingOptions options,
        Action<string>? delete = null,
        Func<string, long>? length = null,
        Func<string, IEnumerable<string>>? list = null)
        => new(options, _naming, _log.Clock, new FailingStore(_log.LogDirectory, delete, length, list), _reports.Add);

    private sealed class FailingStore(
        string directory,
        Action<string>? delete,
        Func<string, long>? length,
        Func<string, IEnumerable<string>>? list)
        : ILogFileStore
    {
        private readonly PhysicalLogFileStore _inner = new(directory);

        public IEnumerable<string> List(string pattern) => list?.Invoke(pattern) ?? _inner.List(pattern);

        public void Delete(string path)
        {
            if (delete is null)
                _inner.Delete(path);
            else
                delete(path);
        }

        public long LengthOf(string path) => length?.Invoke(path) ?? _inner.LengthOf(path);
    }
}
