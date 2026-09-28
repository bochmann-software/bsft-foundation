using Bsft.Caching.Mediator;
using Bsft.Core;
using Bsft.Mediator.DependencyInjection;
using Bsft.Mediator.Tests.Support;
using Bsft.Mediator.Transactions;
using Bsft.Resilience.Mediator;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>
/// The canonical order is the point of the defaults, so it is pinned here as a whole and per
/// adjacency. An adjacency is asserted as the position invariant it stands for, because the folded
/// pipeline gives the outer behavior the power to keep the inner one from running at all — which is
/// what <see cref="OuterBehaviorThatThrows_KeepsTheInnerBehaviorFromRunning"/> demonstrates.
/// </summary>
/// <remarks>
/// This suite references the Foundation behavior packages only (<c>Bsft.Resilience.Mediator</c>,
/// <c>Bsft.Caching.Mediator</c>), so the chain is the canonical order with the authorization,
/// organization authorization and audit slots skipped — what a host without those packages gets. The
/// full eight-behavior chain and the adjacencies involving the skipped slots are pinned in
/// <c>Bsft.Mediator.Scenarios.Tests</c>.
/// </remarks>
public class MediatorDefaultsTests
{
    [Fact]
    public void UseDefaults_WithTheFoundationBehaviorPackagesReferenced_YieldsTheCanonicalOrderOfThose()
    {
        // The slots whose package is absent resolve to nothing and are skipped; the rest keep their
        // canonical relative order.
        var chain = ResolveChain(BuildHost());

        chain.Should().Equal(
            typeof(TracingBehavior<,>),
            typeof(ValidationBehavior<,>),
            typeof(RetryBehavior<,>),
            typeof(TransactionBehavior<,>),
            typeof(CachingBehavior<,>));
    }

    [Fact]
    public void UseDefaults_WithABehaviorPackageRegisteringItselfFirst_StillYieldsTheCanonicalOrder()
    {
        // A behavior package registers its own behavior from its own AddBsft* call, which a host may
        // make before AddBsftMediator. If registration order decided the pipeline, caching would end
        // up ahead of authorization here — the failure this whole feature exists to prevent.
        var services = new ServiceCollection().AddDefaultBehaviorDependencies();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));
        services.AddBsftMediator(mediator => mediator.UseDefaults());

        var chain = ResolveChain(services);

        chain.Should().HaveCount(5);
        chain.Should().EndWith(typeof(CachingBehavior<,>));
    }

    [Fact]
    public void CanonicalOrder_PutsTracingOutermost_SoItsSpanEnclosesEverything()
    {
        var chain = ResolveChain(BuildHost());

        chain[0].Should().Be(typeof(TracingBehavior<,>));
    }

    [Fact]
    public void CanonicalOrder_PutsRetryOutsideTheTransaction_SoEachAttemptGetsItsOwnTransaction()
    {
        var chain = ResolveChain(BuildHost());

        chain.IndexOf(typeof(RetryBehavior<,>)).Should().BeLessThan(chain.IndexOf(typeof(TransactionBehavior<,>)));
    }

    [Fact]
    public void CanonicalOrder_KeepsValidationOutsideRetry_SoARetriedRequestIsValidatedOnce()
    {
        var chain = ResolveChain(BuildHost());

        chain.IndexOf(typeof(ValidationBehavior<,>)).Should().BeLessThan(chain.IndexOf(typeof(RetryBehavior<,>)));
    }

    [Fact]
    public void CanonicalOrder_PutsCachingInnermost_SoItCanNeverServePastAuthorization()
    {
        var chain = ResolveChain(BuildHost());

        chain[^1].Should().Be(typeof(CachingBehavior<,>));
    }

    [Fact]
    public async Task OuterBehaviorThatThrows_KeepsTheInnerBehaviorFromRunning()
    {
        // The mechanism behind every adjacency above: the pipeline is folded, so a behavior that
        // refuses a request is the last thing that runs. An authorization behavior ahead of caching
        // therefore means the cache never observes an unauthorized request.
        var services = new ServiceCollection();
        services.AddBsftMediator(mediator => mediator
            .RegisterServicesFromAssembly(typeof(TestCommandHandler).Assembly)
            .AddBehavior(typeof(RefusingProbeBehavior<,>))
            .AddBehavior(typeof(InnerProbeBehavior<,>)));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        InnerProbeBehavior<ICommand<string>, string>.Invocations = 0;

        var act = async () => await scope.ServiceProvider.GetRequiredService<IMediator>()
            .SendAsync<string>(new TestCommand("refused"));

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        InnerProbeBehavior<ICommand<string>, string>.Invocations.Should().Be(0);
    }

    [Fact]
    public void UseDefaults_WithAHostBehavior_LeavesTheHostBehaviorWhereItWasRegistered()
    {
        var services = new ServiceCollection().AddDefaultBehaviorDependencies();
        services.AddBsftMediator(mediator => mediator
            .UseDefaults()
            .AddBehavior(typeof(InnerProbeBehavior<,>)));

        var chain = ResolveChain(services);

        chain.Should().HaveCount(6);
        chain[^1].Should().Be(typeof(InnerProbeBehavior<,>));
    }

    [Fact]
    public void SuppressingADefaultAndRegisteringItAgain_StillYieldsTheCanonicalOrder()
    {
        // Suppression drops a behavior; it does not hand its slot over. A host that suppresses one
        // and adds it back — the shape somebody reaches for to reorder two defaults, retry and
        // transaction above all — gets the canonical position anyway, because the rank is resolved
        // over every slot regardless of suppression. docs/mediator.md says so, so it is pinned here.
        // The canonical order is retry outside the transaction.
        var services = new ServiceCollection().AddDefaultBehaviorDependencies();
        services.AddBsftMediator(mediator => mediator
            .UseDefaults()
            .SuppressDefaultBehavior(MediatorDefaults.Transaction)
            .AddBehavior(typeof(TransactionBehavior<,>)));

        var chain = ResolveChain(services);

        // Named before the comparison: an absent behavior would make IndexOf return -1 and the
        // ordering assertion pass without proving anything.
        chain.Should().ContainSingle(behavior => behavior == typeof(TransactionBehavior<,>));
        chain.Should().Contain(typeof(RetryBehavior<,>));
        chain.IndexOf(typeof(RetryBehavior<,>))
            .Should().BeLessThan(chain.IndexOf(typeof(TransactionBehavior<,>)));
    }

    [Fact]
    public void WithoutUseDefaults_RegistrationOrderStands()
    {
        // The wholesale escape hatch: a host that wires its own chain gets exactly what it
        // registered, in the order it registered it, and no orderer is in the container at all.
        var services = new ServiceCollection().AddDefaultBehaviorDependencies();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));
        services.AddBsftMediator(mediator => mediator.AddBehavior(typeof(InnerProbeBehavior<,>)));

        using var provider = services.BuildServiceProvider();
        provider.GetService<IPipelineBehaviorOrderer>().Should().BeNull();
        ResolveChain(services).Should().Equal(typeof(CachingBehavior<,>), typeof(InnerProbeBehavior<,>));
    }

    [Fact]
    public void SuppressDefaultBehavior_RemovesOnlyThatBehaviorAndKeepsTheRestInOrder()
    {
        var services = new ServiceCollection().AddDefaultBehaviorDependencies();
        services.AddBsftMediator(mediator => mediator
            .UseDefaults()
            .SuppressDefaultBehavior(MediatorDefaults.Caching));

        var chain = ResolveChain(services);

        chain.Should().NotContain(typeof(CachingBehavior<,>));
        chain.Should().Equal(
            typeof(TracingBehavior<,>),
            typeof(ValidationBehavior<,>),
            typeof(RetryBehavior<,>),
            typeof(TransactionBehavior<,>));
    }

    [Fact]
    public void SuppressDefaultBehavior_WithUnknownName_Throws()
    {
        var configuration = new MediatorConfiguration();

        var act = () => configuration.SuppressDefaultBehavior("Cashing");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("name")
            .WithMessage("*is not a default pipeline behavior*");
    }

    [Fact]
    public void Constructor_WithNullCatalog_Throws()
    {
        var act = () => new MediatorConfiguration(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("catalog");
    }

    [Fact]
    public void SuppressDefaultBehavior_WithNullName_Throws()
    {
        var configuration = new MediatorConfiguration();

        var act = () => configuration.SuppressDefaultBehavior(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("name");
    }

    [Fact]
    public void UseDefaults_WithEnableTracing_RegistersTracingExactlyOnce()
    {
        var services = new ServiceCollection().AddDefaultBehaviorDependencies();
        services.AddBsftMediator(mediator => mediator.EnableTracing().UseDefaults());

        ResolveChain(services).Should().ContainSingle(behavior => behavior == typeof(TracingBehavior<,>));
    }

    [Fact]
    public void UseDefaults_EnablesTracingForNotificationsToo()
    {
        var services = new ServiceCollection().AddDefaultBehaviorDependencies();
        services.AddBsftMediator(mediator => mediator.UseDefaults());

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<MediatorConfiguration>().ShouldTrace.Should().BeTrue();
    }

    [Fact]
    public void UseDefaults_WithTracingSuppressed_LeavesTracingOff()
    {
        var services = new ServiceCollection().AddDefaultBehaviorDependencies();
        services.AddBsftMediator(mediator => mediator
            .UseDefaults()
            .SuppressDefaultBehavior(MediatorDefaults.Tracing));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<MediatorConfiguration>().ShouldTrace.Should().BeFalse();
        ResolveChain(services).Should().NotContain(typeof(TracingBehavior<,>));
    }

    [Fact]
    public async Task Mediator_AppliesTheRegisteredOrderer()
    {
        // Proves the dispatcher is what applies the order, rather than the test resolving the chain
        // the same way twice: a reversing orderer flips two probe behaviors the catalog knows
        // nothing about.
        var services = new ServiceCollection();
        services.AddBsftMediator(mediator => mediator
            .RegisterServicesFromAssembly(typeof(TestCommandHandler).Assembly)
            .AddBehavior(typeof(OuterProbeBehavior<,>))
            .AddBehavior(typeof(InnerProbeBehavior<,>)));
        services.AddSingleton<IPipelineBehaviorOrderer, ReversingBehaviorOrderer>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        ProbeLog.Entries.Clear();

        await scope.ServiceProvider.GetRequiredService<IMediator>().SendAsync<string>(new TestCommand("ordered"));

        ProbeLog.Entries.Should().Equal("inner", "outer");
    }

    private static IServiceCollection BuildHost()
    {
        var services = new ServiceCollection().AddDefaultBehaviorDependencies();
        services.AddBsftMediator(mediator => mediator.UseDefaults());
        return services;
    }

    /// <summary>
    /// Resolves the pipeline the way the dispatcher does — every behavior for the static command
    /// contract, put in order by the registered orderer — and reduces it to the open-generic
    /// definitions the registrations were made with.
    /// </summary>
    private static List<Type> ResolveChain(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var behaviors = scope.ServiceProvider
            .GetServices<IPipelineBehavior<ICommand<Unit>, Unit>>()
            .ToList();

        var orderer = scope.ServiceProvider.GetService<IPipelineBehaviorOrderer>();
        var ordered = orderer is null ? behaviors : orderer.Order(behaviors);

        return ordered.Select(behavior => behavior.GetType().GetGenericTypeDefinition()).ToList();
    }
}
