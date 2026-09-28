using Bsft.Hosting.DependencyInjection;
using Bsft.Hosting.Logging;
using Bsft.Logging;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Bsft.Hosting.Tests.Logging;

public class LoggingTests
{
    [Fact]
    public void AddBsftHostLogging_RegistersLoggingServices()
    {
        var services = new ServiceCollection();
        services.AddBsftHostLogging();

        var provider = services.BuildServiceProvider();
        var loggerFactory = provider.GetService<ILoggerFactory>();

        loggerFactory.Should().NotBeNull();
    }

    [Fact]
    public void AddBsftHostLogging_DefaultLevels_FiltersMicrosoftToWarning()
    {
        var services = new ServiceCollection();
        services.AddBsftHostLogging();

        var provider = services.BuildServiceProvider();
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("Microsoft.Extensions.Something");

        logger.IsEnabled(LogLevel.Information).Should().BeFalse();
        logger.IsEnabled(LogLevel.Warning).Should().BeTrue();
    }

    [Fact]
    public void AddBsftHostLogging_CustomLevel_AppliesCorrectly()
    {
        var services = new ServiceCollection();
        services.AddBsftHostLogging(options =>
        {
            options.MicrosoftLevel = LogLevel.Debug;
        });

        var provider = services.BuildServiceProvider();
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("Microsoft.Extensions.Something");

        logger.IsEnabled(LogLevel.Debug).Should().BeTrue();
    }

    [Fact]
    public void AddBsftHostLogging_JsonFormat_UsesJsonFormatter()
    {
        var services = new ServiceCollection();
        services.AddBsftHostLogging(options =>
        {
            options.UseJsonFormat = true;
        });

        var provider = services.BuildServiceProvider();
        var consoleOptions = provider.GetService<IOptionsMonitor<JsonConsoleFormatterOptions>>();

        consoleOptions.Should().NotBeNull();
        consoleOptions!.CurrentValue.IncludeScopes.Should().BeTrue();
    }

    [Fact]
    public void BsftDefaults_IncludesLogging()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddBsftDefaults();

        var provider = services.BuildServiceProvider();
        var loggerFactory = provider.GetService<ILoggerFactory>();

        loggerFactory.Should().NotBeNull();
    }

    [Fact]
    public void AddBsftHostLogging_ThrowsOnNullServices()
    {
        IServiceCollection services = null!;
        var act = () => services.AddBsftHostLogging();
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddBsftHostLogging_ProvidersDisabled_RegistersNoProvider()
    {
        var services = new ServiceCollection();
        services.AddBsftHostLogging(options =>
        {
            options.EnableConsole = false;
            options.EnableDebug = false;
            options.FileLogging(file => file.EnableFileLogging = false);
        });

        var provider = services.BuildServiceProvider();

        provider.GetServices<ILoggerProvider>().Should().BeEmpty(
            "every provider toggle is off, so the host registers none");
    }

    [Fact]
    public void AddBsftHostLogging_FileLoggingEnabled_RegistersFileProvider()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "bsft-hosting-logging-" + Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        services.AddBsftHostLogging(options =>
        {
            options.EnableConsole = false;
            options.EnableDebug = false;
            options.FileLogging(file => file.LogDirectory = tempDir);
        });

        try
        {
            var provider = services.BuildServiceProvider();

            provider.GetServices<ILoggerProvider>().Should().ContainSingle()
                .Which.Should().BeOfType<BsftFileLoggerProvider>(
                    "the host must use the Bsft.Logging file provider");
            Directory.Exists(tempDir).Should().BeTrue("the file provider creates its log directory");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void AddBsftHostLogging_FileLoggingEnabled_RegistersTheProviderByFactory()
    {
        // Regression: registering a pre-built instance leaves it undisposed, because the container
        // disposes only what it created. The file provider drains its queue in Dispose, so whatever
        // was still queued at shutdown — the shutdown-phase log lines — would never reach the file.
        var services = new ServiceCollection();
        services.AddBsftHostLogging(options =>
        {
            options.EnableConsole = false;
            options.EnableDebug = false;
        });

        var descriptor = services.Single(d => d.ServiceType == typeof(ILoggerProvider));

        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
        descriptor.ImplementationInstance.Should().BeNull("an instance registration is never disposed by the container");
        descriptor.ImplementationFactory.Should().NotBeNull("the container must own the provider to dispose it");
    }

    [Fact]
    public async Task AddBsftHostLogging_ContainerDisposed_DisposesTheFileProvider()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "bsft-hosting-logging-" + Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        services.AddBsftHostLogging(options =>
        {
            options.EnableConsole = false;
            options.EnableDebug = false;
            options.FileLogging(file => file.LogDirectory = tempDir);
        });

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

    [Fact]
    public void FileLogging_ThrowsOnNullConfigure()
    {
        var options = new BsftHostLoggingOptions();

        var act = () => options.FileLogging(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FileLogging_ReturnsSameInstanceForChaining()
    {
        var options = new BsftHostLoggingOptions();

        var returned = options.FileLogging(_ => { });

        returned.Should().BeSameAs(options);
    }

    [Fact]
    public void BuildFileOptions_SeedsMinimumLevelFromDefaultLevel()
    {
        // The host's DefaultLevel must keep reaching the file, which is what the pre-1.3 hosting
        // file logger did by not filtering at provider level at all.
        var options = new BsftHostLoggingOptions { DefaultLevel = LogLevel.Debug };

        var fileOptions = options.BuildFileOptions();

        fileOptions.MinimumLevel.Should().Be(LogLevel.Debug);
    }

    [Fact]
    public void BuildFileOptions_LooserCategoryLevel_WidensTheFileLevel()
    {
        // Regression: a category level looser than DefaultLevel passes the host filter pipeline, so
        // the file provider's own gate must not be stricter or the file would silently miss entries
        // the console shows. The pre-1.3 hosting logger had no provider-level gate at all.
        var options = new BsftHostLoggingOptions { AspNetCoreLevel = LogLevel.Trace };

        var fileOptions = options.BuildFileOptions();

        fileOptions.MinimumLevel.Should().Be(LogLevel.Trace);
    }

    [Fact]
    public void BuildFileOptions_TakesTheMostPermissiveOfEveryCategory()
    {
        // Each category in turn is the most permissive one, so every comparison is exercised.
        var options = new BsftHostLoggingOptions
        {
            DefaultLevel = LogLevel.Information,
            MicrosoftLevel = LogLevel.Debug,
            SystemLevel = LogLevel.Trace,
            AspNetCoreLevel = LogLevel.Warning
        };

        var fileOptions = options.BuildFileOptions();

        fileOptions.MinimumLevel.Should().Be(LogLevel.Trace);
    }

    [Fact]
    public void BuildFileOptions_StricterCategoryLevel_DoesNotNarrowTheFileLevel()
    {
        // The inverse: silencing a category must not silence everything else in the file.
        var options = new BsftHostLoggingOptions
        {
            DefaultLevel = LogLevel.Information,
            MicrosoftLevel = LogLevel.None,
            SystemLevel = LogLevel.None,
            AspNetCoreLevel = LogLevel.None
        };

        var fileOptions = options.BuildFileOptions();

        fileOptions.MinimumLevel.Should().Be(LogLevel.Information);
    }

    [Fact]
    public void BuildFileOptions_ExplicitMinimumLevel_WinsOverDefaultLevel()
    {
        var options = new BsftHostLoggingOptions { DefaultLevel = LogLevel.Debug };
        options.FileLogging(file => file.MinimumLevel = LogLevel.Error);

        var fileOptions = options.BuildFileOptions();

        fileOptions.MinimumLevel.Should().Be(LogLevel.Error);
    }

    [Fact]
    public void BuildFileOptions_WithoutFileConfigure_UsesFileDefaults()
    {
        var options = new BsftHostLoggingOptions();

        var fileOptions = options.BuildFileOptions();

        fileOptions.EnableFileLogging.Should().BeTrue();
        fileOptions.LogDirectory.Should().Be("logs");
        fileOptions.MaxFileSizeMb.Should().Be(10);
        fileOptions.RetainDays.Should().Be(30);
    }


    [Fact]
    public void AddBsftHostLogging_WithConfiguration_BindsSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Hosting:Logging:DefaultLevel"] = "Debug",
                ["Bsft:Hosting:Logging:EnableConsole"] = "false"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddBsftHostLogging(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<BsftHostLoggingOptions>>().Value;

        options.DefaultLevel.Should().Be(LogLevel.Debug);
        options.EnableConsole.Should().BeFalse();
    }

    [Fact]
    public void AddBsftHostLogging_WithNullConfiguration_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftHostLogging(configuration: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void AddBsftHostLogging_WithConfigurationAndNullServices_Throws()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftHostLogging(new ConfigurationBuilder().Build());

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }
}
