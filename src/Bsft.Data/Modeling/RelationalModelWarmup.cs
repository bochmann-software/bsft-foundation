using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Bsft.Data.Modeling;

/// <summary>
/// Builds the relational model of a runtime model once, under one process-wide lock, so that no two
/// threads ever build the same one.
/// </summary>
/// <remarks>
/// <para>
/// EF Core builds a model's relational model lazily, on the first <c>GetRelationalModel</c> call, and
/// that call has a check-then-act gap. It reads the built relational model, finds none, and only then
/// reads the pending factory; the caller that builds it stores the result and removes the factory in
/// between. A caller that read "not built" just before the store and reads the factory just after the
/// removal finds neither and throws "The model must be finalized and its runtime dependencies must be
/// initialized before 'GetRelationalModel' can be used". The gap is open only while a model is new, so
/// it hits concurrent first requests right after a host starts.
/// </para>
/// <para>
/// Building it here, before a context of that model can run a command, closes the gap: every caller
/// that reaches EF Core's own call afterwards finds the built model on its first read. Only the
/// runtime model is built. The design-time model is a second, separately cached model that the
/// migration tooling and <c>EnsureCreated</c> use; building it for every context type would double the
/// model cost of every host to cover a path that runs once, at deployment.
/// </para>
/// </remarks>
internal static class RelationalModelWarmup
{
    private static readonly object Gate = new();

    /// <summary>Builds the relational model of a context's runtime model, when its provider is relational.</summary>
    /// <param name="context">The context whose model is built.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
    internal static void EnsureBuilt(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Database.IsRelational())
            EnsureBuilt(context.Model);
    }

    /// <summary>Builds the relational model of a runtime model unless it is built already.</summary>
    /// <param name="model">A model that has been through EF Core's runtime initialization.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="model"/> is null.</exception>
    /// <remarks>
    /// The check outside the lock keeps a built model off the lock, which is every call but the first
    /// few of each model. A caller that queued behind the builder finds the model built when it gets
    /// the lock, and EF Core's own call returns it without building it again.
    /// </remarks>
    internal static void EnsureBuilt(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (IsBuilt(model))
            return;

        lock (Gate)
        {
            _ = model.GetRelationalModel();
        }
    }

    /// <summary>Whether the relational model of a model is built.</summary>
    /// <param name="model">The model.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    internal static bool IsBuilt(IModel model)
        => model.FindRuntimeAnnotation(RelationalAnnotationNames.RelationalModel) is not null;
}
