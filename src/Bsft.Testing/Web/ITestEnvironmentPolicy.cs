namespace Bsft.Testing.Web;

/// <summary>
/// Decides whether the host the application is running in may use the test authentication scheme.
/// </summary>
public interface ITestEnvironmentPolicy
{
    /// <summary>
    /// Gets a value indicating whether the current host is a recognised test host.
    /// </summary>
    bool IsTestHost { get; }

    /// <summary>Gets the environment name the decision was made from.</summary>
    string EnvironmentName { get; }

    /// <summary>Gets the environment names that are accepted as a test host.</summary>
    IReadOnlyCollection<string> AllowedEnvironments { get; }
}
