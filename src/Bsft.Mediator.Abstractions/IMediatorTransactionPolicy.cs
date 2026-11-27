namespace Bsft.Mediator;

/// <summary>
/// The mediator's transaction policy, read-only, for a behavior package that checks its own
/// configuration against it at startup.
/// </summary>
/// <remarks>
/// Registered by <c>AddBsftMediator</c>. The retry package reads it to refuse retrying every command when
/// not every command is a transaction of its own: a retried command that is not one would repeat the
/// writes its failed attempt already made.
/// </remarks>
public interface IMediatorTransactionPolicy
{
    /// <summary>
    /// Gets a value indicating whether every command is transactional, whether or not its handler is
    /// marked with <see cref="TransactionalAttribute"/>.
    /// </summary>
    bool ShouldApplyTransactionToAllCommands { get; }
}
