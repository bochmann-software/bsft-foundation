namespace Bsft.Data.Tests.Support;

/// <summary>An entity whose key follows no convention the framework can guess.</summary>
public sealed class Ticket
{
    /// <summary>Gets or sets the ticket code that serves as the entity's key.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Gets or sets the ticket label.</summary>
    public string Label { get; set; } = string.Empty;
}
