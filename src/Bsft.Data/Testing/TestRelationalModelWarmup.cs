using Bsft.Data.Modeling;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.Testing;

/// <summary>
/// Builds the relational model of a context's runtime model from a test, the same way
/// <c>RelationalModelWarmupHostedService</c> builds it from a host.
/// </summary>
/// <remarks>
/// Most tests open a relational context without a host, so no hosted service ever subscribes to EF
/// Core's diagnostic listener for them, and the gap <see cref="RelationalModelWarmup"/> closes for a
/// host stays open: parallel test classes that share a context type race the same "The model must be
/// finalized and its runtime dependencies must be initialized" failure a production host does. A test's
/// own startup hook calls this instead of reimplementing the build. It builds only the runtime model;
/// the design-time model that migrations and <c>EnsureCreated</c> use is a second, separately cached
/// model this type leaves alone, the same way the hosted service does for a host.
/// </remarks>
public static class TestRelationalModelWarmup
{
    /// <summary>Builds the relational model of a context's runtime model, when its provider is relational.</summary>
    /// <param name="context">The context whose model is built.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
    public static void EnsureRelationalModelBuilt(this DbContext context) => RelationalModelWarmup.EnsureBuilt(context);
}
