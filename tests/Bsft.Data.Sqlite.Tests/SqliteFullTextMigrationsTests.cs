using Bsft.Data.Search;
using Bsft.Data.Sqlite.DependencyInjection;
using Bsft.Data.Sqlite.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Data.Sqlite.Tests;

public class SqliteFullTextMigrationsTests
{
    private static readonly SqliteCreateFullTextIndexRequest _command =
        new("Candidates_fts", "Candidates", "Id", ["Name", "Notes"]);

    [Fact]
    public void CreateFullTextIndex_BuildsAnExternalContentIndexOverTheNamedColumns()
    {
        var sql = Emit(b => b.CreateFullTextIndex(_command));

        sql.Should().Contain("CREATE VIRTUAL TABLE IF NOT EXISTS \"Candidates_fts\" USING fts5(");
        sql.Should().Contain("\"Name\", \"Notes\"");
        sql.Should().Contain("content='Candidates'");
        sql.Should().Contain("content_rowid='Id'");
    }

    [Theory]
    [InlineData(SqliteTokenizer.Unicode61, "unicode61 remove_diacritics 2")]
    [InlineData(SqliteTokenizer.Porter, "porter unicode61 remove_diacritics 2")]
    [InlineData(SqliteTokenizer.Ascii, "ascii")]
    public void CreateFullTextIndex_CarriesTheConfiguredTokenizer(SqliteTokenizer tokenizer, string expected)
    {
        var options = new BsftDataSqliteOptions().UseTokenizer(tokenizer);

        Emit(b => b.CreateFullTextIndex(_command, options))
            .Should().Contain($"tokenize='{expected}'");
    }

    [Fact]
    public void TheDefaultTokenizerFoldsDiacriticsBeyondLatin1()
    {
        // remove_diacritics 1, the FTS5 default, folds only the Latin-1 range. Without folding a
        // search for "muller" does not find "müller", which is the surprise this package removes.
        Emit(b => b.CreateFullTextIndex(_command)).Should().Contain("remove_diacritics 2");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void CreateFullTextIndex_CarriesTheConfiguredDiacriticFolding(int removeDiacritics)
    {
        var options = new BsftDataSqliteOptions().UseRemoveDiacritics(removeDiacritics);

        Emit(b => b.CreateFullTextIndex(_command, options))
            .Should().Contain($"tokenize='unicode61 remove_diacritics {removeDiacritics}'");
    }

    [Fact]
    public void TheConfiguredDiacriticFoldingReachesThePorterTokenizerToo()
    {
        var options = new BsftDataSqliteOptions().UseRemoveDiacritics(0).UseTokenizer(SqliteTokenizer.Porter);

        Emit(b => b.CreateFullTextIndex(_command, options))
            .Should().Contain("tokenize='porter unicode61 remove_diacritics 0'");
    }

    [Fact]
    public void TheAsciiTokenizerTakesNoDiacriticFoldingArgument()
    {
        // It treats everything above U+007F as a token character, so there is nothing to fold and
        // FTS5 would reject the argument.
        var options = new BsftDataSqliteOptions().UseRemoveDiacritics(0).UseTokenizer(SqliteTokenizer.Ascii);

        Emit(b => b.CreateFullTextIndex(_command, options))
            .Should().Contain("tokenize='ascii'").And.NotContain("remove_diacritics");
    }

    [Fact]
    public void CreateFullTextSyncTriggers_KeepsTheIndexInStepWithEveryWrite()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.Sqlite");

        builder.CreateFullTextSyncTriggers(_command);
        var statements = builder.Operations.OfType<SqlOperation>().Select(o => o.Sql).ToList();

        statements.Should().HaveCount(3);
        statements[0].Should().Contain("\"Candidates_fts_ai\" AFTER INSERT ON \"Candidates\"")
            .And.Contain("INSERT INTO \"Candidates_fts\"(\"rowid\", \"Name\", \"Notes\")")
            .And.Contain("new.\"Id\"");
        statements[1].Should().Contain("\"Candidates_fts_ad\" AFTER DELETE ON \"Candidates\"")
            .And.Contain("'delete'")
            .And.Contain("old.\"Id\"");

        // An update is a delete of the old terms and an insert of the new ones — FTS5 has no update
        // command, and skipping the delete leaves a row findable under a name it no longer has.
        statements[2].Should().Contain("\"Candidates_fts_au\" AFTER UPDATE ON \"Candidates\"")
            .And.Contain("'delete'")
            .And.Contain("new.\"Name\"");
    }

    [Fact]
    public void RebuildFullTextIndex_RepopulatesFromTheContentTable()
    {
        Emit(b => b.RebuildFullTextIndex("Candidates_fts"))
            .Should().Be("INSERT INTO \"Candidates_fts\"(\"Candidates_fts\") VALUES ('rebuild');");
    }

    [Fact]
    public void DropFullTextIndex_DropsTheTriggersBeforeTheTable()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.Sqlite");

        builder.DropFullTextIndex("Candidates_fts");
        var statements = builder.Operations.OfType<SqlOperation>().Select(o => o.Sql).ToList();

        // A trigger left behind would write into a table that no longer exists, which fails every
        // insert into the content table.
        statements.Should().HaveCount(4);
        statements[0].Should().Be("DROP TRIGGER IF EXISTS \"Candidates_fts_ai\";");
        statements[1].Should().Be("DROP TRIGGER IF EXISTS \"Candidates_fts_ad\";");
        statements[2].Should().Be("DROP TRIGGER IF EXISTS \"Candidates_fts_au\";");
        statements[3].Should().Be("DROP TABLE IF EXISTS \"Candidates_fts\";");
    }

    [Fact]
    public void EveryStatementIsReRunnable()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.Sqlite");

        builder.CreateFullTextIndex(_command).CreateFullTextSyncTriggers(_command).DropFullTextIndex("Candidates_fts");

        builder.Operations.OfType<SqlOperation>()
            .Where(o => o.Sql.StartsWith("CREATE", StringComparison.Ordinal) || o.Sql.StartsWith("DROP", StringComparison.Ordinal))
            .Should().OnlyContain(o => o.Sql.Contains("IF NOT EXISTS", StringComparison.Ordinal) || o.Sql.Contains("IF EXISTS", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryOperationChainsOnTheSameBuilder()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.Sqlite");

        builder
            .CreateFullTextIndex(_command)
            .CreateFullTextSyncTriggers(_command)
            .RebuildFullTextIndex("Candidates_fts")
            .DropFullTextIndex("Candidates_fts");

        builder.Operations.Should().HaveCount(9);
    }

    [Fact]
    public void EveryOperationRunsInsideTheMigrationTransaction()
    {
        // The opposite decision to the SQL Server package, and deliberately so: SQLite's DDL is
        // transactional, virtual tables and triggers included, so these statements roll back with
        // the rest of a failing migration instead of leaving a half-created index behind.
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.Sqlite");

        builder
            .CreateFullTextIndex(_command)
            .CreateFullTextSyncTriggers(_command)
            .RebuildFullTextIndex("Candidates_fts")
            .DropFullTextIndex("Candidates_fts");

        builder.Operations.OfType<SqlOperation>().Should().OnlyContain(o => !o.SuppressTransaction);
    }

    [Fact]
    public void EveryOperationRequiresAMigrationBuilder()
    {
        FluentActions.Invoking(() => SqliteFullTextMigrationBuilderExtensions.CreateFullTextIndex(null!, _command))
            .Should().Throw<ArgumentNullException>().WithParameterName("migrationBuilder");
        FluentActions.Invoking(() => SqliteFullTextMigrationBuilderExtensions.CreateFullTextSyncTriggers(null!, _command))
            .Should().Throw<ArgumentNullException>().WithParameterName("migrationBuilder");
        FluentActions.Invoking(() => SqliteFullTextMigrationBuilderExtensions.RebuildFullTextIndex(null!, "Candidates_fts"))
            .Should().Throw<ArgumentNullException>().WithParameterName("migrationBuilder");
        FluentActions.Invoking(() => SqliteFullTextMigrationBuilderExtensions.DropFullTextIndex(null!, "Candidates_fts"))
            .Should().Throw<ArgumentNullException>().WithParameterName("migrationBuilder");
        FluentActions.Invoking(() => Emit(b => b.CreateFullTextIndex(null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("command");
        FluentActions.Invoking(() => Emit(b => b.CreateFullTextSyncTriggers(null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("command");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Candidates_fts; DROP TABLE Users")]
    [InlineData("\"Candidates_fts\"")]
    public void AnIdentifierThatWouldNeedEscaping_IsRejected(string indexTable)
    {
        FluentActions.Invoking(() => Emit(b => b.DropFullTextIndex(indexTable)))
            .Should().Throw<ArgumentException>().WithParameterName("indexTable");
        FluentActions.Invoking(() => Emit(b => b.RebuildFullTextIndex(indexTable)))
            .Should().Throw<ArgumentException>().WithParameterName("indexTable");
    }

    [Fact]
    public void EveryIdentifierParameterIsValidated()
    {
        FluentActions.Invoking(() => Emit(b => b.CreateFullTextIndex(_command with { IndexTable = " " })))
            .Should().Throw<ArgumentException>().WithParameterName("IndexTable");
        FluentActions.Invoking(() => Emit(b => b.CreateFullTextIndex(_command with { ContentTable = " " })))
            .Should().Throw<ArgumentException>().WithParameterName("ContentTable");
        FluentActions.Invoking(() => Emit(b => b.CreateFullTextIndex(_command with { KeyColumn = "Id; DROP TABLE Users" })))
            .Should().Throw<ArgumentException>().WithParameterName("KeyColumn");
        FluentActions.Invoking(() => Emit(b => b.CreateFullTextIndex(_command with { Columns = ["Na me"] })))
            .Should().Throw<ArgumentException>().WithParameterName("columns");
        FluentActions.Invoking(() => Emit(b => b.CreateFullTextSyncTriggers(_command with { Columns = ["Na me"] })))
            .Should().Throw<ArgumentException>().WithParameterName("columns");
    }

    [Fact]
    public void CreateFullTextIndex_RequiresAtLeastOneColumn()
    {
        FluentActions.Invoking(() => Emit(b => b.CreateFullTextIndex(_command with { Columns = [] })))
            .Should().Throw<ArgumentException>().WithParameterName("columns");
        FluentActions.Invoking(() => Emit(b => b.CreateFullTextIndex(_command with { Columns = null! })))
            .Should().Throw<ArgumentException>().WithParameterName("columns");
    }

    [Fact]
    public void AnUnknownTokenizer_IsRejected()
    {
        // Refused while the application is wired up, which is the only way in: the options are the one
        // place a tokenizer is named, so a migration cannot carry an undeclared one to the engine.
        FluentActions.Invoking(() => new BsftDataSqliteOptions().UseTokenizer((SqliteTokenizer)42))
            .Should().Throw<ArgumentOutOfRangeException>().WithParameterName("tokenizer");
    }

    [Fact]
    public void AddBsftDataSqlite_RegistersTheProvider()
    {
        var services = new ServiceCollection();

        services.AddBsftDataSqlite(o => o.HasFullTextIndex("profile", "Candidates_fts"));

        using var sp = services.BuildServiceProvider();
        sp.GetServices<ISearchProvider>().Should().ContainSingle().Which.Should().BeOfType<SqliteSearchProvider>();
        sp.GetRequiredService<IOptions<BsftDataSqliteOptions>>().Value.IndexedGroups.Should().ContainKey("profile")
            .WhoseValue.Should().Be("Candidates_fts");
    }

    [Fact]
    public void AddBsftDataSqlite_AcceptsOptionsTheApplicationBuiltItself()
    {
        // The migration builds the index with the tokenizer specification these options describe, so
        // the application builds them once and hands the same instance to both.
        var options = new BsftDataSqliteOptions()
            .HasFullTextIndex("profile", "Candidates_fts")
            .AcceptsUnstemmedLanguage("de")
            .UseRemoveDiacritics(0);

        var services = new ServiceCollection();
        services.AddBsftDataSqlite(options);

        using var sp = services.BuildServiceProvider();
        sp.GetServices<ISearchProvider>().Should().ContainSingle().Which.Should().BeOfType<SqliteSearchProvider>();
        // The container gets a copy of the declaration, not the caller's instance, so that a later
        // mutation cannot make the provider and the migration disagree about the same index.
        var registered = sp.GetRequiredService<IOptions<BsftDataSqliteOptions>>().Value;
        registered.Should().NotBeSameAs(options).And.BeEquivalentTo(options);

        options.HasFullTextIndex("added-afterwards", "Added_fts");
        registered.IndexedGroups.Should().NotContainKey("added-afterwards");

        // The accepted language is copied too: without it the provider would report the group as
        // unstemmed at startup even though the application accepted that out loud.
        new SqliteSearchProvider(Options.Create(registered))
            .DescribeConfigurationErrors([SqliteSearchProviderTests.Group()])
            .Should().BeEmpty();

        Emit(b => b.CreateFullTextIndex(_command, options)).Should().Contain("remove_diacritics 0");
    }

    [Fact]
    public void AddBsftDataSqlite_MutatedBeforeAnythingResolvesTheOptions_StillRegistersWhatWasDeclared()
    {
        // The copy has to be taken at the call, not when the options are first asked for: a host that
        // registers the declaration and keeps building on the same instance would otherwise hand the
        // provider whatever the instance had grown into by the time something first resolved it.
        var options = new BsftDataSqliteOptions().HasFullTextIndex("profile", "profile_fts");
        var services = new ServiceCollection();
        services.AddBsftDataSqlite(options);

        options.HasFullTextIndex("added-before-anything-resolved", "later_fts");

        using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IOptions<BsftDataSqliteOptions>>().Value.IndexedGroups
            .Should().NotContainKey("added-before-anything-resolved");
    }

    [Fact]
    public void AddBsftDataSqlite_ValidatesItsArguments()
    {
        FluentActions.Invoking(() => DataSqliteServiceCollectionExtensions.AddBsftDataSqlite(null!, _ => { }))
            .Should().Throw<ArgumentNullException>().WithParameterName("services");
        FluentActions.Invoking(() => new ServiceCollection().AddBsftDataSqlite((Action<BsftDataSqliteOptions>)null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("configure");
        FluentActions.Invoking(() => DataSqliteServiceCollectionExtensions.AddBsftDataSqlite(null!, new BsftDataSqliteOptions()))
            .Should().Throw<ArgumentNullException>().WithParameterName("services");
        FluentActions.Invoking(() => new ServiceCollection().AddBsftDataSqlite((BsftDataSqliteOptions)null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void TheOptionsValidateWhatTheyAreGiven()
    {
        FluentActions.Invoking(() => new BsftDataSqliteOptions().HasFullTextIndex(" ", "Candidates_fts"))
            .Should().Throw<ArgumentException>().WithParameterName("groupName");
        FluentActions.Invoking(() => new BsftDataSqliteOptions().HasFullTextIndex("profile", " "))
            .Should().Throw<ArgumentException>().WithParameterName("indexTable");
        FluentActions.Invoking(() => new BsftDataSqliteOptions().AcceptsUnstemmedLanguage(" "))
            .Should().Throw<ArgumentException>().WithParameterName("language");
    }

    [Fact]
    public void TheOptionsDefaultToTheUnicodeTokenizer()
    {
        new BsftDataSqliteOptions().Tokenizer.Should().Be(SqliteTokenizer.Unicode61);
        new BsftDataSqliteOptions().UseTokenizer(SqliteTokenizer.Porter).Tokenizer.Should().Be(SqliteTokenizer.Porter);
    }

    [Fact]
    public void TheOptionsDefaultToFoldingEveryDiacritic()
    {
        var options = new BsftDataSqliteOptions();

        options.RemoveDiacritics.Should().Be(2);
        options.RemoveDiacritics.Should().Be(BsftDataSqliteOptions.DefaultRemoveDiacritics);
        options.UseRemoveDiacritics(1).RemoveDiacritics.Should().Be(1);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void ADiacriticFoldingLevelFts5DoesNotDefine_IsRejected(int removeDiacritics)
    {
        // Caught while the application is wired up rather than by CREATE VIRTUAL TABLE, which would
        // stop a deployment halfway instead of an application from starting.
        FluentActions.Invoking(() => new BsftDataSqliteOptions().UseRemoveDiacritics(removeDiacritics))
            .Should().Throw<ArgumentOutOfRangeException>().WithParameterName("removeDiacritics");
    }

    [Fact]
    public void ChangingTheTokenizerKeepsTheConfiguredDiacriticFolding()
    {
        var options = new BsftDataSqliteOptions().UseRemoveDiacritics(1).UseTokenizer(SqliteTokenizer.Porter);

        options.RemoveDiacritics.Should().Be(1);
        Emit(b => b.CreateFullTextIndex(_command, options))
            .Should().Contain("tokenize='porter unicode61 remove_diacritics 1'");
    }

    /// <summary>Runs one migration operation and returns the SQL it emitted.</summary>
    private static string Emit(Action<MigrationBuilder> operation)
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.Sqlite");
        operation(builder);

        return builder.Operations.OfType<SqlOperation>().Single().Sql;
    }
}
