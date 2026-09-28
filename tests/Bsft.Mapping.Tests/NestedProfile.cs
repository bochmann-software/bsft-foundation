namespace Bsft.Mapping.Tests;

public class NestedProfile : MappingProfileBase
{
    public override void Configure(ProfileBuilder builder)
    {
        builder.CreateMap<OrderEntity, OrderDto>()
            .ForMember(d => d.CustomerLastName, o => o.MapFrom(s => s.Customer.LastName))
            .ForMember(d => d.CustomerCity, o => o.MapFrom(s => s.Customer.City));
    }
}
