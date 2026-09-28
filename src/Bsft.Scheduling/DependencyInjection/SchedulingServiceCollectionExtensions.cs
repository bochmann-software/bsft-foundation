using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Bsft.Instrumentation.DependencyInjection;
using Bsft.Scheduling.Diagnostics;
using Bsft.Scheduling.Execution;
using Bsft.Scheduling.Heartbeats;
using Bsft.Scheduling.Recurring;
using Bsft.Scheduling.StoredSchedules;

namespace Bsft.Scheduling.DependencyInjection;

/// <summary>
/// Extension methods for registering the job scheduler with the dependency injection container.
/// </summary>
public static class SchedulingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the job scheduler: the in-memory store, the dispatcher, the recurring seeder and the
    /// supporting services. Call <see cref="AddBsftJob{TJob}"/> and
    /// <see cref="AddBsftRecurringJob{TJob}(IServiceCollection, JobSchedule, string?, JobOverlapPolicy, Action{BsftJobOptions}?)"/>
    /// afterwards to register the jobs themselves, and <c>AddBsftSchedulingEFCore</c> from
    /// <c>Bsft.Scheduling.EFCore</c> to persist the runs.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">An optional action to configure <see cref="BsftSchedulingOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <remarks>
    /// The options are validated by <c>ValidateOnStart()</c>, so a value that cannot be used fails the
    /// host rather than this call, and fails it with every complaint listed at once instead of a
    /// scheduler that quietly does nothing.
    /// </remarks>
    public static IServiceCollection AddBsftScheduling(
        this IServiceCollection services,
        Action<BsftSchedulingOptions>? configure = null)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        return AddCore(services, configure);
    }

    /// <summary>
    /// Registers the job scheduler with its options bound from configuration section
    /// <see cref="BsftSchedulingOptions.SectionName"/>.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">The configuration the section is read from.</param>
    /// <param name="configure">An optional action applied after the section has been bound.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.</exception>
    /// <remarks>
    /// The section is bound first and the delegate runs after it, so code wins over configuration for
    /// anything both of them set. Everything the other overload documents applies here as well.
    /// </remarks>
    public static IServiceCollection AddBsftScheduling(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BsftSchedulingOptions>? configure = null)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));
        if (configuration is null)
            throw new ArgumentNullException(nameof(configuration));

        // Binds Bsft:Scheduling:Jobs:<job name> for whatever jobs are registered, before or after this
        // call: the jobs come from AddBsftJob and AddBsftRecurringJob, and a module loaded later may
        // add one, so the binding is by name on demand rather than per registration.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<BsftJobOptions>>(
            new JobOptionsConfigurator(configuration)));

        return AddCore(services, options =>
        {
            configuration.GetSection(BsftSchedulingOptions.SectionName).Bind(options);
            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// Registers a job that only runs when it is enqueued explicitly through <see cref="IJobScheduler"/>.
    /// </summary>
    /// <typeparam name="TJob">The job implementation.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="name">The name runs are stored under. Defaults to the job type's full name.</param>
    /// <param name="configure">An optional action to configure this job's <see cref="BsftJobOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftJob<TJob>(
        this IServiceCollection services,
        string? name = null,
        Action<BsftJobOptions>? configure = null)
        where TJob : class, IJob =>
        services.AddJobRegistration<TJob>(name, schedule: null, JobOverlapPolicy.Forbid, configure);

    /// <summary>
    /// Registers a job that recurs on the schedule it declares through <see cref="IRecurringJob.Schedule"/>.
    /// </summary>
    /// <typeparam name="TJob">The recurring job implementation.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="name">The name runs are stored under. Defaults to the job type's full name.</param>
    /// <param name="overlapPolicy">Whether two executions may overlap. Defaults to <see cref="JobOverlapPolicy.Forbid"/>.</param>
    /// <param name="configure">An optional action to configure this job's <see cref="BsftJobOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftRecurringJob<TJob>(
        this IServiceCollection services,
        string? name = null,
        JobOverlapPolicy overlapPolicy = JobOverlapPolicy.Forbid,
        Action<BsftJobOptions>? configure = null)
        where TJob : class, IRecurringJob =>
        services.AddJobRegistration<TJob>(name, schedule: null, overlapPolicy, configure);

    /// <summary>
    /// Registers a job that recurs on the given schedule. Use this overload when the schedule comes
    /// from configuration rather than from the job type itself.
    /// </summary>
    /// <typeparam name="TJob">The job implementation.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="schedule">The recurrence rule.</param>
    /// <param name="name">The name runs are stored under. Defaults to the job type's full name.</param>
    /// <param name="overlapPolicy">Whether two executions may overlap. Defaults to <see cref="JobOverlapPolicy.Forbid"/>.</param>
    /// <param name="configure">An optional action to configure this job's <see cref="BsftJobOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="schedule"/> is null.</exception>
    public static IServiceCollection AddBsftRecurringJob<TJob>(
        this IServiceCollection services,
        JobSchedule schedule,
        string? name = null,
        JobOverlapPolicy overlapPolicy = JobOverlapPolicy.Forbid,
        Action<BsftJobOptions>? configure = null)
        where TJob : class, IJob
    {
        if (schedule is null)
            throw new ArgumentNullException(nameof(schedule));

        return services.AddJobRegistration<TJob>(name, schedule, overlapPolicy, configure);
    }

    /// <summary>
    /// Registers the job liveness health check, which reports a recurring job that has not completed a
    /// run for longer than its own cadence allows.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// Call it after <see cref="AddBsftScheduling(IServiceCollection, Action{BsftSchedulingOptions}?)"/>,
    /// whose registry, schedules and heartbeat store the check reads. It contributes the registration
    /// to <see cref="HealthCheckServiceOptions"/> directly rather than through <c>AddHealthChecks()</c>,
    /// so a host that never calls that gets the check anyway and one that does picks it up unchanged:
    /// both routes populate the same collection.
    /// </para>
    /// <para>
    /// Registering it twice would make the health check service refuse to construct — it rejects two
    /// registrations sharing a name — and take down every host that also calls <c>AddHealthChecks()</c>,
    /// so a second call is a no-op. The check carries <c>bsft.scheduling</c> as its only tag and reports
    /// at most <see cref="HealthStatus.Degraded"/>; adding the readiness tag to it is a decision
    /// <c>docs/scheduling.md</c> spells out the cost of.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddBsftSchedulingHealthCheck(this IServiceCollection services)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        if (services.Any(descriptor => descriptor.ServiceType == typeof(JobHealthCheckContribution)))
            return services;

        services.AddSingleton(new JobHealthCheckContribution());

        services.Configure<HealthCheckServiceOptions>(options => options.Registrations.Add(
            new HealthCheckRegistration(
                JobHeartbeatHealthCheck.Name,
                provider => new JobHeartbeatHealthCheck(
                    provider.GetRequiredService<IJobRegistry>(),
                    provider.GetRequiredService<IJobScheduleResolver>(),
                    provider.GetRequiredService<IJobHeartbeatStore>(),
                    provider.GetRequiredService<IOptionsMonitor<BsftJobOptions>>(),
                    provider.GetRequiredService<TimeProvider>()),
                failureStatus: null,
                tags: [JobHeartbeatHealthCheck.Tag])));

        return services;
    }

    /// <summary>
    /// Registers everything both entry-point overloads register.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">What shapes the options, whatever it was built from.</param>
    /// <returns>The service collection for chaining.</returns>
    private static IServiceCollection AddCore(IServiceCollection services, Action<BsftSchedulingOptions>? configure)
    {
        services.AddOptions<BsftSchedulingOptions>()
            .Configure(options => configure?.Invoke(options))
            .ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<BsftSchedulingOptions>, BsftSchedulingOptionsValidator>());

        // TryAdd rather than Add, so that AddBsftSchedulingEFCore may replace the store before or
        // after this call without the registration order deciding which one wins.
        services.TryAddSingleton<IJobStore, InMemoryJobStore>();

        // The dispatcher, the seeder and the runner all report failures, so logging has to be
        // available even in a bare container.
        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);

        // Telemetry emits a no-op until an observability backend attaches a listener or exporter; the
        // "Bsft.Scheduling" source is advertised for discovery via the vendor-neutral layer.
        services.AddBsftInstrumentationSource(JobTelemetry.SourceName);
        services.TryAddSingleton<IJobTelemetry, JobTelemetry>();

        // The default only knows the ambient id of an executing job. Bsft.Scheduling.Correlation
        // registers a richer accessor with a plain Add, so it wins regardless of the call order.
        services.TryAddScoped<IJobCorrelationAccessor, AmbientJobCorrelationAccessor>();

        services.TryAddSingleton<IJobRegistry, JobRegistry>();
        services.TryAddSingleton<IJobScheduleResolver, JobScheduleResolver>();

        // TryAdd rather than Add, so that AddBsftStoredSchedulesEFCore from Bsft.Scheduling.EFCore may
        // replace the catalog before or after this call without the registration order deciding which
        // one wins, matching how the job store itself is registered above.
        services.TryAddSingleton<IStoredScheduleStore, InMemoryStoredScheduleStore>();
        services.TryAddSingleton<IStoredScheduleService, StoredScheduleService>();

        // Per instance and in memory: what this process has seen since it started. Replaceable, which
        // is the seam a host uses to answer liveness from the run store instead.
        services.TryAddSingleton<IJobHeartbeatStore, InMemoryJobHeartbeatStore>();
        services.TryAddSingleton<IJobExecutor, JobExecutor>();
        services.TryAddSingleton<IRecurringJobPlanner, RecurringJobPlanner>();
        services.TryAddSingleton<IJobRunOutcomePolicy, JobRunOutcomePolicy>();
        services.TryAddSingleton<IJobRunner, JobRunner>();
        services.TryAddScoped<IJobScheduler, JobScheduler>();

        services.AddHostedService<RecurringJobSeeder>();
        services.AddHostedService<StoredScheduleSeeder>();
        services.AddHostedService<JobDispatcher>();

        return services;
    }

    private static IServiceCollection AddJobRegistration<TJob>(
        this IServiceCollection services,
        string? name,
        JobSchedule? schedule,
        JobOverlapPolicy overlapPolicy,
        Action<BsftJobOptions>? configure)
        where TJob : class, IJob
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        var jobType = typeof(TJob);
        var jobName = name ?? jobType.FullName ?? jobType.Name;

        // PostConfigure rather than Configure, so that this delegate runs after the configuration
        // binding whichever order the host called AddBsftScheduling and this method in. Code wins over
        // configuration for anything both of them set, and the registration order does not decide it.
        services.AddOptions<BsftJobOptions>(jobName)
            .PostConfigure(options => configure?.Invoke(options))
            .ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<BsftJobOptions>, BsftJobOptionsValidator>());

        // Scoped, so a job may take a DbContext or any other per-operation dependency; the executor
        // creates a fresh scope for every run.
        services.TryAddScoped<TJob>();

        services.AddSingleton(new JobDefinition(jobName, jobType, schedule, overlapPolicy));

        return services;
    }

    /// <summary>Marks that the liveness check has already been contributed to this container.</summary>
    private sealed class JobHealthCheckContribution;
}
