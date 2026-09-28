using System.Reflection;
using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>The concrete handler the scan registers in its place.</summary>
public sealed class AbstractRegistrationProbeHandler : AbstractRegistrationProbeHandlerBase;
