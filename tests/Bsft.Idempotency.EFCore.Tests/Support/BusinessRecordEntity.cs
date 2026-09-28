namespace Bsft.Idempotency.EFCore.Tests.Support;

/// <summary>The application's own row, standing in for whatever an endpoint actually creates.</summary>
public sealed class BusinessRecordEntity
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
