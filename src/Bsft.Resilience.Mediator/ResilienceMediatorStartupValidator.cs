using Bsft.Core;
using Bsft.Core.Persistence;
using Bsft.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bsft.Resilience.Mediator;

/// <summary>
/// Refuses to start when <see cref="BsftResilienceMediatorOptions.ShouldRetryAllCommands"/> is on but not
/// every command is a transaction of its own, or no <see cref="IAmbientTransaction"/> is registered to
/// run each attempt in a fresh scope.
/// </summary>
/// <remarks>
/// <para>
/// A failure rather than a warning: a retried command whose first attempt committed part of its writes
/// repeats them, and nothing at runtime would say so. The mediator transaction policy says whether every
/// command is transactional; its own startup validator makes sure the transaction behavior is then in
/// the pipeline.
/// </para>
/// <para>
/// A transaction per command is not enough on its own. Without an ambient transaction a retry runs in
/// the request's scope, over the contexts the failed attempt used: an entity it added before its
/// <c>SaveChanges</c> failed is still tracked as added, and the retry writes it a second time. Only the
/// ambient transaction gives every further attempt a fresh scope with fresh contexts.
/// </para>
/// </remarks>
internal sealed class ResilienceMediatorStartupValidator : Bsft.Core.IStartupValidator
{
    private readonly BsftResilienceMediatorOptions _options;
    private readonly IMediatorTransactionPolicy[] _policies;
    private readonly bool _hasAmbientTransaction;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResilienceMediatorStartupValidator"/> class.
    /// </summary>
    /// <param name="options">The mediator resilience options carrying the retry policy.</param>
    /// <param name="policies">The mediator transaction policy, when the mediator is registered.</param>
    /// <param name="registrations">Says whether the container registered an ambient transaction.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public ResilienceMediatorStartupValidator(
        IOptions<BsftResilienceMediatorOptions> options,
        IEnumerable<IMediatorTransactionPolicy> policies,
        IServiceProviderIsService registrations)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(policies);
        ArgumentNullException.ThrowIfNull(registrations);

        _options = options.Value;
        _policies = [.. policies];
        _hasAmbientTransaction = registrations.IsService(typeof(IAmbientTransaction));
    }

    /// <inheritdoc />
    public Task<StartupValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.ShouldRetryAllCommands)
            return Task.FromResult(StartupValidationResult.Success("Only requests marked [Retryable] are retried."));

        if (!_policies.Any(policy => policy.ShouldApplyTransactionToAllCommands))
        {
            return Task.FromResult(StartupValidationResult.Failure(
                "ShouldRetryAllCommands is on, but not every command is a transaction of its own, so a retried command would repeat the writes its failed attempt already made. Call ApplyTransactionToAllCommands() in AddBsftMediator, or retry only the commands marked [Retryable]."));
        }

        if (!_hasAmbientTransaction)
        {
            return Task.FromResult(StartupValidationResult.Failure(
                "ShouldRetryAllCommands is on, but no IAmbientTransaction is registered, so a retried command runs again in the request's scope, over contexts that still track what its failed attempt added, and writes it a second time. Register the shared transaction with AddBsftDataSharedTransaction() or AddBsftDataPostgresTransaction(), or retry only the commands marked [Retryable]."));
        }

        return Task.FromResult(StartupValidationResult.Success(
            "Every command is retried as one transaction, each further attempt in a fresh scope."));
    }
}
