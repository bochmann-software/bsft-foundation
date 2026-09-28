using Bsft.Data.Search;
using Bsft.Data.Filtering;
using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Contracts.Pagination;
using FluentAssertions;
using Xunit;

namespace Bsft.Data.Tests.Filtering;

public class QueryConfigurationStartupValidatorTests
{
    [Fact]
    public async Task ADefaultSortOnAnUndeclaredFieldFailsStartup()
    {
        var result = await ValidateAsync(new UndeclaredDefaultSortConfiguration());

        result.IsValid.Should().BeFalse();

        var error = result.Errors.Should().ContainSingle().Subject;
        error.Should().Contain(nameof(UndeclaredDefaultSortConfiguration), "the error has to name the configuration to open");
        error.Should().Contain("CreatedAt", "the error has to name the offending field");
    }

    [Fact]
    public async Task ADefaultSortOnADeclaredButUnsortableFieldFailsStartup()
    {
        // The mirror case: the field exists, so it is not a typo, but it was never opened for
        // sorting. GetSortField refuses it at request time just the same.
        var result = await ValidateAsync(new UnsortableDefaultSortConfiguration());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain("Name");
    }

    [Fact]
    public async Task ADefaultSortOnADeclaredSortableFieldPassesStartup()
    {
        var result = await ValidateAsync(new WellFormedConfiguration());

        result.IsValid.Should().BeTrue();
        result.Message.Should().Contain("1 query configuration(s)");
    }

    [Fact]
    public async Task AConfigurationWithoutADefaultSortPassesStartup()
    {
        // Nothing to check: paging falls back to the executor's own tie-breaker.
        var result = await ValidateAsync(new NoDefaultSortConfiguration());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task EveryOffendingConfigurationIsReportedAtOnce()
    {
        // One deployment, one list of everything that has to change. Stopping at the first would
        // turn a single fix into as many restarts as there are configurations.
        var validator = new QueryConfigurationStartupValidator(
        [
            Compile(new UndeclaredDefaultSortConfiguration()),
            Compile(new WellFormedConfiguration()),
            Compile(new UnsortableDefaultSortConfiguration()),
        ]);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(2);
    }

    [Fact]
    public async Task AnApplicationWithoutQueryConfigurationsPassesStartup()
    {
        var result = await new QueryConfigurationStartupValidator([]).ValidateAsync();

        result.IsValid.Should().BeTrue();
        result.Message.Should().Contain("No query configurations registered.");
    }

    [Fact]
    public void ConstructorDependenciesAreValidated()
    {
        FluentActions.Invoking(() => new QueryConfigurationStartupValidator(null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("sources");
    }

    [Fact]
    public void TheCompiledConfigurationNamesTheConfigurationItWasBuiltFrom()
    {
        IQueryConfigurationSource source = Compile(new WellFormedConfiguration());

        source.ConfigurationName.Should().Be(nameof(WellFormedConfiguration));
        source.DefaultSortField.Should().Be("Name");
        source.IsSortable("Name").Should().BeTrue();
        source.IsSortable("Missing").Should().BeFalse();
    }

    private static Task<Core.StartupValidationResult> ValidateAsync(
        QueryConfigurationBase<Widget, WidgetDto> configuration) =>
        new QueryConfigurationStartupValidator([Compile(configuration)]).ValidateAsync();

    private static IQueryConfigurationSource Compile(QueryConfigurationBase<Widget, WidgetDto> configuration) =>
        new CompiledSearchConfiguration<Widget, WidgetDto>(configuration);

    /// <summary>
    /// An entity and DTO of their own, so these deliberately broken configurations cannot disturb
    /// the assembly-scanning tests that register every configuration in this assembly.
    /// </summary>
    public class Widget
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
    }

    public record WidgetDto(int Id, string Name, DateTime CreatedAt);

    /// <summary>The defect from the sample: a default sort on a field the configuration never declares.</summary>
    private sealed class UndeclaredDefaultSortConfiguration : QueryConfigurationBase<Widget, WidgetDto>
    {
        public override void Configure(QueryConfigurationBuilder<Widget, WidgetDto> builder)
        {
            builder.Field(d => d.Name).AllowSort();
            builder.DefaultSort(d => d.CreatedAt, SortDirection.Desc);
        }
    }

    private sealed class UnsortableDefaultSortConfiguration : QueryConfigurationBase<Widget, WidgetDto>
    {
        public override void Configure(QueryConfigurationBuilder<Widget, WidgetDto> builder)
        {
            builder.Field(d => d.Name).AllowFilter(FilterOperator.Eq);
            builder.DefaultSort(d => d.Name, SortDirection.Asc);
        }
    }

    private sealed class WellFormedConfiguration : QueryConfigurationBase<Widget, WidgetDto>
    {
        public override void Configure(QueryConfigurationBuilder<Widget, WidgetDto> builder)
        {
            builder.Field(d => d.Name).AllowSort();
            builder.DefaultSort(d => d.Name, SortDirection.Asc);
        }
    }

    private sealed class NoDefaultSortConfiguration : QueryConfigurationBase<Widget, WidgetDto>
    {
        public override void Configure(QueryConfigurationBuilder<Widget, WidgetDto> builder) =>
            builder.Field(d => d.Name).AllowSort();
    }
}
