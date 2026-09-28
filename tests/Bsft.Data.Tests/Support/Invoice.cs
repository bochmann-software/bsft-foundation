namespace Bsft.Data.Tests.Support;

/// <summary>An entity whose key follows the &lt;EntityName&gt;Id convention.</summary>
public sealed class Invoice
{
    /// <summary>Gets or sets the convention-named key.</summary>
    public Guid InvoiceId { get; set; }
}
