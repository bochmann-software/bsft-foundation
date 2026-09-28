namespace Bsft.Logging.LogFiles;

/// <summary>Provides the file-system operations used while discovering and reading rolling log files.</summary>
/// <remarks>
/// The default implementation delegates directly to <see cref="Directory"/> and <see cref="FileStream"/>.
/// The seam lets a host substitute another local-file implementation and lets tests reproduce a file
/// or directory disappearing between discovery and opening without relying on an operating-system race.
/// </remarks>
public interface ILogFileAccess
{
    /// <summary>Returns whether the log directory currently exists.</summary>
    /// <param name="path">The directory path to test.</param>
    bool IsDirectoryPresent(string path);

    /// <summary>Enumerates files matching <paramref name="searchPattern"/> in <paramref name="path"/>.</summary>
    /// <param name="path">The directory to enumerate.</param>
    /// <param name="searchPattern">The file-name pattern to match.</param>
    IEnumerable<string> EnumerateFiles(string path, string searchPattern);

    /// <summary>Opens one discovered file for asynchronous, shared reading.</summary>
    /// <param name="path">The discovered file path.</param>
    Stream OpenRead(string path);
}
