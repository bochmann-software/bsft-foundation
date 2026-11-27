namespace Bsft.Mediator;

/// <summary>
/// Carries the state of a request's scope into a fresh scope the pipeline runs the request in again —
/// a retry attempt of a request over the shared transaction.
/// </summary>
/// <remarks>
/// <para>
/// A retry attempt that runs in a fresh scope starts with fresh scoped services: a new connection, new
/// contexts, nothing a failed attempt left behind. It also starts without what the host put into the
/// request's scope before the pipeline ran — the tenant a middleware resolved, the current user. A seed
/// copies that across: it is resolved from the request's own scope, so it takes the state it carries
/// in its constructor, and <see cref="Seed"/> writes it into the fresh scope.
/// </para>
/// <para>
/// Every registered seed runs right after the fresh scope is created, before anything resolves a
/// service from it — in particular before the ambient transaction and its connection, so a connection
/// factory that reads the tenant sees the seeded one.
/// </para>
/// </remarks>
public interface IRequestScopeSeed
{
    /// <summary>Writes the state of the request's scope into the fresh scope.</summary>
    /// <param name="serviceProvider">The provider of the fresh scope.</param>
    void Seed(IServiceProvider serviceProvider);
}
