using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Bsft.Scheduling;

/// <summary>
/// Binds <c>Bsft:Scheduling:Jobs:&lt;job name&gt;</c> onto the named <see cref="BsftJobOptions"/> of
/// whichever job is asked for.
/// </summary>
/// <remarks>
/// A configurator rather than a binding per registration, because the jobs are registered after
/// <c>AddBsftScheduling</c> has run and a host may add one from a module loaded later still. Binding
/// by name on demand makes the order of those calls irrelevant, which is the whole point of the
/// per-job section.
/// </remarks>
internal sealed class JobOptionsConfigurator : IConfigureNamedOptions<BsftJobOptions>
{
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobOptionsConfigurator"/> class.
    /// </summary>
    /// <param name="configuration">The configuration the per-job sections are read from.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is null.</exception>
    public JobOptionsConfigurator(IConfiguration configuration) =>
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

    /// <inheritdoc />
    public void Configure(string? name, BsftJobOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        // The unnamed instance belongs to no job, so there is no section for it: binding the parent
        // section onto it would hand every job's defaults the first job's values.
        if (string.IsNullOrEmpty(name))
            return;

        _configuration.GetSection($"{BsftJobOptions.SectionName}:{name}").Bind(options);
    }

    /// <inheritdoc />
    public void Configure(BsftJobOptions options) => Configure(Options.DefaultName, options);
}
