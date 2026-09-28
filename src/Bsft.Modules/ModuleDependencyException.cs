namespace Bsft.Modules;

/// <summary>
/// Thrown when a module dependency cannot be resolved, such as a circular dependency
/// or a reference to a module that does not exist in the catalog.
/// </summary>
public sealed class ModuleDependencyException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ModuleDependencyException"/> class.
    /// </summary>
    /// <param name="message">The error message describing the dependency problem.</param>
    public ModuleDependencyException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ModuleDependencyException"/> class with an inner exception.
    /// </summary>
    /// <param name="message">The error message describing the dependency problem.</param>
    /// <param name="innerException">The inner exception.</param>
    public ModuleDependencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
