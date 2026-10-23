using Bsft.Data.Modeling;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Bsft.Data.Tests.Modeling;

/// <summary>
/// Which EF Core events the observer acts on, and that it acts on nothing else. That it hands on the
/// context of a real event is shown end to end in <see cref="RelationalModelWarmupHostedServiceTests"/>.
/// </summary>
public sealed class ContextInitializedObserverTests
{
    [Fact]
    public void WithNullWarmThrows()
    {
        var act = () => new ContextInitializedObserver(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.Infrastructure.ContextInitialized", true)]
    [InlineData("Microsoft.EntityFrameworkCore.Database.Command.CommandExecuting", false)]
    public void SelectsOnlyTheContextInitializedEvent(string name, bool expected)
    {
        ContextInitializedObserver.IsContextInitialized(name).Should().Be(expected);
    }

    [Fact]
    public void IgnoresAPayloadWithoutAContext()
    {
        var seen = new List<DbContext>();
        var observer = new ContextInitializedObserver(seen.Add);

        observer.OnNext(new KeyValuePair<string, object?>(CoreEventId.ContextInitialized.Name!, null));
        observer.OnCompleted();
        observer.OnError(new InvalidOperationException("never sent by DiagnosticListener"));

        seen.Should().BeEmpty();
    }
}
