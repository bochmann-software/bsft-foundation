using Microsoft.Extensions.Configuration;

namespace Bsft.Idempotency.DependencyInjection;

/// <summary>
/// Binds the idempotency section, making a configured list replace the default rather than extend it.
/// </summary>
/// <remarks>
/// The configuration binder appends to a collection that already has items, so a host writing
/// <c>"Methods": [ "POST", "PATCH" ]</c> against the default would get <c>POST</c> twice. A host that
/// lists its methods means those methods, so the list is cleared first — and only when the section
/// actually names it, so a section that says nothing about a list leaves the default alone.
/// </remarks>
internal static class IdempotencyConfigurationBinder
{
    /// <summary>
    /// Binds <see cref="BsftIdempotencyOptions.SectionName"/> onto the options.
    /// </summary>
    /// <param name="configuration">The configuration to read.</param>
    /// <param name="options">The options to bind onto.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    internal static void Bind(IConfiguration configuration, BsftIdempotencyOptions options)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        var section = configuration.GetSection(BsftIdempotencyOptions.SectionName);

        ClearIfNamed(section, nameof(BsftIdempotencyOptions.Methods), options.Methods);
        ClearIfNamed(
            section, nameof(BsftIdempotencyOptions.CachedResponseHeaders), options.CachedResponseHeaders);
        ClearIfNamed(
            section, nameof(BsftIdempotencyOptions.NonReplayableStatusCodes), options.NonReplayableStatusCodes);

        section.Bind(options);
    }

    /// <summary>Empties a list the section is about to supply.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="section">The idempotency section.</param>
    /// <param name="name">The property the list is bound from.</param>
    /// <param name="target">The list to clear.</param>
    private static void ClearIfNamed<T>(IConfigurationSection section, string name, IList<T> target)
    {
        if (section.GetSection(name).Exists())
            target.Clear();
    }
}
