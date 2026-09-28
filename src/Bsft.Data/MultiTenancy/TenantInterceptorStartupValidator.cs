using Bsft.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Data.MultiTenancy;

/// <summary>
/// Fails application startup when a tenant-scoped context was registered without
/// <see cref="TenantSaveChangesInterceptor"/> on its options.
/// </summary>
/// <remarks>
/// <para>
/// The read side of the isolation announces itself: a host that forgets
/// <c>UseTenantFilter</c> sees other tenants' rows the first time it looks. The write side does not.
/// <see cref="TenantSaveChangesInterceptor"/> is registered by <c>AddBsftDataMultiTenancy</c> but has
/// to be put on the context by the host, and a context that never got it stamps no tenant on an insert
/// and refuses no update that moves a row to another tenant. Nothing at runtime says so: the writes
/// simply succeed. That is what this check is for.
/// </para>
/// <para>
/// It reads the options the context is actually built with, and nothing else. An interceptor is not a
/// registration: it is a value inside <see cref="DbContextOptions"/>, which is where
/// <c>AddInterceptors</c> puts it, and a registration in the application's container is not a
/// substitute. Entity Framework Core does not go looking there: an <c>IInterceptor</c> or
/// <c>ISaveChangesInterceptor</c> that only the container knows about never runs, so a check that
/// accepted one would certify exactly the silent unstamped write it exists to prevent. The one host
/// shape where such a registration does run is the one that makes the application's container Entity
/// Framework Core's own, through <c>AddEntityFrameworkSqlServer</c> plus <c>UseInternalServiceProvider</c>;
/// that host is failed here, deliberately, because the check cannot tell it apart from the shape where
/// the registration does nothing. The context instance is asked rather than the registered options
/// object, so that an interceptor the host adds in
/// <see cref="DbContext.OnConfiguring(DbContextOptionsBuilder)"/> counts as well.
/// </para>
/// <para>
/// Options registered for the lifetime of the application are refused outright. The guard reads the
/// tenant off a scoped <c>ITenantContext</c>, so one resolved while building singleton options is
/// resolved from the root and keeps whatever tenant was there when it was built: every later request
/// would be stamped with the first one. It is the same failure <c>AddDbContextPool</c> produces and
/// that <see cref="PooledTenantContextStartupValidator"/> refuses, reached by a different route.
/// </para>
/// <para>
/// A context this check cannot read is reported as a warning and passed rather than failed. Reading it
/// means constructing it, which runs the host's own configuration callback and every constructor
/// dependency it has, and those may want a connection string or a resolved tenant that is not there yet
/// at startup; a check that stopped such a host would be worse than the gap it closes. The warning is
/// what keeps "could not be read" from looking like "is fine". For the same reason a context reached
/// only through <c>AddDbContextFactory</c> may go unexamined: it is discovered through its
/// <see cref="DbContextOptions{TContext}"/> registration, but that one is a singleton, so the scoped
/// interceptor its options action asks for cannot be resolved this early.
/// </para>
/// </remarks>
public sealed class TenantInterceptorStartupValidator : IStartupValidator
{
    private readonly IServiceCollection _services;
    private readonly IServiceScopeFactory _scopes;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantInterceptorStartupValidator"/> class.
    /// </summary>
    /// <param name="services">The service collection the host is registering into.</param>
    /// <param name="scopes">Opens the scope each context's options are read in.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <remarks>
    /// A scope factory rather than the container itself: the one thing this check does with the
    /// host's services is open a scope per context, and a validator holding the root provider is a
    /// validator that can resolve a scoped guard from the root - which is the very shape it exists
    /// to refuse.
    /// </remarks>
    public TenantInterceptorStartupValidator(IServiceCollection services, IServiceScopeFactory scopes)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
    }

    /// <inheritdoc />
    public Task<StartupValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<StartupValidationResult>(cancellationToken);

        var errors = new List<string>();
        var warnings = new List<string>();

        foreach (var descriptor in TenantScopedContexts())
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<StartupValidationResult>(cancellationToken);

            Examine(descriptor, errors, warnings, cancellationToken);
        }

        if (errors.Count > 0)
            return Task.FromResult(StartupValidationResult.Failure(errors, warnings));

        return Task.FromResult(StartupValidationResult.Success(
            "Every tenant-scoped DbContext this check could read carries the tenant save guard.",
            warnings));
    }

    /// <summary>Every options registration the host made for a tenant-scoped context.</summary>
    /// <returns>The context type and the lifetime its options were registered for, once each.</returns>
    /// <remarks>
    /// Only <see cref="ServiceDescriptor.ServiceType"/> and <see cref="ServiceDescriptor.Lifetime"/>
    /// are read, which a keyed descriptor answers like any other;
    /// <see cref="ServiceDescriptor.ImplementationType"/> would throw on one.
    /// </remarks>
    private IEnumerable<(Type ContextType, ServiceLifetime Lifetime)> TenantScopedContexts() =>
        _services
            .Where(descriptor =>
                descriptor.ServiceType.IsGenericType
                && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(DbContextOptions<>))
            .Select(descriptor => (ContextType: descriptor.ServiceType.GetGenericArguments()[0], descriptor.Lifetime))
            .Where(registration => registration.ContextType.IsAssignableTo(typeof(ITenantScopedDbContext)))
            .Distinct();

    /// <summary>Reads one context and records what it found.</summary>
    /// <param name="registration">The context type and the lifetime its options were registered for.</param>
    /// <param name="errors">Collects a context that is built without a usable guard.</param>
    /// <param name="warnings">Collects a context that could not be read.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    private void Examine(
        (Type ContextType, ServiceLifetime Lifetime) registration,
        List<string> errors,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var contextType = registration.ContextType;

        if (registration.Lifetime == ServiceLifetime.Singleton)
        {
            errors.Add(
                $"{contextType.Name} implements {nameof(ITenantScopedDbContext)} but its options are registered for the "
                + "lifetime of the application, which is what AddDbContextFactory and an explicit "
                + "optionsLifetime of Singleton do. A guard built into such options is resolved from the root "
                + "provider and keeps the tenant that was current when it was built, so every later request would "
                + "be stamped with the first one. Register the options per scope.");
            return;
        }

        CoreOptionsExtension? coreOptions;

        try
        {
            using var scope = _scopes.CreateScope();
            coreOptions = CoreOptions(scope.ServiceProvider, contextType);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Deliberately broad: everything behind that call is host code written for request time,
            // not for start-up, and what it throws is the host's business. What must not happen is that
            // it looks like a clean result, which is what the warning is for.
            warnings.Add(
                $"{contextType.Name} could not be read at start-up, so whether it carries "
                + $"{nameof(TenantSaveChangesInterceptor)} is unknown: {exception.Message}");
            return;
        }

        if (coreOptions is null)
            return;

        if (coreOptions.Interceptors?.OfType<TenantSaveChangesInterceptor>().Any() is true)
            return;

        errors.Add(
            $"{contextType.Name} implements {nameof(ITenantScopedDbContext)} but its options carry no "
            + $"{nameof(TenantSaveChangesInterceptor)}, so inserts are stamped with no tenant and an update moving a row "
            + "to another tenant is not refused. Add it where the context is registered: "
            + "AddDbContext<TContext>((sp, options) => options.UseX(...)"
            + $".AddInterceptors(sp.GetRequiredService<{nameof(TenantSaveChangesInterceptor)}>())).");
    }

    /// <summary>The core options of the context as it is actually built.</summary>
    /// <param name="provider">The scoped provider to resolve from.</param>
    /// <param name="contextType">The context type.</param>
    /// <returns>The extension carrying the interceptors, or <see langword="null"/> when nothing answers.</returns>
    private static CoreOptionsExtension? CoreOptions(IServiceProvider provider, Type contextType)
    {
        // The instance first: its options are the registered ones plus whatever OnConfiguring added.
        if (provider.GetService(contextType) is DbContext context)
        {
            return ((IInfrastructure<IServiceProvider>)context).Instance
                .GetRequiredService<IDbContextOptions>()
                .FindExtension<CoreOptionsExtension>();
        }

        return provider.GetService(typeof(DbContextOptions<>).MakeGenericType(contextType)) is DbContextOptions options
            ? options.FindExtension<CoreOptionsExtension>()
            : null;
    }
}
