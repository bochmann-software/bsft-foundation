using System.Reflection;
using System.Reflection.Emit;
using Bsft.EventAggregator.Saga.Timeouts;
using FluentAssertions;
using Xunit;

namespace Bsft.EventAggregator.Saga.Tests;

/// <summary>
/// Resolving a type name read back from saga storage. The resolver caches, and what it may cache is
/// the point: which assemblies are loaded changes while the process runs.
/// </summary>
public class SagaPersistedTypeResolverTests
{
    [Fact]
    public void ATypeFromALoadedAssembly_Resolves()
    {
        SagaPersistedTypeResolver.Resolve(typeof(SagaTimeout).AssemblyQualifiedName!)
            .Should().Be(typeof(SagaTimeout));
    }

    [Fact]
    public void ATypeFromAnAssemblyThatIsNotLoaded_ResolvesToNull()
    {
        SagaPersistedTypeResolver.Resolve("Nowhere.Payload, Nowhere.Assembly, Version=1.0.0.0")
            .Should().BeNull();
    }

    [Fact]
    public void ANameThatFailedBeforeItsAssemblyWasLoaded_ResolvesOnceItIs()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var assemblyName = $"PayloadAssembly_{suffix}";
        var qualifiedName = $"Payload_{suffix}, {assemblyName}";

        // The dispatcher's first pass over a timeout whose payload assembly has not been loaded yet.
        SagaPersistedTypeResolver.Resolve(qualifiedName).Should().BeNull();

        DefinePayloadType(assemblyName, $"Payload_{suffix}");

        // The retry after the assembly arrived. A cached negative answer would keep every retry of
        // that timeout failing for the rest of the process lifetime.
        SagaPersistedTypeResolver.Resolve(qualifiedName).Should().NotBeNull();
    }

    /// <summary>Loads an assembly into the process at a controlled moment.</summary>
    private static void DefinePayloadType(string assemblyName, string typeName)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName), AssemblyBuilderAccess.Run);
        assembly.DefineDynamicModule(assemblyName)
            .DefineType(typeName, TypeAttributes.Public)
            .CreateType();
    }
}
