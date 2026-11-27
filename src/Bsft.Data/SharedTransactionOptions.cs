using System.Data;

namespace Bsft.Data;

/// <summary>
/// How the shared transaction that <c>AddBsftDataSharedTransaction</c> registers is opened.
/// </summary>
/// <remarks>
/// A nested group of <see cref="BsftDataOptions"/>, bound as part of its section
/// (<c>Bsft:Data:SharedTransaction</c>) where a host binds that section, and otherwise configured
/// with <c>services.Configure&lt;BsftDataOptions&gt;(o =&gt; o.SharedTransaction.IsolationLevel = ...)</c>.
/// </remarks>
public sealed class SharedTransactionOptions
{
    /// <summary>
    /// Gets or sets the isolation level the outermost unit opens its transaction with. Defaults to
    /// <see cref="IsolationLevel.Unspecified"/>, which leaves the choice to the provider (read committed
    /// on PostgreSQL and SQL Server).
    /// </summary>
    /// <remarks>
    /// <see cref="IsolationLevel.Serializable"/> and <see cref="IsolationLevel.RepeatableRead"/> make
    /// the store refuse a conflicting commit with a serialization failure instead of letting it through;
    /// pair them with <c>[Retryable]</c> so the whole unit is attempted again.
    /// </remarks>
    public IsolationLevel IsolationLevel { get; set; } = IsolationLevel.Unspecified;
}
