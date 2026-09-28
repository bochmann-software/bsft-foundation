namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>A type the model does not know, used to reach the unmapped-entity refusal.</summary>
public sealed class Referral
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
