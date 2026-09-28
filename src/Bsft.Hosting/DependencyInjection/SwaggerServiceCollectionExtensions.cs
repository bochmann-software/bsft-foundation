using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;

namespace Bsft.Hosting.DependencyInjection;

/// <summary>
/// Extension methods for registering BSFT Swagger/OpenAPI services.
/// </summary>
public static class SwaggerServiceCollectionExtensions
{
    /// <summary>
    /// Adds pre-configured Swashbuckle Swagger generation with API versioning integration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional action to configure <see cref="BsftSwaggerOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftSwagger(
        this IServiceCollection services,
        Action<BsftSwaggerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return Add(services, configuration: null, configure);
    }

    /// <summary>
    /// Adds pre-configured Swashbuckle Swagger generation, bound from configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration carrying the <c>Bsft:Hosting:Swagger</c> section.</param>
    /// <param name="configure">Adjusts what the section bound, and runs after it.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    public static IServiceCollection AddBsftSwagger(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BsftSwaggerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return Add(services, configuration, configure);
    }

    /// <summary>Binds, validates and registers, whichever overload was called.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration to bind from, or <see langword="null"/>.</param>
    /// <param name="configure">Adjusts what the section bound.</param>
    /// <returns>The service collection for chaining.</returns>
    internal static IServiceCollection Add(
        IServiceCollection services, IConfiguration? configuration, Action<BsftSwaggerOptions>? configure)
    {
        void Apply(BsftSwaggerOptions target)
        {
            configuration?.GetSection(BsftSwaggerOptions.SectionName).Bind(target);
            configure?.Invoke(target);
        }

        var options = new BsftSwaggerOptions();
        Apply(options);

        services.AddOptions<BsftSwaggerOptions>().Configure(Apply).ValidateDataAnnotations().ValidateOnStart();

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            var openApiInfo = new OpenApiInfo
            {
                Title = options.Title,
                Version = options.Version,
                Description = options.Description
            };

            if (options.ContactName is not null || options.ContactEmail is not null || options.ContactUrl is not null)
            {
                openApiInfo.Contact = new OpenApiContact
                {
                    Name = options.ContactName,
                    Email = options.ContactEmail,
                    Url = options.ContactUrl
                };
            }

            c.SwaggerDoc(options.Version, openApiInfo);

            if (options.IsBearerAuthEnabled)
            {
                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Description = "JWT Authorization header using the Bearer scheme.",
                    Name = "Authorization",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT"
                });

                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            }
                        },
                        Array.Empty<string>()
                    }
                });
            }

            if (options.IncludesXmlComments)
            {
                var xmlFiles = Directory.GetFiles(AppContext.BaseDirectory, "*.xml");
                foreach (var xmlFile in xmlFiles)
                {
                    c.IncludeXmlComments(xmlFile, includeControllerXmlComments: true);
                }
            }
        });

        return services;
    }
}
