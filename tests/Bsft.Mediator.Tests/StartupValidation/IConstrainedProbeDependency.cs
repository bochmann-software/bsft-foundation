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

/// <summary>A dependency no test registers.</summary>
public interface IConstrainedProbeDependency;
