using FluentAssertions;
using Xunit;

namespace Bsft.Messaging.Tests;

public class MessageCorrelationScopeTests
{
    private readonly MessageCorrelationScope _scope = new();

    [Fact]
    public void Outside_a_delivery_there_is_no_ambient_id()
    {
        _scope.Current.Should().BeNull();
    }

    [Fact]
    public void The_id_is_ambient_inside_the_handle_and_gone_afterwards()
    {
        using (_scope.Use("corr-1"))
        {
            _scope.Current.Should().Be("corr-1");
        }

        _scope.Current.Should().BeNull();
    }

    [Fact]
    public void A_nested_delivery_restores_the_outer_id()
    {
        using (_scope.Use("outer"))
        {
            using (_scope.Use("inner"))
            {
                _scope.Current.Should().Be("inner");
            }

            _scope.Current.Should().Be("outer");
        }
    }

    [Fact]
    public void A_message_without_an_id_hides_the_one_around_it_rather_than_inheriting_it()
    {
        using (_scope.Use("corr-1"))
        {
            using (_scope.Use(null))
            {
                _scope.Current.Should().BeNull();
            }

            _scope.Current.Should().Be("corr-1");
        }
    }

    [Fact]
    public async Task The_id_flows_into_the_asynchronous_continuations_of_the_delivery()
    {
        using (_scope.Use("corr-1"))
        {
            await Task.Yield();

            _scope.Current.Should().Be("corr-1");
        }
    }

    [Fact]
    public void Two_scopes_do_not_see_each_others_ambient_id()
    {
        var other = new MessageCorrelationScope();

        using (_scope.Use("corr-1"))
        {
            other.Current.Should().BeNull();
        }
    }
}
