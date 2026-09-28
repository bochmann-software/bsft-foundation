namespace Bsft.Data.SqlServer;

/// <summary>
/// Declares which search groups are backed by a SQL Server full-text index.
/// </summary>
/// <remarks>
/// <para>
/// The framework cannot inspect the catalogue at startup without a connection, so the application
/// states which groups it has indexed. A group that is declared searchable but not listed here fails
/// startup validation — the deliberate alternative to silently scanning the table.
/// </para>
/// <para>
/// This is the package's primary options type and it deliberately carries no <c>SectionName</c> and
/// binds from no configuration section. What it holds is a statement about the <em>schema</em> — that
/// a full-text index exists for a group — and a deployment that could state it separately could state
/// it wrongly, which would turn every search on that group into the table scan the declaration exists
/// to prevent. It is therefore written beside the migration that creates the index.
/// </para>
/// </remarks>
public sealed class BsftDataSqlServerOptions
{
    private readonly HashSet<string> _indexedGroups = new(StringComparer.Ordinal);

    /// <summary>Gets the names of the groups declared as indexed.</summary>
    public IReadOnlyCollection<string> IndexedGroups => _indexedGroups;

    /// <summary>
    /// Declares that a full-text index exists for the given search group.
    /// </summary>
    /// <param name="groupName">The search group name.</param>
    /// <returns>The same options instance for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="groupName"/> is null or whitespace.</exception>
    public BsftDataSqlServerOptions HasFullTextIndex(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            throw new ArgumentException("A search group name is required.", nameof(groupName));

        _indexedGroups.Add(groupName);
        return this;
    }

    /// <summary>
    /// Copies this declaration onto the instance the container will hand to the provider.
    /// </summary>
    /// <param name="target">The instance the options pattern is building.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target"/> is null.</exception>
    internal void CopyTo(BsftDataSqlServerOptions target)
    {
        ArgumentNullException.ThrowIfNull(target);

        foreach (var groupName in _indexedGroups)
            target.HasFullTextIndex(groupName);
    }
}
