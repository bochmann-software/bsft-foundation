using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>Collects what the behavior below observed, so the test can assert on it.</summary>
internal sealed class UsageRecorder
{
    public List<string> Features { get; } = [];

    public List<Type> RequestTypes { get; } = [];
}
