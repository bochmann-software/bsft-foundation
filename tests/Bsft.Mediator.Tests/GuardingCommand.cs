using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>A command whose handler refuses it outright.</summary>
public sealed record GuardingCommand : ICommand<string>;
