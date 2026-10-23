using Bsft.Hosting.BackgroundWork.Pipelines;
using Bsft.Instrumentation.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bsft.Hosting.BackgroundWork.DependencyInjection;

/// <summary>
/// Extension methods for registering a bounded, never-blocking pipeline: a buffer, a batching worker
/// and, optionally, an enrichment stage in front of it.
/// </summary>
/// <remarks>
/// A pipeline is keyed by its item type, so a host runs one pipeline per item type. Producers inject
/// <see cref="IBoundedBuffer{TItem}"/>, or <see cref="IEnrichmentStage{TItem}"/> when the item is
/// enriched on its way; what the pipeline has done is readable from <see cref="IPipelineCounters{TItem}"/>.
/// </remarks>
public static class PipelineServiceCollectionExtensions
{
    /// <summary>
    /// Registers the buffer and the batching worker of a pipeline of <typeparamref name="TItem"/>,
    /// committing through <typeparamref name="TCommitter"/>.
    /// </summary>
    /// <typeparam name="TItem">The item the pipeline carries.</typeparam>
    /// <typeparam name="TCommitter">The committer; registered as a singleton.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">An optional action to configure this pipeline's options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <remarks>
    /// The options are named after <typeparamref name="TItem"/> and validated by <c>ValidateOnStart()</c>.
    /// The first committer registered for an item type stays: a second call with another committer type
    /// does not replace it.
    /// The worker is a hosted service: register an enrichment stage <em>after</em> it, because the host
    /// stops hosted services in reverse order and the stage has to stop first to hand its last items on.
    /// </remarks>
    public static IServiceCollection AddBsftBatchingWriter<TItem, TCommitter>(
        this IServiceCollection services,
        Action<BsftBoundedPipelineOptions>? configure = null)
        where TCommitter : class, IBatchCommitter<TItem>
    {
        ArgumentNullException.ThrowIfNull(services);

        return Add<TItem, TCommitter>(services, configure);
    }

    /// <summary>
    /// Registers the buffer and the batching worker of a pipeline of <typeparamref name="TItem"/> with its
    /// options bound from <see cref="BsftBoundedPipelineOptions.SectionName"/> followed by the item type's
    /// full name.
    /// </summary>
    /// <typeparam name="TItem">The item the pipeline carries.</typeparam>
    /// <typeparam name="TCommitter">The committer; registered as a singleton.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">The configuration the section is read from.</param>
    /// <param name="configure">An optional action applied after the section has been bound.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.</exception>
    public static IServiceCollection AddBsftBatchingWriter<TItem, TCommitter>(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BsftBoundedPipelineOptions>? configure = null)
        where TCommitter : class, IBatchCommitter<TItem>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = $"{BsftBoundedPipelineOptions.SectionName}:{BsftBoundedPipelineOptions.NameFor<TItem>()}";

        return Add<TItem, TCommitter>(services, options =>
        {
            configuration.GetSection(section).Bind(options);
            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// Registers an enrichment stage in front of the batching worker of <typeparamref name="TItem"/>.
    /// </summary>
    /// <typeparam name="TItem">The item the pipeline carries.</typeparam>
    /// <typeparam name="TEnricher">The enricher; registered as a singleton.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <c>AddBsftBatchingWriter</c> has not registered the pipeline of
    /// <typeparamref name="TItem"/> yet.
    /// </exception>
    /// <remarks>
    /// Call this after <c>AddBsftBatchingWriter</c>: the stage's output is the writer's buffer, and the
    /// host stops the stage first only when it was registered second. A second call for the same item type
    /// changes nothing.
    /// </remarks>
    public static IServiceCollection AddBsftPipelineEnrichment<TItem, TEnricher>(this IServiceCollection services)
        where TItem : class
        where TEnricher : class, IPipelineEnricher<TItem>
    {
        ArgumentNullException.ThrowIfNull(services);

        // Registered the wrong way round the host would stop the writer first, and the stage's last items
        // would meet a completed buffer; refused here instead of lost silently there.
        if (!services.Any(descriptor => !descriptor.IsKeyedService && descriptor.ImplementationType == typeof(BatchingWriterWorker<TItem>)))
        {
            throw new InvalidOperationException(
                $"AddBsftPipelineEnrichment<{typeof(TItem).Name}> needs AddBsftBatchingWriter<{typeof(TItem).Name}, ...> registered first.");
        }

        services.TryAddSingleton<IPipelineEnricher<TItem>, TEnricher>();
        services.TryAddSingleton<EnrichmentStageWorker<TItem>>();
        services.TryAddSingleton<IEnrichmentStage<TItem>>(provider => provider.GetRequiredService<EnrichmentStageWorker<TItem>>());
        services.TryAddSingleton<IPipelineUpstream<TItem>>(provider => provider.GetRequiredService<EnrichmentStageWorker<TItem>>());
        services.Configure<BsftBoundedPipelineOptions>(BsftBoundedPipelineOptions.NameFor<TItem>(), options => options.HasEnrichmentStage = true);

        // One hosted descriptor per item type: a second one would start a second reader on a channel
        // that has exactly one.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, EnrichmentStageWorker<TItem>>(
            provider => provider.GetRequiredService<EnrichmentStageWorker<TItem>>()));

        return services;
    }

    private static IServiceCollection Add<TItem, TCommitter>(IServiceCollection services, Action<BsftBoundedPipelineOptions>? configure)
        where TCommitter : class, IBatchCommitter<TItem>
    {
        services.AddOptions<BsftBoundedPipelineOptions>(BsftBoundedPipelineOptions.NameFor<TItem>())
            .Configure(options => configure?.Invoke(options))
            .ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<BsftBoundedPipelineOptions>, BsftBoundedPipelineOptionsValidator>());

        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        services.AddBsftInstrumentationSource(BackgroundWorkDefaults.PipelineInstrumentationSourceName);

        services.TryAddSingleton<IOperationRacer, OperationRacer>();
        services.TryAddSingleton<PipelineCounters<TItem>>();
        services.TryAddSingleton<IPipelineCounters<TItem>>(provider => provider.GetRequiredService<PipelineCounters<TItem>>());
        services.TryAddSingleton<IPipelineCounterSink<TItem>>(provider => provider.GetRequiredService<PipelineCounters<TItem>>());
        services.TryAddSingleton<BoundedBuffer<TItem>>();
        services.TryAddSingleton<IBoundedBuffer<TItem>>(provider => provider.GetRequiredService<BoundedBuffer<TItem>>());
        services.TryAddSingleton<IBoundedBufferSource<TItem>>(provider => provider.GetRequiredService<BoundedBuffer<TItem>>());
        services.TryAddSingleton<IBatchCommitter<TItem>, TCommitter>();
        services.TryAddSingleton<IBatchCommitRunner<TItem>, BatchCommitRunner<TItem>>();
        services.AddHostedService<BatchingWriterWorker<TItem>>();

        return services;
    }
}
