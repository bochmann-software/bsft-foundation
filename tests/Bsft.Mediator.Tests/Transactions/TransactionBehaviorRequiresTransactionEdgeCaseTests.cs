using Bsft.Core;
using Bsft.Mediator.Transactions;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Reflection;
using Xunit;

namespace Bsft.Mediator.Tests.Transactions;

public class TransactionBehaviorRequiresTransactionEdgeCaseTests
{
    [Fact]
    public async Task RequiresTransaction_WhenAppDomainContainsBrokenAssembly_HandlesReflectionTypeLoadException()
    {
        // Exercises TransactionBehavior.RequiresTransaction lines 144-146:
        // When iterating AppDomain assemblies, an assembly whose GetTypes() throws
        // ReflectionTypeLoadException is silently skipped via the catch block (continue).
        // We write a pre-compiled DLL that references a non-existent assembly "FakeNonExistent"
        // and load it into the AppDomain. Once loaded, any call to RequiresTransaction() that
        // iterates AppDomain.CurrentDomain.GetAssemblies() will encounter it and hit lines 144-146.
        var tempPath = Path.Combine(
            Path.GetTempPath(), "MediatorBrokenTest_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(tempPath, BrokenMediatorTestAssemblyBytes.GetBytes());
        // LoadFrom adds the assembly to AppDomain.CurrentDomain. GetTypes() on it throws
        // ReflectionTypeLoadException because "FakeNonExistent" cannot be resolved.
        var brokenAssembly = Assembly.LoadFrom(tempPath);

        // Verify the DLL does throw ReflectionTypeLoadException from GetTypes()
        var act = () => brokenAssembly.GetTypes();
        act.Should().Throw<ReflectionTypeLoadException>();

        // Exercise RequiresTransaction via HandleAsync. The behavior iterates AppDomain assemblies,
        // encounters our broken assembly, catches ReflectionTypeLoadException, and continues.
        // Use a dedicated request type so the cached IsTransactionRequired scan runs for the first
        // time here -- after the broken assembly is in the AppDomain -- guaranteeing the
        // ReflectionTypeLoadException catch is exercised regardless of test execution order.
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        var logger = sp.GetRequiredService<ILogger<TransactionBehavior<BrokenScanProbeCommand, string>>>();
        var behavior = new TransactionBehavior<BrokenScanProbeCommand, string>(sp, logger);

        var result = await behavior.HandleAsync(
            new BrokenScanProbeCommand("test"), () => Task.FromResult("ok"));

        result.Should().Be("ok");
    }
}
