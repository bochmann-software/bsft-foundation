using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Tests.Shared;

/// <summary>
/// A scope factory that signals the first scope it hands out, so a test of a hosted service can wait
/// for the service's loop to have run instead of racing it, and counts every scope, so a test can see
/// how many passes a loop that opens a scope per pass has run.
/// </summary>
/// <remarks>
/// <para>
/// Since Microsoft.Extensions.Hosting 10, <c>BackgroundService.StartAsync</c> runs the whole of
/// <c>ExecuteAsync</c> on the thread pool, so a stop right after the start can come before the loop
/// has begun, and with an already cancelled stopping token the loop never begins at all. A loop that
/// opens a scope for every pass has provably begun once it opens the first one.
/// </para>
/// <para>
/// Linked into each test project that needs it rather than shipped from a package: it is test
/// scaffolding and nothing a consumer of the framework would ever ask for.
/// </para>
/// </remarks>
internal sealed class SignallingScopeFactory : IServiceScopeFactory
{
    private readonly IServiceScopeFactory _inner;
    private readonly TaskCompletionSource _firstScope = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _created;

    public SignallingScopeFactory(IServiceScopeFactory inner) => _inner = inner;

    /// <summary>Completes when the first scope has been created.</summary>
    public Task FirstScope => _firstScope.Task;

    /// <summary>How many scopes have been created so far.</summary>
    public int Created => Volatile.Read(ref _created);

    public IServiceScope CreateScope()
    {
        var scope = _inner.CreateScope();
        Interlocked.Increment(ref _created);
        _firstScope.TrySetResult();
        return scope;
    }
}
