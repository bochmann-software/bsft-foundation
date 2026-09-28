using Bsft.Data.Contracts.Filtering;
using Bsft.Tests.Shared;
using FluentAssertions;
using Xunit;

namespace Bsft.Data.Contracts.Tests;

/// <summary>
/// The empty dependency list is the whole point of this package, and it is not something the compiler
/// defends: a reference to EF Core or to the ASP.NET Core shared framework compiles perfectly well and
/// quietly imposes an ORM or a web host on every consumer that only wanted the DTOs, the Blazor
/// WebAssembly front end the README invites included.
/// </summary>
public sealed class DependencyClosureTests
{
    private const string ContractsProject = "Bsft.Data.Contracts";

    private const string AspNetCoreSharedFramework = "Microsoft.AspNetCore.App";

    private const string EntityFrameworkCoreAssembly = "Microsoft.EntityFrameworkCore";

    private const string ImplementationAssembly = "Bsft.Data";

    private const string AspNetCoreAssemblyPrefix = "Microsoft.AspNetCore";

    [Fact]
    public void The_contracts_package_carries_nothing_at_all()
    {
        // Emptiness rather than a list of exclusions: naming the ones rejected so far would let the
        // next one in unnoticed, and the README states the list is empty, not short.
        DependencyClosure.DependencyNames(DependencyClosure.RestoredGraph(ContractsProject))
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void The_contracts_package_does_not_impose_the_asp_net_core_shared_framework()
        => DependencyClosure.FrameworkReferences(DependencyClosure.RestoredGraph(ContractsProject))
            .Should()
            .NotContain(AspNetCoreSharedFramework);

    [Fact]
    public void The_compiled_contracts_assembly_references_nothing_of_the_implementation()
    {
        var referenced = typeof(FilterNode).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToList();

        referenced.Should().NotContain(EntityFrameworkCoreAssembly);
        referenced.Should().NotContain(ImplementationAssembly);

        // By prefix, because the shared framework enters an assembly's reference list as the individual
        // assemblies a compilation actually bound to, never under its own id.
        referenced.Should().NotContain(name => name!.StartsWith(AspNetCoreAssemblyPrefix, StringComparison.Ordinal));
    }
}
