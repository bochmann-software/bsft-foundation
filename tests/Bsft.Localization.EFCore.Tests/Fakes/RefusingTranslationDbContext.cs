using Bsft.Localization.EFCore.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Localization.EFCore.Tests.Fakes;

/// <summary>
/// A store whose first writes are refused, standing in for the unique index refusing a row a second
/// instance inserted first.
/// </summary>
/// <remarks>
/// The in-memory provider enforces no index, so the race cannot be staged against it directly. What
/// matters to the seed applier is what a refused write does to it, and that is what this refuses.
/// </remarks>
internal sealed class RefusingTranslationDbContext : ITranslationDbContext
{
    private readonly TestTranslationDbContext _inner;
    private readonly Action? _onRefusal;
    private int _refusals;

    /// <summary>Initializes a new instance of the <see cref="RefusingTranslationDbContext"/> class.</summary>
    /// <param name="inner">The store behind it.</param>
    /// <param name="refusals">How many writes are refused before one is let through.</param>
    /// <param name="onRefusal">What the other instance does while this one's write is refused.</param>
    public RefusingTranslationDbContext(TestTranslationDbContext inner, int refusals, Action? onRefusal = null)
    {
        _inner = inner;
        _refusals = refusals;
        _onRefusal = onRefusal;
    }

    /// <inheritdoc />
    public DbSet<TranslationEntity> Translations => _inner.Translations;

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        if (_refusals <= 0)
            return _inner.SaveChangesAsync(ct);

        _refusals--;
        _onRefusal?.Invoke();

        throw new DbUpdateException("The unique index over key, locale and tenant refused the write.");
    }
}
