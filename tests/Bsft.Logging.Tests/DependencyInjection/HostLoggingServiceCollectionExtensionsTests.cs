using Bsft.Logging.DependencyInjection;
using Bsft.Logging.LogFiles;
using Bsft.Logging.Tests.Support;
using FluentAssertions;
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
    public async Task AddBsftFileLogger_ContainerDisposed_DisposesTheProvider()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "bsft-logging-" + Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddBsftFileLogger(options => options.LogDirectory = tempDir));

        try
        {
            var provider = services.BuildServiceProvider();
            var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Shutdown");

            provider.Dispose();

            // A disposed provider drops what it is handed, so a write that never lands is the
            // observable proof that the container disposed it. An undisposed provider's flush
            // thread would pick this up well inside the window below.
            logger.LogInformation("after-disposal");

            (await ReadLogWithinAsync(tempDir, TimeSpan.FromSeconds(1)))
                .Should().NotContain("after-disposal", "the container must have disposed the file provider");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task<string> ReadLogWithinAsync(string directory, TimeSpan window)
    {
        var deadline = DateTime.UtcNow + window;

        while (DateTime.UtcNow < deadline)
        {
            var content = ReadLog(directory);
            if (content.Contains("after-disposal", StringComparison.Ordinal))
                return content;

            await Task.Delay(50);
        }

        return ReadLog(directory);
    }

    private static string ReadLog(string directory)
    {
        if (!Directory.Exists(directory))
            return string.Empty;

        return string.Concat(Directory.GetFiles(directory, "app-*.log").Select(ReadSharedText));
    }

    // The provider keeps its writer open, so the file has to be read without demanding exclusivity.
    private static string ReadSharedText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    private static T GetField<T>(object instance, string name)
        => (T)instance.GetType()
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(instance)!;
}
