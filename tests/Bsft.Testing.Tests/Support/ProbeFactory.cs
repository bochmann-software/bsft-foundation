using Bsft.Testing.Web;
using Microsoft.AspNetCore.Hosting;

namespace Bsft.Testing.Tests.Support;

/// <summary>Exposes the protected host configuration hook so its guard can be exercised.</summary>
internal sealed class ProbeFactory : BsftWebApplicationFactory<TestAppEntryPoint>
{
    public void ConfigureWebHostWith(IWebHostBuilder builder) => ConfigureWebHost(builder);
}
