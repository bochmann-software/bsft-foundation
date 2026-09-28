using Bsft.Testing.Contexts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Testing.Web;

/// <summary>
/// A <see cref="WebApplicationFactory{TEntryPoint}"/> that boots the application under test with the
/// Bsft test doubles already in place: a resolved tenant and a fixed correlation id, in the
/// <c>Testing</c> environment.
/// </summary>
/// <typeparam name="TEntryPoint">The entry point of the application under test.</typeparam>
/// <remarks>
/// <para>
/// The doubles are registered after the application has configured its own services, so they replace
/// the production registrations rather than competing with them. Reach them from a test through the
/// host: <c>factory.Services.GetRequiredService&lt;TestTenantContext&gt;()</c> returns the very
/// instance the application resolves, so switching the tenant mid-test changes what the next request
/// sees.
/// </para>
/// <para>
/// Doubles of another kind — a signed-in user and an authentication scheme that authenticates every
/// request as that user — belong to the package that owns the concern: a factory for them derives
/// from this type and adds them through <see cref="ConfigureTestDoubles"/>.
/// </para>
/// <para>
/// The database is deliberately not touched by default, because the base class cannot know which
/// contexts an application has. Override <see cref="ConfigureTestServices"/> and call
/// <c>services.AddBsftTestDatabase&lt;AppDbContext&gt;("name")</c> for each of them.
/// </para>
/// <para>
/// This type is designed for inheritance and is the intended way to use it: a test project derives a
/// factory of its own, overrides <see cref="ConfigureTestServices"/> to point the contexts at an
/// in-memory store and to swap whatever else that application needs, and shares the derived factory
/// across its tests.
/// </para>
/// </remarks>
public class BsftWebApplicationFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint>
    where TEntryPoint : class
{
    /// <summary>
    /// Gets the hosting environment the application is booted in. Defaults to <c>Testing</c>, which
    /// lets an application branch on <c>IsEnvironment("Testing")</c> for anything a test host must
    /// not do, such as running migrations against a real server.
    /// </summary>
    protected virtual string EnvironmentName => "Testing";

    /// <summary>Creates the tenant the application sees. Override to start in a different tenant.</summary>
    /// <returns>The tenant context double.</returns>
    protected virtual TestTenantContext CreateTenantContext() => new();

    /// <summary>Creates the correlation context the application sees.</summary>
    /// <returns>The correlation context double.</returns>
    protected virtual TestCorrelationContext CreateCorrelationContext() => new();

    /// <summary>
    /// Registers the test doubles the application boots with, before
    /// <see cref="ConfigureTestServices"/> runs. The base implementation registers the tenant and the
    /// correlation context.
    /// </summary>
    /// <param name="services">The service collection of the application under test.</param>
    /// <remarks>
    /// A factory that brings doubles of its own kind overrides this and calls the base first, so a
    /// test project's <see cref="ConfigureTestServices"/> still runs last and can replace any of them.
    /// </remarks>
    protected virtual void ConfigureTestDoubles(IServiceCollection services)
    {
        TestContextRegistrations.ReplaceTenant(services, CreateTenantContext());
        TestContextRegistrations.ReplaceCorrelation(services, CreateCorrelationContext());
    }

    /// <summary>
    /// Adds or replaces services after the Bsft defaults have been applied. The base implementation
    /// does nothing.
    /// </summary>
    /// <param name="services">The service collection of the application under test.</param>
    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is null.</exception>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (builder is null)
            throw new ArgumentNullException(nameof(builder));

        builder.UseEnvironment(EnvironmentName);
        builder.ConfigureTestServices(services =>
        {
            ConfigureTestDoubles(services);
            ConfigureTestServices(services);
        });
    }
}
