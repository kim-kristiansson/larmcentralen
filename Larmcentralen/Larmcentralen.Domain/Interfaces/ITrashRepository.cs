using Larmcentralen.Domain.Entities;

namespace Larmcentralen.Domain.Interfaces;

/// <summary>What one removal takes with it: the rows that get (or have) the same trash batch.</summary>
public sealed record TrashContents(
    Area? Area,
    IReadOnlyList<Equipment> Equipment,
    IReadOnlyList<Alarm> Alarms,
    IReadOnlyList<Solution> Solutions);

/// <summary>
/// The bin (Papperskorg). The only place that looks past the query filters that hide binned rows.
/// Returned entities are tracked, so changing <c>TrashBatchId</c> and calling <see cref="SaveChangesAsync"/> moves them.
/// </summary>
public interface ITrashRepository
{
    /// <summary>
    /// An avdelning (when <paramref name="areaId"/> is set) or a unit, with everything that would go with it now:
    /// its units, their alarms, and the solutions that only those alarms use. Null when not found (or already binned).
    /// A solution also linked to an alarm that stays is left out (it keeps working for that alarm).
    /// </summary>
    Task<TrashContents?> CollectAsync(int? areaId, int? equipmentId);

    /// <summary>Every row marked with this batch.</summary>
    Task<TrashContents> GetContentsAsync(int batchId);

    Task<List<TrashBatch>> GetBatchesAsync();
    Task<TrashBatch?> GetBatchAsync(int id);

    /// <summary>Binned units (from earlier, separate removals) that belong to this avdelning.</summary>
    Task<List<TrashBatch>> GetUnitBatchesInAreaAsync(int areaId);

    /// <summary>True when the avdelning exists but is in the bin.</summary>
    Task<bool> IsAreaBinnedAsync(int areaId);

    void AddBatch(TrashBatch batch);

    /// <summary>Erases the batch and every row in it for good (links go with their alarms and solutions).</summary>
    Task EraseAsync(TrashBatch batch);

    Task SaveChangesAsync();
}
