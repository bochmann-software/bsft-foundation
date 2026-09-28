namespace Bsft.Messaging.EFCore.Postgres;

/// <summary>
/// Builds the <c>LISTEN</c> statement for a channel, and owns the grammar a channel name must match.
/// </summary>
/// <remarks>
/// It is a statement built by concatenation because <c>LISTEN</c> takes an identifier, which no
/// provider parameterises — the write side sends its half through <c>pg_notify</c> precisely because
/// that one does take the channel as a parameter. The name is therefore quoted here and constrained to
/// an alphabet with no quote in it. Quoting also fixes the case: an unquoted identifier folds to lower
/// case, and a channel listened for as <c>Foo</c> would then never see the <c>pg_notify('Foo', ...)</c>
/// that was sent.
/// <para>
/// The grammar lives here rather than in <see cref="BsftMessagingPostgresOptionsValidator"/>, which
/// calls it, so that the check sits next to the concatenation it protects. The validator still runs at
/// start-up and is where a bad name is meant to be caught; the guard in <see cref="For"/> is the
/// backstop for a caller that reached this type without passing through options binding at all.
/// </para>
/// </remarks>
internal static class PostgresListenStatement
{
    /// <summary>Renders the statement that subscribes the connection to a channel.</summary>
    /// <param name="channel">The channel name, which must match <see cref="IsValidChannel"/>.</param>
    /// <returns>The statement to execute on the listening connection.</returns>
    /// <exception cref="ArgumentException">Thrown when the name does not match the grammar.</exception>
    public static string For(string channel)
    {
        if (!IsValidChannel(channel))
        {
            throw new ArgumentException(
                $"'{channel}' is not a channel name that may be quoted into a LISTEN statement.",
                nameof(channel));
        }

        return $"LISTEN \"{channel}\";";
    }

    /// <summary>
    /// Decides whether a channel name may be quoted into a <c>LISTEN</c> statement.
    /// </summary>
    /// <param name="channel">The configured channel name.</param>
    /// <returns><see langword="true"/> when the name is safe and will not be truncated by the server.</returns>
    /// <remarks>
    /// Lower case only, because that is what an unquoted identifier folds to and what keeps a name
    /// readable in <c>pg_listening_channels()</c>; a leading letter, because a name that starts with a
    /// digit reads as a number; and no other character at all, because the grammar's whole job here is
    /// to leave nothing that could end the quoted identifier early.
    /// </remarks>
    public static bool IsValidChannel(string? channel)
    {
        if (string.IsNullOrEmpty(channel) || channel.Length > PostgresDefaults.MaxNotificationChannelLength)
            return false;

        if (channel[0] is < 'a' or > 'z')
            return false;

        foreach (var character in channel)
        {
            if (character is (< 'a' or > 'z') and (< '0' or > '9') and not '_')
                return false;
        }

        return true;
    }
}
