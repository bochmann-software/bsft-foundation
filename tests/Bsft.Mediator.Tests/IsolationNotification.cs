using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

// Dedicated notification + handlers used only by the handler-isolation test, kept separate from
// TestNotification so the assembly-discovery count for TestNotification stays unaffected.
public record IsolationNotification(string Message) : INotification;
