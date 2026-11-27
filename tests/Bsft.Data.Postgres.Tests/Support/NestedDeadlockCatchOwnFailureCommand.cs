using Bsft.Mediator;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>
/// Outer command that writes a catalog item, sends a nested command that raises 40P01 on every run,
/// catches the PostgresException, and throws its own exception.
/// </summary>
[Retryable]
public sealed record NestedDeadlockCatchOwnFailureCommand(string Name) : ICommand<int>;
