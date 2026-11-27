using Bsft.Caching.Mediator;
using Bsft.Mediator.DependencyInjection;
using Bsft.Mediator.Tests.Support;
using Bsft.Mediator.Transactions;
using Bsft.Resilience.Mediator;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

public class MediatorConfigurationReplaceDefaultBehaviorTests
{
    private sealed class CustomAuthorizationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    {
        public Task<TResponse> HandleAsync(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken = default)
        {
            return next();
        }
    }

    private sealed class AnotherCustomAuthorizationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    {
        public Task<TResponse> HandleAsync(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken = default)
        {
            return next();
        }
    }

    private sealed class CustomValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    {
        public Task<TResponse> HandleAsync(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken = default)
        {
            return next();
        }
    }

    private sealed class NotABehavior
    {
    }

    private sealed class NotGeneric : IPipelineBehavior<TestCommand, string>
    {
        public Task<string> HandleAsync(TestCommand request, Func<Task<string>> next, CancellationToken cancellationToken = default)
        {
            return next();
        }
    }

    [Fact]
    public void ReplaceDefaultBehavior_WithNullName_Throws()
    {
        var configuration = new MediatorConfiguration();

        var act = () => configuration.ReplaceDefaultBehavior(null!, typeof(CustomAuthorizationBehavior<,>));

        act.Should().Throw<ArgumentNullException>().WithParameterName("name");
    }

    [Fact]
    public void ReplaceDefaultBehavior_WithNullType_Throws()
    {
        var configuration = new MediatorConfiguration();

        var act = () => configuration.ReplaceDefaultBehavior(MediatorDefaults.Caching, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("behaviorType");
    }

    [Fact]
    public void ReplaceDefaultBehavior_WithUnknownName_Throws()
    {
        var configuration = new MediatorConfiguration();

        var act = () => configuration.ReplaceDefaultBehavior("UnknownBehavior", typeof(CustomAuthorizationBehavior<,>));

        act.Should().Throw<ArgumentException>()
            .WithParameterName("name")
            .WithMessage("*is not a default pipeline behavior*");
    }

    [Fact]
    public void ReplaceDefaultBehavior_WithNonGenericType_Throws()
    {
        var configuration = new MediatorConfiguration();

        var act = () => configuration.ReplaceDefaultBehavior(MediatorDefaults.Caching, typeof(NotGeneric));

        act.Should().Throw<ArgumentException>()
            .WithParameterName("behaviorType")
            .WithMessage("*must be an open generic type definition*");
    }

    [Fact]
    public void ReplaceDefaultBehavior_WithTypeNotImplementingInterface_Throws()
    {
        var configuration = new MediatorConfiguration();

        var act = () => configuration.ReplaceDefaultBehavior(MediatorDefaults.Caching, typeof(NotABehavior));

        // NotABehavior fails validation on being a non-generic type, which is checked first
        act.Should().Throw<ArgumentException>()
            .WithParameterName("behaviorType")
            .WithMessage("*must be an open generic type definition*");
    }

    [Fact]
    public void ReplaceDefaultBehavior_SetsReplacedDefaultsName()
    {
        var configuration = new MediatorConfiguration();

        configuration.ReplaceDefaultBehavior(MediatorDefaults.Caching, typeof(CustomAuthorizationBehavior<,>));

        configuration.ReplacedDefaults.Should().Contain(MediatorDefaults.Caching);
    }

    [Fact]
    public void ReplaceDefaultBehavior_ReturnsConfigurationForChaining()
    {
        var configuration = new MediatorConfiguration();

        var result = configuration.ReplaceDefaultBehavior(MediatorDefaults.Caching, typeof(CustomAuthorizationBehavior<,>));

        result.Should().BeSameAs(configuration);
    }

    [Fact]
    public void ReplaceDefaultBehavior_TwiceOnSameSlot_Throws()
    {
        var configuration = new MediatorConfiguration();
        configuration.ReplaceDefaultBehavior(MediatorDefaults.Caching, typeof(CustomAuthorizationBehavior<,>));

        var act = () => configuration.ReplaceDefaultBehavior(MediatorDefaults.Caching, typeof(AnotherCustomAuthorizationBehavior<,>));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already replaced*");
    }

    [Fact]
    public void ReplaceDefaultBehavior_AfterSuppressOfSameSlot_Throws()
    {
        var configuration = new MediatorConfiguration();
        configuration.SuppressDefaultBehavior(MediatorDefaults.Caching);

        var act = () => configuration.ReplaceDefaultBehavior(MediatorDefaults.Caching, typeof(CustomAuthorizationBehavior<,>));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*is suppressed*");
    }

    [Fact]
    public void ReplaceDefaultBehavior_OnOneSlotAndSuppressOnAnotherSlot_Succeeds()
    {
        var configuration = new MediatorConfiguration();

        configuration.ReplaceDefaultBehavior(MediatorDefaults.Caching, typeof(CustomAuthorizationBehavior<,>));
        configuration.SuppressDefaultBehavior(MediatorDefaults.Validation);

        configuration.ReplacedDefaults.Should().Contain(MediatorDefaults.Caching);
        configuration.SuppressedDefaults.Should().Contain(MediatorDefaults.Validation);
    }

    [Fact]
    public void SuppressDefaultBehavior_AfterReplaceOfSameSlot_Throws()
    {
        var configuration = new MediatorConfiguration();
        configuration.ReplaceDefaultBehavior(MediatorDefaults.Caching, typeof(CustomAuthorizationBehavior<,>));

        var act = () => configuration.SuppressDefaultBehavior(MediatorDefaults.Caching);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*is replaced*");
        configuration.SuppressedDefaults.Should().NotContain(MediatorDefaults.Caching);
    }

    [Fact]
    public void ReplaceDefaultBehavior_UpdatesCatalogWithReplacementTypeName()
    {
        var configuration = new MediatorConfiguration();

        configuration.ReplaceDefaultBehavior(MediatorDefaults.Caching, typeof(CustomAuthorizationBehavior<,>));

        var descriptor = configuration.Catalog.FindSlot(MediatorDefaults.Caching);
        descriptor.Should().NotBeNull();
        descriptor!.TypeName.Should().Contain("CustomAuthorizationBehavior");
    }

    [Fact]
    public void ReplaceDefaultBehavior_UpdatesCatalogRegistrationCall()
    {
        var configuration = new MediatorConfiguration();

        configuration.ReplaceDefaultBehavior(MediatorDefaults.Caching, typeof(CustomAuthorizationBehavior<,>));

        var descriptor = configuration.Catalog.FindSlot(MediatorDefaults.Caching);
        descriptor.Should().NotBeNull();
        descriptor!.RegistrationCall.Should().Contain("ReplaceDefaultBehavior");
        descriptor.RegistrationCall.Should().Contain("CustomAuthorizationBehavior");
    }

    [Fact]
    public void ReplaceDefaultBehavior_WithReplacement_CatalogIsChanged()
    {
        var configuration = new MediatorConfiguration();
        var originalCatalog = configuration.Catalog;

        configuration.ReplaceDefaultBehavior(MediatorDefaults.Caching, typeof(CustomAuthorizationBehavior<,>));

        configuration.Catalog.Should().NotBeSameAs(originalCatalog);
    }

    [Fact]
    public void ReplaceDefaultBehavior_CanReplaceMultipleDifferentSlots()
    {
        var configuration = new MediatorConfiguration();

        configuration.ReplaceDefaultBehavior(MediatorDefaults.Caching, typeof(CustomAuthorizationBehavior<,>));
        configuration.ReplaceDefaultBehavior(MediatorDefaults.Retry, typeof(CustomValidationBehavior<,>));

        configuration.ReplacedDefaults.Should().HaveCount(2);
        configuration.ReplacedDefaults.Should().Contain(new[] { MediatorDefaults.Caching, MediatorDefaults.Retry });
    }

    [Fact]
    public void ReplaceDefaultBehavior_WithUseDefaults_IncludesReplacementInChain()
    {
        var services = new ServiceCollection()
            .AddDefaultBehaviorDependencies();

        services.AddBsftMediator(mediator => mediator
            .UseDefaults()
            .ReplaceDefaultBehavior(MediatorDefaults.Caching, typeof(CustomAuthorizationBehavior<,>)));

        var chain = ResolveChain(services);

        chain.Should().Contain(typeof(CustomAuthorizationBehavior<,>));
        chain.Should().NotContain(typeof(CachingBehavior<,>));
    }

    [Fact]
    public void ReplaceDefaultBehavior_WithUseDefaults_KeepsReplacementAtCanonicalPosition()
    {
        var services = new ServiceCollection()
            .AddDefaultBehaviorDependencies();

        // Register a host behavior first to create a registration order that differs from canonical.
        // Registration order: InnerProbeBehavior (unknown), Tracing, Validation, Retry, Transaction, replacement.
        // Canonical order after orderer: InnerProbeBehavior (unknown keeps position), Tracing, Validation, Retry, Transaction, CustomAuthorizationBehavior (at Caching).
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(InnerProbeBehavior<,>));

        services.AddBsftMediator(mediator => mediator
            .UseDefaults()
            .ReplaceDefaultBehavior(MediatorDefaults.Caching, typeof(CustomAuthorizationBehavior<,>)));

        var chain = ResolveChain(services);

        // Unknown host behavior keeps its position; known behaviors are ordered by canonical rank
        chain.Should().Equal(
            typeof(InnerProbeBehavior<,>),
            typeof(TracingBehavior<,>),
            typeof(ValidationBehavior<,>),
            typeof(RetryBehavior<,>),
            typeof(TransactionBehavior<,>),
            typeof(CustomAuthorizationBehavior<,>));
    }

    [Fact]
    public void ReplaceDefaultBehavior_OfTheAuthorizationSlot_RunsTheReplacementBeforeEveryLaterDefault()
    {
        var services = new ServiceCollection()
            .AddDefaultBehaviorDependencies();

        // Registered before the mediator, so the raw registration order is: InnerProbeBehavior, Caching,
        // the replacement, then Tracing, Validation, Retry and Transaction (the defaults already present are
        // not added twice). Only the orderer can move Caching last and the replacement into the
        // Authorization slot, after Tracing and before Validation.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(InnerProbeBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CustomAuthorizationBehavior<,>));

        services.AddBsftMediator(mediator => mediator
            .UseDefaults()
            .ReplaceDefaultBehavior(MediatorDefaults.Authorization, typeof(CustomAuthorizationBehavior<,>)));

        var chain = ResolveChain(services);

        // Unknown host behavior keeps its position; known behaviors are ordered by canonical rank
        chain.Should().Equal(
            typeof(InnerProbeBehavior<,>),
            typeof(TracingBehavior<,>),
            typeof(CustomAuthorizationBehavior<,>),
            typeof(ValidationBehavior<,>),
            typeof(RetryBehavior<,>),
            typeof(TransactionBehavior<,>),
            typeof(CachingBehavior<,>));
    }

    [Fact]
    public void SuppressDefaultBehavior_OfTheAuthorizationSlotWithTheBehaviorAdded_GivesItNoCanonicalRank()
    {
        var services = new ServiceCollection()
            .AddDefaultBehaviorDependencies();

        // Register a host behavior first to create a registration order that differs from canonical.
        // Suppress Authorization and add a custom behavior via AddBehavior (unranked).
        // Registration order: InnerProbeBehavior (unknown), Tracing, Validation, Retry, Transaction, Caching, CustomAuthorizationBehavior (unranked).
        // Canonical order after orderer: InnerProbeBehavior (unknown keeps position), Tracing, (Authorization suppressed), Validation, Retry, Transaction, Caching, CustomAuthorizationBehavior (unranked keeps position).
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(InnerProbeBehavior<,>));

        services.AddBsftMediator(mediator => mediator
            .UseDefaults()
            .SuppressDefaultBehavior(MediatorDefaults.Authorization)
            .AddBehavior(typeof(CustomAuthorizationBehavior<,>)));

        var chain = ResolveChain(services);

        // Unknown behaviors keep their position; known behaviors are ordered by canonical rank
        chain.Should().Equal(
            typeof(InnerProbeBehavior<,>),
            typeof(TracingBehavior<,>),
            typeof(ValidationBehavior<,>),
            typeof(RetryBehavior<,>),
            typeof(TransactionBehavior<,>),
            typeof(CachingBehavior<,>),
            typeof(CustomAuthorizationBehavior<,>));
    }

    [Fact]
    public void ReplaceDefaultBehavior_MatchesNameCaseInsensitively()
    {
        var configuration = new MediatorConfiguration();

        // Pass lowercase name, should still work
        configuration.ReplaceDefaultBehavior("caching", typeof(CustomAuthorizationBehavior<,>));

        // The ReplacedDefaults stores the name as passed, so we check with lowercase
        configuration.ReplacedDefaults.Should().Contain("caching");
    }

    [Fact]
    public void ReplaceDefaultBehavior_WithCachingReplaced_CatalogSeesReplacement()
    {
        // This test verifies that the catalog sees the replacement behavior
        var configuration = new MediatorConfiguration();
        configuration.ReplaceDefaultBehavior(MediatorDefaults.Caching, typeof(CustomAuthorizationBehavior<,>));

        configuration.Catalog.FindSlot(MediatorDefaults.Caching).Should().NotBeNull();
        var descriptor = configuration.Catalog.FindSlot(MediatorDefaults.Caching)!;
        descriptor.TypeName.Should().Contain("CustomAuthorizationBehavior");
    }

    private static List<Type> ResolveChain(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        var orderer = provider.GetRequiredService<IPipelineBehaviorOrderer>();
        var behaviors = provider.GetServices<IPipelineBehavior<TestCommand, string>>();
        var ordered = orderer.Order(behaviors.ToList());
        return ordered.Select(b => b.GetType().GetGenericTypeDefinition()).ToList();
    }
}
