using System.ComponentModel;
using System.Reflection;
using Bsft.Core;
using Bsft.Mediator.DependencyInjection;
using Bsft.Mediator.StartupValidation;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Bsft.Mediator.Tests.StartupValidation;

/// <summary>
/// The startup check for a permission nothing enforces. A request carrying
/// <c>[RequirePermission]</c>, <c>[TenantAuthorize]</c> or <c>IRequireOrganizationPermission</c> reaches its
/// handler unchecked when the enforcing behavior was never registered, and every comparable wiring
/// mistake is loud. These tests fail if the diagnostic goes quiet again — or if it starts crying
/// wolf about a behavior that is wired in a shape the check simply did not look at.
/// </summary>
/// <remarks>
/// This suite references no <c>Bsft.Auth</c> or <c>Bsft.Organization</c> package, which is the
/// situation the by-name matching exists for: the declarations are <c>[RequirePermission]</c> from
/// <c>Bsft.Core</c>, and where a test needs the enforcing behavior registered, a catalog points the
/// authorization slot at <see cref="EnforcingProbeBehavior{TRequest, TResponse}"/>. The real
/// declarations and behaviors are exercised in <c>Bsft.Mediator.Scenarios.Tests</c>.
/// </remarks>
public class AuthorizationCoverageValidatorTests
{
    [Fact]
    public async Task ValidateAsync_WithADeclaredPermissionAndNoEnforcingBehavior_Warns()
    {
        var validator = Validator(new ServiceCollection(), typeof(PermissionDeclaringCommand));

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue("an unenforced declaration is reported, never fatal");
        var warning = result.Warnings.Should().ContainSingle().Subject;
        warning.Should().Contain(nameof(PermissionDeclaringCommand));
        warning.Should().Contain("AddBsftAuthMediator()");
        warning.Should().Contain("AuthorizationBehavior");
    }

    [Fact]
    public async Task ValidateAsync_WithAnUnenforcedDeclaration_StatesWhatTheCheckCannotSee()
    {
        // The two limitations a reader has to know before chasing a phantom gap: a factory
        // registration is invisible, and order is not verified at all.
        var validator = Validator(new ServiceCollection(), typeof(PermissionDeclaringCommand));

        var result = await validator.ValidateAsync();

        var warning = result.Warnings.Should().ContainSingle().Subject;
        warning.Should().Contain("factory delegate");
        warning.Should().Contain("pipeline order is not verified");
        warning.Should().Contain("UseDefaults()");
    }

    [Fact]
    public async Task ValidateAsync_WithADeclaredPermissionAndTheEnforcingBehavior_IsSilent()
    {
        var services = new ServiceCollection();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(EnforcingProbeBehavior<,>));
        var validator = Validator(services, ProbeCatalogConfiguration(), typeof(PermissionDeclaringCommand));

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_WithTheEnforcingBehaviorRegisteredAsAClosedGeneric_IsSilent()
    {
        // A hand-wired chain may close the behavior per request type. It runs, so reporting it as
        // unenforced would be a false alarm.
        var services = new ServiceCollection();
        services.AddTransient<
            IPipelineBehavior<PermissionDeclaringCommand, string>,
            EnforcingProbeBehavior<PermissionDeclaringCommand, string>>();

        var validator = Validator(services, ProbeCatalogConfiguration(), typeof(PermissionDeclaringCommand));

        var result = await validator.ValidateAsync();

        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_WithTheEnforcingBehaviorRegisteredThroughAFactory_StillWarns()
    {
        // The documented limitation, pinned: a factory names no implementation type, so the check
        // cannot see the behavior and says so in the warning itself.
        var services = new ServiceCollection();
        services.AddTransient<IPipelineBehavior<PermissionDeclaringCommand, string>>(
            _ => new EnforcingProbeBehavior<PermissionDeclaringCommand, string>());

        var validator = Validator(services, ProbeCatalogConfiguration(), typeof(PermissionDeclaringCommand));

        var result = await validator.ValidateAsync();

        result.Warnings.Should().ContainSingle().Which.Should().Contain("factory delegate");
    }

    [Fact]
    public async Task ValidateAsync_WithADeclaringNotificationAndTheEnforcingBehavior_SaysNoRegistrationCanCloseIt()
    {
        // A notification never enters the pipeline, so registering the enforcing behavior changes
        // nothing: the warning has to say that instead of pointing at a registration call.
        var services = new ServiceCollection();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(EnforcingProbeBehavior<,>));
        var validator = Validator(services, ProbeCatalogConfiguration(), typeof(PermissionDeclaringNotification));

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
        var warning = result.Warnings.Should().ContainSingle().Subject;
        warning.Should().Contain(nameof(PermissionDeclaringNotification));
        warning.Should().Contain("never enter the behavior");
        warning.Should().NotContain("AddBsftProbe()");
    }

    [Fact]
    public async Task ValidateAsync_WithNothingDeclared_IsSilent()
    {
        var validator = Validator(new ServiceCollection(), typeof(PlainCommand));

        var result = await validator.ValidateAsync();

        result.Message.Should().Contain("Checked 0 request type(s)");
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_WithADeclarationOnSomethingNeverDispatched_IsSilent()
    {
        // A permission attribute on a type the mediator never dispatches is not something a pipeline
        // behavior could enforce, so naming it would be noise.
        var validator = Validator(
            new ServiceCollection(),
            typeof(DecoratedNonRequest),
            typeof(AbstractDeclaringCommand));

        var result = await validator.ValidateAsync();

        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_WithTheBehaviorPackageAbsent_StillWarns()
    {
        // The slot resolves to no type at all, which is the case this diagnostic exists for: the
        // default-behavior check skips such a slot, so nothing else would say a word.
        var catalog = new DefaultBehaviorCatalog(
        [
            new(MediatorDefaults.Authorization, "Bsft.Absent.AbsentBehavior`2, Bsft.Absent", "AddBsftAbsent()")
        ]);

        var validator = Validator(
            new ServiceCollection(), new MediatorConfiguration(catalog), typeof(PermissionDeclaringCommand));

        var result = await validator.ValidateAsync();

        result.Warnings.Should().ContainSingle().Which.Should().Contain("AddBsftAbsent()");
    }

    [Fact]
    public async Task ValidateAsync_WithACatalogHoldingNoSuchSlot_IsSilent()
    {
        // Nothing to name, so nothing actionable to say.
        var validator = Validator(
            new ServiceCollection(),
            new MediatorConfiguration(new DefaultBehaviorCatalog([])),
            typeof(PermissionDeclaringCommand));

        var result = await validator.ValidateAsync();

        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_WithNamesakeRequestsInDifferentScopes_NamesThemInFull()
    {
        // Two requests of the same simple name are two gaps. Naming them in full is what keeps them
        // apart, in the count as well as in the warning.
        var validator = Validator(
            new ServiceCollection(),
            typeof(First.NamesakeCommand),
            typeof(Second.NamesakeCommand));

        var result = await validator.ValidateAsync();

        result.Message.Should().Contain("Checked 2 request type(s)");
        var warning = result.Warnings.Should().ContainSingle().Subject;
        warning.Should().Contain(typeof(First.NamesakeCommand).FullName);
        warning.Should().Contain(typeof(Second.NamesakeCommand).FullName);
    }

    [Fact]
    public async Task ValidateAsync_WithoutTheBehaviorRegistrations_IsSilent()
    {
        // Without the registrations the check cannot tell a wired behavior from a missing one, so it
        // says nothing rather than guessing.
        var config = new MediatorConfiguration();
        config.RegisterServicesFromAssembly(AssemblyOf(typeof(PermissionDeclaringCommand)));

        var validator = new AuthorizationCoverageValidator(config, new MediatorTypeScanner(config), null);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_WithAnAssemblyThatCannotBeFullyLoaded_UsesThePartialTypes()
    {
        var assembly = Substitute.For<Assembly>();
        assembly.When(a => a.GetTypes()).Throw(new ReflectionTypeLoadException(
            [typeof(PermissionDeclaringCommand)],
            [new InvalidOperationException("load error")]));

        var config = new MediatorConfiguration();
        config.RegisterServicesFromAssembly(assembly);

        var validator = new AuthorizationCoverageValidator(
            config, new MediatorTypeScanner(config), new PipelineBehaviorRegistrations(new ServiceCollection()));

        var result = await validator.ValidateAsync();

        result.Warnings.Should().ContainSingle().Which.Should().Contain(nameof(PermissionDeclaringCommand));
    }

    [Fact]
    public void Constructor_WithNullArguments_Throws()
    {
        var config = new MediatorConfiguration();

        var withoutConfig = () => new AuthorizationCoverageValidator(null!, new MediatorTypeScanner(config), null);
        var withoutScanner = () => new AuthorizationCoverageValidator(config, null!, null);

        withoutConfig.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
        withoutScanner.Should().Throw<ArgumentNullException>().WithParameterName("scanner");
    }

    [Fact]
    public void IsRegistered_WithNullBehaviorType_Throws()
    {
        var registrations = new PipelineBehaviorRegistrations(new ServiceCollection());

        var act = () => registrations.IsRegistered(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("implementationType");
    }

    [Fact]
    public void IsRegistered_WithARegistrationThatIsNotABehavior_IsFalse()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new object());

        var registrations = new PipelineBehaviorRegistrations(services);

        registrations.IsRegistered(typeof(EnforcingProbeBehavior<,>)).Should().BeFalse();
    }

    [Fact]
    public void IsRegistered_WithABehaviorRegisteredAsAnInstance_IsTrue()
    {
        // A pre-built instance names its type just as well as an implementation type does.
        var services = new ServiceCollection();
        services.AddSingleton<IPipelineBehavior<PlainCommand, string>>(new NoOpBehavior<PlainCommand, string>());

        var registrations = new PipelineBehaviorRegistrations(services);

        registrations.IsRegistered(typeof(NoOpBehavior<,>)).Should().BeTrue();
    }

    [Fact]
    public void IsRegistered_WithADifferentBehaviorClosedOverTheSameRequest_IsFalse()
    {
        var services = new ServiceCollection();
        services.AddTransient<IPipelineBehavior<PlainCommand, string>, NoOpBehavior<PlainCommand, string>>();

        var registrations = new PipelineBehaviorRegistrations(services);

        registrations.IsRegistered(typeof(EnforcingProbeBehavior<,>)).Should().BeFalse();
    }

    [Fact]
    public void AddBsftMediator_RegistersTheCoverageCheck()
    {
        var services = new ServiceCollection();
        services.AddBsftMediator(_ => { });

        using var provider = services.BuildServiceProvider();

        provider.GetServices<IStartupValidator>()
            .OfType<AuthorizationCoverageValidator>()
            .Should().ContainSingle("the check runs whether or not the host asked for the defaults");
    }

    [Fact]
    public void AddBsftMediator_SharesOneScanAcrossTheValidators()
    {
        var services = new ServiceCollection();
        services.AddBsftMediator(_ => { });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<MediatorTypeScanner>()
            .Should().BeSameAs(provider.GetRequiredService<MediatorTypeScanner>());
    }

    [Fact]
    public async Task ValidateAsync_WithADeclaringRequestAndADeclaringNotification_CountsBoth()
    {
        // The summary counts every declaring type the check looked at, the unenforceable
        // notification as much as the request.
        var validator = Validator(
            new ServiceCollection(),
            typeof(PermissionDeclaringCommand),
            typeof(PermissionDeclaringNotification));

        var result = await validator.ValidateAsync();

        result.Message.Should().Contain("Checked 2 request type(s)");
        result.Warnings.Should().HaveCount(2);
    }

    [Fact]
    public async Task ValidateAsync_WithADeclaringRecordThatIsNoRequest_IsSilent()
    {
        // A record implements a generic contract of its own, IEquatable over itself; implementing
        // a generic interface is not what makes a type a command or a query.
        var validator = Validator(new ServiceCollection(), typeof(DecoratedNonRequestRecord));

        var result = await validator.ValidateAsync();

        result.Message.Should().Contain("Checked 0 request type(s)");
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_WithThePermissionDeclaredOnABaseType_WarnsAboutTheDerivedRequest()
    {
        // [RequirePermission] is inherited, so the request deriving from a declaring base carries
        // the declaration as much as one decorated directly.
        var validator = Validator(new ServiceCollection(), typeof(InheritingDeclaringCommand));

        var result = await validator.ValidateAsync();

        result.Message.Should().Contain("Checked 1 request type(s)");
        result.Warnings.Should().ContainSingle().Which.Should().Contain(typeof(InheritingDeclaringCommand).FullName);
    }

    [Fact]
    public void IsRegistered_WithANonGenericBehaviorRegistered_IsFalse()
    {
        // A behavior written for one request type is no closure of an open generic behavior.
        var services = new ServiceCollection();
        services.AddTransient<IPipelineBehavior<ICommand<string>, string>, ConcreteProbeBehavior>();

        var registrations = new PipelineBehaviorRegistrations(services);

        registrations.IsRegistered(typeof(EnforcingProbeBehavior<,>)).Should().BeFalse();
    }

    private static AuthorizationCoverageValidator Validator(IServiceCollection services, params Type[] types) =>
        Validator(services, new MediatorConfiguration(), types);

    /// <summary>
    /// Builds the validator over a mocked assembly holding exactly the given types, so a test states
    /// its scenario instead of depending on what else the test assembly happens to contain.
    /// </summary>
    private static AuthorizationCoverageValidator Validator(
        IServiceCollection services,
        MediatorConfiguration config,
        params Type[] types)
    {
        config.RegisterServicesFromAssembly(AssemblyOf(types));

        return new AuthorizationCoverageValidator(
            config, new MediatorTypeScanner(config), new PipelineBehaviorRegistrations(services));
    }

    /// <summary>
    /// A configuration whose catalog points the authorization slot at
    /// <see cref="EnforcingProbeBehavior{TRequest, TResponse}"/>, standing in for
    /// <c>Bsft.Auth.Mediator</c>, which this suite does not reference.
    /// </summary>
    private static MediatorConfiguration ProbeCatalogConfiguration() =>
        new(new DefaultBehaviorCatalog(
        [
            new(
                MediatorDefaults.Authorization,
                typeof(EnforcingProbeBehavior<,>).AssemblyQualifiedName!,
                "AddBsftProbe()")
        ]));

    /// <summary>An assembly reporting exactly the given types.</summary>
    private static Assembly AssemblyOf(params Type[] types)
    {
        var assembly = Substitute.For<Assembly>();
        assembly.GetTypes().Returns(types);
        return assembly;
    }

    /// <summary>A command declaring its permission without taking the web stack on.</summary>
    [RequirePermission("orders.write")]
    public sealed record PermissionDeclaringCommand : ICommand<string>;

    /// <summary>A notification declaring a permission nothing can enforce.</summary>
    [RequirePermission("orders.write")]
    public sealed record PermissionDeclaringNotification : INotification;

    /// <summary>A request carrying an attribute that declares no permission.</summary>
    [Description("plain")]
    public sealed record PlainCommand : ICommand<string>;

    /// <summary>An abstract request: never dispatched as itself.</summary>
    [RequirePermission("orders.write")]
    public abstract record AbstractDeclaringCommand : ICommand<string>;

    /// <summary>A decorated type the mediator never dispatches.</summary>
    [RequirePermission("orders.write")]
    public sealed class DecoratedNonRequest;

    /// <summary>A decorated record the mediator never dispatches.</summary>
    [RequirePermission("orders.write")]
    public sealed record DecoratedNonRequestRecord;

    /// <summary>A base type carrying the declaration for the requests deriving from it.</summary>
    [RequirePermission("orders.write")]
    public abstract record PermissionDeclaringBase;

    /// <summary>A command declaring its permission only through its base type.</summary>
    public sealed record InheritingDeclaringCommand : PermissionDeclaringBase, ICommand<string>;

    /// <summary>One of two scopes holding a request of the same simple name.</summary>
    public static class First
    {
        /// <summary>A declaring command whose simple name is not unique.</summary>
        [RequirePermission("orders.write")]
        public sealed record NamesakeCommand : ICommand<string>;
    }

    /// <summary>The other scope holding a request of the same simple name.</summary>
    public static class Second
    {
        /// <summary>A declaring command whose simple name is not unique.</summary>
        [RequirePermission("orders.write")]
        public sealed record NamesakeCommand : ICommand<string>;
    }

    /// <summary>A behavior that enforces nothing, for the registration-shape tests.</summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    public sealed class NoOpBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    {
        /// <inheritdoc />
        public Task<TResponse> HandleAsync(
            TRequest request,
            Func<Task<TResponse>> next,
            CancellationToken ct = default) => next();
    }

    /// <summary>The behavior the probe catalog names for the authorization slot.</summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    public sealed class EnforcingProbeBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    {
        /// <inheritdoc />
        public Task<TResponse> HandleAsync(
            TRequest request,
            Func<Task<TResponse>> next,
            CancellationToken ct = default) => next();
    }

    /// <summary>
    /// Handlers for the request types above. They exist because the assembly-wide handler check of
    /// <c>MediatorStartupValidator</c> scans this assembly too, and a request without a handler is a
    /// failure there.
    /// </summary>
    public sealed class DeclaringCommandHandlers :
        ICommandHandler<PermissionDeclaringCommand, string>,
        ICommandHandler<PlainCommand, string>,
        ICommandHandler<InheritingDeclaringCommand, string>,
        ICommandHandler<First.NamesakeCommand, string>,
        ICommandHandler<Second.NamesakeCommand, string>
    {
        /// <inheritdoc />
        public Task<string> HandleAsync(First.NamesakeCommand command, CancellationToken ct = default) =>
            Task.FromResult("handled");

        /// <inheritdoc />
        public Task<string> HandleAsync(Second.NamesakeCommand command, CancellationToken ct = default) =>
            Task.FromResult("handled");

        /// <inheritdoc />
        public Task<string> HandleAsync(PermissionDeclaringCommand command, CancellationToken ct = default) =>
            Task.FromResult("handled");

        /// <inheritdoc />
        public Task<string> HandleAsync(PlainCommand command, CancellationToken ct = default) =>
            Task.FromResult("handled");

        /// <inheritdoc />
        public Task<string> HandleAsync(InheritingDeclaringCommand command, CancellationToken ct = default) =>
            Task.FromResult("handled");
    }
}
