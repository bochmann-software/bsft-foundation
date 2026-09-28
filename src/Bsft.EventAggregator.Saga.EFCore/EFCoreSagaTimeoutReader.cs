using Bsft.EventAggregator.Saga.Reading;
using Microsoft.EntityFrameworkCore;

namespace Bsft.EventAggregator.Saga.EFCore;

/// <summary>
/// The operator read side of the durable timeout table on EF Core.
/// </summary>
public sealed class EFCoreSagaTimeoutReader : ISagaTimeoutReader
{
    private readonly ISagaTimeoutDbContext _db;

    /// <summary>Initializes a new instance of the <see cref="EFCoreSagaTimeoutReader"/> class.</summary>
    /// <param name="db">The context holding the timeouts.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="db"/> is <c>null</c>.</exception>
    public EFCoreSagaTimeoutReader(ISagaTimeoutDbContext db) =>
        _db = db ?? throw new ArgumentNullException(nameof(db));

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when an argument is <c>null</c>.</exception>
    /// <remarks>
    /// Every timeout of the instance, not only the pending ones: what an operator is looking at is
    /// what the saga has been waiting for, and a timeout that was retired is the most interesting row
    /// on that list.
    /// </remarks>
    public async Task<IReadOnlyList<SagaTimeoutView>> ListForAsync(
        string sagaType, string correlationKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sagaType);
        ArgumentNullException.ThrowIfNull(correlationKey);

        var rows = await _db.SagaTimeouts
            .AsNoTracking()
            .Where(e => e.SagaType == sagaType && e.CorrelationKey == correlationKey)
            .OrderBy(e => e.DueAt)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ConvertAll(View);
    }

    /// <summary>Reads a row as the instance shows it.</summary>
    /// <param name="entity">The row.</param>
    /// <returns>The view.</returns>
    private static SagaTimeoutView View(SagaTimeoutEntity entity) => new(
        entity.Id,
        entity.Name,
        entity.StepIndex,
        SagaReadInstants.Moment(entity.DueAt),
        entity.PayloadTypeName,
        entity.PayloadJson,
        entity.CorrelationId,
        entity.AttemptCount,
        SagaReadInstants.Moment(entity.DispatchedAt),
        SagaReadInstants.Moment(entity.DeadLetteredAt),
        entity.DeadLetterReason);
}
