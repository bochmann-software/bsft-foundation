using Bsft.Mediator;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>Command that checks whether it ran in a fresh scope by tracking context instances.</summary>
[Bsft.Mediator.Retryable]
public sealed record CheckScopeCommand : ICommand<string>;
