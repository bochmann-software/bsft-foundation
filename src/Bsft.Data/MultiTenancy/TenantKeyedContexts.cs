using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Data.MultiTenancy;

/// <summary>Reads from a registered context whether its model is mapped with <c>UseTenantKeys</c>.</summary>
internal static class TenantKeyedContexts
{
    /// <summary>Every context type the host registered options for, once each.</summary>
    /// <param name="services">The service collection the host registered into.</param>
    /// <param name="lifetime">Only registrations of this lifetime, or <see langword="null"/> for any.</param>
    /// <returns>The context types.</returns>
    /// <remarks>
    /// Read from the descriptor alone: only <see cref="ServiceDescriptor.ServiceType"/> and
    /// <see cref="ServiceDescriptor.Lifetime"/>, which a keyed descriptor answers like any other.
    /// </remarks>
    public static IEnumerable<Type> Registered(IServiceCollection services, ServiceLifetime? lifetime = null) =>
        services
            .Where(descriptor =>
                (lifetime is null || descriptor.Lifetime == lifetime)
                && descriptor.ServiceType.IsGenericType
                && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(DbContextOptions<>))
            .Select(descriptor => descriptor.ServiceType.GetGenericArguments()[0])
            .Distinct();

    /// <summary>Constructs the context in a scope of its own and reads its model.</summary>
    /// <param name="scopes">Opens the scope the context is constructed in.</param>
    /// <param name="contextType">The context type.</param>
    /// <returns><see langword="true"/> when the model is mapped with <c>UseTenantKeys</c>; otherwise <see langword="false"/>, also when nothing constructs the context.</returns>
    /// <remarks>Whatever constructing the context throws is passed on; the caller decides what it means.</remarks>
    public static bool IsTenantKeyed(IServiceScopeFactory scopes, Type contextType)
    {
        using var scope = scopes.CreateScope();
        using var context = Create(scope.ServiceProvider, contextType);

        return context?.Model.FindAnnotation(TenantKeys.TenantKeyedAnnotation)?.Value is true;
    }

    /// <summary>Constructs the context the way the host's registration does.</summary>
    /// <param name="provider">The scoped provider to resolve from.</param>
    /// <param name="contextType">The context type.</param>
    /// <returns>The context, or <see langword="null"/> when nothing constructs one.</returns>
    private static DbContext? Create(IServiceProvider provider, Type contextType)
    {
        // AddDbContext, AddDbContextPool and AddDbContextFactory register the context itself; only
        // AddPooledDbContextFactory leaves it to the factory.
        if (provider.GetService(contextType) is DbContext context)
            return context;

        var factoryType = typeof(IDbContextFactory<>).MakeGenericType(contextType);

        return provider.GetService(factoryType) is { } factory
            ? (DbContext?)factoryType
                .GetMethod(nameof(IDbContextFactory<DbContext>.CreateDbContext), Type.EmptyTypes)!
                .Invoke(factory, BindingFlags.DoNotWrapExceptions, binder: null, parameters: null, culture: null)
            : null;
    }
}
