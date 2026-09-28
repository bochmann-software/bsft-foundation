using Microsoft.Extensions.Options;

namespace Bsft.Messaging.RabbitMQ;

/// <summary>
/// Runs the connection and topology rules of <see cref="BsftRabbitMQOptions"/> under
/// <c>ValidateOnStart()</c>.
/// </summary>
/// <remarks>
/// <para>
/// It validates by building the two types that read the options —
/// <see cref="RabbitMQConnectionSettings"/> and <see cref="RabbitMQTopology"/> — rather than by
/// repeating their rules. Each of them already refuses what it cannot work with, and a second copy of
/// those checks would be the kind that drifts: a rule tightened in one place and not the other leaves
/// a host that starts and then fails on its first publish, which is precisely what start-up validation
/// exists to prevent.
/// </para>
/// <para>
/// Both failures are reported together where both apply, so a host with a missing password and a
/// dead-letter name colliding with its queue is told about both at once instead of over two restarts.
/// </para>
/// </remarks>
internal sealed class BsftRabbitMQOptionsValidator : IValidateOptions<BsftRabbitMQOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, BsftRabbitMQOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        var failures = new List<string>();

        Collect(() => new RabbitMQConnectionSettings(options), failures);
        Collect(() => new RabbitMQTopology(options), failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Runs one of the two resolutions and records the complaint it raised, if any.
    /// </summary>
    /// <param name="resolve">The resolution to attempt.</param>
    /// <param name="failures">The list every complaint is added to.</param>
    /// <remarks>
    /// Only <see cref="ArgumentException"/> and its derived types are caught, which is the whole of
    /// what the two constructors document themselves as raising for an option they refuse. Anything
    /// else is a defect rather than a misconfiguration and is left to surface as itself.
    /// </remarks>
    private static void Collect(Action resolve, List<string> failures)
    {
        try
        {
            resolve();
        }
        catch (ArgumentException failure)
        {
            failures.Add(failure.Message);
        }
    }
}
