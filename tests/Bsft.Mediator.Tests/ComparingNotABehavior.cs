using System.Reflection;
using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>An open generic of the right arity implementing a generic contract that is no behavior.</summary>
public sealed class ComparingNotABehavior<TRequest, TResponse> : IComparer<TRequest>
{
    public int Compare(TRequest? x, TRequest? y) => 0;
}
