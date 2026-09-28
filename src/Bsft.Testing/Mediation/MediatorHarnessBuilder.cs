using Bsft.Mediator;
using Bsft.Mediator.DependencyInjection;
using Bsft.Testing.Contexts;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Testing.Mediation;

/// <summary>
/// The default <see cref="IMediatorHarnessBuilder"/>. It collects registrations and applies them in
/// the order they were made when <see cref="Build"/> assembles the container.
/// </summary>
public sealed class MediatorHarnessBuilder : IMediatorHarnessBuilder
{
    private readonly List<Action<IServiceCollection>> _registrations = [];

    /// <inheritdoc />
    public IMediatorHarnessBuilder WithHandler<THandler>()
        where THandler : class =>
        WithHandler(typeof(THandler));

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="handlerType"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the type is not a concrete class, or implements no handler interface at all.
    /// </exception>
    public IMediatorHarnessBuilder WithHandler(Type handlerType)
    {
        if (handlerType is null)
            throw new ArgumentNullException(nameof(handlerType));
        if (!handlerType.IsClass || handlerType.IsAbstract)
            throw new ArgumentException($"'{handlerType.Name}' is not a concrete class.", nameof(handlerType));

        var interfaces = handlerType.GetInterfaces().Where(IsHandlerInterface).ToList();
        if (interfaces.Count == 0)
        {
            throw new ArgumentException(
                $"'{handlerType.Name}' implements no command, query, or notification handler interface.",
                nameof(handlerType));
        }

        _registrations.Add(services =>
        {
            foreach (var handlerInterface in interfaces)
            {
                services.AddTransient(handlerInterface, handlerType);
            }
        });

        return this;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="behaviorType"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the type does not implement <see cref="IPipelineBehavior{TRequest, TResponse}"/>.
    /// </exception>
    public IMediatorHarnessBuilder WithBehavior(Type behaviorType)
    {
        if (behaviorType is null)
            throw new ArgumentNullException(nameof(behaviorType));
        if (!behaviorType.IsClass || behaviorType.IsAbstract)
            throw new ArgumentException($"'{behaviorType.Name}' is not a concrete class.", nameof(behaviorType));

        var interfaces = behaviorType.GetInterfaces().Where(IsBehaviorInterface).ToList();
        if (interfaces.Count == 0)
        {
            throw new ArgumentException(
                $"'{behaviorType.Name}' does not implement {nameof(IPipelineBehavior<object, object>)}.",
                nameof(behaviorType));
        }

        // An open generic serves every request and response pair and is registered once against the
        // open interface. A closed behavior has to be registered against the closed interfaces it
        // implements, or the mediator would never resolve it into the pipeline.
        _registrations.Add(services =>
        {
            if (behaviorType.IsGenericTypeDefinition)
            {
                services.AddTransient(typeof(IPipelineBehavior<,>), behaviorType);
                return;
            }

            foreach (var behaviorInterface in interfaces)
            {
                services.AddTransient(behaviorInterface, behaviorType);
            }
        });

        return this;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="instance"/> is null.</exception>
    public IMediatorHarnessBuilder WithService<TService>(TService instance)
        where TService : class
    {
        _registrations.AddInstance(instance);
        return this;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is null.</exception>
    public IMediatorHarnessBuilder WithServices(Action<IServiceCollection> configure)
    {
        _registrations.AddConfiguration(configure);
        return this;
    }

    /// <inheritdoc />
    public IMediatorHarnessBuilder WithTestContexts()
    {
        _registrations.AddConfiguration(TestContextRegistrations.ReplaceAllWithDefaults);
        return this;
    }

    /// <inheritdoc />
    public IMediatorTestHarness Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        _registrations.ApplyTo(services);

        // The mediator is registered last so that the behaviors above are the ones it picks up, in
        // the order they were added.
        services.AddBsftMediator(_ => { });

        return new MediatorTestHarness(services.BuildServiceProvider(validateScopes: true));
    }

    private static bool IsHandlerInterface(Type candidate)
    {
        if (!candidate.IsGenericType)
            return false;

        var definition = candidate.GetGenericTypeDefinition();

        return definition == typeof(ICommandHandler<,>)
            || definition == typeof(ICommandHandler<>)
            || definition == typeof(IQueryHandler<,>)
            || definition == typeof(INotificationHandler<>);
    }

    private static bool IsBehaviorInterface(Type candidate) =>
        candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>);
}
