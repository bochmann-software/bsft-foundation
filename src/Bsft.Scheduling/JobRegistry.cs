using Microsoft.Extensions.Options;

namespace Bsft.Scheduling;

/// <summary>
/// Default <see cref="IJobRegistry"/> built from the job definitions collected by the service
/// collection extensions.
/// </summary>
public sealed class JobRegistry : IJobRegistry
{
    private readonly Dictionary<string, JobDefinition> _byName;
    private readonly IOptionsMonitor<BsftJobOptions> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobRegistry"/> class.
    /// </summary>
    /// <param name="definitions">The registered jobs.</param>
    /// <param name="options">Supplies each job's own switches, read whenever they are asked for.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when two jobs share a name.</exception>
    public JobRegistry(IEnumerable<JobDefinition> definitions, IOptionsMonitor<BsftJobOptions> options)
    {
        if (definitions is null)
            throw new ArgumentNullException(nameof(definitions));

        _options = options ?? throw new ArgumentNullException(nameof(options));

        Definitions = definitions.ToList();
        _byName = new Dictionary<string, JobDefinition>(Definitions.Count, StringComparer.Ordinal);

        foreach (var definition in Definitions)
        {
            // Two jobs under one name would silently share a pending-run slot and execute each
            // other's occurrences, so this has to fail at startup rather than at three in the morning.
            if (!_byName.TryAdd(definition.Name, definition))
            {
                throw new InvalidOperationException(
                    $"Two jobs are registered under the name '{definition.Name}'. Job names must be unique; pass an explicit name when registering.");
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<JobDefinition> Definitions { get; }

    /// <inheritdoc />
    public JobDefinition? Find(string name)
    {
        if (name is null)
            throw new ArgumentNullException(nameof(name));

        return _byName.GetValueOrDefault(name);
    }

    /// <inheritdoc />
    public bool IsEnabled(string name)
    {
        if (name is null)
            throw new ArgumentNullException(nameof(name));

        // An unregistered name is not enabled rather than enabled-by-default: the only caller that
        // reaches here with one is settling a run whose job was renamed or removed, and answering
        // "yes" would send it on to be executed by nothing.
        return _byName.ContainsKey(name) && _options.Get(name).Enabled;
    }

    /// <inheritdoc />
    public JobDefinition? FindByType(Type jobType)
    {
        if (jobType is null)
            throw new ArgumentNullException(nameof(jobType));

        // A type registered twice under different names is ambiguous here, and the first match is as
        // good an answer as any: the caller that wants the second one has to name it.
        return Definitions.FirstOrDefault(d => d.JobType == jobType);
    }
}
