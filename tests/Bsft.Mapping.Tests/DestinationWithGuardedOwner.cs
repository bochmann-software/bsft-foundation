namespace Bsft.Mapping.Tests;

/// <summary>A destination whose FirstName only it may set.</summary>
public class DestinationWithGuardedOwner
{
    /// <summary>Initializes a new instance of the <see cref="DestinationWithGuardedOwner"/> class.</summary>
    /// <param name="firstName">The name the instance keeps.</param>
    public DestinationWithGuardedOwner(string firstName) => FirstName = firstName;

    public int Id { get; set; }

    public string FirstName { get; private set; }
}
