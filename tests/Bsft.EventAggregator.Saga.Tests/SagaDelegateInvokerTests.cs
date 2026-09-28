using FluentAssertions;
using Xunit;

namespace Bsft.EventAggregator.Saga.Tests;

public sealed class SagaDelegateInvokerTests
{
    [Fact]
    public void For_ADelegateTakingTheTwoArguments_CallsIt()
    {
        Func<string, int, bool> predicate = (text, length) => text.Length == length;

        var invoke = SagaDelegateInvoker.For<bool>(predicate);

        invoke("abc", 3).Should().BeTrue();
        invoke("abc", 2).Should().BeFalse();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void For_ADelegateOfAnotherArity_Throws(int arity)
    {
        // The compiled caller passes exactly two arguments, so a delegate wanting another number of
        // them has to be refused where the definition is built rather than where an event arrives.
        Delegate target = arity == 1
            ? new Func<string, bool>(text => text.Length > 0)
            : new Func<string, int, int, bool>((text, a, b) => text.Length == a + b);

        var act = () => SagaDelegateInvoker.For<bool>(target);

        act.Should().Throw<ArgumentException>().WithParameterName("target");
    }
}
