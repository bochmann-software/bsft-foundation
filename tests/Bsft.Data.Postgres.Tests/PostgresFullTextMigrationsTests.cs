using Bsft.Data.Search;
using Bsft.Data.Postgres.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using Bsft.Data.Postgres.DependencyInjection;

namespace Bsft.Data.Postgres.Tests;

public class PostgresFullTextMigrationsTests
{
    private static readonly PostgresCreateFullTextIndexRequest _command =
        new("Candidates", ["Name", "Notes"], Language: "de");

    [Fact]
    public void AddFullTextVector_AddsAStoredGeneratedColumn()
    {
        var sql = Emit(b => b.AddFullTextVector(_command));

        // Generated and stored, so PostgreSQL keeps it in step itself: no trigger to forget and no
        // rebuild after a bulk import.
        sql.Should().Contain("ALTER TABLE \"Candidates\" ADD COLUMN IF NOT EXISTS \"SearchVector\" tsvector");
        sql.Should().Contain("GENERATED ALWAYS AS (").And.Contain(") STORED;");
    }

    [Fact]
    public void AddFullTextVector_WeightsTheColumnsInTheOrderTheGroupDeclaresThem()
    {
        var sql = Emit(b => b.AddFullTextVector(_command));

        // The labels are how the declared field weights reach ts_rank, which reads them as {D,C,B,A}.
        sql.Should().Contain("setweight(to_tsvector('german', coalesce(\"Name\", '')), 'A')");
        sql.Should().Contain("setweight(to_tsvector('german', coalesce(\"Notes\", '')), 'B')");
        sql.Should().Contain(" || ");
    }

    [Fact]
    public void AddFullTextVector_CoalescesEveryColumn()
    {
        // Concatenating a null tsvector yields null, so one empty Notes column would erase the whole
        // document and the row would stop being findable by its name.
        Emit(b => b.AddFullTextVector(_command)).Should().Contain("coalesce(\"Notes\", '')");
    }

    [Fact]
    public void AddFullTextVector_DefaultsToEnglish()
    {
        Emit(b => b.AddFullTextVector(_command with { Language = "en" })).Should().Contain("'english'");
        Emit(b => b.AddFullTextVector(new PostgresCreateFullTextIndexRequest("Candidates", ["Name"]))).Should().Contain("'english'");
    }

    [Fact]
    public void AddFullTextVector_TakesTheDeploymentsDefaultLanguageWhenTheIndexNamesNone()
    {
        // The regconfig is baked into the index, so a German-first deployment states it once instead
        // of losing on the one migration that forgets to.
        var options = new BsftDataPostgresOptions().UseDefaultLanguage("de");

        Emit(b => b.AddFullTextVector(new PostgresCreateFullTextIndexRequest("Candidates", ["Name"]), options))
            .Should().Contain("'german'");
    }

    [Fact]
    public void AddFullTextVector_LetsAnIndexOverrideTheDeploymentsDefaultLanguage()
    {
        var options = new BsftDataPostgresOptions().UseDefaultLanguage("de");

        Emit(b => b.AddFullTextVector(_command with { Language = "fr" }, options)).Should().Contain("'french'");
    }

    [Fact]
    public void AddFullTextVector_RejectsAnUnknownDefaultLanguage()
    {
        // A default nobody can map is the same deployment-time failure as an unknown per-index one,
        // and naming the value is what makes it fixable.
        var options = new BsftDataPostgresOptions().UseDefaultLanguage("klingon");

        FluentActions.Invoking(() => Emit(b => b.AddFullTextVector(new PostgresCreateFullTextIndexRequest("Candidates", ["Name"]), options)))
            .Should().Throw<ArgumentException>().WithParameterName("Language").WithMessage("*klingon*");
    }

    [Fact]
    public void ARegisteredTextSearchConfigurationOverridesTheBuiltInMap()
    {
        // A deployment shipping an unaccent-composed configuration wants every group declaring de to
        // use it, not only the codes the framework never knew.
        var options = new BsftDataPostgresOptions().UseTextSearchConfiguration("de", "german_unaccent");

        Emit(b => b.AddFullTextVector(_command, options)).Should().Contain("'german_unaccent'").And.NotContain("'german'");
        options.TextSearchConfigurations.Should().ContainKey("de").WhoseValue.Should().Be("german_unaccent");
    }

    [Fact]
    public void ARegisteredTextSearchConfigurationCanNameALanguageTheBuiltInMapDoesNotKnow()
    {
        var options = new BsftDataPostgresOptions().UseTextSearchConfiguration("pt", "portuguese");

        Emit(b => b.AddFullTextVector(_command with { Language = "pt" }, options)).Should().Contain("'portuguese'");
    }

    [Fact]
    public void AddFullTextVector_RejectsAnUnknownLanguage()
    {
        // The configuration is a literal in a generated column, so an unknown one is a statement that
        // fails at deployment rather than a search that quietly stems wrong.
        FluentActions.Invoking(() => Emit(b => b.AddFullTextVector(_command with { Language = "klingon" })))
            .Should().Throw<ArgumentException>().WithParameterName("Language");
    }

    [Fact]
    public void CreateFullTextIndex_BuildsAGinIndexOverTheGeneratedColumn()
    {
        Emit(b => b.CreateFullTextIndex(_command))
            .Should().Be("CREATE INDEX IF NOT EXISTS \"IX_Candidates_SearchVector\" ON \"Candidates\" USING GIN (\"SearchVector\");");
    }

    [Fact]
    public void TheVectorColumnCanBeNamed()
    {
        var named = _command with { VectorColumn = "ProfileVector" };

        Emit(b => b.AddFullTextVector(named)).Should().Contain("\"ProfileVector\" tsvector");
        Emit(b => b.CreateFullTextIndex(named)).Should().Contain("\"IX_Candidates_ProfileVector\"");
    }

    [Fact]
    public void DropFullTextIndex_DropsTheIndexBeforeTheColumn()
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");

        builder.DropFullTextIndex("Candidates");
        var statements = builder.Operations.OfType<SqlOperation>().Select(o => o.Sql).ToList();

        statements.Should().Equal(
            "DROP INDEX IF EXISTS \"IX_Candidates_SearchVector\";",
            "ALTER TABLE \"Candidates\" DROP COLUMN IF EXISTS \"SearchVector\";");
    }

    [Fact]
    public void EveryStatementIsReRunnable()
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");

        builder.AddFullTextVector(_command).CreateFullTextIndex(_command).DropFullTextIndex("Candidates");

        builder.Operations.OfType<SqlOperation>().Should()
            .OnlyContain(o => o.Sql.Contains("IF NOT EXISTS", StringComparison.Ordinal) || o.Sql.Contains("IF EXISTS", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryOperationChainsOnTheSameBuilder()
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");

        builder
            .AddFullTextVector(_command)
            .CreateFullTextIndex(_command)
            .DropFullTextIndex("Candidates");

        builder.Operations.Should().HaveCount(4);
    }

    [Fact]
    public void EveryOperationRunsInsideTheMigrationTransaction()
    {
        // The opposite decision to the SQL Server package, and deliberately so: PostgreSQL's DDL is
        // transactional, so these roll back with the rest of a failing migration. CREATE INDEX
        // CONCURRENTLY is the one form that could not, and it is deliberately not offered.
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");

        builder.AddFullTextVector(_command).CreateFullTextIndex(_command).DropFullTextIndex("Candidates");

        builder.Operations.OfType<SqlOperation>().Should().OnlyContain(o => !o.SuppressTransaction);
    }

    [Fact]
    public void EveryOperationRequiresAMigrationBuilder()
    {
        FluentActions.Invoking(() => PostgresFullTextMigrationBuilderExtensions.AddFullTextVector(null!, _command))
            .Should().Throw<ArgumentNullException>().WithParameterName("migrationBuilder");
        FluentActions.Invoking(() => PostgresFullTextMigrationBuilderExtensions.CreateFullTextIndex(null!, _command))
            .Should().Throw<ArgumentNullException>().WithParameterName("migrationBuilder");
        FluentActions.Invoking(() => PostgresFullTextMigrationBuilderExtensions.DropFullTextIndex(null!, "Candidates"))
            .Should().Throw<ArgumentNullException>().WithParameterName("migrationBuilder");
        FluentActions.Invoking(() => Emit(b => b.AddFullTextVector(null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("command");
        FluentActions.Invoking(() => Emit(b => b.CreateFullTextIndex(null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("command");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Candidates; DROP TABLE Users")]
    [InlineData("\"Candidates\"")]
    public void AnIdentifierThatWouldNeedEscaping_IsRejected(string table)
    {
        FluentActions.Invoking(() => Emit(b => b.DropFullTextIndex(table)))
            .Should().Throw<ArgumentException>().WithParameterName("table");
    }

    [Fact]
    public void EveryIdentifierParameterIsValidated()
    {
        FluentActions.Invoking(() => Emit(b => b.AddFullTextVector(_command with { Table = " " })))
            .Should().Throw<ArgumentException>().WithParameterName("Table");
        FluentActions.Invoking(() => Emit(b => b.AddFullTextVector(_command with { VectorColumn = "Search Vector" })))
            .Should().Throw<ArgumentException>().WithParameterName("VectorColumn");
        FluentActions.Invoking(() => Emit(b => b.AddFullTextVector(_command with { Columns = ["Na me"] })))
            .Should().Throw<ArgumentException>().WithParameterName("columns");
        FluentActions.Invoking(() => Emit(b => b.CreateFullTextIndex(_command with { Table = " " })))
            .Should().Throw<ArgumentException>().WithParameterName("Table");
        FluentActions.Invoking(() => Emit(b => b.CreateFullTextIndex(_command with { VectorColumn = " " })))
            .Should().Throw<ArgumentException>().WithParameterName("VectorColumn");
        FluentActions.Invoking(() => Emit(b => b.DropFullTextIndex("Candidates", " ")))
            .Should().Throw<ArgumentException>().WithParameterName("vectorColumn");
    }

    [Fact]
    public void AddFullTextVector_RequiresBetweenOneAndFourColumns()
    {
        FluentActions.Invoking(() => Emit(b => b.AddFullTextVector(_command with { Columns = [] })))
            .Should().Throw<ArgumentException>().WithParameterName("columns");
        FluentActions.Invoking(() => Emit(b => b.AddFullTextVector(_command with { Columns = null! })))
            .Should().Throw<ArgumentException>().WithParameterName("columns");

        // A fifth column has no label left, and labelling two columns alike would merge weights the
        // search group declares as different.
        FluentActions.Invoking(() => Emit(b => b.AddFullTextVector(_command with { Columns = ["A", "B", "C", "D", "E"] })))
            .Should().Throw<ArgumentException>().WithParameterName("columns");
        Emit(b => b.AddFullTextVector(_command with { Columns = ["A", "B", "C", "D"] })).Should().Contain("'D')");
    }

    [Fact]
    public void AddBsftDataPostgres_RegistersTheProvider()
    {
        var services = new ServiceCollection();

        services.AddBsftDataPostgres(o => o.HasFullTextIndex("profile"));

        using var sp = services.BuildServiceProvider();
        sp.GetServices<ISearchProvider>().Should().ContainSingle().Which.Should().BeOfType<PostgresSearchProvider>();
        sp.GetRequiredService<IOptions<BsftDataPostgresOptions>>().Value.IndexedGroups.Should().ContainKey("profile")
            .WhoseValue.Should().Be("SearchVector");
    }

    [Fact]
    public void AddBsftDataPostgres_AcceptsOptionsTheApplicationBuiltItself()
    {
        // The migration and the provider have to agree on the text search configuration, so the
        // application builds the options once and hands the same instance to both.
        var options = new BsftDataPostgresOptions()
            .HasFullTextIndex("profile")
            .UseTextSearchConfiguration("de", "german_unaccent");

        var services = new ServiceCollection();
        services.AddBsftDataPostgres(options);

        using var sp = services.BuildServiceProvider();
        sp.GetServices<ISearchProvider>().Should().ContainSingle().Which.Should().BeOfType<PostgresSearchProvider>();
        // The container gets a copy of the declaration, not the caller's instance, so that a later
        // mutation cannot make the provider and the migration disagree about the same index.
        var registered = sp.GetRequiredService<IOptions<BsftDataPostgresOptions>>().Value;
        registered.Should().NotBeSameAs(options).And.BeEquivalentTo(options);

        options.HasFullTextIndex("added-afterwards");
        registered.IndexedGroups.Should().NotContainKey("added-afterwards");

        Emit(b => b.AddFullTextVector(_command, options)).Should().Contain("'german_unaccent'");
    }

    [Fact]
    public void AddBsftDataPostgres_MutatedBeforeAnythingResolvesTheOptions_StillRegistersWhatWasDeclared()
    {
        // The copy has to be taken at the call, not when the options are first asked for: a host that
        // registers the declaration and keeps building on the same instance would otherwise hand the
        // provider whatever the instance had grown into by the time something first resolved it.
        var options = new BsftDataPostgresOptions().HasFullTextIndex("profile");
        var services = new ServiceCollection();
        services.AddBsftDataPostgres(options);

        options.HasFullTextIndex("added-before-anything-resolved");

        using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IOptions<BsftDataPostgresOptions>>().Value.IndexedGroups
            .Should().NotContainKey("added-before-anything-resolved");
    }

    [Fact]
    public void AddBsftDataPostgres_ValidatesItsArguments()
    {
        FluentActions.Invoking(() => DataPostgresServiceCollectionExtensions.AddBsftDataPostgres(null!, _ => { }))
            .Should().Throw<ArgumentNullException>().WithParameterName("services");
        FluentActions.Invoking(() => new ServiceCollection().AddBsftDataPostgres((Action<BsftDataPostgresOptions>)null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("configure");
        FluentActions.Invoking(() => DataPostgresServiceCollectionExtensions.AddBsftDataPostgres(null!, new BsftDataPostgresOptions()))
            .Should().Throw<ArgumentNullException>().WithParameterName("services");
        FluentActions.Invoking(() => new ServiceCollection().AddBsftDataPostgres((BsftDataPostgresOptions)null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void TheOptionsValidateWhatTheyAreGiven()
    {
        FluentActions.Invoking(() => new BsftDataPostgresOptions().HasFullTextIndex(" "))
            .Should().Throw<ArgumentException>().WithParameterName("groupName");
        FluentActions.Invoking(() => new BsftDataPostgresOptions().HasFullTextIndex("profile", " "))
            .Should().Throw<ArgumentException>().WithParameterName("vectorProperty");
        FluentActions.Invoking(() => new BsftDataPostgresOptions().UseDefaultLanguage(" "))
            .Should().Throw<ArgumentException>().WithParameterName("language");
        FluentActions.Invoking(() => new BsftDataPostgresOptions().UseTextSearchConfiguration(" ", "german"))
            .Should().Throw<ArgumentException>().WithParameterName("languageCode");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("german'; DROP TABLE Users --")]
    [InlineData("public.german_unaccent")]
    public void ATextSearchConfigurationNameThatWouldNeedEscaping_IsRejected(string configurationName)
    {
        // The name is interpolated into the generated column as a literal, so it is whitelisted the
        // same way every other identifier this package emits is. A schema-qualified name is refused
        // with it; the configuration belongs on the search_path.
        FluentActions.Invoking(() => new BsftDataPostgresOptions().UseTextSearchConfiguration("de", configurationName))
            .Should().Throw<ArgumentException>().WithParameterName("configurationName");
    }

    [Fact]
    public void TheOptionsDefaultToEnglishAndTheBuiltInMapAlone()
    {
        var options = new BsftDataPostgresOptions();

        options.DefaultLanguage.Should().Be("en");
        options.DefaultLanguage.Should().Be(BsftDataPostgresOptions.DefaultSearchLanguage);
        options.TextSearchConfigurations.Should().BeEmpty();
    }

    [Fact]
    public void AGroupCanNameItsOwnVectorProperty()
    {
        new BsftDataPostgresOptions().HasFullTextIndex("profile", "ProfileVector")
            .IndexedGroups.Should().ContainKey("profile").WhoseValue.Should().Be("ProfileVector");
    }

    [Theory]
    [InlineData("de", "german")]
    [InlineData("EN", "english")]
    [InlineData("fr", "french")]
    [InlineData("it", "italian")]
    [InlineData("es", "spanish")]
    [InlineData("nl", "dutch")]
    public void KnownLanguagesResolveToTheirTextSearchConfiguration(string language, string expected)
    {
        FullTextLanguageResolver.Resolve(language).Should().Be(expected);
    }

    [Fact]
    public void AnUnknownOrAbsentLanguageResolvesToNull()
    {
        FullTextLanguageResolver.Resolve("klingon").Should().BeNull();
        FullTextLanguageResolver.Resolve(null).Should().BeNull();
    }

    /// <summary>Runs one migration operation and returns the SQL it emitted.</summary>
    private static string Emit(Action<MigrationBuilder> operation)
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        operation(builder);

        return builder.Operations.OfType<SqlOperation>().First().Sql;
    }
}
