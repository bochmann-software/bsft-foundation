namespace Bsft.Core.Tenancy;

/// <summary>
/// The tenant a background pass is running for, carried along the pass's asynchronous flow so that every
/// dependency-injection scope the pass opens can find it.
/// </summary>
/// <remarks>
/// <para>
/// A request learns its tenant from the request; a background pass has no request. The services that open
/// scopes in a pass — a store opening one per batch, a dispatcher opening one per job — would otherwise each
/// need the tenant handed to them explicitly. Instead the pass enters the tenant here once
/// (<see cref="BackgroundTenantPasses"/> does that, by creating an instance and disposing it after the pass), and the tenant context of <c>Bsft.Data</c> reads it, once when it is created, in
/// every scope created inside the pass whose tenant was not set otherwise.
/// </para>
/// <para>
/// The value flows like an <see cref="AsyncLocal{T}"/>: into everything the pass awaits or starts, and back
/// out when the instance is disposed. It is not set during an HTTP request, so it never competes with
/// a tenant a request resolved.
/// </para>
/// </remarks>
public sealed class BackgroundTenant : IDisposable
{
    private static readonly AsyncLocal<string?> Tenant = new();

    private readonly string? _previous;
    private bool _isDisposed;

    /// <summary>
    /// Sets the tenant of the current asynchronous flow until this instance is disposed, which restores the
    /// tenant that was current before.
    /// </summary>
    /// <param name="tenantId">The tenant to run for.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="tenantId"/> is null, empty or white space.</exception>
    public BackgroundTenant(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("A background pass runs for a named tenant.", nameof(tenantId));

        _previous = Tenant.Value;
        TenantId = tenantId;
        Tenant.Value = tenantId;
    }

    /// <summary>Gets the tenant the current background pass runs for, or <see langword="null"/> outside one.</summary>
    public static string? Current => Tenant.Value;

    /// <summary>Gets the tenant this instance entered.</summary>
    public string TenantId { get; }

    /// <summary>Restores the tenant that was current before; a second call does nothing.</summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        Tenant.Value = _previous;
    }
}
