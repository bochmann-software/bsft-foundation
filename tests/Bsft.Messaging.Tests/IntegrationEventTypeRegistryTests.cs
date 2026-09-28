using Bsft.Messaging.Tests.Fakes;
using Bsft.Messaging.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Messaging.Tests;

public class IntegrationEventTypeRegistryTests
{
    private static IntegrationEventTypeRegistry CreateRegistry(string? name = null)
    {
        var options = new BsftMessagingOptions();
        options.AddEvent<OrderPlacedEvent>(name);

        return new IntegrationEventTypeRegistry(TestMessagingOptions.For(options));
    }

    [Fact]
    public void A_declared_type_resolves_to_its_name_and_back()
    {
        var registry = CreateRegistry("orders.placed.v1");

        registry.GetName(typeof(OrderPlacedEvent)).Should().Be("orders.placed.v1");
        registry.FindType("orders.placed.v1").Should().Be<OrderPlacedEvent>();
    }

    [Fact]
    public void An_undeclared_type_is_rejected_rather_than_guessed()
    {
        var registry = CreateRegistry();

        var act = () => registry.GetName(typeof(OrderShippedEvent));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*OrderShippedEvent*not registered*");
    }

    [Fact]
    public void An_unknown_name_from_a_stored_row_resolves_to_null_instead_of_a_type()
    {
        var registry = CreateRegistry();

        registry.FindType("System.String").Should().BeNull();
    }

    [Fact]
    public void The_mapping_is_copied_so_a_later_declaration_cannot_change_it()
    {
        var options = new BsftMessagingOptions();
        options.AddEvent<OrderPlacedEvent>("orders.placed.v1");
        var registry = new IntegrationEventTypeRegistry(TestMessagingOptions.For(options));

        options.AddEvent<OrderShippedEvent>("orders.shipped.v1");

        registry.FindType("orders.shipped.v1").Should().BeNull();
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        var registry = CreateRegistry();

        var withoutOptions = () => new IntegrationEventTypeRegistry(null!);
        var withoutType = () => registry.GetName(null!);
        var withoutName = () => registry.FindType(null!);

        withoutOptions.Should().Throw<ArgumentNullException>().WithParameterName("options");
        withoutType.Should().Throw<ArgumentNullException>().WithParameterName("eventType");
        withoutName.Should().Throw<ArgumentNullException>().WithParameterName("name");
    }
}
