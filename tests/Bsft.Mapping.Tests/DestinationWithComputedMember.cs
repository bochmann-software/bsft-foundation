namespace Bsft.Mapping.Tests;

/// <summary>A destination that computes a member of its own, so nothing may assign it.</summary>
public class DestinationWithComputedMember
{
    public int Id { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string Initials => FirstName.Length > 0 ? FirstName[..1] : string.Empty;
}
