using Bsft.Core.Persistence;
using Bsft.Mediator;
using Bsft.Resilience.Mediator.DependencyInjection;
using Bsft.Resilience.Mediator.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Resilience.Mediator.Tests;

/// <summary>
/// Retrying every command is refused at startup unless every command is a transaction of its own and
/// an ambient transaction gives every further attempt a fresh scope.
/// </summary>
public sealed class ResilienceMediatorStartupValidatorTests
{
    [Fact]
    public async Task ValidateAsync_WithoutTheRetryPolicy_Succeeds()
    {
        var result = await Validator(isRetryingAllCommands: false, Registrations(hasAmbientTransaction: false)).ValidateAsync();

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WhenEveryCommandIsATransactionOfTheAmbientOne_Succeeds()
    {
        var result = await Validator(isRetryingAllCommands: true, Registrations(hasAmbientTransaction: true), new Policy(true))
            .ValidateAsync();

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WhenNotEveryCommandIsATransaction_Fails()
    {
        var registrations = Registrations(hasAmbientTransaction: true);

        var withoutMediator = await Validator(isRetryingAllCommands: true, registrations).ValidateAsync();
        var withoutTheTransactionPolicy = await Validator(isRetryingAllCommands: true, registrations, new Policy(false))
            .ValidateAsync();

        withoutMediator.IsValid.Should().BeFalse();
        withoutTheTransactionPolicy.Errors.Should().ContainSingle().Which.Should().Contain("ApplyTransactionToAllCommands");
    }

    [Fact]
    public async Task ValidateAsync_WhenNoAmbientTransactionIsRegistered_FailsNamingTheRegistrations()
    {
        var result = await Validator(isRetryingAllCommands: true, Registrations(hasAmbientTransaction: false), new Policy(true))
            .ValidateAsync();

        result.Errors.Should().ContainSingle().Which.Should()
            .Contain("IAmbientTransaction")
            .And.Contain("AddBsftDataSharedTransaction()")
            .And.Contain("AddBsftDataPostgresTransaction()");
    }

    [Fact]
    public void Constructor_WithANullArgument_Throws()
    {
        var options = Options.Create(new BsftResilienceMediatorOptions());
        var registrations = Registrations(hasAmbientTransaction: false);

        FluentActions.Invoking(() => new ResilienceMediatorStartupValidator(null!, [], registrations))
            .Should().Throw<ArgumentNullException>().WithParameterName("options");
        FluentActions.Invoking(() => new ResilienceMediatorStartupValidator(options, null!, registrations))
            .Should().Throw<ArgumentNullException>().WithParameterName("policies");
        FluentActions.Invoking(() => new ResilienceMediatorStartupValidator(options, [], null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("registrations");
    }

    [Fact]
    public void AddBsftResilienceMediator_RegistersTheValidatorOnce()
    {
        var services = new ServiceCollection();
        services.AddBsftResilienceMediator();
        services.AddBsftResilienceMediator();
        using var provider = services.BuildServiceProvider();

        provider.GetServices<Bsft.Core.IStartupValidator>().Should().ContainSingle()
            .Which.Should().BeOfType<ResilienceMediatorStartupValidator>();
    }

    private static ResilienceMediatorStartupValidator Validator(
        bool isRetryingAllCommands, IServiceProviderIsService registrations, params IMediatorTransactionPolicy[] policies) =>
        new(Options.Create(new BsftResilienceMediatorOptions { ShouldRetryAllCommands = isRetryingAllCommands }), policies, registrations);

    private static IServiceProviderIsService Registrations(bool hasAmbientTransaction)
    {
        var services = new ServiceCollection();
        if (hasAmbientTransaction)
            services.AddScoped<IAmbientTransaction, FakeAmbientTransaction>();

        return services.BuildServiceProvider().GetRequiredService<IServiceProviderIsService>();
    }

    private sealed class Policy(bool shouldApplyTransactionToAllCommands) : IMediatorTransactionPolicy
    {
        public bool ShouldApplyTransactionToAllCommands => shouldApplyTransactionToAllCommands;
    }
}
