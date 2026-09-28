using Microsoft.Extensions.Hosting;

namespace Bsft.Testing.Web;

/// <summary>
/// Recognises a test host by an allow-list of environment names.
/// </summary>
/// <remarks>
/// The list is an allow-list rather than a list of environments to refuse, because the failure modes
/// are not symmetric. Refusing a test host that uses an unusual environment name costs one explicit
/// argument at registration and says so in the exception; accepting a deployed host because it calls
/// itself <c>Live</c> or <c>PRD</c> instead of <c>Production</c> would authenticate every caller.
/// <para>
/// For the same reason <see cref="Environments.Development"/> is not seeded: a deployed host left on
/// <c>ASPNETCORE_ENVIRONMENT=Development</c> is the misconfiguration this guard exists to catch, and
/// it would silently authenticate every request as the default test user. A project that really does
/// run its tests against a Development host says so through <c>additionalEnvironments</c>.
/// </para>
/// </remarks>
public sealed class TestEnvironmentPolicy : ITestEnvironmentPolicy
{
    /// <summary>The environment name the Bsft web application factory boots a test host in.</summary>
    public const string TestingEnvironment = "Testing";

    private readonly HashSet<string> _allowed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestEnvironmentPolicy"/> class.
    /// </summary>
    /// <param name="environment">The environment the host reports.</param>
    /// <param name="additionalEnvironments">
    /// Further environment names to accept, for a project whose test host does not use
    /// <see cref="TestingEnvironment"/> — <c>Development</c> included, which is not accepted
    /// unless it is named here.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="environment"/> or <paramref name="additionalEnvironments"/> is null.
    /// </exception>
    public TestEnvironmentPolicy(IHostEnvironment environment, params string[] additionalEnvironments)
    {
        if (environment is null)
            throw new ArgumentNullException(nameof(environment));
        if (additionalEnvironments is null)
            throw new ArgumentNullException(nameof(additionalEnvironments));

        EnvironmentName = environment.EnvironmentName;
        _allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { TestingEnvironment };

        foreach (var name in additionalEnvironments.Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            _allowed.Add(name);
        }
    }

    /// <inheritdoc />
    public bool IsTestHost => _allowed.Contains(EnvironmentName);

    /// <inheritdoc />
    public string EnvironmentName { get; }

    /// <inheritdoc />
    public IReadOnlyCollection<string> AllowedEnvironments => _allowed;
}
