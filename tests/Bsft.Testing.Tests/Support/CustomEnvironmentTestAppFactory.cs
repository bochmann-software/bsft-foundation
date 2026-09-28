using Bsft.Testing.Web;

namespace Bsft.Testing.Tests.Support;

/// <summary>
/// The same application, but booted in the environment name a project with its own convention would
/// pick. Overriding <see cref="BsftWebApplicationFactory{TEntryPoint}.EnvironmentName"/> is the
/// documented way to do that.
/// </summary>
internal sealed class CustomEnvironmentTestAppFactory : TestAppFactory
{
    public const string Environment = "IntegrationTest";

    protected override string EnvironmentName => Environment;
}
