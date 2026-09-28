using Bsft.Instrumentation;
using Bsft.Mediator.DependencyInjection;
using Bsft.Mediator.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

public record TracingPubNotification(string Message) : INotification;
