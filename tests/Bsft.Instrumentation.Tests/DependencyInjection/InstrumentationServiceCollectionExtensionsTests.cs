using System.Diagnostics.Metrics;
using Bsft.Instrumentation.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Instrumentation.Tests.DependencyInjection;

public sealed class InstrumentationServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBsftInstrumentationSource_NullServices_Throws()
    {
        var act = () => InstrumentationServiceCollectionExtensions.AddBsftInstrumentationSource(null!, "X");
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddBsftInstrumentationSource_EmptyName_Throws()
    {
        var act = () => new ServiceCollection().AddBsftInstrumentationSource("  ");
        act.Should().Throw<ArgumentException>().WithParameterName("name");
    }

    [Fact]
    public void AddBsftInstrumentationSource_RegistersRegistrationProviderAndMeterFactory()
    {
        var services = new ServiceCollection();

        services.AddBsftInstrumentationSource("MyApp.Orders", ObservabilitySignals.Traces);

        using var sp = services.BuildServiceProvider();
        sp.GetService<IMeterFactory>().Should().NotBeNull();
        sp.GetService<IInstrumentationSourceProvider>().Should().NotBeNull();

        var registration = sp.GetRequiredService<ObservabilitySourceRegistration>();
        registration.Name.Should().Be("MyApp.Orders");
        registration.EnableTracing.Should().BeTrue();
        registration.EnableMetrics.Should().BeFalse();
    }

    [Fact]
    public void AddBsftInstrumentationSource_MultipleSources_AllDiscoverable()
    {
        var services = new ServiceCollection();

        services.AddBsftInstrumentationSource("Bsft.Mediator");
        services.AddBsftInstrumentationSource("Bsft.Saga");

        using var sp = services.BuildServiceProvider();
        sp.GetServices<ObservabilitySourceRegistration>()
            .Select(r => r.Name)
            .Should().BeEquivalentTo("Bsft.Mediator", "Bsft.Saga");

        // Provider is a singleton registered once even across multiple source registrations.
        sp.GetServices<IInstrumentationSourceProvider>().Should().ContainSingle();
    }

    [Fact]
    public void AddBsftInstrumentationSource_SameNameTwice_RegistersOnce()
    {
        var services = new ServiceCollection();

        services.AddBsftInstrumentationSource("Bsft.Mediator");
        services.AddBsftInstrumentationSource("Bsft.Mediator");

        using var sp = services.BuildServiceProvider();
        sp.GetServices<ObservabilitySourceRegistration>().Should().ContainSingle();
    }

    [Fact]
    public void AddBsftInstrumentationSource_ResolvedSource_EmitsThroughProvider()
    {
        var services = new ServiceCollection();
        services.AddBsftInstrumentationSource("MyApp.Orders");
        using var sp = services.BuildServiceProvider();

        var source = sp.GetRequiredService<IInstrumentationSourceProvider>().Get("MyApp.Orders");

        source.Name.Should().Be("MyApp.Orders");

        long measured = 0;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "MyApp.Orders")
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((_, value, _, _) => measured += value);
        meterListener.Start();

        source.GetOrCreateCounter("orders.imported").Add(3);

        measured.Should().Be(3);
    }
}
