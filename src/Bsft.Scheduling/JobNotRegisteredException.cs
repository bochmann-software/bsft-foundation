namespace Bsft.Scheduling;

/// <summary>
/// Thrown when a job is enqueued that the scheduler does not know, which would otherwise store a run
/// that no instance can ever execute.
/// </summary>
public sealed class JobNotRegisteredException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="JobNotRegisteredException"/> class.</summary>
    public JobNotRegisteredException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="JobNotRegisteredException"/> class.</summary>
    /// <param name="message">The message describing which job was missing.</param>
    public JobNotRegisteredException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="JobNotRegisteredException"/> class.</summary>
    /// <param name="message">The message describing which job was missing.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public JobNotRegisteredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates the exception for a job name that is not registered.
    /// </summary>
    /// <param name="jobName">The name that could not be resolved.</param>
    /// <returns>The exception to throw.</returns>
    public static JobNotRegisteredException ForName(string jobName) =>
        new($"No job is registered under the name '{jobName}'. Register it with AddBsftJob or AddBsftRecurringJob before enqueuing it.");

    /// <summary>
    /// Creates the exception for a job type that is not registered.
    /// </summary>
    /// <param name="jobType">The type that could not be resolved.</param>
    /// <returns>The exception to throw.</returns>
    public static JobNotRegisteredException ForType(Type jobType) =>
        new($"The job type '{jobType?.FullName}' is not registered. Register it with AddBsftJob or AddBsftRecurringJob before enqueuing it.");
}
