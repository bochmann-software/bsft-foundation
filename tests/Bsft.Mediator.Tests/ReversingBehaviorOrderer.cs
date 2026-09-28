using Bsft.Caching.Mediator;
using Bsft.Core;
using Bsft.Mediator.DependencyInjection;
using Bsft.Mediator.Tests.Support;
using Bsft.Mediator.Transactions;
using Bsft.Resilience.Mediator;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>An orderer that reverses the resolved behaviors, so the dispatcher's use of it is visible.</summary>
internal sealed class ReversingBehaviorOrderer : IPipelineBehaviorOrderer
{
    public IReadOnlyList<TBehavior> Order<TBehavior>(IReadOnlyList<TBehavior> behaviors)
        where TBehavior : notnull =>
        behaviors.Reverse().ToList();
}
