namespace Bsft.Hosting.BackgroundWork;

/// <summary>
/// What an operator decides about one run-once startup operation: how long it waits before its first
/// attempt, how the delay between attempts grows, how long that delay may become and how many
/// attempts it is allowed at all.
/// </summary>
/// <remarks>
/// These options are registered as <em>named</em> options, keyed by the hosted service type's full
/// name, so a host that runs several startup operations configures each one independently. The
/// configuration overload of <c>AddBsftBackgroundWorkRunOnce</c> binds
/// <c>Bsft:Hosting:RunOnce:&lt;service type full name&gt;</c> for that reason.
/// </remarks>
public sealed class BsftRunOnceOptions
{
    /// <summary>The configuration section the per-service sections sit under.</summary>
    public const string SectionName = "Bsft:Hosting:RunOnce";

    /// <summary>
    /// The name the options of <typeparamref name="TService"/> are registered under, which is also
    /// its per-service configuration section under <see cref="SectionName"/>.
    /// </summary>
    /// <typeparam name="TService">The run-once background service.</typeparam>
    /// <returns>The service type's full name, or its short name where the runtime gives no full one.</returns>
    public static string NameFor<TService>()
        where TService : RunOnceWithRetryBackgroundServiceBase
        => NameFor(typeof(TService));

    /// <summary>
    /// The name the options of a run-once background service are registered under.
    /// </summary>
    /// <param name="serviceType">The run-once background service type.</param>
    /// <returns>The service type's full name, or its short name where the runtime gives no full one.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="serviceType"/> is null.</exception>
    /// <remarks>
    /// The base class needs the untyped overload: it reads its own options from its most-derived
    /// runtime type, which it only has as a <see cref="Type"/>. Deriving the key by hand in either
    /// place would be a string that silently stops matching when the type moves.
    /// </remarks>
    public static string NameFor(Type serviceType)
    {
        if (serviceType is null)
            throw new ArgumentNullException(nameof(serviceType));

        return serviceType.FullName ?? serviceType.Name;
    }

    /// <summary>
    /// Gets or sets how long the service waits before its first attempt. Defaults to zero, which
    /// attempts the operation as soon as the host starts it.
    /// </summary>
    /// <remarks>
    /// A startup operation that depends on something else the same host is bringing up — a migration
    /// against a database whose connection pool is still warming, a registration against a service
    /// discovered at startup — costs one failed attempt and one backoff for nothing. Where the wait is
    /// known, stating it here is cheaper than paying it as an error in the log of every deployment.
    /// </remarks>
    public TimeSpan StartupDelay { get; set; } = TimeSpan.Zero;

    /// <summary>Gets or sets how long the service waits after its first failed attempt. Defaults to five seconds.</summary>
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the longest the service waits between two attempts, however far the backoff has
    /// grown. Defaults to five minutes.
    /// </summary>
    /// <remarks>
    /// The cap is what keeps an operation whose dependency is down for hours from backing off into a
    /// delay measured in days, at which point the host would stay broken long after the dependency
    /// came back.
    /// </remarks>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets what the delay is multiplied by after each failed attempt. Defaults to two;
    /// one retries on a fixed delay.
    /// </summary>
    public double BackoffFactor { get; set; } = 2;

    /// <summary>
    /// Gets or sets how many attempts the operation gets. Defaults to zero, which means it is
    /// attempted until it succeeds or the host stops.
    /// </summary>
    /// <remarks>
    /// Unbounded by default because the operation this base class exists for is one the host needs
    /// done: giving up leaves the process running in a state nobody asked for. A bound is for the
    /// operation whose failure is a decision — a one-off backfill an operator would rather see fail
    /// loudly than retry all night.
    /// </remarks>
    public int MaxAttempts { get; set; }
}
