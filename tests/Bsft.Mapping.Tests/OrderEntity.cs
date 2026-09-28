namespace Bsft.Mapping.Tests;

public class OrderEntity
{
    public int Id { get; set; }
    public CustomerEntity Customer { get; set; } = null!;
}
