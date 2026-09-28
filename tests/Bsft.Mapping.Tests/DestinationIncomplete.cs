namespace Bsft.Mapping.Tests;

/// <summary>A destination two of whose members no profile accounts for.</summary>
public class DestinationIncomplete
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Unmapped { get; set; } = string.Empty;
}
