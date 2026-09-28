using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Bsft.Hosting;

/// <summary>
/// Configuration options for the BSFT Swagger/OpenAPI integration.
/// </summary>
public sealed class BsftSwaggerOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Bsft:Hosting:Swagger";

    /// <summary>
    /// Gets or sets the API title displayed in Swagger UI.
    /// Defaults to the entry assembly name.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string Title { get; set; } = Assembly.GetEntryAssembly()?.GetName().Name ?? "API";

    /// <summary>
    /// Gets or sets the API version string displayed in Swagger UI. Defaults to "v1". It also names the
    /// generated document, so an empty value produces a document that cannot be served.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string Version { get; set; } = "v1";

    /// <summary>
    /// Gets or sets the API description displayed on the Swagger UI homepage.
    /// When null, no description is rendered.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the contact name displayed in the Swagger document.
    /// </summary>
    public string? ContactName { get; set; }

    /// <summary>
    /// Gets or sets the contact email displayed in the Swagger document.
    /// </summary>
    public string? ContactEmail { get; set; }

    /// <summary>
    /// Gets or sets the contact URL displayed in the Swagger document.
    /// </summary>
    public Uri? ContactUrl { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Bearer authentication should be added to the Swagger
    /// document. <see cref="AddBearerAuth"/> is the fluent way of setting it.
    /// </summary>
    public bool IsBearerAuthEnabled { get; set; }

    /// <summary>
    /// Gets a value indicating whether XML documentation comments should be included in the Swagger document.
    /// </summary>
    public bool IncludesXmlComments { get; set; } = true;

    /// <summary>
    /// Enables Bearer authentication in the Swagger document.
    /// </summary>
    /// <returns>The current options instance for fluent chaining.</returns>
    public BsftSwaggerOptions AddBearerAuth()
    {
        IsBearerAuthEnabled = true;
        return this;
    }
}
