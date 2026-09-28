using System.Reflection;
using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Bsft.Mediator.Tests;

public class ServiceRegistrationTests
{
    [Fact]
    public void AddBsftMediator_RegistersMediator()
    {
        var services = new ServiceCollection();

        services.AddBsftMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestCommandHandler).Assembly));

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetService<IMediator>();

        mediator.Should().NotBeNull();
        mediator.Should().BeOfType<DefaultMediator>();
    }

    [Fact]
    public void AddBsftMediator_DiscoverCommandHandlers()
    {
        var services = new ServiceCollection();

        services.AddBsftMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestCommandHandler).Assembly));

        using var sp = services.BuildServiceProvider();
        var handler = sp.GetService<ICommandHandler<TestCommand, string>>();

        handler.Should().NotBeNull();
        handler.Should().BeOfType<TestCommandHandler>();
    }

    [Fact]
    public void AddBsftMediator_DiscoverVoidCommandHandlers()
    {
        var services = new ServiceCollection();

        services.AddBsftMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestVoidCommandHandler).Assembly));

        using var sp = services.BuildServiceProvider();
        var handler = sp.GetService<ICommandHandler<TestVoidCommand>>();

        handler.Should().NotBeNull();
        handler.Should().BeOfType<TestVoidCommandHandler>();
    }

    [Fact]
    public void AddBsftMediator_DiscoverQueryHandlers()
    {
        var services = new ServiceCollection();

        services.AddBsftMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestQueryHandler).Assembly));

        using var sp = services.BuildServiceProvider();
        var handler = sp.GetService<IQueryHandler<TestQuery, string>>();

        handler.Should().NotBeNull();
        handler.Should().BeOfType<TestQueryHandler>();
    }

    [Fact]
    public void AddBsftMediator_DiscoverNotificationHandlers()
    {
        var services = new ServiceCollection();

        services.AddBsftMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestNotificationHandler1).Assembly));

        using var sp = services.BuildServiceProvider();
        var handlers = sp.GetServices<INotificationHandler<TestNotification>>().ToList();

        handlers.Should().HaveCount(2);
    }

    [Fact]
    public void AddBsftMediator_WithNullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;
        var configured = false;

        var act = () => services.AddBsftMediator(_ => configured = true);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
        configured.Should().BeFalse("a missing collection is refused before the host's configuration runs");
    }

    [Fact]
    public void AddBsftMediator_WithNullConfigure_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftMediator(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configure");
    }

    [Fact]
    public void MediatorConfiguration_RegisterServicesFromAssembly_WithNull_ThrowsArgumentNullException()
    {
        var config = new MediatorConfiguration();

        var act = () => config.RegisterServicesFromAssembly(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("assembly");
    }

    [Fact]
    public void MediatorConfiguration_AddBehavior_WithNullType_ThrowsArgumentNullException()
    {
        var config = new MediatorConfiguration();

        var act = () => config.AddBehavior(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("behaviorType");
    }

    [Fact]
    public void MediatorConfiguration_AddBehavior_WithClosedGenericType_ThrowsArgumentException()
    {
        // A behavior closed over a concrete request type can never enter the pipeline, because the
        // mediator resolves behaviors by the static request type. Registering it used to succeed and
        // do nothing; it is rejected now.
        var config = new MediatorConfiguration();

        var act = () => config.AddBehavior(typeof(TestPipelineBehavior<TestCommand, string>));

        act.Should().Throw<ArgumentException>()
            .WithParameterName("behaviorType")
            .WithMessage("*open generic type definition*");
    }

    [Fact]
    public void MediatorConfiguration_AddBehavior_WithWrongArity_ThrowsArgumentException()
    {
        var config = new MediatorConfiguration();

        var act = () => config.AddBehavior(typeof(SingleParameterBehavior<>));

        act.Should().Throw<ArgumentException>()
            .WithParameterName("behaviorType")
            .WithMessage("*exactly two type parameters*");
    }

    [Fact]
    public void MediatorConfiguration_AddBehavior_WithNonBehaviorType_ThrowsArgumentException()
    {
        var config = new MediatorConfiguration();

        var act = () => config.AddBehavior(typeof(NotABehavior<,>));

        act.Should().Throw<ArgumentException>()
            .WithParameterName("behaviorType")
            .WithMessage("*IPipelineBehavior*");
    }

    [Fact]
    public void MediatorConfiguration_AddBehavior_WithAGenericContractThatIsNoBehavior_ThrowsArgumentException()
    {
        // Implementing some generic interface is not implementing the pipeline behavior.
        var config = new MediatorConfiguration();

        var act = () => config.AddBehavior(typeof(ComparingNotABehavior<,>));

        act.Should().Throw<ArgumentException>()
            .WithParameterName("behaviorType")
            .WithMessage("*must implement IPipelineBehavior*");
    }

    [Fact]
    public void AddBsftMediator_WithAnAbstractHandlerInTheScannedAssembly_RegistersOnlyTheConcreteHandler()
    {
        // The container cannot activate an abstract type, so registering it would fail the first
        // dispatch; the concrete handler deriving from it is the one that handles the command.
        var services = new ServiceCollection();

        services.AddBsftMediator(cfg => cfg.RegisterServicesFromAssembly(typeof(AbstractRegistrationProbeHandlerBase).Assembly));

        services.Where(descriptor => descriptor.ServiceType == typeof(ICommandHandler<AbstractRegistrationProbeCommand, string>))
            .Select(descriptor => descriptor.ImplementationType)
            .Should().Equal(typeof(AbstractRegistrationProbeHandler));
    }

    [Fact]
    public void MediatorConfiguration_AddBehavior_OpenGenericDefinition_RegistersAsOpenGeneric()
    {
        var services = new ServiceCollection();

        services.AddBsftMediator(cfg => cfg.AddBehavior(typeof(OpenGenericTestBehavior<,>)));

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IPipelineBehavior<,>) &&
            sd.ImplementationType == typeof(OpenGenericTestBehavior<,>));
    }

    [Fact]
    public async Task MediatorConfiguration_AddBehavior_OpenGenericDefinition_RunsInThePipeline()
    {
        // Uses a probe type of its own rather than TestPipelineBehavior, whose static log is shared
        // with the pipeline tests that run in parallel with this class.
        var services = new ServiceCollection();
        services.AddBsftMediator(cfg => cfg
            .RegisterServicesFromAssembly(typeof(TestCommandHandler).Assembly)
            .AddBehavior(typeof(PipelineProbeBehavior<,>)));

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMediator>().SendAsync<string>(new TestCommand("piped"));

        PipelineProbeBehavior<ICommand<string>, string>.ExecutionLog.Should()
            .Equal("Before:ICommand`1", "After:ICommand`1");
    }

    [Fact]
    public async Task AddBsftMediator_EndToEnd_CommandDispatch()
    {
        var services = new ServiceCollection();
        services.AddBsftMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestCommandHandler).Assembly));

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.SendAsync<string>(new TestCommand("e2e"));

        result.Should().Be("Handled: e2e");
    }

    [Fact]
    public void MediatorConfiguration_RegisterHandlers_AssemblyWithReflectionLoadError_PartialTypes()
    {
        // Exercises MediatorConfiguration.RegisterHandlers lines 71-74:
        // ReflectionTypeLoadException catch block.
        // We mock an Assembly whose GetTypes() throws ReflectionTypeLoadException.
        // The catch block in RegisterHandlers should handle it and use the partial types.
        var mockAssembly = Substitute.For<Assembly>();
        var loadedException = new ReflectionTypeLoadException(
            [typeof(TestCommandHandler)],  // partial type: a handler type
            [new Exception("load error")]);
        mockAssembly.When(a => a.GetTypes()).Throw(loadedException);

        var config = new MediatorConfiguration();
        config.RegisterServicesFromAssembly(mockAssembly);

        var services = new ServiceCollection();
        var registerHandlersMethod = typeof(MediatorConfiguration)
            .GetMethod("RegisterHandlers", BindingFlags.Instance | BindingFlags.NonPublic)!;

        // Should not throw; the ReflectionTypeLoadException is caught and partial types are used
        var act = () => registerHandlersMethod.Invoke(config, [services]);
        act.Should().NotThrow();
    }

    [Fact]
    public void AddBsftMediator_AfterTheHostRegisteredItsOwnDispatcher_KeepsTheHostsOne()
    {
        // IMediator is documented as replaceable, and a host that registers before this call would
        // otherwise be resolved past: the container hands out the last registration.
        var services = new ServiceCollection();
        services.AddScoped<IMediator, SubstituteMediator>();

        services.AddBsftMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestCommandHandler).Assembly));

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        scope.ServiceProvider.GetService<IMediator>().Should().BeOfType<SubstituteMediator>();
    }

    [Fact]
    public void AddBsftMediator_AfterTheHostRegisteredItsOwnTelemetry_KeepsTheHostsOne()
    {
        // The emitter is the documented seam for sending somewhere other than the shipped source.
        var services = new ServiceCollection();
        var telemetry = Substitute.For<IMediatorTelemetry>();
        services.AddSingleton(telemetry);

        services.AddBsftMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestCommandHandler).Assembly));

        using var sp = services.BuildServiceProvider();

        sp.GetService<IMediatorTelemetry>().Should().BeSameAs(telemetry);
    }
}
