using System.Reflection;
using Bsft.Core;
using Bsft.Mediator.DependencyInjection;
using Bsft.Mediator.StartupValidation;
using Bsft.Mediator.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Bsft.Mediator.Tests.StartupValidation;

/// <summary>The request shape <see cref="ConstrainedProbeBehavior{TRequest, TResponse}"/> accepts.</summary>
public interface IConstrainedProbeRequest;
