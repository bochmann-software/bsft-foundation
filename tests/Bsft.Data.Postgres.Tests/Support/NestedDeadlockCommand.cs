using Bsft.Mediator;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>
/// Nested command that raises a deadlock (40P01) on the first run only,
/// allowing the outer handler to catch the exception and test retry behavior.
/// </summary>
[Retryable]
public sealed record NestedDeadlockCommand : ICommand<int>;
