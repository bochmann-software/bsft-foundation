using System.Text.Json;
using Bsft.Modules.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Bsft.Modules.Tests;

public class ModuleDiscoveryTests
{
    [Fact]
    public void GetModuleInfo_ReturnsExpectedFormat()
    {
        var catalog = new ModuleCatalog();
        catalog.Add<TestModuleA>();
        catalog.Add<TestModuleB>();
        var host = new ModuleHost([new TestModuleA(), new TestModuleB()], catalog);

        var info = host.ListModuleInfo();

        info.Should().HaveCount(2);
        info[0].Name.Should().Be("TestModuleA");
        info[0].Version.Should().Be("1.0.0");
        info[0].Enabled.Should().BeTrue();
        info[1].Name.Should().Be("TestModuleB");
        info[1].Version.Should().Be("1.1.0");
        info[1].Enabled.Should().BeTrue();
    }

    [Fact]
    public void ModuleInfo_SerializesToExpectedJson()
    {
        var info = new ModuleInfo("Orders", "1.2.0", true);

        var json = JsonSerializer.Serialize(info);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("name").GetString().Should().Be("Orders");
        root.GetProperty("version").GetString().Should().Be("1.2.0");
        root.GetProperty("enabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void DiscoveryResponse_SerializesToExpectedFormat()
    {
        var catalog = new ModuleCatalog();
        catalog.Add<TestModuleA>();
        var host = new ModuleHost([new TestModuleA()], catalog);
        var response = new { modules = host.ListModuleInfo() };

        var json = JsonSerializer.Serialize(response);
        using var doc = JsonDocument.Parse(json);
        var modulesArray = doc.RootElement.GetProperty("modules");

        modulesArray.GetArrayLength().Should().Be(1);
        modulesArray[0].GetProperty("name").GetString().Should().Be("TestModuleA");
    }
}
