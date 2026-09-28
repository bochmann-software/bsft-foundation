using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bsft.Localization.EFCore;

/// <summary>
/// Configures the Entity Framework mapping for <see cref="TranslationEntity"/>,
/// including a unique index on the combination of Key, Locale, and TenantId.
/// </summary>
/// <remarks>
/// Two indexes, one per read: the unique one over key, locale and tenant both enforces that a tenant
/// holds one override of a key and answers the single-key lookup, and a second one over locale and
/// tenant answers the whole-locale read a bundle is built from.
/// </remarks>
public sealed class TranslationEntityConfiguration : IEntityTypeConfiguration<TranslationEntity>
{
    // Column widths: a key is a dotted path, a locale an IETF tag, a tenant id an external
    // identifier. All three are indexed together, so each needs a bounded length.
    private const int KeyMaxLength = 512;
    private const int LocaleMaxLength = 16;
    private const int TenantIdMaxLength = 128;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TranslationEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Key)
            .IsRequired()
            .HasMaxLength(KeyMaxLength);

        builder.Property(e => e.Locale)
            .IsRequired()
            .HasMaxLength(LocaleMaxLength);

        builder.Property(e => e.Value)
            .IsRequired();

        builder.Property(e => e.TenantId)
            .HasMaxLength(TenantIdMaxLength);

        builder.HasIndex(e => new { e.Key, e.Locale, e.TenantId })
            .IsUnique();

        // The read behind a bundle names a locale and a tenant and no key at all, and it is the read
        // that happens most: one per locale per tenant, against every row of the table. The unique
        // index above is led by the key and cannot answer it, so that read would scan the table.
        builder.HasIndex(e => new { e.Locale, e.TenantId });
    }
}
