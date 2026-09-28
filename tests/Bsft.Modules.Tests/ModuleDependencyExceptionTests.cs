using FluentAssertions;
using Xunit;

namespace Bsft.Modules.Tests;

public class ModuleDependencyExceptionTests
{
    [Fact]
    public void Constructor_WithMessage_SetsMessage()
    {
        var ex = new ModuleDependencyException("Module 'X' not found");

        ex.Message.Should().Be("Module 'X' not found");
        ex.InnerException.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithMessageAndInnerException_SetsBoth()
    {
        var inner = new InvalidOperationException("inner error");

        var ex = new ModuleDependencyException("Dependency failed", inner);

        ex.Message.Should().Be("Dependency failed");
        ex.InnerException.Should().BeSameAs(inner);
    }
}
