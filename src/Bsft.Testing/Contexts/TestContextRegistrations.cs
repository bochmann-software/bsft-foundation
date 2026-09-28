using Bsft.Correlation;
using Bsft.Data.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bsft.Testing.Contexts;

/// <summary>
/// Swaps the production tenant and correlation contexts for their test doubles.
/// </summary>
/// <remarks>
/// The public entry points in <c>Bsft.Testing.DependencyInjection</c> check their arguments and
/// delegate here, and the harnesses and the web application factory call these methods directly, so
/// a feature of this package never depends on the namespace that wires it.
/// </remarks>
internal static class TestContextRegistrations
{
    /// <summary>Replaces the tenant context with <paramref name="tenantContext"/>.</summary>
    /// <param name="services">The service collection to modify.</param>
    /// <param name="tenantContext">The double, registered for the concrete type as well.</param>
    public static void ReplaceTenant(IServiceCollection services, TestTenantContext tenantContext)
    {
        services.RemoveAll<TestTenantContext>();
        services.RemoveAll<ITenantContext>();

        services.AddSingleton(tenantContext);
        services.AddSingleton<ITenantContext>(tenantContext);
    }

    /// <summary>Replaces the correlation context with <paramref name="correlationContext"/>.</summary>
    /// <param name="services">The service collection to modify.</param>
    /// <param name="correlationContext">The double, registered for the concrete type as well.</param>
    public static void ReplaceCorrelation(IServiceCollection services, TestCorrelationContext correlationContext)
    {
        services.RemoveAll<TestCorrelationContext>();
        services.RemoveAll<CorrelationContext>();
        services.RemoveAll<ICorrelationContext>();

        services.AddSingleton(correlationContext);
        services.AddSingleton<CorrelationContext>(correlationContext);
        services.AddSingleton<ICorrelationContext>(correlationContext);
    }

    /// <summary>Replaces the tenant and correlation contexts, each with its default double.</summary>
    /// <param name="services">The service collection to modify.</param>
    public static void ReplaceAllWithDefaults(IServiceCollection services)
    {
        ReplaceTenant(services, new TestTenantContext());
        ReplaceCorrelation(services, new TestCorrelationContext());
    }
}
