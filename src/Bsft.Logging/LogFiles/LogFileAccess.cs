namespace Bsft.Logging.LogFiles;

/// <summary>The production file-system access used by <see cref="LogFileReader"/>.</summary>
internal sealed class LogFileAccess : ILogFileAccess
{
    /// <inheritdoc />
    public bool IsDirectoryPresent(string path) => Directory.Exists(path);

    /// <inheritdoc />
    public IEnumerable<string> EnumerateFiles(string path, string searchPattern)
        => Directory.EnumerateFiles(path, searchPattern);

    /// <inheritdoc />
    public Stream OpenRead(string path)
        => new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            LogFileBufferDefaults.SizeBytes,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
}
