using System.Reflection;
using Bsft.Modules.Tests.Fakes;
using Bsft.Modules.Tests.Support;
using FluentAssertions;
using Xunit.Abstractions;
using Xunit;

namespace Bsft.Modules.Tests;

public class ModuleCatalogTests
{
    private readonly ITestOutputHelper _output;

    public ModuleCatalogTests(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public void Add_RegistersModuleWithCorrectMetadata()
    {
        var catalog = new ModuleCatalog();

        catalog.Add<TestModuleA>();

        var modules = catalog.ListModules();
        modules.Should().HaveCount(1);
        modules[0].Name.Should().Be("TestModuleA");
        modules[0].Version.Should().Be("1.0.0");
        modules[0].Enabled.Should().BeTrue();
        modules[0].ModuleType.Should().Be(typeof(TestModuleA));
    }

    [Fact]
    public void Add_MultipleModules_RegistersAll()
    {
        var catalog = new ModuleCatalog();

        catalog.Add<TestModuleA>().Add<TestModuleB>();

        catalog.ListModules().Should().HaveCount(2);
    }

    [Fact]
    public void Add_DuplicateModule_ThrowsInvalidOperationException()
    {
        var catalog = new ModuleCatalog();
        catalog.Add<TestModuleA>();

        var act = () => catalog.Add<TestModuleA>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'TestModuleA'*already registered*");
    }

    [Fact]
    public void FromConfigFile_ReadsModulesFromJson()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
                {
                    "modules": [
                        { "name": "Orders", "assembly": "./modules/Orders.dll", "enabled": true },
                        { "name": "Shipping", "assembly": "./modules/Shipping.dll", "enabled": true, "dependsOn": ["Orders"] }
                    ]
                }
                """);

            var catalog = new ModuleCatalog();
            catalog.FromConfigurationFile(tempFile);

            var modules = catalog.ListModules();
            modules.Should().HaveCount(2);
            modules[0].Name.Should().Be("Orders");
            modules[0].Enabled.Should().BeTrue();
            modules[0].AssemblyPath.Should().Be("./modules/Orders.dll");
            modules[1].Name.Should().Be("Shipping");
            modules[1].DependsOn.Should().Contain("Orders");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Names are compared the way the loader compares them. A catalog that held both spellings would
    /// fail much later, at load time, over a duplicate the manifest's author never wrote twice.
    /// </summary>
    [Fact]
    public void FromConfigFile_WithTwoNamesDifferingOnlyInCase_KeepsTheFirst()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
                {
                    "modules": [
                        { "name": "Orders", "assembly": "./modules/Orders.dll" },
                        { "name": "orders", "assembly": "./modules/Other.dll" }
                    ]
                }
                """);

            var catalog = new ModuleCatalog();
            catalog.FromConfigurationFile(tempFile);

            catalog.ListModules().Should().ContainSingle()
                .Which.AssemblyPath.Should().Be("./modules/Orders.dll");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void FromConfigFile_DisabledModule_PreservesEnabledFlag()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
                {
                    "modules": [
                        { "name": "Disabled", "assembly": "./disabled.dll", "enabled": false }
                    ]
                }
                """);

            var catalog = new ModuleCatalog();
            catalog.FromConfigurationFile(tempFile);

            catalog.ListModules()[0].Enabled.Should().BeFalse();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void FromConfigFile_FileNotFound_ThrowsFileNotFoundException()
    {
        var catalog = new ModuleCatalog();

        var act = () => catalog.FromConfigurationFile("/nonexistent/path.json");

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void FromConfigFile_NullPath_ThrowsArgumentNullException()
    {
        var catalog = new ModuleCatalog();

        var act = () => catalog.FromConfigurationFile(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("path");
    }

    [Fact]
    public void FromConfigFile_EmptyModulesSection_ReturnsEmptyCatalog()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """{ "modules": null }""");

            var catalog = new ModuleCatalog();
            catalog.FromConfigurationFile(tempFile);

            catalog.ListModules().Should().BeEmpty();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void FromDirectory_NullPath_ThrowsArgumentNullException()
    {
        var catalog = new ModuleCatalog();

        var act = () => catalog.FromDirectory(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("path");
    }

    [Fact]
    public void FromDirectory_NonExistentPath_ThrowsDirectoryNotFoundException()
    {
        var catalog = new ModuleCatalog();

        var act = () => catalog.FromDirectory("/nonexistent/path/that/does/not/exist");

        act.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void FromDirectory_EmptyDirectory_ReturnsEmptyCatalog()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var catalog = new ModuleCatalog();
            catalog.FromDirectory(tempDir);

            catalog.ListModules().Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void FromDirectory_DirectoryWithNonDotNetDll_IsSkippedGracefully()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            // Write a file that looks like a DLL but is not a valid .NET assembly (BadImageFormatException path)
            File.WriteAllBytes(Path.Combine(tempDir, "fake.dll"), [0xFF, 0xFE, 0x00, 0x01, 0x02, 0x03]);

            var catalog = new ModuleCatalog();
            catalog.FromDirectory(tempDir);

            // Should not throw; the bad DLL is skipped
            catalog.ListModules().Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void FromDirectory_WithValidModuleDll_DiscoversModuleTypes()
    {
        // Copy the test assembly (which contains TestModuleA etc. - all with parameterless constructors)
        // to a temp dir along with its Bsft.Modules dependency so that IModule resolves correctly.
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var testAssemblyPath = typeof(TestModuleA).Assembly.Location;
            var modulesDll = typeof(IModule).Assembly.Location;
            var testAssemblyDir = Path.GetDirectoryName(testAssemblyPath)!;

            // Copy the test assembly and all its dependencies to the temp dir
            File.Copy(testAssemblyPath, Path.Combine(tempDir, Path.GetFileName(testAssemblyPath)));
            File.Copy(modulesDll, Path.Combine(tempDir, Path.GetFileName(modulesDll)), overwrite: true);

            // Also copy Microsoft.Extensions.Configuration.Abstractions which IModule.ConfigureServices needs
            foreach (var dep in Directory.GetFiles(testAssemblyDir, "Microsoft.Extensions.*.dll"))
            {
                var dest = Path.Combine(tempDir, Path.GetFileName(dep));
                if (!File.Exists(dest))
                    File.Copy(dep, dest);
            }

            var catalog = new ModuleCatalog();
            catalog.FromDirectory(tempDir);

            // The test assembly contains TestModuleA, TestModuleB, TestModuleC (all with parameterless ctors)
            catalog.ListModules().Should().NotBeEmpty();
            catalog.ListModules().Should().Contain(m => m.Name == "TestModuleA");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void FromDirectory_DuplicateModuleName_SkipsSecondInstance()
    {
        // Pre-register TestModuleA, then scan a directory that also discovers it.
        // The duplicate should be skipped.
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var testAssemblyPath = typeof(TestModuleA).Assembly.Location;
            var modulesDll = typeof(IModule).Assembly.Location;
            var testAssemblyDir = Path.GetDirectoryName(testAssemblyPath)!;

            File.Copy(testAssemblyPath, Path.Combine(tempDir, Path.GetFileName(testAssemblyPath)));
            File.Copy(modulesDll, Path.Combine(tempDir, Path.GetFileName(modulesDll)), overwrite: true);

            foreach (var dep in Directory.GetFiles(testAssemblyDir, "Microsoft.Extensions.*.dll"))
            {
                var dest = Path.Combine(tempDir, Path.GetFileName(dep));
                if (!File.Exists(dest))
                    File.Copy(dep, dest);
            }

            var catalog = new ModuleCatalog();
            catalog.Add<TestModuleA>(); // pre-register
            catalog.FromDirectory(tempDir);

            // TestModuleA should appear only once (the duplicate from the DLL is skipped)
            catalog.ListModules().Count(m => m.Name == "TestModuleA").Should().Be(1);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void FromDirectory_WithAnAssemblyWhoseTypesCannotBeRead_NamesTheAssembly()
    {
        // We write a minimal valid .NET assembly DLL (pre-compiled IL) that references a non-existent
        // assembly "FakeNonExistent". Assembly.LoadFrom() succeeds but assembly.GetTypes() throws
        // ReflectionTypeLoadException because "FakeNonExistent" cannot be resolved. That is a managed
        // assembly with a broken deployment, not a file that merely happens to sit in the directory,
        // so the scan says which file it was instead of quietly finding no modules in it.
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            // Write the pre-crafted DLL bytes. This assembly was created with ilasm and contains
            // a single type BrokenClass that extends FakeNonExistent.BaseClass.
            // The assembly loads fine but GetTypes() throws ReflectionTypeLoadException.
            var brokenDllBytes = BrokenAssemblyBytes.GetBytes();
            File.WriteAllBytes(Path.Combine(tempDir, "BrokenTestAssembly.dll"), brokenDllBytes);

            var catalog = new ModuleCatalog();
            var act = () => catalog.FromDirectory(tempDir);

            act.Should().Throw<ModuleDependencyException>()
                .WithMessage("*BrokenTestAssembly.dll*")
                .WithInnerException<ReflectionTypeLoadException>();

            catalog.ListModules().Should().BeEmpty();
        }
        finally
        {
            // Assembly.LoadFrom() locks the DLL on Windows; attempt cleanup but ignore access errors.
            // Assembly.LoadFrom() keeps the DLL mapped for the life of the test process, so the
            // directory can still be locked here. Report it instead of swallowing it silently;
            // the OS reclaims the temp folder afterwards.
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch (UnauthorizedAccessException ex)
            {
                _output.WriteLine($"Temp directory '{tempDir}' stayed behind: {ex.Message}");
            }
        }
    }

    [Fact]
    public void FromConfigFile_DuplicateWithExisting_SkipsDuplicate()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
                {
                    "modules": [
                        { "name": "TestModuleA", "assembly": "./a.dll", "enabled": true }
                    ]
                }
                """);

            var catalog = new ModuleCatalog();
            catalog.Add<TestModuleA>();
            catalog.FromConfigurationFile(tempFile);

            catalog.ListModules().Should().HaveCount(1);
            catalog.ListModules()[0].ModuleType.Should().Be(typeof(TestModuleA));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
