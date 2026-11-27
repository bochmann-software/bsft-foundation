using Bsft.Mediator;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>
/// Outer command that writes a catalog item, sends a nested command that raises a non-conflict error (P0001),
/// and catches the PostgresException to verify non-conflicts are not retried.
/// </summary>
[Retryable]
public sealed record NestedNonConflictFailureCommand(string Name) : ICommand<int>;
