using System.Diagnostics;
using System.Runtime.CompilerServices;
using Bsft.Data.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Bsft.Tests.Shared;

/// <summary>
/// Builds the relational view of a relational context's models as the context initializes, one context
/// at a time, before the context can run a command.
/// </summary>
/// <remarks>
/// <para>
/// EF Core builds a model's relational view lazily, on the first <c>GetRelationalModel</c> call, and that
/// first call has a check-then-act gap: a caller that finds the view missing, and looks for the pending
/// factory just after another caller has built the view and removed the factory, finds neither and throws
/// "The model must be finalized and its runtime dependencies must be initialized". Test classes run in
/// parallel and share the cached models of each context type, so the first <c>EnsureCreated</c> of
/// several classes (design-time model) and their first query or <c>SaveChanges</c> (runtime model) race,
/// and on a loaded machine one of them loses.
/// </para>
/// <para>
/// Every context reports its initialization to the EF Core diagnostic listener before it runs anything.
/// Building both views there leaves every later caller on the already-built path. The runtime model is
/// built by <see cref="TestRelationalModelWarmup"/>, the same guard a host gets from
/// <c>RelationalModelWarmupHostedService</c>, under its own process-wide lock; a test still has to build
/// the design-time model itself, because the product guard deliberately leaves it lazy (it is used by
/// migrations and <c>EnsureCreated</c> only, a path no host warms up either). Directory.Build.targets
/// compiles this file into every test project that references EF Core's relational layer, so a new test
/// project is covered without remembering to link it.
/// </para>
/// </remarks>
internal static class RelationalModelWarmup
{
    private static readonly object Gate = new();

#pragma warning disable CA2255 // A test assembly has no entry point; a module initializer is the one hook that runs before its first test.
    [ModuleInitializer]
    internal static void Install() => DiagnosticListener.AllListeners.Subscribe(new ListenerObserver());
#pragma warning restore CA2255

    private static void BuildRelationalModels(DbContext context)
    {
        if (!context.Database.IsRelational())
            return;

        context.EnsureRelationalModelBuilt();

        lock (Gate)
        {
            _ = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        }
    }

    /// <summary>Subscribes to each EF Core diagnostic listener as it is created.</summary>
    private sealed class ListenerObserver : IObserver<DiagnosticListener>
    {
        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == DbLoggerCategory.Name)
                value.Subscribe(new ContextInitializedObserver(), static (name, _, _) => name == CoreEventId.ContextInitialized.Name);
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }

    /// <summary>Builds the views of each context that reports its initialization.</summary>
    private sealed class ContextInitializedObserver : IObserver<KeyValuePair<string, object?>>
    {
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Value is ContextInitializedEventData { Context: { } context })
                BuildRelationalModels(context);
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }
}
