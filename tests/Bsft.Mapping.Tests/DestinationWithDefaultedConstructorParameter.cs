namespace Bsft.Mapping.Tests;

/// <summary>A destination whose constructor takes a parameter it carries under another name.</summary>
public class DestinationWithDefaultedConstructorParameter
{
    /// <summary>Initializes a new instance of the <see cref="DestinationWithDefaultedConstructorParameter"/> class.</summary>
    /// <param name="id">The identifier the source carries.</param>
    /// <param name="separator">What separates the parts, defaulted because no source states it.</param>
    public DestinationWithDefaultedConstructorParameter(int id, string separator = "-")
    {
        Id = id;
        Divider = separator;
    }

    public int Id { get; }

    public string Divider { get; }
}
