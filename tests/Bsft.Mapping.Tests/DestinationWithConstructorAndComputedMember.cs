namespace Bsft.Mapping.Tests;

/// <summary>A destination taking its name through a constructor and computing its initials.</summary>
public class DestinationWithConstructorAndComputedMember
{
    /// <summary>Initializes a new instance of the <see cref="DestinationWithConstructorAndComputedMember"/> class.</summary>
    /// <param name="firstName">The name the instance is built with.</param>
    public DestinationWithConstructorAndComputedMember(string firstName) => FirstName = firstName;

    public string FirstName { get; }

    public string Initials => FirstName.Length > 0 ? FirstName[..1] : string.Empty;

    public int Id { get; set; }
}
