using Bsft.Logging.Tests.Fakes;

namespace Bsft.Logging.Tests.Support;

/// <summary>
/// A log directory of a test's own and a clock it controls, taken down again when the test ends.
/// </summary>
/// <remarks>
/// The clock is fixed and shared by the system under test and the assertions, so file-name dates are
/// deterministic: reading the real clock twice can cross midnight between the write and the check.
/// </remarks>
internal sealed class LogFileFixture : IDisposable
{
    /// <summary>The instant every test starts at.</summary>
    public static DateTimeOffset FixedNow { get; } = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The clock the provider reads its dates from.</summary>
    public MovableTimeProvider Clock { get; } = new(FixedNow);

    /// <summary>The directory the provider writes to, which no other test shares.</summary>
    public string LogDirectory { get; } =
        Path.Combine(Path.GetTempPath(), "bsft-logging-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>Builds the options a test starts from, which it is free to adjust.</summary>
    /// <returns>The options.</returns>
    public static BsftLoggingOptions Options() => new()
    {
        EnableFileLogging = true,
        MaxFileSizeMb = 10,
        RetainDays = 30
    };

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(LogDirectory))
            Directory.Delete(LogDirectory, recursive: true);
    }
}
