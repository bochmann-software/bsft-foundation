using Bsft.Data.Search;
using Bsft.Data.SqlServer.DependencyInjection;
using Bsft.Data.SqlServer.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Data.SqlServer.Tests;

public class SqlServerFullTextMigrationsTests
{
    [Fact]
    public void CreateFullTextCatalog_IsIdempotent()
    {
        var sql = Emit(b => b.CreateFullTextCatalog("BsftSearch"));

        sql.Should().Contain("IF NOT EXISTS").And.Contain("CREATE FULLTEXT CATALOG [BsftSearch]");
    }

    [Fact]
    public void CreateFullTextIndex_NamesEveryColumnWithItsLanguage()
    {
        var sql = Emit(b => b.CreateFullTextIndex(new SqlServerCreateFullTextIndexRequest("Candidates", "PK_Candidates", "BsftSearch", ["Name", "Notes"], "de")));

        sql.Should().Contain("CREATE FULLTEXT INDEX ON [Candidates]");
        sql.Should().Contain("[Name] LANGUAGE 1031");
        sql.Should().Contain("[Notes] LANGUAGE 1031");
        sql.Should().Contain("KEY INDEX [PK_Candidates] ON [BsftSearch]");
        sql.Should().Contain("CHANGE_TRACKING AUTO");
    }

    [Fact]
    public void CreateFullTextIndex_DefaultsToEnglish()
    {
        Emit(b => b.CreateFullTextIndex(new SqlServerCreateFullTextIndexRequest("Candidates", "PK_Candidates", "BsftSearch", ["Name"])))
            .Should().Contain("LANGUAGE 1033");
    }

    [Fact]
    public void SetFullTextStopList_BracketsAStopListName()
    {
        Emit(b => b.SetFullTextStopList("Candidates", "GermanStopwords"))
            .Should().Be("ALTER FULLTEXT INDEX ON [Candidates] SET STOPLIST [GermanStopwords];");
    }

    [Theory]
    [InlineData("SYSTEM", "SYSTEM")]
    [InlineData("off", "OFF")]
    public void SetFullTextStopList_LeavesTheKeywordsUnbracketed(string stopList, string expected)
    {
        // Bracketing these would name a stoplist object that does not exist.
        Emit(b => b.SetFullTextStopList("Candidates", stopList))
            .Should().Be($"ALTER FULLTEXT INDEX ON [Candidates] SET STOPLIST {expected};");
    }

    [Fact]
    public void SetFullTextStopList_ValidatesItsIdentifiers()
    {
        FluentActions.Invoking(() => Emit(b => b.SetFullTextStopList(" ", "List")))
            .Should().Throw<ArgumentException>().WithParameterName("table");
        FluentActions.Invoking(() => Emit(b => b.SetFullTextStopList("Candidates", " ")))
            .Should().Throw<ArgumentException>().WithParameterName("stopList");
        FluentActions.Invoking(() => Emit(b => b.SetFullTextStopList("Candidates", "List; DROP TABLE Users")))
            .Should().Throw<ArgumentException>().WithParameterName("stopList");
    }

    [Fact]
    public void DropFullTextIndex_TargetsTheTable()
    {
        Emit(b => b.DropFullTextIndex("Candidates")).Should().Be("DROP FULLTEXT INDEX ON [Candidates];");
    }

    [Fact]
    public void EveryOperationChainsOnTheSameBuilder()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");

        builder
            .CreateFullTextCatalog("BsftSearch")
            .CreateFullTextIndex(new SqlServerCreateFullTextIndexRequest("Candidates", "PK_Candidates", "BsftSearch", ["Name"], "de"))
            .SetFullTextStopList("Candidates", "SYSTEM")
            .DropFullTextIndex("Candidates");

        builder.Operations.Should().HaveCount(4);
    }

    [Fact]
    public void EveryOperationRequiresAMigrationBuilder()
    {
        FluentActions.Invoking(() => SqlServerFullTextMigrationBuilderExtensions.CreateFullTextCatalog(null!, "Cat"))
            .Should().Throw<ArgumentNullException>().WithParameterName("migrationBuilder");
        FluentActions.Invoking(() => SqlServerFullTextMigrationBuilderExtensions.CreateFullTextIndex(
                null!, new SqlServerCreateFullTextIndexRequest("T", "PK", "Cat", ["Name"])))
            .Should().Throw<ArgumentNullException>().WithParameterName("migrationBuilder");
        FluentActions.Invoking(() => Emit(b => b.CreateFullTextIndex(null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("command");
        FluentActions.Invoking(() => SqlServerFullTextMigrationBuilderExtensions.SetFullTextStopList(null!, "T", "List"))
            .Should().Throw<ArgumentNullException>().WithParameterName("migrationBuilder");
        FluentActions.Invoking(() => SqlServerFullTextMigrationBuilderExtensions.DropFullTextIndex(null!, "T"))
            .Should().Throw<ArgumentNullException>().WithParameterName("migrationBuilder");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Candidates; DROP TABLE Users")]
    [InlineData("[Candidates]")]
    public void AnIdentifierThatWouldNeedEscaping_IsRejected(string table)
    {
        var act = () => Emit(b => b.DropFullTextIndex(table));

        act.Should().Throw<ArgumentException>().WithParameterName("table");
    }

    [Fact]
    public void EveryIdentifierParameterIsValidated()
    {
        FluentActions.Invoking(() => Emit(b => b.CreateFullTextCatalog(" ")))
            .Should().Throw<ArgumentException>().WithParameterName("catalogName");
        FluentActions.Invoking(() => Emit(b => b.CreateFullTextIndex(new SqlServerCreateFullTextIndexRequest(" ", "PK", "Cat", ["Name"]))))
            .Should().Throw<ArgumentException>().WithParameterName("table");
        FluentActions.Invoking(() => Emit(b => b.CreateFullTextIndex(new SqlServerCreateFullTextIndexRequest("T", " ", "Cat", ["Name"]))))
            .Should().Throw<ArgumentException>().WithParameterName("keyIndexName");
        FluentActions.Invoking(() => Emit(b => b.CreateFullTextIndex(new SqlServerCreateFullTextIndexRequest("T", "PK", " ", ["Name"]))))
            .Should().Throw<ArgumentException>().WithParameterName("catalogName");
        FluentActions.Invoking(() => Emit(b => b.CreateFullTextIndex(new SqlServerCreateFullTextIndexRequest("T", "PK", "Cat", ["Na me"]))))
            .Should().Throw<ArgumentException>().WithParameterName("columns");
    }

    [Fact]
    public void CreateFullTextIndex_RequiresAtLeastOneColumn()
    {
        FluentActions.Invoking(() => Emit(b => b.CreateFullTextIndex(new SqlServerCreateFullTextIndexRequest("T", "PK", "Cat", []))))
            .Should().Throw<ArgumentException>().WithParameterName("columns");
        FluentActions.Invoking(() => Emit(b => b.CreateFullTextIndex(new SqlServerCreateFullTextIndexRequest("T", "PK", "Cat", null!))))
            .Should().Throw<ArgumentException>().WithParameterName("columns");
    }

    [Fact]
    public void CreateFullTextIndex_RejectsAnUnknownLanguage()
    {
        FluentActions.Invoking(() => Emit(b => b.CreateFullTextIndex(new SqlServerCreateFullTextIndexRequest("T", "PK", "Cat", ["Name"], "klingon"))))
            .Should().Throw<ArgumentException>().WithParameterName("language");
    }

    [Theory]
    [InlineData("de", 1031)]
    [InlineData("EN", 1033)]
    [InlineData("fr", 1036)]
    [InlineData("it", 1040)]
    [InlineData("es", 3082)]
    [InlineData("nl", 1043)]
    public void KnownLanguagesResolveToTheirLocaleIdentifier(string language, int expected)
    {
        FullTextLanguageResolver.Resolve(language).Should().Be(expected);
    }

    [Fact]
    public void AnUnknownOrAbsentLanguageResolvesToNull()
    {
        FullTextLanguageResolver.Resolve("klingon").Should().BeNull();
        FullTextLanguageResolver.Resolve(null).Should().BeNull();
    }

    [Fact]
    public void AddBsftDataSqlServer_RegistersTheProvider()
    {
        var services = new ServiceCollection();

        services.AddBsftDataSqlServer(o => o.HasFullTextIndex("profile"));

        using var sp = services.BuildServiceProvider();
        sp.GetServices<ISearchProvider>().Should().ContainSingle().Which.Should().BeOfType<SqlServerSearchProvider>();
        sp.GetRequiredService<IOptions<BsftDataSqlServerOptions>>().Value.IndexedGroups.Should().Contain("profile");
    }

    [Fact]
    public void AddBsftDataSqlServer_MutatedBeforeAnythingResolvesTheOptions_StillRegistersWhatWasDeclared()
    {
        // The copy has to be taken at the call, not when the options are first asked for: a host that
        // registers the declaration and keeps building on the same instance would otherwise hand the
        // provider whatever the instance had grown into by the time something first resolved it.
        var options = new BsftDataSqlServerOptions().HasFullTextIndex("profile");
        var services = new ServiceCollection();
        services.AddBsftDataSqlServer(options);

        options.HasFullTextIndex("added-before-anything-resolved");

        using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IOptions<BsftDataSqlServerOptions>>().Value.IndexedGroups
            .Should().NotContain("added-before-anything-resolved");
    }

    [Fact]
    public void AddBsftDataSqlServer_ValidatesItsArguments()
    {
        FluentActions.Invoking(() => DataSqlServerServiceCollectionExtensions.AddBsftDataSqlServer(null!, _ => { }))
            .Should().Throw<ArgumentNullException>().WithParameterName("services");
        FluentActions.Invoking(() => new ServiceCollection().AddBsftDataSqlServer((Action<BsftDataSqlServerOptions>)null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("configure");
    }

    [Fact]
    public void HasFullTextIndex_RejectsAnEmptyGroupName()
    {
        FluentActions.Invoking(() => new BsftDataSqlServerOptions().HasFullTextIndex(" "))
            .Should().Throw<ArgumentException>().WithParameterName("groupName");
    }

    [Fact]
    public void EveryOperationRunsOutsideTheMigrationTransaction()
    {
        // SQL Server rejects full-text DDL inside a user transaction (Msg 574), and EF wraps a
        // migration in one. Without this flag `dotnet ef database update` aborts and rolls back.
        EmitOperation(b => b.CreateFullTextCatalog("BsftSearch")).SuppressTransaction.Should().BeTrue();
        EmitOperation(b => b.CreateFullTextIndex(new SqlServerCreateFullTextIndexRequest("Candidates", "PK_Candidates", "BsftSearch", ["Name"], "de")))
            .SuppressTransaction.Should().BeTrue();
        EmitOperation(b => b.SetFullTextStopList("Candidates", "SYSTEM")).SuppressTransaction.Should().BeTrue();
        EmitOperation(b => b.DropFullTextIndex("Candidates")).SuppressTransaction.Should().BeTrue();
    }

    /// <summary>Runs one migration operation and returns the SQL it emitted.</summary>
    private static string Emit(Action<MigrationBuilder> operation) => EmitOperation(operation).Sql;

    /// <summary>Runs one migration operation and returns the operation it appended.</summary>
    private static SqlOperation EmitOperation(Action<MigrationBuilder> operation)
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        operation(builder);

        return builder.Operations.OfType<SqlOperation>().Single();
    }
}
