using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Bsft.Data.Modeling;

/// <summary>
/// Hands every context that EF Core reports as initialized to the warm-up, before the context can run
/// a command.
/// </summary>
/// <remarks>
/// EF Core raises <see cref="CoreEventId.ContextInitialized"/> synchronously on the thread that
/// initializes the context, so whatever runs here is done before that context's first query or save.
/// </remarks>
internal sealed class ContextInitializedObserver : IObserver<KeyValuePair<string, object?>>
{
    private readonly Action<DbContext> _warm;

    /// <summary>Initializes a new instance of the <see cref="ContextInitializedObserver"/> class.</summary>
    /// <param name="warm">Builds the relational model of the context it is given.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="warm"/> is null.</exception>
    public ContextInitializedObserver(Action<DbContext> warm)
    {
        _warm = warm ?? throw new ArgumentNullException(nameof(warm));
    }

    /// <summary>Whether an EF Core event is the one this observer handles.</summary>
    /// <param name="name">The event name.</param>
    /// <returns><see langword="true"/> for <see cref="CoreEventId.ContextInitialized"/>.</returns>
    internal static bool IsContextInitialized(string name)
        => string.Equals(name, CoreEventId.ContextInitialized.Name, StringComparison.Ordinal);

    /// <inheritdoc />
    public void OnNext(KeyValuePair<string, object?> value)
    {
        if (value.Value is ContextInitializedEventData { Context: { } context })
            _warm(context);
    }

    /// <inheritdoc />
    public void OnCompleted()
    {
        // A listener completes when it is disposed; there is nothing of this observer's to release.
    }

    /// <inheritdoc />
    public void OnError(Exception error)
    {
        // DiagnosticListener never reports errors to its observers.
    }
}
