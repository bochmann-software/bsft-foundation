namespace Bsft.Hosting.Logging;

/// <summary>Options of the reporter that logs the observer failures an <c>IObserverGuard</c> contained.</summary>
/// <remarks>
/// An observer sees the data of the operation it watches, so the message of its exception may hold
/// some of it: a payload, a path, a credential. Logging the exception is therefore opt-in.
/// </remarks>
public sealed class ObserverFailureLoggingOptions
{
    /// <summary>
    /// Gets or sets whether the exception itself, with its message and stack trace, is logged.
    /// Defaults to <see langword="false"/>: the log line then names the observer and the exception
    /// type only.
    /// </summary>
    public bool IncludeExceptionDetails { get; set; }
}
