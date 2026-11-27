namespace Bsft.Logging;

/// <summary>The log directory on the local file system.</summary>
/// <param name="directory">The log directory.</param>
internal sealed class PhysicalLogFileStore(string directory) : ILogFileStore
{
    /// <inheritdoc />
    public IEnumerable<string> List(string pattern) => Directory.EnumerateFiles(directory, pattern);

    /// <inheritdoc />
    public void Delete(string path) => File.Delete(path);

    /// <inheritdoc />
    public long LengthOf(string path) => new FileInfo(path).Length;
}
