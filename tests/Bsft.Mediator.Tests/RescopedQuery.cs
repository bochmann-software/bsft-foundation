namespace Bsft.Mediator.Tests;

/// <summary>A query whose handler reports the scope marker it was resolved with.</summary>
public sealed record RescopedQuery : IQuery<string>;
