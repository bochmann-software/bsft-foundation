using System.Reflection;
using Bsft.Core;
using Bsft.Mediator.DependencyInjection;
using Bsft.Mediator.StartupValidation;
using Bsft.Mediator.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Bsft.Mediator.Tests.StartupValidation;

public class MediatorStartupValidatorTests
{
    [Fact]
    public async Task ValidateAsync_WithAllHandlersRegistered_ReturnsSuccess()
    {
        // AddBsftMediator scans the assembly for both command/query types and their handlers,
        // so all discoverable types will have matching handler registrations.
        var services = new ServiceCollection();
        services.AddBsftMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestCommandHandler).Assembly));

        using var provider = services.BuildServiceProvider();
        var validator = provider.GetServices<IStartupValidator>()
            .OfType<MediatorStartupValidator>()
            .Single();

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_WithForwardingButNoForwarder_ReturnsFailure()
    {
        var services = new ServiceCollection();
        services.AddBsftMediator(cfg => cfg.ForwardNotifications());

        using var provider = services.BuildServiceProvider();
        var validator = provider.GetServices<IStartupValidator>()
            .OfType<MediatorStartupValidator>()
            .Single();

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain("INotificationForwarder");
    }

    [Fact]
    public async Task ValidateAsync_WithForwardingAndAForwarder_ReturnsSuccess()
    {
        var services = new ServiceCollection();
        services.AddBsftMediator(cfg => cfg.ForwardNotifications());
        services.AddSingleton<INotificationForwarder>(new RecordingForwarder());

        using var provider = services.BuildServiceProvider();
        var validator = provider.GetServices<IStartupValidator>()
            .OfType<MediatorStartupValidator>()
            .Single();

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WithAForwarderThatCannotBeActivated_ReturnsFailure()
    {
        // A forwarder registered without the dependency it needs fails on activation. The validator
        // exists to report configuration gaps, so this one belongs in the aggregated error report
        // rather than faulting ValidateAsync and hiding every other finding with it.
        var services = new ServiceCollection();
        services.AddBsftMediator(cfg => cfg.ForwardNotifications());
        services.AddSingleton<INotificationForwarder, DependentForwarder>();

        using var provider = services.BuildServiceProvider();
        var validator = provider.GetServices<IStartupValidator>()
            .OfType<MediatorStartupValidator>()
            .Single();

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain(nameof(DependentForwarder));
    }

    [Fact]
    public async Task ValidateAsync_WithMissingCommandHandler_ReturnsFailure()
    {
        // Scan the assembly to discover command types, but do not register any handlers.
        var config = new MediatorConfiguration();
        config.RegisterServicesFromAssembly(typeof(TestCommand).Assembly);

        var services = new ServiceCollection();
        services.AddSingleton(config);

        using var provider = services.BuildServiceProvider();
        var validator = new MediatorStartupValidator(config, provider, new MediatorTypeScanner(config));

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TestCommand"));
    }

    [Fact]
    public async Task ValidateAsync_WithTwoHandlersForOneCommand_ReturnsFailure()
    {
        // Dispatch resolves a single service, so a second registration means the container decides
        // which handler runs and the other one silently never does.
        var services = new ServiceCollection();
        services.AddBsftMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestCommandHandler).Assembly));
        services.AddTransient<ICommandHandler<TestCommand, string>, TestCommandHandler>();

        using var provider = services.BuildServiceProvider();
        var validator = provider.GetServices<IStartupValidator>()
            .OfType<MediatorStartupValidator>()
            .Single();

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Be($"More than one handler is registered for command '{nameof(TestCommand)}'.");
    }

    [Fact]
    public async Task ValidateAsync_WithMissingQueryHandler_ReturnsFailure()
    {
        var config = new MediatorConfiguration();
        config.RegisterServicesFromAssembly(typeof(TestQuery).Assembly);

        var services = new ServiceCollection();
        services.AddSingleton(config);

        using var provider = services.BuildServiceProvider();
        var validator = new MediatorStartupValidator(config, provider, new MediatorTypeScanner(config));

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TestQuery"));
    }

    [Fact]
    public async Task ValidateAsync_WithNoAssemblies_ReturnsSuccess()
    {
        var services = new ServiceCollection();
        services.AddBsftMediator(_ => { });

        using var provider = services.BuildServiceProvider();
        var validator = provider.GetServices<IStartupValidator>()
            .OfType<MediatorStartupValidator>()
            .Single();

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_AbstractCommandType_IsSkipped()
    {
        // Exercises the abstract/interface skip in MediatorStartupValidator.ValidateAsync:
        // if (type.IsAbstract || type.IsInterface) continue;
        // AbstractTestCommand is abstract and implements ICommand<string>.
        // It should be skipped (no error for missing handler), so validation returns success.
        var config = new MediatorConfiguration();
        config.RegisterServicesFromAssembly(typeof(AbstractTestCommand).Assembly);

        // Register handlers for all CONCRETE commands/queries to avoid other errors
        var services = new ServiceCollection();
        services.AddBsftMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(AbstractTestCommand).Assembly));
        services.AddSingleton(config);

        using var provider = services.BuildServiceProvider();
        var validator = new MediatorStartupValidator(config, provider, new MediatorTypeScanner(config));

        var result = await validator.ValidateAsync();

        // AbstractTestCommand should be skipped (is abstract), so no error for it
        result.Errors.Should().NotContain(e => e.Contains("AbstractTestCommand"));
    }

    [Fact]
    public async Task ValidateAsync_AssemblyGetTypesThrowsReflectionTypeLoadException_ReturnsPartialTypes()
    {
        // Exercises the ReflectionTypeLoadException fallback in MediatorStartupValidator.GetLoadableTypes:
        // When assembly.GetTypes() throws ReflectionTypeLoadException, the validator
        // falls back to ex.Types (filtered for non-null).
        // We use a mock Assembly whose GetTypes() throws the exception.
        var mockAssembly = Substitute.For<Assembly>();
        var loadedException = new ReflectionTypeLoadException(
            [typeof(AbstractTestCommand)],  // partially loaded types (non-null)
            [new Exception("load error")]);
        mockAssembly.When(a => a.GetTypes()).Throw(loadedException);

        var config = new MediatorConfiguration();
        config.RegisterServicesFromAssembly(mockAssembly);

        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();
        var validator = new MediatorStartupValidator(config, provider, new MediatorTypeScanner(config));

        // AbstractTestCommand is abstract -> skipped. No concrete commands found -> success.
        var result = await validator.ValidateAsync();

        // Should not throw; the exception is handled internally and partial types are used.
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ValidateAsync_WithDefaultsAndEveryBehaviorPackageRegistered_ReturnsSuccess()
    {
        var services = new ServiceCollection().AddDefaultBehaviorDependencies();
        services.AddBsftMediator(cfg => cfg.UseDefaults());

        using var provider = services.BuildServiceProvider();
        var validator = provider.GetServices<IStartupValidator>()
            .OfType<MediatorStartupValidator>()
            .Single();

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WithDefaultsAndAnUnregisteredBehaviorPackage_NamesTheMissingCall()
    {
        // Referencing Bsft.Caching.Mediator turns the behavior on; registering its services is still the
        // host's job. Reported here, with the call to make, instead of on the first dispatch.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftMediator(cfg => cfg.UseDefaults());

        using var provider = services.BuildServiceProvider();
        var validator = provider.GetServices<IStartupValidator>()
            .OfType<MediatorStartupValidator>()
            .Single();

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("AddBsftCachingMediator"));
    }

    [Fact]
    public async Task ValidateAsync_WithDefaultsAndASuppressedBehavior_DoesNotCheckIt()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftMediator(cfg => cfg
            .UseDefaults()
            .SuppressDefaultBehavior(MediatorDefaults.Caching));

        using var provider = services.BuildServiceProvider();
        var validator = provider.GetServices<IStartupValidator>()
            .OfType<MediatorStartupValidator>()
            .Single();

        var result = await validator.ValidateAsync();

        result.Errors.Should().NotContain(error => error.Contains("AddBsftCaching"));
    }

    [Fact]
    public async Task ValidateAsync_WithADefaultBehaviorNoRequestTypeSatisfies_SkipsThatSlot()
    {
        // The check closes a behavior over the command contract, or over its own request constraint
        // when it has one. A behavior neither fits cannot be activated here at all, and reporting it
        // would say something about this check rather than about the host's wiring.
        var catalog = new DefaultBehaviorCatalog(
        [
            new("Struct", "Bsft.Mediator.Tests.StructConstrainedBehavior`2, Bsft.Mediator.Tests", "AddNothing()")
        ]);

        var config = new MediatorConfiguration(catalog);
        config.UseDefaults();

        var services = new ServiceCollection();
        services.AddSingleton(config);

        using var provider = services.BuildServiceProvider();
        var validator = new MediatorStartupValidator(config, provider, new MediatorTypeScanner(config));

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WithEveryHandlerRegistered_CountsTheCommandsAndQueriesItValidated()
    {
        // Two commands - one of them resultless - and one query, each with its handler: the summary
        // is the evidence of what the check covered.
        var config = new MediatorConfiguration();
        config.RegisterServicesFromAssembly(AssemblyOf(typeof(TestCommand), typeof(TestVoidCommand), typeof(TestQuery)));

        var services = new ServiceCollection();
        services.AddTransient<ICommandHandler<TestCommand, string>, TestCommandHandler>();
        services.AddTransient<ICommandHandler<TestVoidCommand>, TestVoidCommandHandler>();
        services.AddTransient<IQueryHandler<TestQuery, string>, TestQueryHandler>();

        using var provider = services.BuildServiceProvider();
        var validator = new MediatorStartupValidator(config, provider, new MediatorTypeScanner(config));

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
        result.Message.Should().Be("Validated 2 command(s) and 1 query/queries, all handlers registered.");
    }

    [Fact]
    public async Task ValidateAsync_WithADefaultBehaviorClosedOverItsConstraintAndAMissingDependency_NamesTheMissingCall()
    {
        // The command contract does not satisfy this behavior's request constraint, so it is closed
        // over the constraint itself - and its unregistered dependency is reported, not skipped.
        var catalog = new DefaultBehaviorCatalog(
        [
            new("Constrained", "Bsft.Mediator.Tests.StartupValidation.ConstrainedProbeBehavior`2, Bsft.Mediator.Tests", "AddConstrainedProbe()")
        ]);

        var config = new MediatorConfiguration(catalog);
        config.UseDefaults();

        using var provider = new ServiceCollection().BuildServiceProvider();
        var validator = new MediatorStartupValidator(config, provider, new MediatorTypeScanner(config));

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain("AddConstrainedProbe()");
    }

    [Fact]
    public async Task ValidateAsync_WithABehaviorRegisteredTwice_ReturnsFailure()
    {
        // Two registrations run the behavior twice per request — two nested spans, two audit
        // entries — which is invisible at runtime.
        var services = new ServiceCollection();
        services.AddBsftMediator(_ => { });
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(InnerProbeBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(InnerProbeBehavior<,>));

        using var provider = services.BuildServiceProvider();
        var validator = provider.GetServices<IStartupValidator>()
            .OfType<MediatorStartupValidator>()
            .Single();

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain("InnerProbeBehavior");
    }

    /// <summary>An assembly reporting exactly the given types.</summary>
    private static Assembly AssemblyOf(params Type[] types)
    {
        var assembly = Substitute.For<Assembly>();
        assembly.GetTypes().Returns(types);
        return assembly;
    }
}
