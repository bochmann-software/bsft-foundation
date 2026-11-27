using Bsft.Logging.DependencyInjection;
using Bsft.Logging.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Logging.Tests.DependencyInjection;

public class FileLoggerConfigurationTests
{
    private static IConfigurationSection Section(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection(BsftLoggingOptions.SectionName);

    private static ServiceProvider Build(IConfiguration section)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddBsftFileLogger(section));
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddBsftFileLogger_NullBuilder_ThrowsArgumentNullException()
    {
        ILoggingBuilder builder = null!;

        var act = () => builder.AddBsftFileLogger(Section([]));

        act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }

    [Fact]
    public void AddBsftFileLogger_NullSection_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        IConfiguration section = null!;

        var act = () => services.AddLogging(builder => builder.AddBsftFileLogger(section));

        act.Should().Throw<ArgumentNullException>().WithParameterName("section");
    }

    [Fact]
    public void AddBsftFileLogger_EnabledSection_WritesFileToConfiguredDirectory()
    {
        using var fixture = new LogFileFixture();
        var section = Section(new()
        {
            ["Bsft:Logging:EnableFileLogging"] = "true",
            ["Bsft:Logging:LogDirectory"] = fixture.LogDirectory,
        });
        var provider = Build(section);

        provider.GetRequiredService<ILoggerFactory>().CreateLogger("Test").LogWarning("written by section");
        provider.Dispose();

        Directory.GetFiles(fixture.LogDirectory, "*.log")
            .Select(File.ReadAllText).Should().ContainSingle().Which.Should().Contain("written by section");
    }

    [Fact]
    public void AddBsftFileLogger_DisabledSection_RegistersNothing()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddBsftFileLogger(Section(new() { ["Bsft:Logging:EnableFileLogging"] = "false" })));

        services.Should().NotContain(sd => sd.ServiceType == typeof(ILoggerProvider));
    }

    [Fact]
    public void AddBsftFileLogger_MissingSection_RegistersNothingAndDoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddBsftFileLogger(Section([])));

        services.Should().NotContain(sd => sd.ServiceType == typeof(ILoggerProvider));
    }

    [Fact]
    public void AddBsftFileLogger_InvalidOptions_FailStartupValidationWithClearMessage()
    {
        var provider = Build(Section(new()
        {
            ["Bsft:Logging:EnableFileLogging"] = "true",
            ["Bsft:Logging:MaxFileSizeMb"] = "0",
        }));

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>().WithMessage("*MaxFileSizeMb*");
    }

    [Fact]
    public void AddBsftFileLogger_Section_BindsEveryOptionName()
    {
        using var fixture = new LogFileFixture();
        var provider = Build(Section(new()
        {
            ["Bsft:Logging:EnableFileLogging"] = "true",
            ["Bsft:Logging:LogDirectory"] = fixture.LogDirectory,
            ["Bsft:Logging:MaxFileSizeMb"] = "7",
            ["Bsft:Logging:RetainDays"] = "3",
            ["Bsft:Logging:EnableJsonFormat"] = "true",
            ["Bsft:Logging:MinimumLevel"] = "Warning",
            ["Bsft:Logging:LogReaderPollInterval"] = "00:00:02",
            ["Bsft:Logging:MaxEntryBytes"] = "4096",
            ["Bsft:Logging:MaxRetainedFiles"] = "10",
            ["Bsft:Logging:MaxTotalSizeMb"] = "100",
        }));

        var options = provider.GetRequiredService<IOptions<BsftLoggingOptions>>().Value;
        provider.Dispose();

        options.LogDirectory.Should().Be(fixture.LogDirectory);
        options.MaxFileSizeMb.Should().Be(7);
        options.RetainDays.Should().Be(3);
        options.EnableJsonFormat.Should().BeTrue();
        options.MinimumLevel.Should().Be(LogLevel.Warning);
        options.LogReaderPollInterval.Should().Be(TimeSpan.FromSeconds(2));
        options.MaxEntryBytes.Should().Be(4096);
        options.MaxRetainedFiles.Should().Be(10);
        options.MaxTotalSizeMb.Should().Be(100);
    }

    [Fact]
    public void AddBsftFileLogger_ActionOverload_StillRegistersProviderByFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddBsftFileLogger(options => options.RetainDays = 5));

        var descriptor = services.Single(sd => sd.ServiceType == typeof(ILoggerProvider));

        descriptor.ImplementationFactory.Should().NotBeNull();
    }

    [Fact]
    public void AddBsftFileLogger_Section_LeavesOtherProvidersUntouched()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().AddBsftFileLogger(Section([])));

        services.Count(sd => sd.ServiceType == typeof(ILoggerProvider)).Should().Be(1);
    }
}
