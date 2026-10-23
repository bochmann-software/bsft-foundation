using Bsft.Hosting.BackgroundWork.Pipelines;
using Bsft.Hosting.BackgroundWork.DependencyInjection;
using Bsft.Hosting.BackgroundWork.Tests.Pipelines.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Hosting.BackgroundWork.Tests.DependencyInjection;

public sealed class PipelineServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBsftBatchingWriter_RegistersTheBufferTheCountersAndOneHostedWorker()
    {
        var services = new ServiceCollection();

        services.AddBsftBatchingWriter<SampleItem, RecordingBatchCommitter>(options => options.Capacity = 7);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IBoundedBuffer<SampleItem>>().Should().BeSameAs(provider.GetRequiredService<IBoundedBufferSource<SampleItem>>());
        provider.GetRequiredService<IPipelineCounters<SampleItem>>().Should().NotBeNull();
        provider.GetServices<IHostedService>().Should().ContainSingle();
        provider.GetRequiredService<IOptionsMonitor<BsftBoundedPipelineOptions>>()
            .Get(BsftBoundedPipelineOptions.NameFor<SampleItem>()).Capacity.Should().Be(7);
    }

    [Fact]
    public void AddBsftBatchingWriter_WhenTheOptionsAreInvalid_ValidationRejectsThem()
    {
        var services = new ServiceCollection();
        services.AddBsftBatchingWriter<SampleItem, RecordingBatchCommitter>(options => options.Capacity = 0);

        using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptionsMonitor<BsftBoundedPipelineOptions>>()
            .Get(BsftBoundedPipelineOptions.NameFor<SampleItem>());

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void AddBsftBatchingWriter_WhenConfigurationIsGiven_BindsThePerItemSectionAndLetsCodeWin()
    {
        var section = $"{BsftBoundedPipelineOptions.SectionName}:{BsftBoundedPipelineOptions.NameFor<SampleItem>()}";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{section}:Capacity"] = "11",
                [$"{section}:MaxBatchSize"] = "3",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddBsftBatchingWriter<SampleItem, RecordingBatchCommitter>(configuration, options => options.MaxBatchSize = 4);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<BsftBoundedPipelineOptions>>()
            .Get(BsftBoundedPipelineOptions.NameFor<SampleItem>());
        options.Capacity.Should().Be(11);
        options.MaxBatchSize.Should().Be(4);
    }

    [Fact]
    public void AddBsftPipelineEnrichment_RegistersTheStageAsASecondHostedServiceAfterTheWriter()
    {
        var services = new ServiceCollection();
        services.AddBsftBatchingWriter<SampleItem, RecordingBatchCommitter>();

        services.AddBsftPipelineEnrichment<SampleItem, ScriptedEnricher>();

        using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().ToArray();
        hosted.Should().HaveCount(2);
        hosted[1].Should().BeSameAs(provider.GetRequiredService<IEnrichmentStage<SampleItem>>());
    }

    [Fact]
    public void AddBsftBatchingWriter_WhenServicesOrConfigurationAreNull_Throws()
    {
        var services = new ServiceCollection();

        var nullServices = () => PipelineServiceCollectionExtensions.AddBsftBatchingWriter<SampleItem, RecordingBatchCommitter>(null!);
        var nullServicesWithConfiguration = () => PipelineServiceCollectionExtensions.AddBsftBatchingWriter<SampleItem, RecordingBatchCommitter>(null!, new ConfigurationBuilder().Build());
        var nullConfiguration = () => services.AddBsftBatchingWriter<SampleItem, RecordingBatchCommitter>((IConfiguration)null!);
        var nullEnrichmentServices = () => PipelineServiceCollectionExtensions.AddBsftPipelineEnrichment<SampleItem, ScriptedEnricher>(null!);

        nullServices.Should().Throw<ArgumentNullException>();
        nullServicesWithConfiguration.Should().Throw<ArgumentNullException>();
        nullConfiguration.Should().Throw<ArgumentNullException>();
        nullEnrichmentServices.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddBsftPipelineEnrichment_WhenCalledTwice_RegistersTheStageOnce()
    {
        var services = new ServiceCollection();
        services.AddBsftBatchingWriter<SampleItem, RecordingBatchCommitter>();

        services.AddBsftPipelineEnrichment<SampleItem, ScriptedEnricher>();
        services.AddBsftPipelineEnrichment<SampleItem, ScriptedEnricher>();

        using var provider = services.BuildServiceProvider();
        provider.GetServices<IHostedService>().Should().HaveCount(2);
    }

    [Fact]
    public void AddBsftPipelineEnrichment_WhenNoWriterIsRegisteredYet_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftPipelineEnrichment<SampleItem, ScriptedEnricher>();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddBsftPipelineEnrichment_WhenAKeyedServiceIsRegisteredFirst_StillRegistersTheStage()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IPipelineEnricher<SampleItem>, ScriptedEnricher>("other");
        services.AddBsftBatchingWriter<SampleItem, RecordingBatchCommitter>();

        services.AddBsftPipelineEnrichment<SampleItem, ScriptedEnricher>();

        using var provider = services.BuildServiceProvider();
        provider.GetServices<IHostedService>().Should().HaveCount(2);
    }
}
