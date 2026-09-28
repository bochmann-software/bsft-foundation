using Microsoft.Extensions.Options;

namespace Bsft.Scheduling.Tests.Support;

/// <summary>
/// An <see cref="IOptionsMonitor{TOptions}"/> over per-job options held in a dictionary, so a test
/// states only the jobs whose switches it cares about and every other job gets the defaults.
/// </summary>
internal sealed class JobOptionsMonitor : IOptionsMonitor<BsftJobOptions>
{
    private readonly Dictionary<string, BsftJobOptions> _byName = new(StringComparer.Ordinal);

    /// <summary>A monitor in which every job has its default options.</summary>
    public static JobOptionsMonitor Defaults => new();

    /// <inheritdoc />
    public BsftJobOptions CurrentValue => Get(Options.DefaultName);

    /// <summary>Gives one job options of its own.</summary>
    /// <param name="jobName">The registered job name.</param>
    /// <param name="configure">What to change about that job's options.</param>
    /// <returns>The monitor, for chaining.</returns>
    public JobOptionsMonitor For(string jobName, Action<BsftJobOptions> configure)
    {
        var options = new BsftJobOptions();
        configure(options);
        _byName[jobName] = options;

        return this;
    }

    /// <inheritdoc />
    public BsftJobOptions Get(string? name) =>
        name is not null && _byName.TryGetValue(name, out var options) ? options : new BsftJobOptions();

    /// <inheritdoc />
    public IDisposable? OnChange(Action<BsftJobOptions, string?> listener) => null;
}
