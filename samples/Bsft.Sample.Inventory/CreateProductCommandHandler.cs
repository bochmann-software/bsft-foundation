using Bsft.Core;
using Bsft.Mediator;
using Bsft.Sample.Inventory.Core;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Sample.Inventory;

/// <summary>
/// Persists a new product to the database, publishes a <see cref="ProductCreatedNotification"/>
/// and returns the identifier. The create half of the CRUD convention described in
/// <c>docs/crud-conventions.md</c>.
/// </summary>
public class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, Guid>
{
    private readonly IInventoryDbContext _db;
    private readonly IValidator<CreateProductCommand> _validator;
    private readonly IMediator _mediator;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateProductCommandHandler"/> class.
    /// </summary>
    /// <param name="db">The inventory database context.</param>
    /// <param name="validator">The validator for the command's editable fields.</param>
    /// <param name="mediator">The mediator for publishing notifications.</param>
    public CreateProductCommandHandler(
        IInventoryDbContext db,
        IValidator<CreateProductCommand> validator,
        IMediator mediator)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    /// <inheritdoc />
    public async Task<Guid> HandleAsync(CreateProductCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null)
            throw new ArgumentNullException(nameof(command));

        var validation = await _validator.ValidateAsync(command, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
            throw new ValidationException(validation.Errors.ToDictionary(e => e.Key, e => e.Value));

        // The same uniqueness rule the update enforces. A create that may take a name an update
        // may not would make the 409 depend on the order the client happened to save things in.
        var nameTaken = await _db.Products
            .AsNoTracking()
            .AnyAsync(p => p.Name == command.Name, cancellationToken)
            .ConfigureAwait(false);

        if (nameTaken)
            throw new ConflictException($"Another product is already named '{command.Name}'.");

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = command.Name,
            StockQuantity = command.StockQuantity,
            Price = command.Price,
            // The first version, so a read of the new product hands out an ETag a later write can match.
            Version = Guid.NewGuid()
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _mediator.PublishAsync(
            new ProductCreatedNotification(product.Id, product.Name, product.StockQuantity, product.Price),
            cancellationToken).ConfigureAwait(false);

        return product.Id;
    }
}
