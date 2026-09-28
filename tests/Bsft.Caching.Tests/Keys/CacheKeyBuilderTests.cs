using Bsft.Caching.Keys;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Caching.Tests.Keys;

public class CacheKeyBuilderTests
{
    private static CacheKeyBuilder CreateBuilder(string prefix = "bsft", string? scope = null) =>
        new(Options.Create(new BsftCachingOptions { KeyPrefix = prefix }), new FixedCacheKeyScopeProvider(scope));

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        var act = () => new CacheKeyBuilder(null!, new GlobalCacheKeyScopeProvider());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullScopeProvider_Throws()
    {
        var act = () => new CacheKeyBuilder(Options.Create(new BsftCachingOptions()), null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Build_ComposesPrefixScopeTypeAndKey()
    {
        var request = new DefaultsQuery("42");
        var type = typeof(DefaultsQuery).AssemblyQualifiedName!;

        var key = CreateBuilder("app", "tenant-1").Build(request);

        key.Should().Be($"app:8:tenant-1:{type.Length}:{type}:2:42");
    }

    [Fact]
    public void Build_WithoutScope_LeavesTheScopeSegmentEmpty()
    {
        var request = new DefaultsQuery("42");

        CreateBuilder().Build(request).Should().StartWith("bsft:0::");
    }

    [Fact]
    public void Build_UsesTheAssemblyQualifiedTypeName()
    {
        var request = new DefaultsQuery("42");

        CreateBuilder().Build(request).Should().Contain(typeof(DefaultsQuery).AssemblyQualifiedName!);
    }

    [Fact]
    public void Build_DifferentScopes_DoNotCollide()
    {
        var request = new DefaultsQuery("42");

        CreateBuilder(scope: "tenant-1").Build(request)
            .Should().NotBe(CreateBuilder(scope: "tenant-2").Build(request));
    }

    [Fact]
    public void Build_SeparatorInsideTheSegments_DoesNotShiftTheBoundary()
    {
        // "a" + "b:c" and "a:b" + "c" would produce one and the same key without length prefixes.
        var first = new DefaultsQuery("b:c");
        var second = new DefaultsQuery("c");

        CreateBuilder(scope: "a").Build(first)
            .Should().NotBe(CreateBuilder(scope: "a:b").Build(second));
    }

    [Fact]
    public void Build_DifferentRequestTypesWithSameKey_DoNotCollide()
    {
        var builder = CreateBuilder();

        builder.Build(new DefaultsQuery("same"))
            .Should().NotBe(builder.Build(new TaggedQuery("same", null, [])));
    }

    [Fact]
    public void Build_NullRequest_Throws()
    {
        var act = () => CreateBuilder().Build(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Build_WhenTheScopeIsNotCacheable_ReturnsNoKey()
    {
        var builder = new CacheKeyBuilder(
            Options.Create(new BsftCachingOptions()),
            new FixedCacheKeyScopeProvider(CacheKeyScope.NotCacheable));

        builder.Build(new DefaultsQuery("42")).Should().BeNull();
    }

    [Fact]
    public void Build_WhenTheScopeProviderReturnsTheDefaultScope_ReturnsNoKey()
    {
        // default(CacheKeyScope) is NotCacheable deliberately: an uninitialized scope disables
        // caching instead of silently caching globally.
        var builder = new CacheKeyBuilder(
            Options.Create(new BsftCachingOptions()),
            new FixedCacheKeyScopeProvider(default(CacheKeyScope)));

        builder.Build(new DefaultsQuery("42")).Should().BeNull();
    }

    [Fact]
    public void GlobalScopeProvider_ReturnsTheGlobalScope()
    {
        var scope = new GlobalCacheKeyScopeProvider().GetScope();

        scope.Should().Be(CacheKeyScope.Global);
        scope.IsCacheable.Should().BeTrue();
        scope.Discriminator.Should().BeNull();
    }
}
