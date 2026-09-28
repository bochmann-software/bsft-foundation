using Bsft.Core;
using Bsft.Hosting.DependencyInjection;
using Bsft.Hosting.StartupValidation;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Bsft.Hosting.Tests.StartupValidation;

public class StartupValidationTests
{
    [Fact]
    public async Task ConfigurationValidator_WithValidConfig_ReturnsSuccess()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Server=localhost;Database=test"
            })
            .Build();

        var validator = new ConfigurationValidator(config);
        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ConfigurationValidator_WithEmptyConnectionString_ReturnsFailure()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = ""
            })
            .Build();

        var validator = new ConfigurationValidator(config);
        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("Default");
    }

    [Fact]
    public async Task ConfigurationValidator_WithNoConnectionStrings_ReturnsSuccess()
    {
        var config = new ConfigurationBuilder().Build();

        var validator = new ConfigurationValidator(config);
        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task StartupValidationHostedService_WithPassingValidators_Succeeds()
    {
        var validator = Substitute.For<IStartupValidator>();
        validator.ValidateAsync(Arg.Any<CancellationToken>())
            .Returns(StartupValidationResult.Success());

        var logger = Substitute.For<ILogger<StartupValidationHostedService>>();
        var service = new StartupValidationHostedService([validator], logger);

        var act = () => service.StartAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartupValidationHostedService_WithFailingValidator_Throws()
    {
        var validator = Substitute.For<IStartupValidator>();
        validator.ValidateAsync(Arg.Any<CancellationToken>())
            .Returns(StartupValidationResult.Failure("Test error"));

        var logger = Substitute.For<ILogger<StartupValidationHostedService>>();
        var service = new StartupValidationHostedService([validator], logger);

        var act = () => service.StartAsync(CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Test error*");
    }

    [Fact]
    public async Task StartupValidationHostedService_WithMultipleErrors_IncludesAll()
    {
        var validator1 = Substitute.For<IStartupValidator>();
        validator1.ValidateAsync(Arg.Any<CancellationToken>())
            .Returns(StartupValidationResult.Failure("Error 1"));

        var validator2 = Substitute.For<IStartupValidator>();
        validator2.ValidateAsync(Arg.Any<CancellationToken>())
            .Returns(StartupValidationResult.Failure("Error 2"));

        var logger = Substitute.For<ILogger<StartupValidationHostedService>>();
        var service = new StartupValidationHostedService([validator1, validator2], logger);

        var act = () => service.StartAsync(CancellationToken.None);
        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Error 1");
        exception.Which.Message.Should().Contain("Error 2");
    }

    [Fact]
    public void StartupValidationResult_Success_IsValid()
    {
        var result = StartupValidationResult.Success();

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void StartupValidationResult_Failure_IsNotValid()
    {
        var result = StartupValidationResult.Failure("Something failed");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Be("Something failed");
    }

    [Fact]
    public void AddBsftStartupValidation_RegistersConfigurationValidatorAndHostedService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddBsftStartupValidation();

        var provider = services.BuildServiceProvider();
        var validators = provider.GetServices<IStartupValidator>();

        validators.Should().ContainSingle()
            .Which.Should().BeOfType<ConfigurationValidator>();
    }

    [Fact]
    public void AddBsftStartupValidation_NullServices_ThrowsArgumentNullException()
    {
        // Exercises ValidationServiceCollectionExtensions.AddBsftStartupValidation line 20.
        IServiceCollection services = null!;

        var act = () => services.AddBsftStartupValidation();

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public async Task StartupValidationHostedService_WithNoValidators_LogsSkipped()
    {
        // Exercises StartupValidationHostedService.StartAsync lines 38-40: validators.Count == 0 branch.
        var logger = Substitute.For<ILogger<StartupValidationHostedService>>();
        var service = new StartupValidationHostedService([], logger);

        var act = () => service.StartAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartupValidationHostedService_StopAsync_ReturnsCompletedTask()
    {
        // Exercises StartupValidationHostedService.StopAsync line 82.
        var logger = Substitute.For<ILogger<StartupValidationHostedService>>();
        var service = new StartupValidationHostedService([], logger);

        var act = () => service.StopAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartupValidationHostedService_WithAPassingValidatorReportingWarnings_LogsThemAsWarnings()
    {
        // A validator reporting a finding needs no logger of its own: the warnings on its result are
        // what the runner surfaces, and startup still succeeds.
        var validator = Substitute.For<IStartupValidator>();
        validator.ValidateAsync(Arg.Any<CancellationToken>())
            .Returns(StartupValidationResult.Success("Checked.", ["a permission nothing enforces"]));

        var logger = new RecordingLogger<StartupValidationHostedService>();
        var service = new StartupValidationHostedService([validator], logger);

        await service.StartAsync(CancellationToken.None);

        logger.Warnings.Should().ContainSingle()
            .Which.Should().Contain("a permission nothing enforces");
    }

    [Fact]
    public async Task StartupValidationHostedService_WithAFailingValidatorReportingWarnings_LogsThemToo()
    {
        var failing = Substitute.For<IStartupValidator>();
        failing.ValidateAsync(Arg.Any<CancellationToken>()).Returns(StartupValidationResult.Failure("Error 1"));

        var warning = Substitute.For<IStartupValidator>();
        warning.ValidateAsync(Arg.Any<CancellationToken>())
            .Returns(StartupValidationResult.Success(null, ["still worth saying"]));

        var logger = new RecordingLogger<StartupValidationHostedService>();
        var service = new StartupValidationHostedService([failing, warning], logger);

        var act = () => service.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        logger.Warnings.Should().ContainSingle().Which.Should().Contain("still worth saying");
    }

    [Fact]
    public void StartupValidationResult_SuccessWithWarnings_StaysValid()
    {
        var result = StartupValidationResult.Success("Checked.", ["look at this"]);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Warnings.Should().ContainSingle().Which.Should().Be("look at this");
    }

    [Fact]
    public void StartupValidationResult_SuccessWithoutWarnings_HasNone()
    {
        StartupValidationResult.Success("Checked.").Warnings.Should().BeEmpty();
        StartupValidationResult.Failure("Broken").Warnings.Should().BeEmpty();
    }

    [Fact]
    public void StartupValidationResult_SuccessWithNullWarnings_Throws()
    {
        var act = () => StartupValidationResult.Success("Checked.", null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("warnings");
    }

    [Fact]
    public async Task StartupValidationHostedService_WhenAValidatorThrows_ReportsANamedFailureInsteadOfCrashing()
    {
        // A validator may resolve or construct its own scoped dependencies lazily inside ValidateAsync
        // (TemplateContractStartupValidator's template sources, for instance) rather than through its
        // own constructor — that failure must become a named Failure result, not an unhandled exception
        // that skips every other registered validator.
        var throwing = Substitute.For<IStartupValidator>();
        throwing.ValidateAsync(Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("a scoped dependency's construction blew up"));

        var passing = Substitute.For<IStartupValidator>();
        passing.ValidateAsync(Arg.Any<CancellationToken>()).Returns(StartupValidationResult.Success());

        var logger = new RecordingLogger<StartupValidationHostedService>();
        var service = new StartupValidationHostedService([throwing, passing], logger);

        var act = () => service.StartAsync(CancellationToken.None);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain(nameof(InvalidOperationException))
            .And.Contain("a scoped dependency's construction blew up");
        await passing.Received(1).ValidateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartupValidationHostedService_WhenAValidatorIsCancelled_PropagatesTheCancellation()
    {
        var cancelling = Substitute.For<IStartupValidator>();
        cancelling.ValidateAsync(Arg.Any<CancellationToken>()).Throws(new OperationCanceledException());

        var logger = Substitute.For<ILogger<StartupValidationHostedService>>();
        var service = new StartupValidationHostedService([cancelling], logger);

        var act = () => service.StartAsync(CancellationToken.None);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
