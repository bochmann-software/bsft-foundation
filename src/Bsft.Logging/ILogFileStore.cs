namespace Bsft.Logging;

/// <summary>The file-system operations log file cleanup needs, so a failing file system can be substituted.</summary>
internal interface ILogFileStore
{
    /// <summary>Lists the files in the log directory that match a pattern.</summary>
    /// <param name="pattern">The file name pattern.</param>
    /// <returns>The full paths.</returns>
    IEnumerable<string> List(string pattern);

    /// <summary>Deletes one file.</summary>
    /// <param name="path">The file to delete.</param>
    void Delete(string path);

    /// <summary>Reads the size of one file.</summary>
    /// <param name="path">The file to measure.</param>
    /// <returns>The size in bytes.</returns>
    long LengthOf(string path);
}
