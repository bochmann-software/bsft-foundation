namespace Bsft.Logging.Tests.Fakes;

/// <summary>
/// A stream that fails every write and flush with a configured exception, so the provider's
/// drain-failure handling can be exercised without depending on the host file system.
/// </summary>
internal sealed class ThrowingStream : Stream
{
    private readonly Exception _exception;

    /// <summary>Initializes a new instance of the <see cref="ThrowingStream"/> class.</summary>
    /// <param name="exception">The failure every write and flush raises.</param>
    public ThrowingStream(Exception exception) => _exception = exception;

    /// <inheritdoc />
    public override bool CanRead => false;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => true;

    /// <inheritdoc />
    public override long Length => 0;

    /// <inheritdoc />
    public override long Position
    {
        get => 0;
        set { }
    }

    /// <inheritdoc />
    public override void Flush() => throw _exception;

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw _exception;
}
