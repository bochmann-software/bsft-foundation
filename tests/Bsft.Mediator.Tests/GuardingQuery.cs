using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>A query whose handler refuses it outright.</summary>
public sealed record GuardingQuery : IQuery<string>;
