using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Resilience.Mediator.Tests;

public class MediatorResiliencePipelineProviderTests
{
    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        var act = () => new MediatorResiliencePipelineProvider(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullShouldRetry_Throws()
    {
        var options = new BsftResilienceMediatorOptions { ShouldRetry = null! };

        var act = () => new MediatorResiliencePipelineProvider(Options.Create(options));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Pipeline_IsBuiltAndReused()
    {
        var provider = new MediatorResiliencePipelineProvider(Options.Create(new BsftResilienceMediatorOptions()));

        provider.Pipeline.Should().NotBeNull();
        provider.Pipeline.Should().BeSameAs(provider.Pipeline);
    }
}
