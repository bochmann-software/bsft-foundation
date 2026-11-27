using Bsft.Mediator;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>
/// Nested command that raises a non-conflict error (P0001) to verify
/// non-conflict failures are not retried by the retry mechanism.
/// </summary>
[Retryable]
public sealed record NestedNonConflictFailureInnerCommand : ICommand<int>;
