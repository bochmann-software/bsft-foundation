using Bsft.Data.MultiTenancy;

namespace Bsft.Data.Tests.Support;

public sealed class TestEntity : TenantEntity
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Age { get; set; }

    public string Email { get; set; } = string.Empty;

    public decimal Price { get; set; }
}
