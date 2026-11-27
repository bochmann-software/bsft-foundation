using Bsft.Caching.Mediator;
using Bsft.Core;
using Bsft.Mediator.DependencyInjection;
using Bsft.Mediator.StartupValidation;
using Bsft.Mediator.Tests.Support;
using Bsft.Mediator.Transactions;
using Bsft.Resilience.Mediator;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>
/// <see cref="MediatorConfiguration.ReplaceDefaultBehavior(string, Type, DefaultBehaviorPlacement)"/>: where a
/// slot replacement runs, and that it stays the slot's behavior whatever the placement.
/// </summary>
public class MediatorConfigurationReplaceDefaultBehaviorPlacementTests
{
    [Fact]
    public void ReplaceDefaultBehavior_AfterDefaults_RunsInsideRetryAndTransactionAmongTheAddedBehaviors()
    {
        var services = new ServiceCollection().AddDefaultBehaviorDependencies();

        services.AddBsftMediator(mediator => mediator
            .UseDefaults()
            .AddBehavior(typeof(FirstAddedBehavior<,>))
            .ReplaceDefaultBehavior(MediatorDefaults.Authorization, typeof(AccessProbeBehavior<,>), DefaultBehaviorPlacement.AfterDefaults)
            .AddBehavior(typeof(SecondAddedBehavior<,>)));

        var chain = ResolveChain(services);

        chain.Should().Equal(
            typeof(TracingBehavior<,>),
            typeof(ValidationBehavior<,>),
            typeof(RetryBehavior<,>),
            typeof(TransactionBehavior<,>),
            typeof(CachingBehavior<,>),
            typeof(FirstAddedBehavior<,>),
            typeof(AccessProbeBehavior<,>),
            typeof(SecondAddedBehavior<,>));
    }

    [Fact]
    public void ReplaceDefaultBehavior_AfterDefaultsCalledBeforeAddBehavior_RunsAheadOfTheAddedBehavior()
    {
        // The counter-check to the call order: the same calls swapped swap the two behaviors, and both stay
        // behind the default chain.
        var services = new ServiceCollection().AddDefaultBehaviorDependencies();

        services.AddBsftMediator(mediator => mediator
            .ReplaceDefaultBehavior(MediatorDefaults.Authorization, typeof(AccessProbeBehavior<,>), DefaultBehaviorPlacement.AfterDefaults)
            .AddBehavior(typeof(FirstAddedBehavior<,>))
            .UseDefaults());

        var chain = ResolveChain(services);

        chain.Should().Equal(
            typeof(TracingBehavior<,>),
            typeof(ValidationBehavior<,>),
            typeof(RetryBehavior<,>),
            typeof(TransactionBehavior<,>),
            typeof(CachingBehavior<,>),
            typeof(AccessProbeBehavior<,>),
            typeof(FirstAddedBehavior<,>));
    }

    [Fact]
    public void ReplaceDefaultBehavior_InSlot_KeepsTheReplacementAtTheSlotOutsideRetryAndTransaction()
    {
        // The old default, unchanged: the same configuration with InSlot puts the replacement into the
        // authorization slot, right behind tracing, and ahead of the added behavior.
        var services = new ServiceCollection().AddDefaultBehaviorDependencies();

        services.AddBsftMediator(mediator => mediator
            .UseDefaults()
            .AddBehavior(typeof(FirstAddedBehavior<,>))
            .ReplaceDefaultBehavior(MediatorDefaults.Authorization, typeof(AccessProbeBehavior<,>), DefaultBehaviorPlacement.InSlot));

        var chain = ResolveChain(services);

        chain.Should().Equal(
            typeof(TracingBehavior<,>),
            typeof(AccessProbeBehavior<,>),
            typeof(ValidationBehavior<,>),
            typeof(RetryBehavior<,>),
            typeof(TransactionBehavior<,>),
            typeof(CachingBehavior<,>),
            typeof(FirstAddedBehavior<,>));
    }

    [Fact]
    public void ReplaceDefaultBehavior_WithoutPlacement_BehavesAsInSlot()
    {
        var withoutPlacement = new ServiceCollection().AddDefaultBehaviorDependencies();
        withoutPlacement.AddBsftMediator(mediator => mediator
            .UseDefaults()
            .AddBehavior(typeof(FirstAddedBehavior<,>))
            .ReplaceDefaultBehavior(MediatorDefaults.Authorization, typeof(AccessProbeBehavior<,>)));

        var inSlot = new ServiceCollection().AddDefaultBehaviorDependencies();
        inSlot.AddBsftMediator(mediator => mediator
            .UseDefaults()
            .AddBehavior(typeof(FirstAddedBehavior<,>))
            .ReplaceDefaultBehavior(MediatorDefaults.Authorization, typeof(AccessProbeBehavior<,>), DefaultBehaviorPlacement.InSlot));

        ResolveChain(withoutPlacement).Should().Equal(ResolveChain(inSlot));
    }

    [Fact]
    public void ReplaceDefaultBehavior_AfterDefaultsWithDefaultsRegisteredFirstByTheirPackages_StaysBehindThem()
    {
        // The behavior packages may register their own behavior before AddBsftMediator; the replacement still
        // follows the registration of the default chain, and the defaults are not registered twice.
        var services = new ServiceCollection().AddDefaultBehaviorDependencies();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(RetryBehavior<,>));

        services.AddBsftMediator(mediator => mediator
            .UseDefaults()
            .ReplaceDefaultBehavior(MediatorDefaults.Authorization, typeof(AccessProbeBehavior<,>), DefaultBehaviorPlacement.AfterDefaults));

        var chain = ResolveChain(services);

        chain.Should().Equal(
            typeof(TracingBehavior<,>),
            typeof(ValidationBehavior<,>),
            typeof(RetryBehavior<,>),
            typeof(TransactionBehavior<,>),
            typeof(CachingBehavior<,>),
            typeof(AccessProbeBehavior<,>));
    }

    [Fact]
    public void ReplaceDefaultBehavior_AfterDefaultsWithTheTypeAlreadyRegistered_RegistersItOnce()
    {
        var services = new ServiceCollection().AddDefaultBehaviorDependencies();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AccessProbeBehavior<,>));

        services.AddBsftMediator(mediator => mediator
            .UseDefaults()
            .ReplaceDefaultBehavior(MediatorDefaults.Authorization, typeof(AccessProbeBehavior<,>), DefaultBehaviorPlacement.AfterDefaults));

        services.Count(descriptor => descriptor.ImplementationType == typeof(AccessProbeBehavior<,>))
            .Should().Be(1);
    }

    [Fact]
    public void ReplaceDefaultBehavior_AfterDefaultsWithoutUseDefaults_RegistersNothing()
    {
        // A replacement shapes the default chain, whatever its placement; AddBehavior, by contrast, registers.
        var services = new ServiceCollection();

        services.AddBsftMediator(mediator => mediator
            .AddBehavior(typeof(FirstAddedBehavior<,>))
            .ReplaceDefaultBehavior(MediatorDefaults.Authorization, typeof(AccessProbeBehavior<,>), DefaultBehaviorPlacement.AfterDefaults));

        services.Should().NotContain(descriptor => descriptor.ImplementationType == typeof(AccessProbeBehavior<,>));
        services.Should().ContainSingle(descriptor => descriptor.ImplementationType == typeof(FirstAddedBehavior<,>));
    }

    [Fact]
    public void ReplaceDefaultBehavior_AfterDefaults_TakesTheSlotOutOfTheCanonicalRanks()
    {
        var configuration = new MediatorConfiguration()
            .ReplaceDefaultBehavior(MediatorDefaults.Caching, typeof(AccessProbeBehavior<,>), DefaultBehaviorPlacement.AfterDefaults);

        var descriptor = configuration.Catalog.FindSlot(MediatorDefaults.Caching)!;
        descriptor.Placement.Should().Be(DefaultBehaviorPlacement.AfterDefaults);
        descriptor.TypeName.Should().Be(typeof(AccessProbeBehavior<,>).AssemblyQualifiedName);
        descriptor.RegistrationCall.Should().Be(
            "ReplaceDefaultBehavior(MediatorDefaults.Caching, typeof(AccessProbeBehavior<,>), DefaultBehaviorPlacement.AfterDefaults)");
        configuration.Catalog.ResolveRanks().Should().NotContainKey(typeof(AccessProbeBehavior<,>));
        configuration.Catalog.ResolveRanks().Should().NotContainKey(typeof(CachingBehavior<,>));
        configuration.Catalog.Available([]).Should().Contain(slot => slot.Value == typeof(AccessProbeBehavior<,>));
        configuration.Catalog.AvailableInSlot([]).Should().NotContain(slot => slot.Value == typeof(AccessProbeBehavior<,>));
    }

    [Fact]
    public void ReplaceDefaultBehavior_InSlot_KeepsTheSlotRanked()
    {
        var configuration = new MediatorConfiguration()
            .ReplaceDefaultBehavior(MediatorDefaults.Caching, typeof(AccessProbeBehavior<,>), DefaultBehaviorPlacement.InSlot);

        var descriptor = configuration.Catalog.FindSlot(MediatorDefaults.Caching)!;
        descriptor.Placement.Should().Be(DefaultBehaviorPlacement.InSlot);
        descriptor.RegistrationCall.Should().Be(
            "ReplaceDefaultBehavior(MediatorDefaults.Caching, typeof(AccessProbeBehavior<,>))");
        configuration.Catalog.ResolveRanks().Should().ContainKey(typeof(AccessProbeBehavior<,>));
        configuration.Catalog.AvailableInSlot([]).Should().Contain(slot => slot.Value == typeof(AccessProbeBehavior<,>));
    }

    [Fact]
    public void ReplaceDefaultBehavior_AfterDefaultsWithUnknownName_ThrowsAndRecordsNothing()
    {
        var configuration = new MediatorConfiguration();

        var act = () => configuration.ReplaceDefaultBehavior("UnknownBehavior", typeof(AccessProbeBehavior<,>), DefaultBehaviorPlacement.AfterDefaults);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("name")
            .WithMessage("*is not a default pipeline behavior*");
        configuration.AddedBehaviors.Should().BeEmpty();
        configuration.ReplacedDefaults.Should().BeEmpty();
    }

    [Fact]
    public void ReplaceDefaultBehavior_AfterDefaultsWithClosedType_ThrowsAndRecordsNothing()
    {
        var configuration = new MediatorConfiguration();

        var act = () => configuration.ReplaceDefaultBehavior(
            MediatorDefaults.Authorization, typeof(AccessProbeBehavior<TestCommand, string>), DefaultBehaviorPlacement.AfterDefaults);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("behaviorType")
            .WithMessage("*must be an open generic type definition*");
        configuration.AddedBehaviors.Should().BeEmpty();
    }

    [Fact]
    public void ReplaceDefaultBehavior_WithNullArguments_ThrowsForTheNullOne()
    {
        var configuration = new MediatorConfiguration();

        var nullName = () => configuration.ReplaceDefaultBehavior(null!, typeof(AccessProbeBehavior<,>), DefaultBehaviorPlacement.AfterDefaults);
        var nullType = () => configuration.ReplaceDefaultBehavior(MediatorDefaults.Authorization, null!, DefaultBehaviorPlacement.AfterDefaults);

        nullName.Should().Throw<ArgumentNullException>().WithParameterName("name");
        nullType.Should().Throw<ArgumentNullException>().WithParameterName("behaviorType");
    }

    [Fact]
    public void ReplaceDefaultBehavior_WithUndefinedPlacement_ThrowsAndRecordsNothing()
    {
        var configuration = new MediatorConfiguration();

        var act = () => configuration.ReplaceDefaultBehavior(
            MediatorDefaults.Authorization, typeof(AccessProbeBehavior<,>), (DefaultBehaviorPlacement)2);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("placement");
        configuration.ReplacedDefaults.Should().BeEmpty();
        configuration.AddedBehaviors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(DefaultBehaviorPlacement.InSlot, DefaultBehaviorPlacement.AfterDefaults)]
    [InlineData(DefaultBehaviorPlacement.AfterDefaults, DefaultBehaviorPlacement.InSlot)]
    [InlineData(DefaultBehaviorPlacement.AfterDefaults, DefaultBehaviorPlacement.AfterDefaults)]
    public void ReplaceDefaultBehavior_TwiceOnSameSlot_ThrowsWhateverThePlacement(
        DefaultBehaviorPlacement first, DefaultBehaviorPlacement second)
    {
        var configuration = new MediatorConfiguration()
            .ReplaceDefaultBehavior(MediatorDefaults.Authorization, typeof(AccessProbeBehavior<,>), first);

        var act = () => configuration.ReplaceDefaultBehavior(MediatorDefaults.Authorization, typeof(FirstAddedBehavior<,>), second);

        act.Should().Throw<InvalidOperationException>().WithMessage("*already replaced*");
        configuration.AddedBehaviors.Should().HaveCount(first == DefaultBehaviorPlacement.AfterDefaults ? 1 : 0);
    }

    [Fact]
    public void ReplaceDefaultBehavior_AfterDefaultsOfASuppressedSlot_Throws()
    {
        var configuration = new MediatorConfiguration().SuppressDefaultBehavior(MediatorDefaults.Authorization);

        var act = () => configuration.ReplaceDefaultBehavior(
            MediatorDefaults.Authorization, typeof(AccessProbeBehavior<,>), DefaultBehaviorPlacement.AfterDefaults);

        act.Should().Throw<InvalidOperationException>().WithMessage("*is suppressed*");
        configuration.AddedBehaviors.Should().BeEmpty();
    }

    [Fact]
    public void SuppressDefaultBehavior_OfASlotReplacedAfterTheDefaults_Throws()
    {
        var configuration = new MediatorConfiguration()
            .ReplaceDefaultBehavior(MediatorDefaults.Authorization, typeof(AccessProbeBehavior<,>), DefaultBehaviorPlacement.AfterDefaults);

        var act = () => configuration.SuppressDefaultBehavior(MediatorDefaults.Authorization);

        act.Should().Throw<InvalidOperationException>().WithMessage("*is replaced*");
    }

    [Fact]
    public async Task ValidateAsync_WithAnAfterDefaultsReplacementMissingItsDependency_NamesTheReplacementCall()
    {
        // The startup validator checks the replacement like every default behavior, whatever its placement.
        var services = new ServiceCollection().AddDefaultBehaviorDependencies();
        services.AddBsftMediator(mediator => mediator
            .UseDefaults()
            .ReplaceDefaultBehavior(MediatorDefaults.Authorization, typeof(DependentProbeBehavior<,>), DefaultBehaviorPlacement.AfterDefaults));

        using var provider = services.BuildServiceProvider();
        var validator = provider.GetServices<IStartupValidator>().OfType<MediatorStartupValidator>().Single();

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(error =>
            error.Contains("'Authorization'") &&
            error.Contains("ReplaceDefaultBehavior(MediatorDefaults.Authorization, typeof(DependentProbeBehavior<,>), DefaultBehaviorPlacement.AfterDefaults)"));
    }

    [Fact]
    public async Task ValidateAsync_WithAnAfterDefaultsReplacementAndItsDependency_ReportsNoError()
    {
        // The counter-check: the same wiring with the dependency registered is valid and reports no duplicate.
        var services = new ServiceCollection().AddDefaultBehaviorDependencies();
        services.AddSingleton<ProbeDependency>();
        services.AddBsftMediator(mediator => mediator
            .UseDefaults()
            .ReplaceDefaultBehavior(MediatorDefaults.Authorization, typeof(DependentProbeBehavior<,>), DefaultBehaviorPlacement.AfterDefaults));

        using var provider = services.BuildServiceProvider();
        var validator = provider.GetServices<IStartupValidator>().OfType<MediatorStartupValidator>().Single();

        var result = await validator.ValidateAsync();

        result.Errors.Should().BeEmpty();
    }

    private static List<Type> ResolveChain(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        var orderer = provider.GetRequiredService<IPipelineBehaviorOrderer>();
        var behaviors = provider.GetServices<IPipelineBehavior<TestCommand, string>>();
        var ordered = orderer.Order(behaviors.ToList());
        return ordered.Select(behavior => behavior.GetType().GetGenericTypeDefinition()).ToList();
    }

    private sealed class AccessProbeBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    {
        public Task<TResponse> HandleAsync(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken = default) =>
            next();
    }

    private sealed class FirstAddedBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    {
        public Task<TResponse> HandleAsync(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken = default) =>
            next();
    }

    private sealed class SecondAddedBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    {
        public Task<TResponse> HandleAsync(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken = default) =>
            next();
    }

    private sealed class ProbeDependency
    {
    }

    private sealed class DependentProbeBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    {
        public DependentProbeBehavior(ProbeDependency dependency)
        {
            ArgumentNullException.ThrowIfNull(dependency);
        }

        public Task<TResponse> HandleAsync(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken = default) =>
            next();
    }
}
