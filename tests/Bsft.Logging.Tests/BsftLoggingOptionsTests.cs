using Bsft.Logging.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Logging.Tests;

public class BsftLoggingOptionsTests
{
    [Fact]
    public void Defaults_BoundReaderEntrySizeToOneMebibyte()
    {
        new BsftLoggingOptions().MaxEntryBytes.Should().Be(1024 * 1024);
    }

    [Fact]
    public void SectionName_IsTheDocumentedSection()
    {
        BsftLoggingOptions.SectionName.Should().Be("Bsft:Logging");
    }

    [Fact]
    public void AddBsftLogging_BindsTheConfigurationSection()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Bsft:Logging:LogDirectory"] = "bound-logs",
            ["Bsft:Logging:MaxFileSizeMb"] = "7",
            ["Bsft:Logging:RetainDays"] = "3",
            ["Bsft:Logging:EnableJsonFormat"] = "true",
            ["Bsft:Logging:MinimumLevel"] = "Warning",
            ["Bsft:Logging:LogReaderPollInterval"] = "00:00:02",
            ["Bsft:Logging:MaxEntryBytes"] = "4096",
        });

        var services = new ServiceCollection();
        services.AddBsftLogging(configuration);

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<BsftLoggingOptions>>().Value;

        options.LogDirectory.Should().Be("bound-logs");
        options.MaxFileSizeMb.Should().Be(7);
        options.RetainDays.Should().Be(3);
        options.EnableJsonFormat.Should().BeTrue();
        options.MinimumLevel.Should().Be(LogLevel.Warning);
        options.LogReaderPollInterval.Should().Be(TimeSpan.FromSeconds(2));
        options.MaxEntryBytes.Should().Be(4096);
    }

    [Fact]
    public void AddBsftLogging_NonPositiveReaderPollInterval_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftLogging(options => options.LogReaderPollInterval = TimeSpan.Zero);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*LogReaderPollInterval*positive*");
    }

    [Fact]
    public void AddBsftLogging_NonPositiveMaximumEntryBytes_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftLogging(options => options.MaxEntryBytes = 0);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MaxEntryBytes*positive*");
    }

    [Fact]
    public void AddBsftLogging_ConfigureRunsAfterTheSection()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Bsft:Logging:LogDirectory"] = "bound-logs",
        });

        var services = new ServiceCollection();
        services.AddBsftLogging(configuration, options => options.LogDirectory = "overridden");

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<BsftLoggingOptions>>().Value;

        options.LogDirectory.Should().Be("overridden");
    }

    [Fact]
    public void AddBsftLogging_NullConfiguration_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftLogging((IConfiguration)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void AddBsftLogging_NullServicesWithConfiguration_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftLogging(Configuration([]));

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddBsftLogging_RegistersTheOptionsThroughTheOptionsPattern()
    {
        var services = new ServiceCollection();
        services.AddBsftLogging(options => options.LogDirectory = "from-delegate");

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<BsftLoggingOptions>>().Value;

        options.LogDirectory.Should().Be("from-delegate");
        services.Should().NotContain(
            sd => sd.ServiceType == typeof(BsftLoggingOptions),
            "the options instance itself is never registered");
    }

    [Theory]
    [InlineData("", nameof(BsftLoggingOptions.LogDirectory))]
    [InlineData("   ", nameof(BsftLoggingOptions.LogDirectory))]
    public void AddBsftLogging_BlankDirectory_Throws(string directory, string member)
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftLogging(options => options.LogDirectory = directory);

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{member}*");
    }

    [Fact]
    public void AddBsftLogging_NullDirectory_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftLogging(options => options.LogDirectory = null!);

        act.Should().Throw<InvalidOperationException>().WithMessage("*LogDirectory*<null>*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1025)]
    public void AddBsftLogging_UnusableFileSize_Throws(int megabytes)
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftLogging(options => options.MaxFileSizeMb = megabytes);

        act.Should().Throw<InvalidOperationException>().WithMessage("*MaxFileSizeMb*");
    }

    [Fact]
    public void AddBsftLogging_MaximumSequenceCompatibleFileSize_IsAccepted()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftLogging(options => options.MaxFileSizeMb = 1024);

        act.Should().NotThrow();
    }

    [Fact]
    public void AddBsftLogging_NonPositiveRetention_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftLogging(options => options.RetainDays = 0);

        act.Should().Throw<InvalidOperationException>().WithMessage("*RetainDays*");
    }

    [Fact]
    public void AddBsftLogging_RetentionBeyondTheLimit_Throws()
    {
        // A retention window of more than ten years is a mistake, and left unbounded it turns into a
        // cutoff date that cannot be represented, which would fail the provider's construction.
        var services = new ServiceCollection();

        var act = () => services.AddBsftLogging(options => options.RetainDays = 3651);

        act.Should().Throw<InvalidOperationException>().WithMessage("*RetainDays*");
    }

    [Fact]
    public void AddBsftLogging_FileLoggingOff_SkipsTheFileRules()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftLogging(options =>
        {
            options.EnableFileLogging = false;
            options.LogDirectory = string.Empty;
            options.MaxFileSizeMb = 0;
            options.RetainDays = 0;
        });

        act.Should().NotThrow();
    }

    [Fact]
    public void Validation_ValidOptions_Succeeds()
    {
        var validation = new BsftLoggingOptionsValidator();

        validation.Validate(name: null, new BsftLoggingOptions()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validation_UnusableOptions_FailsNamingTheMember()
    {
        var validation = new BsftLoggingOptionsValidator();

        var result = validation.Validate(name: null, new BsftLoggingOptions { RetainDays = 0 });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(BsftLoggingOptions.RetainDays));
    }

    [Fact]
    public void Validation_RetentionBeyondTheLimit_FailsNamingTheMember()
    {
        var validation = new BsftLoggingOptionsValidator();

        var result = validation.Validate(name: null, new BsftLoggingOptions { RetainDays = 3651 });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(BsftLoggingOptions.RetainDays));
    }

    [Fact]
    public void Validation_NullOptions_ThrowsArgumentNullException()
    {
        var validation = new BsftLoggingOptionsValidator();

        var act = () => validation.Validate(name: null, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void ValidateOnStart_UnusableOptions_FailsWhenTheOptionsAreRead()
    {
        // The registration call validates what it was given, so an unusable value can only reach the
        // start-time check by arriving from a source the call did not see — a reloaded section.
        var services = new ServiceCollection();
        services.AddBsftLogging();
        services.Configure<BsftLoggingOptions>(options => options.RetainDays = 0);

        var act = () => services.BuildServiceProvider().GetRequiredService<IOptions<BsftLoggingOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*RetainDays*");
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
