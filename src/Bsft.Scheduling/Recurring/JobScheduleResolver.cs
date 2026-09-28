using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Scheduling.Recurring;

/// <summary>
/// Default <see cref="IJobScheduleResolver"/>.
/// </summary>
internal sealed class JobScheduleResolver : IJobScheduleResolver
{
    private readonly IJobRegistry _registry;
    private readonly IServiceScopeFactory _scopeFactory;

    // A job's schedule is a constant of its type, so resolving an instance to read it once is enough.
    private readonly ConcurrentDictionary<string, JobSchedule> _schedules = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="JobScheduleResolver"/> class.
    /// </summary>
    /// <param name="registry">The registry the job name is resolved against.</param>
    /// <param name="scopeFactory">Creates the scope a job is resolved from to read its own schedule.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public JobScheduleResolver(IJobRegistry registry, IServiceScopeFactory scopeFactory)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    /// <inheritdoc />
    public JobSchedule Resolve(string jobName)
    {
        if (jobName is null)
            throw new ArgumentNullException(nameof(jobName));

        var definition = _registry.Find(jobName) ?? throw JobNotRegisteredException.ForName(jobName);

        if (definition.Schedule is { } declared)
            return declared;

        return _schedules.GetOrAdd(jobName, _ =>
        {
            using var scope = _scopeFactory.CreateScope();
            var job = (IRecurringJob)scope.ServiceProvider.GetRequiredService(definition.JobType);

            return job.Schedule;
        });
    }
}
