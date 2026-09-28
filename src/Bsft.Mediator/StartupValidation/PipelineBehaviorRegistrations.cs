using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Mediator.StartupValidation;

/// <summary>
/// Reads the pipeline behavior registrations back out of the service collection, so the startup
/// validator can report a behavior registered twice instead of letting it run twice — two nested
/// tracing spans per request, or two audit entries for one operation.
/// </summary>
/// <remarks>
/// The live collection is held rather than a copy of it: registrations made after
/// <c>AddBsftMediator</c> — a behavior package's own <c>AddBsft*</c> call, or a direct
/// <c>services.AddTransient(typeof(IPipelineBehavior&lt;,&gt;), ...)</c> — are exactly the ones a
/// snapshot would miss. Nothing here mutates it.
/// </remarks>
internal sealed class PipelineBehaviorRegistrations
{
    private readonly IServiceCollection _services;

    /// <summary>
    /// Initializes a new instance of the <see cref="PipelineBehaviorRegistrations"/> class.
    /// </summary>
    /// <param name="services">The service collection the mediator was registered into.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public PipelineBehaviorRegistrations(IServiceCollection services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// Reports whether a behavior implementation is registered against
    /// <see cref="IPipelineBehavior{TRequest, TResponse}"/> at all, so a check can tell a behavior
    /// that is in the pipeline from one the host never wired up.
    /// </summary>
    /// <remarks>
    /// Both wirings that name a type are recognised: the open-generic
    /// <c>AddTransient(typeof(IPipelineBehavior&lt;,&gt;), typeof(Behavior&lt;,&gt;))</c> and a
    /// closed-generic registration whose implementation closes over the same behavior.
    /// <para>
    /// A registration made through an <c>ImplementationFactory</c> delegate names no implementation
    /// type and is therefore undetectable — the container itself only learns what the delegate
    /// returns when it runs it. A caller reporting an absent behavior has to say so, because for
    /// such a host the finding is a phantom.
    /// </para>
    /// </remarks>
    /// <param name="implementationType">The open-generic behavior type to look for.</param>
    /// <returns><see langword="true"/> when the behavior is registered under a named implementation type.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="implementationType"/> is null.</exception>
    public bool IsRegistered(Type implementationType)
    {
        if (implementationType is null)
            throw new ArgumentNullException(nameof(implementationType));

        return _services.Any(descriptor => IsRegistrationOf(descriptor, implementationType));
    }

    /// <summary>
    /// Reports whether one descriptor puts the given behavior into the pipeline.
    /// </summary>
    private static bool IsRegistrationOf(ServiceDescriptor descriptor, Type implementationType)
    {
        if (!IsPipelineBehaviorService(descriptor.ServiceType))
            return false;

        var registered = descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType();
        if (registered is null)
            return false;

        return registered == implementationType ||
               (registered.IsGenericType &&
                !registered.IsGenericTypeDefinition &&
                registered.GetGenericTypeDefinition() == implementationType);
    }

    /// <summary>
    /// Reports whether a service type is the pipeline behavior contract, open or closed.
    /// </summary>
    private static bool IsPipelineBehaviorService(Type serviceType) =>
        serviceType.IsGenericType &&
        serviceType.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>);

    /// <summary>
    /// The names of the behavior implementations registered more than once against
    /// <see cref="IPipelineBehavior{TRequest, TResponse}"/>.
    /// </summary>
    /// <returns>The duplicated behavior type names, each reported once.</returns>
    public IReadOnlyList<string> ListDuplicates() =>
        _services
            .Where(descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(descriptor => descriptor.ImplementationType)
            .Where(implementationType => implementationType is not null)
            .GroupBy(implementationType => implementationType!)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key.Name)
            .ToList();
}
