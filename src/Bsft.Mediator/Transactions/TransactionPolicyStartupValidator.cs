using Bsft.Core;
using Bsft.Mediator.StartupValidation;

namespace Bsft.Mediator.Transactions;

/// <summary>
/// Refuses to start when <see cref="MediatorConfiguration.ApplyTransactionToAllCommands"/> is on but the
/// transaction behavior is not in the pipeline, so the policy would silently do nothing.
/// </summary>
/// <remarks>
/// A failure rather than a warning: a host that asked for every command to be one transaction and gets
/// none commits half of a failed command, and nothing at runtime would say so.
/// </remarks>
internal sealed class TransactionPolicyStartupValidator : IStartupValidator
{
    private static readonly Type TransactionBehaviorType = typeof(TransactionBehavior<,>);

    private readonly MediatorConfiguration _configuration;
    private readonly PipelineBehaviorRegistrations _registrations;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransactionPolicyStartupValidator"/> class.
    /// </summary>
    /// <param name="configuration">The mediator configuration carrying the transaction policy.</param>
    /// <param name="registrations">The live pipeline behavior registrations.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public TransactionPolicyStartupValidator(MediatorConfiguration configuration, PipelineBehaviorRegistrations registrations)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _registrations = registrations ?? throw new ArgumentNullException(nameof(registrations));
    }

    /// <inheritdoc />
    public Task<StartupValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        if (!_configuration.ShouldApplyTransactionToAllCommands)
            return Task.FromResult(StartupValidationResult.Success("Commands are transactional only where their handler is marked."));

        if (_registrations.IsRegistered(TransactionBehaviorType))
            return Task.FromResult(StartupValidationResult.Success("Every command runs in a transaction."));

        return Task.FromResult(StartupValidationResult.Failure(
            "ApplyTransactionToAllCommands() is configured, but TransactionBehavior is not in the pipeline, so no command runs in a transaction. Call UseDefaults(), which adds it, or register TransactionBehavior<,> as a pipeline behavior."));
    }
}
