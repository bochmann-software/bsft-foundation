using System.Globalization;
using Bsft.Correlation;
using Bsft.Logging.LogFiles;
using Bsft.Logging.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace Bsft.Logging.Tests;

/// <summary>Covers the configurable scope keys the file logger persists and the encodings they travel in.</summary>
public sealed class BsftFileLoggerScopePropertiesTests : IDisposable
{
    private readonly LogFileFixture _log = new();

    /// <inheritdoc />
    public void Dispose() => _log.Dispose();

    [Fact]
    public void LogScopeKeys_CorrelationId_IsTheWireNameOfTheShippedKey()
    {
        LogScopeKeys.CorrelationId.Should().Be("CorrelationId");
    }

    [Fact]
    public void Options_ByDefault_PersistTheCorrelationIdOnly()
    {
        var options = new BsftLoggingOptions();

        options.PersistedScopeKeys.Should().Equal(LogScopeKeys.CorrelationId);
        options.MaxScopePropertyChars.Should().Be(1024);
    }

    [Fact]
    public void TextFormat_WithTheDefaultKeys_IsByteIdenticalToTheShippedLine()
    {
        var lines = Write(new BsftLoggingOptions(), logger =>
        {
            using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = "c1" }))
            using (logger.BeginScope(new Dictionary<string, object> { ["RunId"] = "r1" }))
            {
                logger.LogInformation("hello");
            }

            logger.LogWarning("outside");
        });

        lines.Should().Equal(
            $"[{Timestamp()} INF] [c1] Cat: hello",
            $"[{Timestamp()} WRN] [-] Cat: outside");
    }

    [Fact]
    public void JsonFormat_WithTheDefaultKeys_IsByteIdenticalToTheShippedLine()
    {
        var lines = Write(new BsftLoggingOptions { EnableJsonFormat = true }, logger =>
        {
            using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = "c1" }))
            using (logger.BeginScope(new Dictionary<string, object> { ["RunId"] = "r1" }))
            {
                logger.LogInformation("hello");
            }
        });

        lines.Should().Equal(
            "{\"timestamp\":\"" + JsonTimestamp() + "\",\"level\":\"Information\",\"correlationId\":\"c1\","
            + "\"category\":\"My.Cat\",\"message\":\"hello\"}");
    }

    [Fact]
    public void TextFormat_WithConfiguredKeys_WritesAnEscapedPropertyBlockInConfiguredOrder()
    {
        var lines = Write(Options("RunId", "Tenant"), logger =>
        {
            using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = "c1", ["Tenant"] = "a;b=c}d\\e" }))
            using (logger.BeginScope(new Dictionary<string, object> { ["RunId"] = "r1" }))
            {
                logger.LogInformation("hello");
            }
        });

        lines.Should().Equal($"[{Timestamp()} INF] [c1] {{RunId=r1;Tenant=a\\;b\\=c\\}}d\\\\e}} Cat: hello");
    }

    [Fact]
    public void JsonFormat_WithConfiguredKeys_WritesAScopeObjectInConfiguredOrder()
    {
        var lines = Write(Json(Options("RunId", "Tenant")), logger =>
        {
            using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = "c1", ["Tenant"] = "t1" }))
            using (logger.BeginScope(new Dictionary<string, object> { ["RunId"] = "r1" }))
            {
                logger.LogInformation("hello");
            }
        });

        lines.Should().Equal(
            "{\"timestamp\":\"" + JsonTimestamp() + "\",\"level\":\"Information\",\"correlationId\":\"c1\","
            + "\"scope\":{\"RunId\":\"r1\",\"Tenant\":\"t1\"},\"category\":\"My.Cat\",\"message\":\"hello\"}");
    }

    [Fact]
    public void Scopes_WhenNestedAndShadowing_PersistTheInnermostValueOfEachKeyAndRestoreOnDispose()
    {
        var lines = Write(Options("RunId"), logger =>
        {
            using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = "request", ["RunId"] = "outer" }))
            {
                using (logger.BeginScope(new Dictionary<string, object> { ["RunId"] = "inner" }))
                {
                    logger.LogInformation("in");
                }

                logger.LogInformation("out");
            }

            logger.LogInformation("none");
        });

        lines.Should().Equal(
            $"[{Timestamp()} INF] [request] {{RunId=inner}} Cat: in",
            $"[{Timestamp()} INF] [request] {{RunId=outer}} Cat: out",
            $"[{Timestamp()} INF] [-] Cat: none");
    }

    [Fact]
    public void Scopes_WhenTheStateIsAnEnumerableOrNotPairs_ArePersistedOrIgnored()
    {
        var lines = Write(Options("RunId"), logger =>
        {
            using (logger.BeginScope(PairsOf(new KeyValuePair<string, object>("RunId", "enumerated"))))
            using (logger.BeginScope("just a string"))
            {
                logger.LogInformation("hello");
            }
        });

        lines.Should().Equal($"[{Timestamp()} INF] [-] {{RunId=enumerated}} Cat: hello");
    }

    [Fact]
    public void Scopes_WhenTheValueIsNull_PersistAnEmptyValue()
    {
        var lines = Write(Options("RunId"), logger =>
        {
            using (logger.BeginScope(PairsOf(new KeyValuePair<string, object>("RunId", null!))))
                logger.LogInformation("hello");
        });

        lines.Should().Equal($"[{Timestamp()} INF] [-] {{RunId=}} Cat: hello");
    }

    [Fact]
    public void LineBreaksInScopeValues_AreReplacedInBothFormats()
    {
        void Log(ILogger logger)
        {
            using (logger.BeginScope(new Dictionary<string, object> { ["RunId"] = "a\r\nb\nc" }))
                logger.LogInformation("hello");
        }

        var text = Write(Options("RunId"), Log);
        _log.Dispose();
        var json = Write(Json(Options("RunId")), Log);

        text.Should().ContainSingle().Which.Should().Contain("{RunId=a b c}");
        json.Should().ContainSingle().Which.Should().Contain("\"RunId\":\"a b c\"");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScopeProperties_BeyondTheBudget_DropWholeAndMarkTheLineTheSameInBothFormats(bool json)
    {
        var options = Options("A", "B", "C");
        options.EnableJsonFormat = json;
        options.MaxScopePropertyChars = 10;

        var line = Write(options, logger =>
        {
            using (logger.BeginScope(new Dictionary<string, object> { ["A"] = "12345", ["B"] = "6789012", ["C"] = "x" }))
                logger.LogInformation("hello");
        }).Single();

        // B (1 + 7) does not fit the 4 characters left after A (1 + 5) and is dropped whole, never cut; C still fits.
        if (json)
            line.Should().Contain("\"scope\":{\"A\":\"12345\",\"C\":\"x\",\"~truncated\":\"1\"}").And.NotContain("\"B\"");
        else
            line.Should().Contain("{A=12345;C=x;~truncated=1}").And.NotContain("B=");
    }

    [Fact]
    public void ScopeProperties_WhenOnlyDroppedOnes_WriteTheMarkerAlone()
    {
        var options = Options("A");
        options.MaxScopePropertyChars = 3;

        var lines = Write(options, logger =>
        {
            using (logger.BeginScope(new Dictionary<string, object> { ["A"] = "x😀" }))
                logger.LogInformation("hello");
        });

        lines.Should().Equal($"[{Timestamp()} INF] [-] {{~truncated=1}} Cat: hello");
    }

    [Fact]
    public void ScopeProperties_WhenTheBudgetIsExactlyUsed_AreNotMarked()
    {
        var options = Options("A");
        options.MaxScopePropertyChars = 6;

        var lines = Write(options, logger =>
        {
            using (logger.BeginScope(new Dictionary<string, object> { ["A"] = "12345" }))
                logger.LogInformation("hello");
        });

        lines.Should().Equal($"[{Timestamp()} INF] [-] {{A=12345}} Cat: hello");
    }

    [Fact]
    public void ScopeKeys_WhenConfiguredWithoutTheCorrelationId_WriteTheMarkerInItsSlot()
    {
        var options = new BsftLoggingOptions { PersistedScopeKeys = new List<string> { "RunId" } };

        var lines = Write(options, logger =>
        {
            using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = "c1", ["RunId"] = "r1" }))
                logger.LogInformation("hello");
        });

        lines.Should().Equal($"[{Timestamp()} INF] [-] {{RunId=r1}} Cat: hello");
    }

    [Fact]
    public void ScopeKeys_WhenListedTwiceOrWithTheCorrelationId_AreWrittenOnce()
    {
        var lines = Write(Options("RunId", "RunId", LogScopeKeys.CorrelationId), logger =>
        {
            using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = "c1", ["RunId"] = "r1" }))
                logger.LogInformation("hello");
        });

        lines.Should().Equal($"[{Timestamp()} INF] [c1] {{RunId=r1}} Cat: hello");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RoundTrip_ForBothFormats_ReadsTheSamePropertiesAndFiltersOnBothNestedScopes(bool json)
    {
        var options = Options("RunId");
        options.EnableJsonFormat = json;
        options.LogDirectory = _log.LogDirectory;
        Write(options, logger =>
        {
            using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = "req-1" }))
            {
                using (logger.BeginScope(new Dictionary<string, object> { ["RunId"] = "run;1" }))
                    logger.LogInformation("with run");

                logger.LogInformation("request only");
            }

            logger.LogInformation("neither");
        });
        var reader = new LogFileReader(options, _log.Clock);

        var all = await reader.ReadAsync(new LogReadRequest(10));
        all.Entries.Select(entry => entry.Message).Should().Equal("neither", "request only", "with run");
        all.Entries[2].ScopeProperties.Should().BeEquivalentTo(
            new Dictionary<string, string> { ["CorrelationId"] = "req-1", ["RunId"] = "run;1" });
        all.Entries[1].ScopeProperties.Should().BeEquivalentTo(new Dictionary<string, string> { ["CorrelationId"] = "req-1" });
        all.Entries[0].ScopeProperties.Should().BeEmpty();

        var byRun = await reader.ReadAsync(
            new LogReadRequest(10) { ScopeFilter = new Dictionary<string, string> { ["RunId"] = "run;1" } });
        byRun.Entries.Select(entry => entry.Message).Should().Equal("with run");

        var byBoth = await reader.ReadAsync(new LogReadRequest(10)
        {
            CorrelationId = "req-1",
            ScopeFilter = new Dictionary<string, string> { ["RunId"] = "run;1" }
        });
        byBoth.Entries.Select(entry => entry.Message).Should().Equal("with run");

        var byRequest = await reader.ReadAsync(
            new LogReadRequest(10) { ScopeFilter = new Dictionary<string, string> { ["CorrelationId"] = "req-1" } });
        byRequest.Entries.Select(entry => entry.Message).Should().Equal("request only", "with run");
    }

    private string[] Write(BsftLoggingOptions options, Action<ILogger> act)
    {
        var provider = new BsftFileLoggerProvider(options, _log.LogDirectory, _log.Clock);
        act(provider.CreateLogger("My.Cat"));
        provider.Flush();
        provider.Dispose();

        return File.ReadAllText(Directory.GetFiles(_log.LogDirectory, "app-*.log").Single())
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    private static BsftLoggingOptions Options(params string[] keys)
    {
        var options = new BsftLoggingOptions();
        foreach (var key in keys)
            options.PersistedScopeKeys.Add(key);
        return options;
    }

    private static BsftLoggingOptions Json(BsftLoggingOptions options)
    {
        options.EnableJsonFormat = true;
        return options;
    }

    private string Timestamp() => _log.Clock.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

    private string JsonTimestamp() => _log.Clock.GetLocalNow().ToString("yyyy-MM-ddTHH:mm:ss.fffK", CultureInfo.InvariantCulture).Replace("+", "\\u002B", StringComparison.Ordinal);

    private static IEnumerable<KeyValuePair<string, object>> PairsOf(params KeyValuePair<string, object>[] pairs)
    {
        foreach (var pair in pairs)
            yield return pair;
    }
}
