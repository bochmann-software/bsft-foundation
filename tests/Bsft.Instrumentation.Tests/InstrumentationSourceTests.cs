using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Instrumentation.Tests;

public sealed class InstrumentationSourceTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly System.Diagnostics.Metrics.IMeterFactory _meterFactory;

    public InstrumentationSourceTests()
    {
        _provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        _meterFactory = _provider.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>();
    }

    [Fact]
    public void Constructor_NullOrEmptyName_Throws()
    {
        var act = () => new InstrumentationSource("  ", _meterFactory);
        act.Should().Throw<ArgumentException>().WithParameterName("name");
    }

    [Fact]
    public void Constructor_NullMeterFactory_Throws()
    {
        var act = () => new InstrumentationSource("Test.Source", null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("meterFactory");
    }

    [Fact]
    public void Name_ReturnsConfiguredName()
    {
        using var source = new InstrumentationSource("Test.Name", _meterFactory);
        source.Name.Should().Be("Test.Name");
    }

    [Fact]
    public void StartActivity_WithListener_ReturnsActivity()
    {
        using var source = new InstrumentationSource("Test.Listened", _meterFactory);
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Test.Listened",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = source.StartActivity("op", ActivityKind.Server);

        activity.Should().NotBeNull();
        activity!.OperationName.Should().Be("op");
        activity.Kind.Should().Be(ActivityKind.Server);
    }

    [Fact]
    public void StartActivity_WithoutListener_ReturnsNull()
    {
        using var source = new InstrumentationSource("Test.Unlistened", _meterFactory);

        using var activity = source.StartActivity("op");

        activity.Should().BeNull();
    }

    [Fact]
    public void StartActivity_NullOrEmptyName_Throws()
    {
        using var source = new InstrumentationSource("Test.Guard", _meterFactory);

        var act = () => source.StartActivity("  ");

        act.Should().Throw<ArgumentException>().WithParameterName("name");
    }

    [Fact]
    public void GetOrCreateCounter_SameName_ReturnsCachedInstrument()
    {
        using var source = new InstrumentationSource("Test.Counter", _meterFactory);

        var first = source.GetOrCreateCounter("hits", "By");
        var second = source.GetOrCreateCounter("hits");

        first.Should().BeSameAs(second);
        first.Unit.Should().Be("By");
    }

    [Fact]
    public void GetOrCreateHistogram_SameName_ReturnsCachedInstrument()
    {
        using var source = new InstrumentationSource("Test.Histogram", _meterFactory);

        var first = source.GetOrCreateHistogram("duration", "s");
        var second = source.GetOrCreateHistogram("duration");

        first.Should().BeSameAs(second);
        first.Unit.Should().Be("s");
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var source = new InstrumentationSource("Test.Dispose", _meterFactory);
        var act = source.Dispose;
        act.Should().NotThrow();
    }

    public void Dispose() => _provider.Dispose();
}
