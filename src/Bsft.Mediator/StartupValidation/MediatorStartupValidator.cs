using Bsft.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Mediator.StartupValidation;

/// <summary>
/// Startup validator that verifies every command and query type discovered in the configured
/// assemblies has a corresponding handler registered in the DI container.
/// </summary>
public sealed class MediatorStartupValidator : IStartupValidator
{
    private readonly MediatorConfiguration _config;
    private readonly IServiceProvider _serviceProvider;
    private readonly MediatorTypeScanner _scanner;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediatorStartupValidator"/> class.
    /// </summary>
    /// <param name="configuration">The mediator configuration containing the assemblies to scan.</param>
    /// <param name="serviceProvider">The service provider for resolving handler registrations.</param>
    /// <param name="scanner">
    /// The shared reflection pass, so this validator and the coverage check walk the assemblies once
    /// between them.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    internal MediatorStartupValidator(
        MediatorConfiguration configuration,
        IServiceProvider serviceProvider,
        MediatorTypeScanner scanner)
    {
        _config = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
    }

    /// <summary>
    /// Validates that every command and query type found in the configured assemblies has
    /// a registered handler. Reports errors for any missing handlers.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A <see cref="StartupValidationResult"/> indicating whether validation passed.</returns>
    public Task<StartupValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var commandCount = 0;
        var queryCount = 0;

        foreach (var scanned in _scanner.ScanTypes())
        {
            if (scanned.Type.IsAbstract || scanned.Type.IsInterface)
                continue;

            ValidateType(scanned, errors, ref commandCount, ref queryCount);
        }

        ValidateNotificationForwarding(errors);
        ValidateBehaviorRegistrations(errors);
        ValidateDefaultBehaviors(errors);

        if (errors.Count > 0)
            return Task.FromResult(StartupValidationResult.Failure(errors.ToArray()));

        var message = $"Validated {commandCount} command(s) and {queryCount} query/queries, all handlers registered.";
        return Task.FromResult(StartupValidationResult.Success(message));
    }

    private void ValidateType(ScannedType scanned, List<string> errors, ref int commandCount, ref int queryCount)
    {
        var type = scanned.Type;

        foreach (var iface in scanned.Interfaces)
        {
            if (!iface.IsGenericType)
                continue;

            var genDef = iface.GetGenericTypeDefinition();
            var resultType = iface.GetGenericArguments()[0];

            if (genDef == typeof(ICommand<>))
            {
                commandCount++;
                ValidateCommandHandler(type, resultType, errors);
            }
            else if (genDef == typeof(IQuery<>))
            {
                queryCount++;
                ValidateQueryHandler(type, resultType, errors);
            }
        }
    }

    /// <summary>
    /// Reports a configuration that enables notification forwarding without a usable bridge to
    /// carry it — either none registered at all, or one that cannot be activated — so the gap
    /// surfaces at startup instead of as notifications going nowhere.
    /// </summary>
    private void ValidateNotificationForwarding(List<string> errors)
    {
        if (!_config.ShouldForwardNotifications)
            return;

        using var scope = _serviceProvider.CreateScope();

        try
        {
            if (scope.ServiceProvider.GetService<INotificationForwarder>() is null)
                errors.Add(MediatorConfiguration.MissingForwarderMessage);
        }
        // A forwarder registered without the dependency it needs fails on activation, which is a
        // configuration gap of exactly the kind this validator exists to report. It belongs in the
        // aggregated report instead of faulting ValidateAsync and hiding every other finding with
        // it. InvalidOperationException is what the container raises for an unsatisfiable
        // dependency; a forwarder whose own constructor throws is a broken forwarder rather than a
        // wiring gap and still fails loudly.
        catch (InvalidOperationException ex)
        {
            errors.Add($"The registered INotificationForwarder could not be activated: {ex.Message}");
        }
    }

    /// <summary>
    /// Reports a pipeline behavior that is registered more than once. Two registrations of the same
    /// behavior run it twice per request — two nested tracing spans, two audit entries for one
    /// operation — which is invisible at runtime and always a wiring mistake.
    /// </summary>
    private void ValidateBehaviorRegistrations(List<string> errors)
    {
        var registrations = _serviceProvider.GetService<PipelineBehaviorRegistrations>();
        if (registrations is null)
            return;

        foreach (var behaviorName in registrations.ListDuplicates())
        {
            errors.Add(
                $"""
                 The pipeline behavior '{behaviorName}' is registered more than once and would run
                 twice per request. Register it once, or rely on UseDefaults() alone for a behavior
                 the default chain already contains.
                 """);
        }
    }

    /// <summary>
    /// Reports a default behavior whose package is referenced — so the defaults registered it — but
    /// whose own services were never registered, naming the call that registers them. Without this
    /// the gap surfaces on the first dispatch, as a container error about a dependency of a type the
    /// host never mentioned.
    /// </summary>
    private void ValidateDefaultBehaviors(List<string> errors)
    {
        if (!_config.ShouldUseDefaults)
            return;

        using var scope = _serviceProvider.CreateScope();

        foreach (var slot in _config.Catalog.Available(_config.SuppressedDefaults))
        {
            ValidateDefaultBehavior(scope.ServiceProvider, slot.Key, slot.Value, errors);
        }
    }

    private static void ValidateDefaultBehavior(
        IServiceProvider provider,
        DefaultBehaviorDescriptor descriptor,
        Type behaviorType,
        List<string> errors)
    {
        // The behavior is an open generic that the container closes per dispatch; one closure stands
        // in for all of them, because a behavior's dependencies do not vary with the request type it
        // is closed over.
        var closedType = CloseForActivationOrNull(behaviorType);

        // A behavior whose request parameter no candidate satisfies cannot be activated here at all;
        // reporting it would be a diagnostic about this check rather than about the host's wiring.
        if (closedType is null)
            return;

        try
        {
            ActivatorUtilities.CreateInstance(provider, closedType);
        }
        // An unsatisfiable dependency is what the container raises InvalidOperationException for,
        // and it is the whole point of this check: the behavior package is referenced but its own
        // registration call was never made.
        catch (InvalidOperationException ex)
        {
            errors.Add(
                $"""
                 The default pipeline behavior '{descriptor.Name}' is registered by UseDefaults() but
                 cannot be activated: {ex.Message}
                 Call {descriptor.RegistrationCall}, or drop the behavior with
                 SuppressDefaultBehavior("{descriptor.Name}").
                 """);
        }
    }

    /// <summary>
    /// Closes a behavior over a request type it accepts. The command contract is tried first; a
    /// behavior that constrains its request further — the org authorization behavior requires
    /// <c>IRequireOrganizationPermission</c> — is closed over the constraint itself, so its dependencies are
    /// checked as well instead of the slot being skipped.
    /// </summary>
    private static Type? CloseForActivationOrNull(Type behaviorType)
    {
        var requestParameter = behaviorType.GetGenericArguments()[0];
        var candidates = new List<Type> { typeof(ICommand<Unit>) };
        candidates.AddRange(requestParameter.GetGenericParameterConstraints());

        foreach (var candidate in candidates)
        {
            if (CloseOrNull(behaviorType, candidate) is { } closedType)
                return closedType;
        }

        return null;
    }

    private static Type? CloseOrNull(Type behaviorType, Type requestType)
    {
        try
        {
            return behaviorType.MakeGenericType(requestType, typeof(Unit));
        }
        // Closing a type over an argument its constraints exclude is reported as an ArgumentException
        // wrapping a TypeLoadException; here it means this candidate is not the behavior's request.
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Checks that exactly one handler is registered for a command.</summary>
    /// <param name="commandType">The command type.</param>
    /// <param name="resultType">The result the command returns.</param>
    /// <param name="errors">The collected errors.</param>
    /// <remarks>
    /// A command that returns nothing extends <c>ICommand&lt;Unit&gt;</c>, so it may be handled
    /// through either shape. That is not an ambiguity: dispatch looks for the resultless handler
    /// first and only falls back, so each shape is counted on its own and either one satisfies the
    /// command.
    /// </remarks>
    private void ValidateCommandHandler(Type commandType, Type resultType, List<string> errors)
    {
        using var scope = _serviceProvider.CreateScope();

        var registered = CountRegistered(
            scope.ServiceProvider, typeof(ICommandHandler<,>).MakeGenericType(commandType, resultType),
            "command", commandType, errors);

        if (typeof(ICommand).IsAssignableFrom(commandType))
        {
            registered += CountRegistered(
                scope.ServiceProvider, typeof(ICommandHandler<>).MakeGenericType(commandType),
                "command", commandType, errors);
        }

        if (registered == 0)
            errors.Add($"No handler registered for command '{commandType.Name}'.");
    }

    /// <summary>Checks that exactly one handler is registered for a query.</summary>
    /// <param name="queryType">The query type.</param>
    /// <param name="resultType">The result the query returns.</param>
    /// <param name="errors">The collected errors.</param>
    private void ValidateQueryHandler(Type queryType, Type resultType, List<string> errors)
    {
        using var scope = _serviceProvider.CreateScope();

        var registered = CountRegistered(
            scope.ServiceProvider, typeof(IQueryHandler<,>).MakeGenericType(queryType, resultType),
            "query", queryType, errors);

        if (registered == 0)
            errors.Add($"No handler registered for query '{queryType.Name}'.");
    }

    /// <summary>Counts the registrations of one closed handler interface, reporting a second one.</summary>
    /// <param name="provider">The scoped service provider.</param>
    /// <param name="handlerType">The closed handler interface.</param>
    /// <param name="kind">The word naming the request's kind in the message.</param>
    /// <param name="requestType">The request type.</param>
    /// <param name="errors">The collected errors.</param>
    /// <returns>The number of registrations.</returns>
    /// <remarks>
    /// A second handler of one shape is as much a wiring mistake as a missing one, and a quieter
    /// one: dispatch resolves a single service, so the container hands the mediator whichever
    /// registration came last and the other handler simply never runs. The behavior registrations
    /// are checked for the same thing, for the same reason.
    /// </remarks>
    private static int CountRegistered(
        IServiceProvider provider, Type handlerType, string kind, Type requestType, List<string> errors)
    {
        var registered = provider.GetServices(handlerType).Count();

        if (registered > 1)
            errors.Add($"More than one handler is registered for {kind} '{requestType.Name}'.");

        return registered;
    }
}
