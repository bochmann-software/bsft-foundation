namespace Bsft.Mapping.Tests;

/// <summary>
/// A destination with two public constructors, the narrower one declared first, and no setter for
/// what only the wider one takes.
/// </summary>
public class DestinationWithTwoConstructors
{
    /// <summary>Initializes a new instance of the <see cref="DestinationWithTwoConstructors"/> class.</summary>
    /// <param name="id">The identifier.</param>
    public DestinationWithTwoConstructors(int id)
    {
        Id = id;
    }

    /// <summary>Initializes a new instance of the <see cref="DestinationWithTwoConstructors"/> class.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="firstName">The first name, which only this constructor can set.</param>
    public DestinationWithTwoConstructors(int id, string firstName)
    {
        Id = id;
        FirstName = firstName;
    }

    public int Id { get; }

    public string? FirstName { get; }
}
