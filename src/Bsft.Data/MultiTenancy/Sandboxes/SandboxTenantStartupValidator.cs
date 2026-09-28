using Bsft.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Data.MultiTenancy.Sandboxes;

/// <summary>Fails startup when sandbox tenants are registered but nothing remembers them.</summary>
/// <remarks>
/// The seam's whole promise is an identifier nothing else uses, and only something durable can keep
/// that across a restart. There is deliberately no in-memory default to fall back on: it would look
/// like the seam working and would hand out a colliding identifier the first time the process
/// recycled. So the decision is the host's, and the refusal arrives at deployment rather than at the
/// first sandbox.
/// </remarks>
public sealed class SandboxTenantStartupValidator : IStartupValidator
{
    private readonly IServiceScopeFactory _scopes;

    /// <summary>Initializes a new instance of the <see cref="SandboxTenantStartupValidator"/> class.</summary>
    /// <param name="scopes">The factory a probe scope is opened from, so a registry registered per scope answers too.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scopes"/> is null.</exception>
    public SandboxTenantStartupValidator(IServiceScopeFactory scopes)
    {
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
    }

    /// <inheritdoc />
    public Task<StartupValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();

        if (scope.ServiceProvider.GetService<ISandboxTenantRegistry>() is not null)
            return Task.FromResult(StartupValidationResult.Success("A sandbox tenant registry is registered."));

        return Task.FromResult(StartupValidationResult.Failure(
            """
            AddBsftSandboxTenants was called but no ISandboxTenantRegistry is registered. A sandbox identifier is only isolated if something durable remembers it was handed out, so this package ships no default. Register one before the host starts.
            """));
    }
}
