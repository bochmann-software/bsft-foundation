using Bsft.Hosting.Cors;
using Bsft.Hosting.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace Bsft.Hosting.Tests.DependencyInjection;

public class BsftDefaultsConfigurationTests
{
    [Fact]
    public void DisableHttpsRedirection_SetsFlag()
    {
        var config = new BsftDefaultsConfiguration();

        config.DisableHttpsRedirection();

        config.IsHttpsRedirectionDisabled.Should().BeTrue();
    }

    [Fact]
    public void DisableHttpsRedirection_ReturnsThis_ForChaining()
    {
        var config = new BsftDefaultsConfiguration();

        var result = config.DisableHttpsRedirection();

        result.Should().BeSameAs(config);
    }

    [Fact]
    public void Swagger_NullConfigure_ThrowsArgumentNullException()
    {
        var config = new BsftDefaultsConfiguration();

        var act = () => config.Swagger(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("configure");
    }

    [Fact]
    public void Swagger_SetsConfigure()
    {
        var config = new BsftDefaultsConfiguration();
        Action<BsftSwaggerOptions> configure = _ => { };

        config.Swagger(configure);

        config.SwaggerConfigure.Should().BeSameAs(configure);
    }

    [Fact]
    public void ApiVersioning_NullConfigure_ThrowsArgumentNullException()
    {
        var config = new BsftDefaultsConfiguration();

        var act = () => config.ApiVersioning(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("configure");
    }

    [Fact]
    public void Cors_NullConfigure_ThrowsArgumentNullException()
    {
        var config = new BsftDefaultsConfiguration();

        var act = () => config.Cors(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("configure");
    }

    [Fact]
    public void Cors_SetsConfigure()
    {
        var config = new BsftDefaultsConfiguration();
        Action<BsftCorsOptions> configure = _ => { };

        var result = config.Cors(configure);

        result.Should().BeSameAs(config);
        config.CorsConfigure.Should().BeSameAs(configure);
    }

    [Fact]
    public void RateLimiting_NullConfigure_ThrowsArgumentNullException()
    {
        var config = new BsftDefaultsConfiguration();

        var act = () => config.RateLimiting(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("configure");
    }

    [Fact]
    public void Compression_NullConfigure_ThrowsArgumentNullException()
    {
        var config = new BsftDefaultsConfiguration();

        var act = () => config.Compression(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("configure");
    }

    [Fact]
    public void Json_NullConfigure_ThrowsArgumentNullException()
    {
        var config = new BsftDefaultsConfiguration();

        var act = () => config.Json(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("configure");
    }

    [Fact]
    public void Logging_NullConfigure_ThrowsArgumentNullException()
    {
        var config = new BsftDefaultsConfiguration();

        var act = () => config.Logging(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("configure");
    }

    [Fact]
    public void AllFluent_Methods_ReturnThis()
    {
        var config = new BsftDefaultsConfiguration();

        config.Swagger(_ => { }).Should().BeSameAs(config);
        config.ApiVersioning(_ => { }).Should().BeSameAs(config);
        config.Cors(_ => { }).Should().BeSameAs(config);
        config.RateLimiting(_ => { }).Should().BeSameAs(config);
        config.Compression(_ => { }).Should().BeSameAs(config);
        config.Json(_ => { }).Should().BeSameAs(config);
        config.Logging(_ => { }).Should().BeSameAs(config);
    }
}
