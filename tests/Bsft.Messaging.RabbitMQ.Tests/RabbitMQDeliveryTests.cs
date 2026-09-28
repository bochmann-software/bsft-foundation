using FluentAssertions;
using System.Text;
using Xunit;

namespace Bsft.Messaging.RabbitMQ.Tests;

/// <summary>
/// The ownership rule the delivery type carries: its body is a copy, because the only memory it is
/// ever built from is memory the client rents from a pool and takes back when the receive callback
/// returns — while this outlives that callback by the whole delivery.
/// </summary>
public class RabbitMQDeliveryTests
{
    [Fact]
    public void The_body_is_copied_out_of_the_buffer_it_was_assigned_from()
    {
        var rented = Encoding.UTF8.GetBytes("""{"id":7}""");

        var delivery = new RabbitMQDelivery { Body = rented };

        // The pool handing the same array to the next delivery.
        Array.Fill(rented, (byte)'X');

        Encoding.UTF8.GetString(delivery.Body.Span).Should().Be("""{"id":7}""");
    }

    [Fact]
    public void An_unset_body_is_empty_rather_than_null()
    {
        var delivery = new RabbitMQDelivery();

        delivery.Body.IsEmpty.Should().BeTrue();
    }
}
