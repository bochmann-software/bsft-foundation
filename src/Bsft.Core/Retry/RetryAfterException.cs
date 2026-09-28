namespace Bsft.Core.Retry;

/// <summary>
/// The ready-made <see cref="IRetryAfter"/>: thrown by code that wants to fail the current attempt and
/// name when the next one should happen, and that has no exception type of its own to carry it.
/// </summary>
/// <remarks>
/// <para>
/// It derives from <see cref="Exception"/> rather than from <c>BsftException</c>, and that is the whole
/// reason it sits here rather than in <c>Bsft.Core/Exceptions</c>. Everything below <c>BsftException</c>
/// is a failure of a request, recognised by the exception middleware and turned into a status code and
/// an error code for a client. This one has no client: it is raised inside a background pipeline, and
/// its audience is the component that owns the retry. Giving it an error code would advertise a
/// response it never produces.
/// </para>
/// <para>
/// Code that already has a suitable exception type implements <see cref="IRetryAfter"/> on that type
/// instead — the interface exists precisely so that a channel's own <c>ThrottledException</c> does not
/// have to be replaced to gain a wait.
/// </para>
/// </remarks>
public sealed class RetryAfterException : Exception, IRetryAfter
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RetryAfterException"/> class without naming a wait.
    /// </summary>
    public RetryAfterException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RetryAfterException"/> class without naming a wait.
    /// </summary>
    /// <param name="message">What failed, and why it is worth attempting again.</param>
    public RetryAfterException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RetryAfterException"/> class without naming a wait.
    /// </summary>
    /// <param name="message">What failed, and why it is worth attempting again.</param>
    /// <param name="innerException">The failure underneath.</param>
    public RetryAfterException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RetryAfterException"/> class asking for a wait.
    /// </summary>
    /// <param name="message">What failed, and why it is worth attempting again.</param>
    /// <param name="retryAfter">
    /// How long to wait before the next attempt. It is a request: what honours it is free to clamp it.
    /// </param>
    /// <param name="innerException">The failure underneath, if any.</param>
    public RetryAfterException(string message, TimeSpan retryAfter, Exception? innerException = null)
        : base(message, innerException) => RetryAfter = retryAfter;

    /// <inheritdoc />
    public TimeSpan? RetryAfter { get; }
}
