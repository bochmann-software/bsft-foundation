namespace Bsft.Logging.Tests.Support;

/// <summary>
/// A fact that needs file permissions the process cannot bypass, and reports as skipped, not passed, when it can.
/// </summary>
/// <remarks>
/// xunit v2 has no runtime skip, so the attribute probes when it is discovered: it takes the read bit off a
/// scratch directory and sets <see cref="FactAttribute.Skip"/> when listing it still works, which is what
/// root, uid 0 without a name or CAP_DAC_OVERRIDE gets, and what Windows gives for the Unix file mode.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class EnforcedPermissionsFactAttribute : FactAttribute
{
    /// <summary>Initializes a new instance of the <see cref="EnforcedPermissionsFactAttribute"/> class.</summary>
    public EnforcedPermissionsFactAttribute()
    {
        if (!PermissionsAreEnforced())
            Skip = "The process can read a directory whose permissions deny it.";
    }

    private static bool PermissionsAreEnforced()
    {
        if (OperatingSystem.IsWindows())
            return false;

        var probe = Path.Combine(Path.GetTempPath(), "bsft-permission-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(probe);
            File.SetUnixFileMode(probe, UnixFileMode.None);
            return !CanList(probe);
        }
        catch (Exception exception) when (IsFileSystemRefusal(exception))
        {
            // A file system that cannot take the probe (no chmod, no temp directory) cannot run the tests either:
            // they skip with a reason instead of failing discovery.
            return false;
        }
        finally
        {
            Restore(probe);
        }
    }

    private static bool CanList(string directory)
    {
        try
        {
            _ = Directory.EnumerateFiles(directory).ToList();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    // Each step on its own: a mode that cannot be put back must not keep the empty directory from going.
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static void Restore(string probe)
    {
        try
        {
            File.SetUnixFileMode(probe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (Exception exception) when (IsFileSystemRefusal(exception))
        {
            // Left as it is; the delete below needs only the parent's write access.
        }

        try
        {
            Directory.Delete(probe);
        }
        catch (Exception exception) when (IsFileSystemRefusal(exception))
        {
            // A scratch directory left in the temp path is harmless.
        }
    }

    private static bool IsFileSystemRefusal(Exception exception)
        => exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException;
}
