using System.Reflection;

namespace Bsft.Mediator;

/// <summary>
/// Reads the types of an assembly, tolerating one that cannot be loaded in full.
/// </summary>
internal static class AssemblyTypes
{
    /// <summary>Returns the types of an assembly that could be loaded.</summary>
    /// <param name="assembly">The assembly to read.</param>
    /// <returns>The loadable types.</returns>
    /// <remarks>
    /// An assembly referencing something the host never deployed throws while its types are read,
    /// and reports the ones it did manage to load. Scanning has to carry on with those: a single
    /// unloadable optional dependency must not take the whole scan, and with it the registration or
    /// the diagnostic that depends on it, down with it.
    /// </remarks>
    internal static IEnumerable<Type> Loadable(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null)!;
        }
    }
}
