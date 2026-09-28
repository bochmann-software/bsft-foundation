namespace Bsft.Data;

/// <summary>
/// Abstract base class for defining query configurations that map DTO fields to entity properties.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TDto">The DTO type.</typeparam>
public abstract class QueryConfigurationBase<TEntity, TDto>
{
    /// <summary>
    /// Configures the query field mappings using the provided builder.
    /// </summary>
    /// <param name="builder">The builder used to define field mappings.</param>
    public abstract void Configure(QueryConfigurationBuilder<TEntity, TDto> builder);
}
