using Bsft.Hosting.BackgroundWork.Pipelines;
using Bsft.Hosting.BackgroundWork.DependencyInjection;
using Bsft.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bsft.Hosting.BackgroundWork.Tests.Pipelines.Fakes;

/// <summary>
/// A container with one pipeline of <see cref="SampleItem"/> registered through the production
/// extensions, so a test starts and stops the real hosted services around scripted fakes.
/// </summary>
internal sealed class PipelineHost : IDisposable
{
    private static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(30);

    private readonly ServiceProvider _provider;

    public PipelineHost(Action<BsftBoundedPipelineOptions>? configure = null, bool withEnrichment = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<BatchingWriterWorker<SampleItem>>>(Writer);
        services.AddSingleton<ILogger<EnrichmentStageWorker<SampleItem>>>(Stage);
        services.AddSingleton<IBatchCommitter<SampleItem>>(Committer);
        services.AddSingleton<IPipelineEnricher<SampleItem>>(Enricher);
        services.AddSingleton<TimeProvider>(Clock);

        services.AddBsftBatchingWriter<SampleItem, RecordingBatchCommitter>(options =>
        {
            options.FlushInterval = TimeSpan.FromSeconds(1);
            options.FlushOnStopTimeout = TimeSpan.FromSeconds(10);
            options.EnrichmentTimeout = TimeSpan.FromSeconds(30);
            options.EnrichmentOnStopTimeout = TimeSpan.FromSeconds(5);
            configure?.Invoke(options);
        });

        if (withEnrichment)
            services.AddBsftPipelineEnrichment<SampleItem, ScriptedEnricher>();

        _provider = services.BuildServiceProvider();
    }

    public ManualTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

    public RecordingBatchCommitter Committer { get; } = new();

    public ScriptedEnricher Enricher { get; } = new();

    public RecordingLogger<BatchingWriterWorker<SampleItem>> Writer { get; } = new();

    public RecordingLogger<EnrichmentStageWorker<SampleItem>> Stage { get; } = new();

    public IBoundedBuffer<SampleItem> Buffer => _provider.GetRequiredService<IBoundedBuffer<SampleItem>>();

    public IEnrichmentStage<SampleItem> EnrichmentStage => _provider.GetRequiredService<IEnrichmentStage<SampleItem>>();

    public IPipelineCounterSink<SampleItem> CounterSink => _provider.GetRequiredService<IPipelineCounterSink<SampleItem>>();

    public IPipelineCounters<SampleItem> Counters => _provider.GetRequiredService<IPipelineCounters<SampleItem>>();

    public IReadOnlyList<IHostedService> Services => _provider.GetServices<IHostedService>().ToArray();

    public T Get<T>()
        where T : notnull => _provider.GetRequiredService<T>();

    public async Task StartAsync()
    {
        foreach (var service in Services)
            await service.StartAsync(CancellationToken.None);
    }

    /// <summary>Stops the services in reverse registration order, as the host does.</summary>
    /// <param name="cancellationToken">The host's stop deadline.</param>
    /// <returns>A task that completes when every service has stopped; it fails instead of hanging.</returns>
    public Task StopAsync(CancellationToken cancellationToken = default) =>
        StopSequentiallyAsync(cancellationToken).WaitAsync(Ceiling);

    /// <summary>Stops every service at once, as a host with <c>ServicesStopConcurrently</c> does.</summary>
    /// <param name="cancellationToken">The host's stop deadline.</param>
    /// <returns>A task that completes when every service has stopped; it fails instead of hanging.</returns>
    public Task StopConcurrentlyAsync(CancellationToken cancellationToken = default) =>
        Task.WhenAll(Services.Select(service => service.StopAsync(cancellationToken))).WaitAsync(Ceiling);

    private async Task StopSequentiallyAsync(CancellationToken cancellationToken)
    {
        foreach (var service in Services.Reverse())
            await service.StopAsync(cancellationToken);
    }

    public void Dispose() => _provider.Dispose();
}
