using Bsft.Core;
using Bsft.Mediator.DependencyInjection;
using Bsft.Mediator.StartupValidation;
using Bsft.Mediator.Transactions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests.Transactions;

/// <summary>
/// Asking for every command to be a transaction fails startup when the behavior that opens them is
/// missing from the pipeline.
/// </summary>
public sealed class TransactionPolicyStartupValidatorTests
{
    [Fact]
    public async Task ValidateAsync_WithoutThePolicy_Succeeds()
    {
        var result = await Validator(new MediatorConfiguration(), new ServiceCollection()).ValidateAsync();

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WithThePolicyAndTheBehavior_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        var result = await Validator(new MediatorConfiguration().ApplyTransactionToAllCommands(), services).ValidateAsync();

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WithThePolicyButWithoutTheBehavior_Fails()
    {
        var result = await Validator(new MediatorConfiguration().ApplyTransactionToAllCommands(), new ServiceCollection()).ValidateAsync();

        result.Errors.Should().ContainSingle().Which.Should().Contain("TransactionBehavior");
    }

    [Fact]
    public void Constructor_WithANullArgument_Throws()
    {
        var registrations = new PipelineBehaviorRegistrations(new ServiceCollection());

        FluentActions.Invoking(() => new TransactionPolicyStartupValidator(null!, registrations))
            .Should().Throw<ArgumentNullException>().WithParameterName("configuration");
        FluentActions.Invoking(() => new TransactionPolicyStartupValidator(new MediatorConfiguration(), null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("registrations");
    }

    [Fact]
    public async Task AddBsftMediator_WithThePolicyAndNoBehavior_RegistersAFailingValidator()
    {
        var services = new ServiceCollection();
        services.AddBsftMediator(mediator => mediator.ApplyTransactionToAllCommands());
        await using var provider = services.BuildServiceProvider();

        var validator = provider.GetServices<IStartupValidator>().OfType<TransactionPolicyStartupValidator>().Single();

        (await validator.ValidateAsync()).IsValid.Should().BeFalse();
    }

    private static TransactionPolicyStartupValidator Validator(MediatorConfiguration configuration, IServiceCollection services) =>
        new(configuration, new PipelineBehaviorRegistrations(services));
}
