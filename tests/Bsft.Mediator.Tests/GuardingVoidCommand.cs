using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>A command without a result whose handler refuses it outright.</summary>
public sealed record GuardingVoidCommand : ICommand;
