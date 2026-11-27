namespace Bsft.Mediator.Tests.Transactions;

/// <summary>
/// An outer command neither marked retryable nor handled transactionally, which only forwards to the
/// nested module's command: retried under the all-commands retry policy alone.
/// </summary>
public sealed record ForwardReservationCommand(string Name) : ICommand<int>;
