using System.Collections.Concurrent;
using System.Reflection;

namespace Bsft.EventAggregator.Saga;

/// <summary>
/// Resolves the CLR type of a value read back from saga storage: a timeout payload, or the state of a
/// persisted saga instance.
/// </summary>
/// <remarks>
/// The type name is read back from the database, so it is untrusted input. A plain
/// <see cref="Type.GetType(string)"/> on an assembly-qualified name will happily <em>load an
/// assembly</em> named in that string. Resolution is therefore restricted to assemblies already
/// loaded into the process: a tampered row can then at worst name a type the application already
/// carries, and can never pull new code in. Both readers of persisted type names go through here, so
/// the guard holds for the timeout table and the state table alike.
/// <para>
/// Residual risk, accepted knowingly: this restricts <em>where</em> a type may come from, not
/// <em>which</em> type. Somebody able to write arbitrary rows into the timeout table can still have
/// any already-loaded, JSON-constructible type built and populated. Constraining that further needs
/// a registry of legitimate payload types, which is only complete once the saga flows have been
/// started — a dependency whose failure mode (no dispatch at all) is worse than the risk it closes.
/// The timeout table is therefore treated as trusted storage: an attacker who can write to it is
/// already inside the database.
/// </para>
/// </remarks>
internal static class SagaPersistedTypeResolver
{
    // Resolution walks every loaded assembly, so the answer is cached per type name: the dispatcher
    // otherwise repeats that scan for every single timer it fires.
    private static readonly ConcurrentDictionary<string, Type> _resolved = new(StringComparer.Ordinal);

    /// <summary>
    /// Resolves a persisted type name to a type from an already-loaded assembly.
    /// </summary>
    /// <param name="assemblyQualifiedName">The type name as stored with the row.</param>
    /// <returns>The type, or <c>null</c> when it is not present in the process.</returns>
    /// <remarks>
    /// Only a successful resolution is cached. Which assemblies are loaded changes over the lifetime
    /// of the process, so a failure is an answer about this moment, not about the type name: caching
    /// it would let a dispatcher pass that ran before the payload's assembly was loaded keep every
    /// later retry of that timeout failing, long after the type became resolvable.
    /// </remarks>
    public static Type? Resolve(string assemblyQualifiedName)
    {
        if (_resolved.TryGetValue(assemblyQualifiedName, out var cached))
            return cached;

        var resolved = Type.GetType(
            assemblyQualifiedName,
            assemblyResolver: FindLoadedAssembly,
            typeResolver: (assembly, typeName, ignoreCase) =>
                assembly is null ? null : assembly.GetType(typeName, throwOnError: false, ignoreCase),
            throwOnError: false,
            ignoreCase: false);

        if (resolved is not null)
            _resolved[assemblyQualifiedName] = resolved;

        return resolved;
    }

    private static Assembly? FindLoadedAssembly(AssemblyName name) =>
        AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, name.Name, StringComparison.Ordinal));
}
