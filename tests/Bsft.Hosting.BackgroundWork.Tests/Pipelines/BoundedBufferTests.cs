using Bsft.Hosting.BackgroundWork.Pipelines;
using Bsft.Hosting.BackgroundWork.Tests.Pipelines.Fakes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Hosting.BackgroundWork.Tests.Pipelines;

public sealed class BoundedBufferTests
{
    [Fact]
    public void TryEnqueue_WhenThereIsRoom_QueuesTheItemAndCountsIt()
    {
        using var host = new PipelineHost();

        host.Buffer.TryEnqueue(new SampleItem(1)).Should().BeTrue();

        host.Counters.Enqueued.Should().Be(1);
        host.Get<IBoundedBufferSource<SampleItem>>().Reader.TryRead(out var item).Should().BeTrue();
        item.Should().Be(new SampleItem(1));
    }

    [Fact]
    public void TryEnqueue_WhenTheBufferIsFull_DropsTheItemCountsItAndNeverBlocks()
    {
        using var host = new PipelineHost(options => options.Capacity = 2);

        var results = Enumerable.Range(1, 5).Select(id => host.Buffer.TryEnqueue(new SampleItem(id))).ToArray();

        results.Should().Equal(true, true, false, false, false);
        host.Counters.Enqueued.Should().Be(2);
        host.Counters.Dropped.Should().Be(3);
    }

    [Fact]
    public void TryEnqueue_WhenTheBufferIsComplete_RefusesTheItemButKeepsWhatWasQueued()
    {
        using var host = new PipelineHost();
        var source = host.Get<IBoundedBufferSource<SampleItem>>();
        host.Buffer.TryEnqueue(new SampleItem(1));

        source.Complete();

        host.Buffer.TryEnqueue(new SampleItem(2)).Should().BeFalse();
        host.Counters.Dropped.Should().Be(1);
        source.Reader.TryRead(out var kept).Should().BeTrue();
        kept.Should().Be(new SampleItem(1));
    }

    [Fact]
    public void TryEnqueue_WhenTheItemIsNull_Throws()
    {
        using var host = new PipelineHost();

        var act = () => host.Buffer.TryEnqueue(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task TryEnqueue_WhenManyProducersRace_AccountsForEveryItemExactlyOnce()
    {
        using var host = new PipelineHost(options => options.Capacity = 700);
        const int producers = 8;
        const int perProducer = 500;

        await Task.WhenAll(Enumerable.Range(0, producers).Select(producer => Task.Run(() =>
        {
            for (var index = 0; index < perProducer; index++)
                host.Buffer.TryEnqueue(new SampleItem((producer * perProducer) + index));
        })));

        host.Counters.Enqueued.Should().Be(700);
        host.Counters.Dropped.Should().Be((producers * perProducer) - 700);
        var reader = host.Get<IBoundedBufferSource<SampleItem>>().Reader;
        var drained = 0;
        while (reader.TryRead(out _))
            drained++;

        drained.Should().Be(700);
    }

    [Fact]
    public void BoundedBuffer_WhenTheOptionsAreNull_Throws()
    {
        using var host = new PipelineHost();

        var act = () => new BoundedBuffer<SampleItem>(null!, host.CounterSink);

        act.Should().Throw<ArgumentNullException>();
    }
}
