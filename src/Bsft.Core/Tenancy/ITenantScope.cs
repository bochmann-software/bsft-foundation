namespace Bsft.Core.Tenancy;

/// <summary>A dependency-injection scope created for one tenant by an <see cref="ITenantScopeFactory"/>.</summary>
public interface ITenantScope : IDisposable, IAsyncDisposable
{
    /// <summary>Gets the tenant the scope was created for.</summary>
    string TenantId { get; }

    /// <summary>Gets the provider of the scope's services.</summary>
    IServiceProvider ServiceProvider { get; }
}
