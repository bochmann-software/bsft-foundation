using Bsft.Messaging.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Bsft.Messaging.Tests;

public class BsftMessagingOptionsTests
{
    [Fact]
    public void An_event_declared_without_a_name_travels_under_its_full_type_name()
    {
        var options = new BsftMessagingOptions();

        options.AddEvent<OrderPlacedEvent>();

        options.RegisteredEventTypes[typeof(OrderPlacedEvent)].Should().Be(typeof(OrderPlacedEvent).FullName);
    }

    [Fact]
    public void An_explicit_name_decouples_the_wire_contract_from_the_clr_type()
    {
        var options = new BsftMessagingOptions();

        options.AddEvent<OrderPlacedEvent>("orders.placed.v1");

        options.RegisteredEventTypes[typeof(OrderPlacedEvent)].Should().Be("orders.placed.v1");
    }

    [Fact]
    public void Declaring_returns_the_options_so_declarations_can_be_chained()
    {
        var options = new BsftMessagingOptions();

        var chained = options.AddEvent<OrderPlacedEvent>().AddEvent<OrderShippedEvent>();

        chained.Should().BeSameAs(options);
        chained.RegisteredEventTypes.Should().HaveCount(2);
    }

    [Fact]
    public void Redeclaring_the_same_type_replaces_its_name()
    {
        var options = new BsftMessagingOptions();

        options.AddEvent<OrderPlacedEvent>("orders.placed.v1");
        options.AddEvent<OrderPlacedEvent>("orders.placed.v2");

        options.RegisteredEventTypes.Should().HaveCount(1);
        options.RegisteredEventTypes[typeof(OrderPlacedEvent)].Should().Be("orders.placed.v2");
    }

    [Fact]
    public void Two_types_under_one_name_are_rejected_at_startup()
    {
        var options = new BsftMessagingOptions();
        options.AddEvent<OrderPlacedEvent>("orders.v1");

        var act = () => options.AddEvent<OrderShippedEvent>("orders.v1");

        act.Should().Throw<ArgumentException>().WithParameterName("name");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_name_is_rejected(string name)
    {
        var options = new BsftMessagingOptions();

        var act = () => options.AddEvent<OrderPlacedEvent>(name);

        act.Should().Throw<ArgumentException>().WithParameterName("name");
    }
}
