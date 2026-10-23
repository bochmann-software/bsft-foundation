using System.Diagnostics.Metrics;
using Bsft.Hosting.BackgroundWork.Pipelines;
using Bsft.Instrumentation;
using Bsft.Instrumentation.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Hosting.BackgroundWork.Tests.Pipelines;

public sealed class PipelineCountersTests
{
    [Fact]
    public void PipelineCounters_WhenEachOutcomeIsCounted_ExposesTheTotalsAndEmitsTheMetric()
    {
        using var provider = new ServiceCollection()
            .AddBsftInstrumentationSource(BackgroundWorkDefaults.PipelineInstrumentationSourceName)
            .BuildServiceProvider();
        var counters = new PipelineCounters<string>(provider.GetRequiredService<IInstrumentationSourceProvider>());
        var emitted = new List<(string Outcome, long Value)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Name == PipelineCounters<string>.CounterName)
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var tagList = tags.ToArray();
            if (!tagList.Any(tag => tag.Key == "pipeline" && Equals(tag.Value, "System.String")))
                return;

            lock (emitted)
                emitted.Add((tagList.Single(tag => tag.Key == "outcome").Value!.ToString()!, value));
        });
        listener.Start();

        counters.CountEnqueued();
        counters.CountDropped();
        counters.CountCommitted(3);
        counters.CountFailedBatch(2);
        counters.CountAbandoned(4);
        counters.CountEnrichmentFailed();
        counters.CountEnrichmentSkipped(2);

        counters.Enqueued.Should().Be(1);
        counters.Dropped.Should().Be(1);
        counters.Committed.Should().Be(3);
        counters.Failed.Should().Be(6);
        counters.FailedBatches.Should().Be(1);
        counters.EnrichmentFailed.Should().Be(1);
        counters.EnrichmentSkipped.Should().Be(2);
        emitted.Should().Equal(
            ("enqueued", 1),
            ("dropped", 1),
            ("committed", 3),
            ("failed", 2),
            ("failed", 4),
            ("enrichment_failed", 1),
            ("enrichment_skipped", 2));
    }

    [Fact]
    public void PipelineCounters_WhenTheProviderIsNull_Throws()
    {
        var act = () => new PipelineCounters<string>(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
