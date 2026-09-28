namespace Bsft.Scheduling;

/// <summary>
/// Thrown when a cron expression cannot be parsed.
/// </summary>
public sealed class CronFormatException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="CronFormatException"/> class.</summary>
    public CronFormatException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CronFormatException"/> class.</summary>
    /// <param name="message">The message describing what could not be parsed.</param>
    public CronFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CronFormatException"/> class.</summary>
    /// <param name="message">The message describing what could not be parsed.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public CronFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
