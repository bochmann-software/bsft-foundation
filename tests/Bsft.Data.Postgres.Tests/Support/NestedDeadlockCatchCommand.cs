using Bsft.Mediator;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>
/// Outer command that writes a catalog item, sends a nested command that raises 40P01 on the first run,
/// and catches the PostgresException when the nested handler lets it escape.
/// </summary>
[Retryable]
public sealed record NestedDeadlockCatchCommand(string Name) : ICommand<int>;
