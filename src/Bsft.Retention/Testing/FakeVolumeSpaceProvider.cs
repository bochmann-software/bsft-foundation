using System.Collections.Concurrent;
using Bsft.Retention.Watermarks;

namespace Bsft.Retention.Testing;

/// <summary>
/// An <see cref="IVolumeSpaceProvider"/> whose volume has whatever space a test sets, for driving a
/// disk watermark without filling a disk.
/// </summary>
/// <remarks>
/// Register it before <c>AddBsftRetentionDiskWatermark</c>, which only adds the real measurement when
/// none is registered, or replace the registration afterwards. Space does not follow deletions on its
/// own: a test whose source deletes files sets <see cref="AvailableBytes"/> to what the deletion
/// would have freed.
/// </remarks>
public sealed class FakeVolumeSpaceProvider : IVolumeSpaceProvider
{
    private readonly ConcurrentQueue<string> _measuredPaths = new();

    /// <summary>Gets or sets the bytes every measurement reports as available.</summary>
    public long AvailableBytes { get; set; }

    /// <summary>Gets the paths measured so far, in the order they were measured.</summary>
    public IReadOnlyCollection<string> MeasuredPaths => _measuredPaths.ToArray();

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is null.</exception>
    public long GetAvailableBytes(string path)
    {
        if (path is null)
            throw new ArgumentNullException(nameof(path));

        _measuredPaths.Enqueue(path);

        return AvailableBytes;
    }
}
