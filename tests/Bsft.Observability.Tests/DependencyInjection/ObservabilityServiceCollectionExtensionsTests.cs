using System.Diagnostics;
using Bsft.Instrumentation;
using Bsft.Instrumentation.DependencyInjection;
using Bsft.Observability.DependencyInjection;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace Bsft.Observability.Tests.DependencyInjection;

public sealed class ObservabilityServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBsftObservability_NullServices_Throws()
    {
        var act = () => ObservabilityServiceCollectionExtensions.AddBsftObservability(null!, _ => { });
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddBsftObservability_NullConfigurationServices_Throws()
    {
        var config = new ConfigurationBuilder().Build();
        var act = () => ObservabilityServiceCollectionExtensions.AddBsftObservability(null!, config);
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddBsftObservability_NullConfiguration_Throws()
    {
        var act = () => new ServiceCollection().AddBsftObservability((IConfiguration)null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void AddBsftObservability_Defaults_RegistersTracerAndMeterProvider()
    {
        var services = new ServiceCollection();
        services.AddBsftObservability(o => o.EnableOtlpExporter = false);

        using var sp = services.BuildServiceProvider();

        sp.GetService<TracerProvider>().Should().NotBeNull();
        sp.GetService<MeterProvider>().Should().NotBeNull();
    }

    [Fact]
    public void AddBsftObservability_AllFlagsOffAndOn_BuildsProviderWithoutThrowing()
    {
        var hooksInvoked = 0;

        var off = new ServiceCollection();
        off.AddBsftObservability(o =>
        {
            o.EnableTracing = false;
            o.EnableMetrics = false;
        });
        var offBuild = () => off.BuildServiceProvider().Dispose();
        offBuild.Should().NotThrow();

        var on = new ServiceCollection();
        on.AddBsftObservability(o =>
        {
            o.EnableEntityFrameworkCoreInstrumentation = true;
            o.EnableConsoleExporter = true;
            o.EnableLogging = true;
            o.OtlpEndpoint = new Uri("http://collector:4317");
            o.OtlpProtocol = OtlpExportProtocol.HttpProtobuf;
            o.ConfigureTracerProvider = _ => hooksInvoked++;
            o.ConfigureMeterProvider = _ => hooksInvoked++;
            o.ConfigureLoggerProvider = _ => hooksInvoked++;
        });

        using var sp = on.BuildServiceProvider();
        // Force provider build so the WithTracing/WithMetrics/WithLogging callbacks (and hooks) execute.
        sp.GetService<TracerProvider>();
        sp.GetService<MeterProvider>();
        sp.GetService<LoggerProvider>();

        hooksInvoked.Should().Be(3);
    }

    [Fact]
    public void AddBsftObservability_Defaults_DoesNotRegisterLoggerProvider()
    {
        var services = new ServiceCollection();
        services.AddBsftObservability(o => o.EnableOtlpExporter = false);

        using var sp = services.BuildServiceProvider();

        sp.GetService<LoggerProvider>().Should().BeNull();
    }

    [Fact]
    public void AddBsftObservability_EnableLogging_RegistersLoggerProviderAndExportsLogRecords()
    {
        var exported = new List<LogRecord>();
        var services = new ServiceCollection();
        services.AddBsftObservability(o =>
        {
            o.EnableOtlpExporter = false;
            o.EnableLogging = true;
            o.ConfigureLoggerProvider = b => b.AddInMemoryExporter(exported);
        });

        using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<LoggerProvider>();

        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        loggerFactory.CreateLogger("Test.Category").LogInformation("hello from the log pipeline");

        exported.Should().Contain(r => r.CategoryName == "Test.Category");
    }

    [Fact]
    public void AddBsftObservability_FromConfigurationSection_BindsAndBuilds()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Observability:ServiceName"] = "orders-api",
                ["Bsft:Observability:ServiceVersion"] = "2.0.0",
                ["Bsft:Observability:EnableConsoleExporter"] = "true",
                ["Bsft:Observability:EnableOtlpExporter"] = "false",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddBsftObservability(config);

        using var sp = services.BuildServiceProvider();
        // Resolving the provider builds the resource, applying the bound ServiceName/ServiceVersion.
        var act = () => sp.GetRequiredService<TracerProvider>();
        act.Should().NotThrow();
    }

    [Fact]
    public void Discovery_SourceRegisteredBeforeObservability_ActivityExported() =>
        AssertSourceDiscovered(registerSourceFirst: true);

    [Fact]
    public void Discovery_SourceRegisteredAfterObservability_ActivityExported() =>
        AssertSourceDiscovered(registerSourceFirst: false);

    [Fact]
    public void Discovery_AdditionalActivitySource_IsCollected()
    {
        var exported = new List<Activity>();
        using var ownSource = new ActivitySource("MyApp.Extra");

        var services = new ServiceCollection();
        services.AddBsftObservability(o =>
        {
            o.EnableOtlpExporter = false;
            o.AdditionalActivitySources.Add("MyApp.Extra");
            o.ConfigureTracerProvider = b => b.AddInMemoryExporter(exported);
        });

        using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<TracerProvider>();   // forces deferred discovery

        using (ownSource.StartActivity("extra-op")) { }

        exported.Should().Contain(a => a.DisplayName == "extra-op");
    }

    [Fact]
    public void Discovery_MeterRegistered_MetricExported()
    {
        var exported = new List<Metric>();
        var services = new ServiceCollection();
        services.AddBsftInstrumentationSource("Test.Metered");
        services.AddBsftObservability(o =>
        {
            o.EnableOtlpExporter = false;
            o.AdditionalMeters.Add("MyApp.ExtraMeter");
            o.ConfigureMeterProvider = b => b.AddInMemoryExporter(exported);
        });

        using var sp = services.BuildServiceProvider();
        var meterProvider = sp.GetRequiredService<MeterProvider>();
        var source = sp.GetRequiredService<IInstrumentationSourceProvider>().Get("Test.Metered");

        source.GetOrCreateCounter("hits").Add(1);
        meterProvider.ForceFlush();

        exported.Should().Contain(m => m.Name == "hits");
    }

    private static void AssertSourceDiscovered(bool registerSourceFirst)
    {
        var exported = new List<Activity>();
        var services = new ServiceCollection();

        void RegisterSource() => services.AddBsftInstrumentationSource("Test.Discovered");
        void RegisterObservability() => services.AddBsftObservability(o =>
        {
            o.EnableOtlpExporter = false;
            o.ConfigureTracerProvider = b => b.AddInMemoryExporter(exported);
        });

        if (registerSourceFirst)
        {
            RegisterSource();
            RegisterObservability();
        }
        else
        {
            RegisterObservability();
            RegisterSource();
        }

        using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<TracerProvider>();   // forces deferred discovery
        var source = sp.GetRequiredService<IInstrumentationSourceProvider>().Get("Test.Discovered");

        using (source.StartActivity("op")) { }

        exported.Should().Contain(a => a.DisplayName == "op");
    }

    [Fact]
    public void ApplyOtlp_EndpointAndProtocolSet_AreApplied()
    {
        var options = new BsftObservabilityOptions
        {
            OtlpEndpoint = new Uri("http://collector:4317"),
            OtlpProtocol = OtlpExportProtocol.HttpProtobuf,
        };
        var exporter = new OtlpExporterOptions();

        ObservabilityServiceCollectionExtensions.ApplyOtlp(exporter, options);

        exporter.Endpoint.Should().Be(options.OtlpEndpoint);
        exporter.Protocol.Should().Be(OtlpExportProtocol.HttpProtobuf);
    }

    [Fact]
    public void ApplyOtlp_NothingSet_LeavesDefaults()
    {
        var exporter = new OtlpExporterOptions();
        var defaultEndpoint = exporter.Endpoint;

        ObservabilityServiceCollectionExtensions.ApplyOtlp(exporter, new BsftObservabilityOptions());

        exporter.Endpoint.Should().Be(defaultEndpoint);
    }

    [Theory]
    [InlineData("abc-123", "abc-123")]
    [InlineData("has space", null)]
    // A trailing newline is the one malformed value a $-anchored pattern lets through, and a tag is
    // exactly where it must not arrive.
    [InlineData("trailing-newline" + "\n", null)]
    [InlineData("", null)]
    public void EnrichWithCorrelationId_RecordsOnlyWellFormedValues(string headerValue, string? expectedTag)
    {
        using var activity = new Activity("test").Start();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-Id"] = headerValue;

        ObservabilityServiceCollectionExtensions.EnrichWithCorrelationId(activity, context.Request, "X-Correlation-Id");

        activity.GetTagItem(ObservabilityDefaults.CorrelationTagName).Should().Be(expectedTag);
    }

    [Fact]
    public void EnrichWithCorrelationId_MissingHeader_RecordsNothing()
    {
        using var activity = new Activity("test").Start();
        var context = new DefaultHttpContext();

        ObservabilityServiceCollectionExtensions.EnrichWithCorrelationId(activity, context.Request, "X-Correlation-Id");

        activity.GetTagItem(ObservabilityDefaults.CorrelationTagName).Should().BeNull();
    }
}
