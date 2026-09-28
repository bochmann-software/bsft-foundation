using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Instrumentation.Tests;

public sealed class InstrumentationSourceProviderTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly System.Diagnostics.Metrics.IMeterFactory _meterFactory;

    public InstrumentationSourceProviderTests()
    {
        _provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        _meterFactory = _provider.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>();
    }

    [Fact]
    public void Constructor_NullMeterFactory_Throws()
    {
        var act = () => new InstrumentationSourceProvider(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("meterFactory");
    }

    [Fact]
    public void Get_NullOrEmptyName_Throws()
    {
        using var sut = new InstrumentationSourceProvider(_meterFactory);
        var act = () => sut.Get("");
        act.Should().Throw<ArgumentException>().WithParameterName("name");
    }

    [Fact]
    public void Get_SameName_ReturnsCachedSource()
    {
        using var sut = new InstrumentationSourceProvider(_meterFactory);

        var first = sut.Get("MyApp.A");
        var second = sut.Get("MyApp.A");

        first.Should().BeSameAs(second);
        first.Name.Should().Be("MyApp.A");
    }

    [Fact]
    public void Get_DifferentNames_ReturnsDistinctSources()
    {
        using var sut = new InstrumentationSourceProvider(_meterFactory);

        var a = sut.Get("MyApp.A");
        var b = sut.Get("MyApp.B");

        a.Should().NotBeSameAs(b);
    }

    [Fact]
    public void Dispose_DisposesCachedSources_AndDoesNotThrow()
    {
        var sut = new InstrumentationSourceProvider(_meterFactory);
        sut.Get("MyApp.A");
        sut.Get("MyApp.B");

        var act = sut.Dispose;

        act.Should().NotThrow();
    }

    public void Dispose() => _provider.Dispose();
}
