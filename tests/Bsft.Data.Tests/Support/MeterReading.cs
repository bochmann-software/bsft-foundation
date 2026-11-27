namespace Bsft.Data.Tests.Support;

/// <summary>A row of a large, growing table that is listed by keyset: many rows share a timestamp.</summary>
public sealed class MeterReading
{
    public long Id { get; set; }

    public Guid Serial { get; set; }

    public DateTime RecordedAt { get; set; }

    public string Meter { get; set; } = string.Empty;

    public int Value { get; set; }
}
