namespace Bsft.Data.Sqlite;

/// <summary>
/// One row of an FTS5 index table, as EF Core sees it.
/// </summary>
/// <remarks>
/// <para>
/// It is mapped as a shared-type entity, once per index table, by
/// <see cref="ModelBuilderExtensions.HasBsftFullTextIndex(Microsoft.EntityFrameworkCore.ModelBuilder, string, string[])"/>.
/// Shared-type, because an application with three indexes needs three mappings of the same shape
/// differing only in the table — and in the name of the hidden column, which FTS5 names after the
/// table itself.
/// </para>
/// <para>
/// Two columns are enough for everything the provider does: the <c>rowid</c> joins the index back to
/// the row it describes, and the hidden column is what <c>match()</c> and <c>bm25()</c> take as their
/// operand. The indexed text itself is never read back — the content lives in the base table.
/// </para>
/// </remarks>
public sealed class BsftFullTextIndexRow
{
    /// <summary>
    /// Gets or sets the <c>rowid</c> of the indexed row, which for an external-content index is the
    /// integer primary key of the base table.
    /// </summary>
    public long RowId { get; set; }

    /// <summary>
    /// Gets or sets the FTS5 hidden column named after the index table. It carries no readable value;
    /// it exists so that a query can name the index in <c>match()</c> and <c>bm25()</c>.
    /// </summary>
    public string Match { get; set; } = string.Empty;
}
