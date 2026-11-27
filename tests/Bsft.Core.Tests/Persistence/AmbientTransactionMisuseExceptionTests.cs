using Bsft.Core.Persistence;
using FluentAssertions;
using Xunit;

namespace Bsft.Core.Tests.Persistence;

public sealed class AmbientTransactionMisuseExceptionTests
{
    [Fact]
    public void AmbientTransactionMisuseException_WithNoArguments_IsAnInvalidOperation()
    {
        var exception = new AmbientTransactionMisuseException();

        exception.Should().BeAssignableTo<InvalidOperationException>();
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public void AmbientTransactionMisuseException_WithAMessage_StoresIt()
    {
        var exception = new AmbientTransactionMisuseException("misused");

        exception.Message.Should().Be("misused");
    }

    [Fact]
    public void AmbientTransactionMisuseException_WithASwallowedFailure_KeepsItAsTheCause()
    {
        var swallowed = new TimeoutException("deadlock");

        var exception = new AmbientTransactionMisuseException("misused", swallowed);

        exception.Message.Should().Be("misused");
        exception.InnerException.Should().BeSameAs(swallowed);
    }
}
