namespace Bsft.Outbox.Postgres;

/// <summary>
/// The name of a PostgreSQL notification channel that is safe to listen on: lower-case letters, digits
/// and underscores, starting with a letter, at most <see cref="MaxLength"/> characters.
/// </summary>
/// <remarks>
/// <para>
/// <c>LISTEN</c> takes an identifier, which no provider parameterises, while <c>pg_notify</c> takes the
/// channel as an ordinary value. The listening side therefore hands the name to the server as a
/// parameter and lets <c>format('%I')</c> quote it, so no statement is ever built from it. The narrow
/// alphabet is what keeps the two halves naming the same channel: an unquoted identifier folds to lower
/// case, and a name that starts with a digit reads as a number. Checking it once, when the name is
/// made, means a rejected value costs a failed boot rather than a failed session.
/// </para>
/// </remarks>
public readonly struct PostgresNotificationChannel : IEquatable<PostgresNotificationChannel>
{
    /// <summary>
    /// The longest a channel name may be. PostgreSQL truncates an identifier at <c>NAMEDATALEN - 1</c>,
    /// which is 63 characters in a stock server, and a truncated <c>LISTEN</c> would silently stop
    /// matching the <c>pg_notify</c> the writer sends.
    /// </summary>
    public const int MaxLength = 63;

    private readonly string? _name;

    /// <summary>Initializes a new instance of the <see cref="PostgresNotificationChannel"/> struct.</summary>
    /// <param name="name">The channel name, which must satisfy <see cref="IsValid"/>.</param>
    /// <exception cref="ArgumentException">Thrown when the name does not match the grammar.</exception>
    public PostgresNotificationChannel(string name)
    {
        if (!IsValid(name))
        {
            throw new ArgumentException(
                $"'{name}' is not a notification channel name: use 1 to {MaxLength} characters of lower-case letters, digits and underscores, starting with a letter.",
                nameof(name));
        }

        _name = name;
    }

    /// <summary>Gets the channel name; empty for a default value, which no session accepts.</summary>
    public string Name => _name ?? string.Empty;

    /// <summary>Decides whether a name may be used as a notification channel.</summary>
    /// <param name="name">The configured channel name.</param>
    /// <returns><see langword="true"/> when the name matches the grammar and will not be truncated by the server.</returns>
    public static bool IsValid(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxLength || name[0] is < 'a' or > 'z')
            return false;

        foreach (var character in name)
        {
            if (character is (< 'a' or > 'z') and (< '0' or > '9') and not '_')
                return false;
        }

        return true;
    }

    /// <inheritdoc />
    public bool Equals(PostgresNotificationChannel other) => string.Equals(Name, other.Name, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PostgresNotificationChannel other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Name.GetHashCode(StringComparison.Ordinal);

    /// <inheritdoc />
    public override string ToString() => Name;

    /// <summary>Compares two channels by name.</summary>
    /// <param name="left">The first channel.</param>
    /// <param name="right">The second channel.</param>
    /// <returns><see langword="true"/> when the names are equal.</returns>
    public static bool operator ==(PostgresNotificationChannel left, PostgresNotificationChannel right) => left.Equals(right);

    /// <summary>Compares two channels by name.</summary>
    /// <param name="left">The first channel.</param>
    /// <param name="right">The second channel.</param>
    /// <returns><see langword="true"/> when the names differ.</returns>
    public static bool operator !=(PostgresNotificationChannel left, PostgresNotificationChannel right) => !left.Equals(right);
}
