using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>A notification used only by the forwarding tests.</summary>
public record ForwardedNotification(string Id) : INotification;
