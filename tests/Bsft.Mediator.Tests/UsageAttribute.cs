using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

[AttributeUsage(AttributeTargets.Class, Inherited = true)]
internal sealed class UsageAttribute(string feature) : Attribute
{
    public string Feature { get; } = feature;
}
