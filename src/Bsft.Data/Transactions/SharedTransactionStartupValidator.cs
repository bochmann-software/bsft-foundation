using Bsft.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Data.Transactions;

/// <summary>
/// Fails startup when a host registered the shared transaction and the single-context unit of work
/// together, because a unit started through the latter would not join the shared transaction.
/// </summary>
/// <remarks>
/// <c>AddBsftDataSharedTransaction</c> registers <c>IUnitOfWork</c> and <c>ITransactionalUnitOfWork</c>
/// with <c>TryAdd</c>, so a host that called <c>AddBsftDataUnitOfWork&lt;TContext&gt;</c> first keeps
/// <see cref="DbContextUnitOfWork{TContext}"/> behind both. That unit joins only a transaction its
/// context already uses, and a context enlists lazily, at its first command — so a package that opens
/// a unit before the context ran one would begin a second transaction on the shared connection, which
/// the providers refuse. The collision is visible as a registration, so it is reported here rather than
/// on the first request that happens to hit it.
/// </remarks>
internal sealed class SharedTransactionStartupValidator : IStartupValidator
{
    private readonly IServiceCollection _services;

    /// <summary>
    /// Initializes a new instance of the <see cref="SharedTransactionStartupValidator"/> class.
    /// </summary>
    /// <param name="services">The live service collection, so a registration made after this one is seen too.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public SharedTransactionStartupValidator(IServiceCollection services) =>
        _services = services ?? throw new ArgumentNullException(nameof(services));

    /// <summary>Fails when a single-context unit of work is registered beside the shared transaction.</summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A result that fails only on the colliding registration.</returns>
    public Task<StartupValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        var colliding = _services
            .Select(descriptor => descriptor.ServiceType)
            .Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(DbContextUnitOfWork<>))
            .Select(type => type.GetGenericArguments()[0].Name)
            .Distinct()
            .ToList();

        if (colliding.Count == 0)
        {
            return Task.FromResult(StartupValidationResult.Success(
                "The shared transaction is the only unit of work registered."));
        }

        return Task.FromResult(StartupValidationResult.Failure(
            $"AddBsftDataSharedTransaction is registered together with AddBsftDataUnitOfWork<{string.Join(", ", colliding)}>. The shared transaction already provides IUnitOfWork and ITransactionalUnitOfWork across every enlisted context, while the single-context unit of work would open a second transaction on the shared connection. Remove the AddBsftDataUnitOfWork call."));
    }
}
