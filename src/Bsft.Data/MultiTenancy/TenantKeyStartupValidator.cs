using Bsft.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bsft.Data.MultiTenancy;

/// <summary>
/// Fails application startup when a context mapped with <c>UseTenantKeys</c> is registered with
/// <c>AddDbContextPool</c> or <c>AddDbContextFactory</c>, and warns when such a context runs without a
/// configured node id.
/// </summary>
/// <remarks>
/// <para>
/// A tenant-keyed table takes the tenant of every new row from the scope the context was created in:
/// the request's <see cref="ITenantContext"/>, otherwise the running <c>BackgroundTenant</c>. A pooled
/// context and a context made by a factory are not created in that scope. Their options, and with them
/// the services the tenant is read from, belong to the root provider, so a row written through them
/// gets whatever tenant the root resolves, which is not the request's. Nothing at runtime signals it:
/// the insert succeeds under the wrong tenant. The combination is therefore refused with a sentence
/// naming the context, rather than left to a paragraph in the documentation.
/// </para>
/// <para>
/// Both registrations are recognised from their <see cref="ServiceDescriptor"/> alone: they register the
/// context's <see cref="Microsoft.EntityFrameworkCore.DbContextOptions{TContext}"/> for the lifetime of the application, where
/// <c>AddDbContext</c> registers them per scope. Only such a context is constructed, to read from its
/// model whether it is tenant-keyed, so a pooled context without <c>UseTenantKeys</c> is left alone and a
/// context registered per scope is never built by this check.
/// </para>
/// <para>
/// Every id of a tenant-keyed table carries the node id of the process that made it
/// (<see cref="TenantKeyOptions.NodeId"/>). Without one, each process draws its own at random, and two
/// processes against one database can draw the same; the check reports that as a warning, because a
/// single process is fine with it. The check is registered by <c>AddBsftDataTenantKeys</c> only, the call
/// a host makes when it switches tenant keys on.
/// </para>
/// <para>
/// Reading a context's model means constructing it, which runs the host's own configuration and
/// constructor dependencies. A context whose construction fails at startup for want of a service or a
/// configuration value (an <see cref="InvalidOperationException"/> or <see cref="ArgumentException"/>)
/// is reported as a warning and passed, so that "could not be read" never looks like "is fine"; any
/// other failure fails the check.
/// </para>
/// </remarks>
public sealed class TenantKeyStartupValidator : Bsft.Core.IStartupValidator
{
    private readonly IServiceCollection _services;
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptions<BsftDataOptions> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantKeyStartupValidator"/> class.
    /// </summary>
    /// <param name="services">The service collection the host is registering into.</param>
    /// <param name="scopes">Opens the scope each context is constructed in.</param>
    /// <param name="options">The data options carrying the node id.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public TenantKeyStartupValidator(
        IServiceCollection services, IServiceScopeFactory scopes, IOptions<BsftDataOptions> options)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public Task<StartupValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<StartupValidationResult>(cancellationToken);

        var errors = new List<string>();
        var warnings = new List<string>();

        // Only what the registration alone cannot answer is read from a context: whether a context
        // created outside a scope maps its tables with UseTenantKeys. A context registered per scope is
        // never constructed here.
        // AddDbContextPool, AddDbContextFactory, AddPooledDbContextFactory and an explicit singleton
        // options lifetime register the options for the lifetime of the application; AddDbContext per scope.
        foreach (var contextType in TenantKeyedContexts.Registered(_services, ServiceLifetime.Singleton))
        {
            if (!IsTenantKeyed(contextType, warnings))
                continue;

            errors.Add(
                $"""
                 {contextType.Name} maps its tables with UseTenantKeys but is registered with AddDbContextPool, AddDbContextFactory or options for the lifetime of the application. Such a context is not created in the request's scope, so the tenant of every row it inserts is read from the root provider and can be another tenant's. Register it with AddDbContext instead.
                 """);
        }

        if (_options.Value.TenantKeys.NodeId is null)
        {
            warnings.Add(
                """
                Tenant keys are switched on (AddBsftDataTenantKeys), but no node id is configured, so this process draws one at random. Two processes against one database can draw the same one and then make the same id; give each its own with AddBsftDataTenantKeys(options => options.NodeId = ...).
                """);
        }

        return Task.FromResult(errors.Count > 0
            ? StartupValidationResult.Failure(errors, warnings)
            : StartupValidationResult.Success("No tenant-keyed DbContext is created outside a scope.", warnings));
    }

    /// <summary>Whether a context's model is mapped with <c>UseTenantKeys</c>.</summary>
    /// <param name="contextType">The context type.</param>
    /// <param name="warnings">Collects a context that could not be constructed.</param>
    /// <returns><see langword="true"/> when it is; <see langword="false"/> when it is not or could not be read.</returns>
    private bool IsTenantKeyed(Type contextType, List<string> warnings)
    {
        try
        {
            return TenantKeyedContexts.IsTenantKeyed(_scopes, contextType);
        }
        catch (InvalidOperationException exception)
        {
            // What the container throws for a dependency it cannot resolve this early.
            AddUnreadable(contextType, exception, warnings);
            return false;
        }
        catch (ArgumentException exception)
        {
            // What a provider throws for a connection string the configuration does not hold yet.
            AddUnreadable(contextType, exception, warnings);
            return false;
        }
    }

    /// <summary>Records a context that could not be constructed.</summary>
    /// <param name="contextType">The context type.</param>
    /// <param name="exception">Why it could not be constructed.</param>
    /// <param name="warnings">Collects the warning.</param>
    private static void AddUnreadable(Type contextType, Exception exception, List<string> warnings) =>
        warnings.Add(
            $"{contextType.Name} could not be constructed at start-up, so whether it maps its tables with UseTenantKeys and is registered safely is unknown: {exception.Message}");
}
