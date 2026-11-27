using Bsft.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Data.MultiTenancy;

/// <summary>
/// Fails application startup when a context is mapped with <c>UseTenantKeys</c> but the host never
/// called <c>AddBsftDataTenantKeys</c>.
/// </summary>
/// <remarks>
/// <para>
/// The switch has two halves: <c>modelBuilder.UseTenantKeys()</c> in the model, and
/// <c>AddBsftDataTenantKeys</c> in the container. Only the second registers the node id the ids carry
/// and the check that refuses a pooled or factory-made tenant-keyed context
/// (<see cref="TenantKeyStartupValidator"/>). A host that made only the first would write rows that look
/// right while neither of those applied, so the half-made switch is refused with a sentence naming the
/// context and the missing call.
/// </para>
/// <para>
/// It is registered with multi-tenancy, which every tenant-keyed host has, and does nothing once
/// <c>AddBsftDataTenantKeys</c> has been called. Otherwise it constructs each registered context once,
/// in a scope of its own, to read its model; the model warm-up builds the same models at start anyway.
/// A context whose construction throws <see cref="InvalidOperationException"/> or
/// <see cref="ArgumentException"/> this early is passed without a word: a host without tenant keys must
/// not see a new warning, and a tenant-keyed one that cannot be read here fails its first write under
/// the same tenant rules as before. Any other exception fails the start.
/// </para>
/// </remarks>
public sealed class TenantKeyRegistrationStartupValidator : Bsft.Core.IStartupValidator
{
    private readonly IServiceCollection _services;
    private readonly IServiceScopeFactory _scopes;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantKeyRegistrationStartupValidator"/> class.
    /// </summary>
    /// <param name="services">The service collection the host is registering into.</param>
    /// <param name="scopes">Opens the scope each context is constructed in.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public TenantKeyRegistrationStartupValidator(IServiceCollection services, IServiceScopeFactory scopes)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
    }

    /// <inheritdoc />
    public Task<StartupValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<StartupValidationResult>(cancellationToken);

        if (_services.Any(descriptor => descriptor.ServiceType == typeof(TenantKeyRegistration)))
            return Task.FromResult(StartupValidationResult.Success("Tenant keys are registered with AddBsftDataTenantKeys."));

        var errors = new List<string>();

        foreach (var contextType in TenantKeyedContexts.Registered(_services))
        {
            if (!IsTenantKeyed(contextType))
                continue;

            errors.Add(
                $"""
                 {contextType.Name} maps its tables with UseTenantKeys, but AddBsftDataTenantKeys is not called, so the ids carry no configured node id and pooled or factory-made contexts are not checked. Call services.AddBsftDataTenantKeys(options => options.NodeId = ...) together with UseTenantKeys.
                 """);
        }

        return Task.FromResult(errors.Count > 0
            ? StartupValidationResult.Failure([.. errors])
            : StartupValidationResult.Success("No DbContext maps its tables with UseTenantKeys."));
    }

    /// <summary>Whether a context's model is mapped with <c>UseTenantKeys</c>.</summary>
    /// <param name="contextType">The context type.</param>
    /// <returns><see langword="true"/> when it is; <see langword="false"/> when it is not or could not be read.</returns>
    private bool IsTenantKeyed(Type contextType)
    {
        try
        {
            return TenantKeyedContexts.IsTenantKeyed(_scopes, contextType);
        }
        catch (InvalidOperationException)
        {
            // What the container throws for a dependency it cannot resolve this early.
            return false;
        }
        catch (ArgumentException)
        {
            // What a provider throws for a connection string the configuration does not hold yet.
            return false;
        }
    }
}
