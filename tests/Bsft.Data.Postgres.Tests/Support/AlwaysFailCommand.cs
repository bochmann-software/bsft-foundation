using Bsft.Mediator;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>Command that always fails with a configured SQL error.</summary>
[Bsft.Mediator.Retryable]
public sealed record AlwaysFailCommand(string Mode) : ICommand<int>;
