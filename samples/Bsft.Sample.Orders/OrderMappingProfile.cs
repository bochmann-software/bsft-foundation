using Bsft.Mapping;
using Bsft.Sample.Orders.Core;

namespace Bsft.Sample.Orders;

/// <summary>
/// Convention-based mapping from <see cref="Order"/> to <see cref="OrderDto"/>.
/// All properties match by name so no explicit ForMember calls are needed.
/// </summary>
public class OrderMappingProfile : MappingProfileBase
{
    /// <inheritdoc />
    public override void Configure(ProfileBuilder builder)
    {
        builder.CreateMap<Order, OrderDto>();
    }
}
