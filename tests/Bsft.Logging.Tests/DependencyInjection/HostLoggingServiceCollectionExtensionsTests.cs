using Bsft.Logging.DependencyInjection;
using Bsft.Logging.LogFiles;
using Bsft.Logging.Tests.Support;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Reflection;

namespace Bsft.Logging.Tests.DependencyInjection;

public class HostLoggingServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBsftLogging_RegistersLoggingServices()
    {
        var services = new ServiceCollection();
        services.AddBsftLogging();

        var provider = services.BuildServiceProvider();
        var loggerFactory = provider.GetService<ILoggerFactory>();

        loggerFactory.Should().NotBeNull();
    }

    [Fact]
    public void AddBsftLogging_RegistersLogFileReaderAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddBsftLogging(options => options.EnableFileLogging = false);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ILogFileReader>()
            .Should().BeSameAs(provider.GetRequiredService<ILogFileReader>());
    }

    [Fact]
    public void AddBsftFileLogger_AloneRegistersLogFileReader()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddBsftFileLogger());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ILogFileAccess>().Should().NotBeNull();
        provider.GetRequiredService<ILogFileReader>().Should().NotBeNull();
    }

    [Fact]
    public void BothLoggingEntryPoints_RegisterOneLogFileReader()
    {
        var services = new ServiceCollection();
        services.AddBsftLogging();
        services.AddLogging(builder => builder.AddBsftFileLogger());

        services.Count(descriptor => descriptor.ServiceType == typeof(ILogFileReader)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(ILogFileAccess)).Should().Be(1);
    }

    [Fact]
    public void AddBsftLogging_RegistersDefaultLogFileAccessWithoutReplacingHostOverride()
    {
        var replacement = Substitute.For<ILogFileAccess>();
        var services = new ServiceCollection();
        services.AddSingleton(replacement);

        services.AddBsftLogging(options => options.EnableFileLogging = false);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ILogFileAccess>().Should().BeSameAs(replacement);
    }

    [Fact]
    public void AddBsftLogging_UsesOneLogFileNamingInstanceForWriterAndReader()
    {
        using var log = new LogFileFixture();
        var services = new ServiceCollection();
        services.AddBsftLogging(options => options.LogDirectory = log.LogDirectory);

        using var provider = services.BuildServiceProvider();
        var naming = provider.GetRequiredService<ILogFileNaming>();
        var writer = provider.GetServices<ILoggerProvider>().OfType<BsftFileLoggerProvider>().Single();
        var reader = (LogFileReader)provider.GetRequiredService<ILogFileReader>();

        GetField<ILogFileNaming>(writer, "_fileNaming").Should().BeSameAs(naming);
        GetField<ILogFileNaming>(reader, "_fileNaming").Should().BeSameAs(naming);
    }

    [Fact]
    public void AddBsftLogging_FiltersMicrosoftToWarning()
    {
        var services = new ServiceCollection();
        services.AddBsftLogging();

        var provider = services.BuildServiceProvider();
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("Microsoft.Extensions.Something");

        logger.IsEnabled(LogLevel.Information).Should().BeFalse();
        logger.IsEnabled(LogLevel.Warning).Should().BeTrue();
    }

    [Fact]
    public void AddBsftLogging_FiltersSystemToWarning()
    {
        var services = new ServiceCollection();
        services.AddBsftLogging();

        var provider = services.BuildServiceProvider();
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("System.Net.Http.HttpClient");

        logger.IsEnabled(LogLevel.Information).Should().BeFalse();
        logger.IsEnabled(LogLevel.Warning).Should().BeTrue();
    }

    [Fact]
    public void AddBsftLogging_QuieterFileLevel_LeavesTheConsoleWhereItIs()
    {
        // MinimumLevel is the file sink's level. Raising it quietens the file, and the console has
        // to keep seeing what it saw before.
        using var log = new LogFileFixture();
        var services = new ServiceCollection();
        services.AddBsftLogging(options =>
        {
            options.LogDirectory = log.LogDirectory;
            options.MinimumLevel = LogLevel.Warning;
        });

        using var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("App");

        logger.IsEnabled(LogLevel.Information).Should().BeTrue();
    }

    [Fact]
    public void AddBsftLogging_MoreVerboseFileLevel_LowersThePipelineFloor()
    {
        // The factory drops a record before any provider is offered it, so a file sink asking for
        // more than the floor only gets it if the floor comes down with it.
        using var log = new LogFileFixture();
        var services = new ServiceCollection();
        services.AddBsftLogging(options =>
        {
            options.LogDirectory = log.LogDirectory;
            options.MinimumLevel = LogLevel.Debug;
        });

        using var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("App");

        logger.IsEnabled(LogLevel.Debug).Should().BeTrue();
    }

    [Fact]
    public void AddBsftLogging_FileLoggingOffAndAVerboseLevel_LeavesTheFloorAlone()
    {
        // With no file sink there is nothing asking the pipeline for more than the default.
        var services = new ServiceCollection();
        services.AddBsftLogging(options =>
        {
            options.EnableFileLogging = false;
            options.MinimumLevel = LogLevel.Debug;
        });

        using var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("App");

        logger.IsEnabled(LogLevel.Debug).Should().BeFalse();
        logger.IsEnabled(LogLevel.Information).Should().BeTrue();
    }

    [Fact]
    public void AddBsftLogging_ThrowsOnNullServices()
    {
        IServiceCollection services = null!;
        var act = () => services.AddBsftLogging();
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddBsftFileLogger_NullBuilder_ThrowsArgumentNullException()
    {
        ILoggingBuilder builder = null!;

        var act = () => builder.AddBsftFileLogger();

        act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }

    [Fact]
    public void AddBsftFileLogger_RegistersProviderAndReturnsBuilder()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddBsftFileLogger());

        services.Should().Contain(sd => sd.ServiceType == typeof(ILoggerProvider));
    }

    [Fact]
    public void AddBsftFileLogger_RegistersTheProviderByFactory()
    {
        // Regression: registering a pre-built instance leaves it undisposed, because the container
        // disposes only what it created. The file provider drains its queue in Dispose, so whatever
        // was still queued at shutdown — the shutdown-phase log lines — would never reach the file.
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddBsftFileLogger());

        var descriptor = services.Single(sd => sd.ServiceType == typeof(ILoggerProvider));

        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
        descriptor.ImplementationInstance.Should().BeNull("an instance registration is never disposed by the container");
        descriptor.ImplementationFactory.Should().NotBeNull("the container must own the provider to dispose it");
    }

    [Fact]
    public void AddBsftFileLogger_ContainerDisposed_DisposesTheProvider()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "bsft-logging-" + Guid.NewGuid().ToString("N"));
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddLogging(b => b.AddBsftFileLogger(options => options.LogDirectory = tempDir));

        try
        {
            var provider = services.BuildServiceProvider();
            _ = provider.GetRequiredService<ILoggerFactory>();

            // The file provider arms its flush interval on the injected clock and disposes that timer in
            // its own Dispose, so a timer that is gone is the observable proof that the container
            // disposed the provider.
            clock.PendingTimerCount.Should().Be(1, "the live file provider holds its flush-interval timer");

            provider.Dispose();

            clock.PendingTimerCount.Should().Be(0, "the container must have disposed the file provider");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private static T GetField<T>(object instance, string name)
        => (T)instance.GetType()
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(instance)!;

    /// <summary>
    /// Pins what the worker template's documentation says: a host's own <c>Logging:LogLevel</c> entry for exactly
    /// the category the code filters (<c>Microsoft</c>) loses to the code filter, which is added later, while a more
    /// specific category in the configuration still wins over it.
    /// </summary>
    [Fact]
    public void AddBsftLogging_WithConfiguredLevelsForMicrosoft_KeepsItsFilterForThatCategoryAndYieldsToMoreSpecificOnes()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:LogLevel:Microsoft"] = "Debug",
                ["Logging:LogLevel:Microsoft.Hosting.Lifetime"] = "Information",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddConfiguration(configuration.GetSection("Logging")));
        services.AddBsftLogging(options => options.EnableFileLogging = false);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ILoggerFactory>();

        factory.CreateLogger("Microsoft.Something").IsEnabled(LogLevel.Information).Should().BeFalse();
        factory.CreateLogger("Microsoft.Hosting.Lifetime").IsEnabled(LogLevel.Information).Should().BeTrue();
    }
}
