using Bsft.Mediator;

namespace Bsft.Caching.Mediator.Tests;

/// <summary>A query without the cacheable marker.</summary>
public sealed record PlainQuery(string Value) : IQuery<string>;
