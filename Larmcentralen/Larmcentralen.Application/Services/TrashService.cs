using Larmcentralen.Application.DTOs;
using Larmcentralen.Application.Interfaces;
using Larmcentralen.Domain.Entities;
using Larmcentralen.Domain.Interfaces;

namespace Larmcentralen.Application.Services;

/// <summary>
/// The bin (Papperskorg). Removing an avdelning or a unit moves it there with everything below it: units, their
/// alarms, and the solutions only those alarms use (a solution shared with an alarm that stays, stays). Alarm codes
/// are shared by the whole plant and never go. Restoring brings the whole batch back; after
/// <see cref="TrashBatch.RetentionDays"/> days, <see cref="EraseExpiredAsync"/> erases it for good.
///
/// Offline PCs need nothing special: the sync snapshot leaves binned rows out, so they disappear on the next sync.
///
/// SharePoint (later): when solutions are kept in SharePoint too, the final erase here should archive their documents
/// there (move or mark), never delete them, so SharePoint stays the copy a mistake in the app cannot wipe.
/// Hook it into <see cref="EraseBatchAsync"/>.
/// </summary>
public class TrashService(ITrashRepository repo, IEquipmentRepository equipment, IAreaRepository areas) : ITrashService
{
    public async Task<TrashPreviewDto?> PreviewAsync(int? areaId, int? equipmentId)
    {
        var contents = await repo.CollectAsync(areaId, equipmentId);
        if (contents is null) return null;
        var (kind, id, title) = Describe(contents);
        return new TrashPreviewDto(kind, id, title, contents.Equipment.Count, contents.Alarms.Count, contents.Solutions.Count);
    }

    public async Task<WriteResult<TrashItemDto>> MoveToTrashAsync(int? areaId, int? equipmentId, DateTime? now = null)
    {
        var contents = await repo.CollectAsync(areaId, equipmentId);
        if (contents is null) return WriteResult<TrashItemDto>.NotFound();

        var (kind, id, title) = Describe(contents);
        var batch = new TrashBatch
        {
            Kind = kind,
            ItemId = id,
            Title = title,
            EquipmentCount = contents.Equipment.Count,
            AlarmCount = contents.Alarms.Count,
            SolutionCount = contents.Solutions.Count,
            DeletedAt = now ?? DateTime.UtcNow
        };
        if (kind == TrashBatch.KindEquipment) batch.AreaTitle = (await areas.GetByIdAsync(contents.Equipment[0].AreaId))?.Title;

        repo.AddBatch(batch);
        await repo.SaveChangesAsync();

        if (contents.Area is not null) contents.Area.TrashBatchId = batch.Id;
        foreach (var e in contents.Equipment) e.TrashBatchId = batch.Id;
        foreach (var a in contents.Alarms) a.TrashBatchId = batch.Id;
        foreach (var s in contents.Solutions) s.TrashBatchId = batch.Id;
        await repo.SaveChangesAsync();

        return WriteResult<TrashItemDto>.Ok(ToDto(batch));
    }

    public async Task<List<TrashItemDto>> GetAllAsync()
        => (await repo.GetBatchesAsync()).Select(ToDto).ToList();

    public async Task<WriteResult<TrashItemDto>> RestoreAsync(int batchId)
    {
        var batch = await repo.GetBatchAsync(batchId);
        if (batch is null) return WriteResult<TrashItemDto>.NotFound();
        var contents = await repo.GetContentsAsync(batchId);

        if (batch.Kind == TrashBatch.KindEquipment && contents.Equipment.Count > 0)
        {
            var unit = contents.Equipment[0];
            if (await repo.IsAreaBinnedAsync(unit.AreaId))
                return WriteResult<TrashItemDto>.Conflict(
                    $"Avdelningen {batch.AreaTitle} ligger också i papperskorgen. Återställ den först.");

            // A new unit with the same name may have been added in the meantime.
            var active = await equipment.GetByAreaIdAsync(unit.AreaId);
            if (active.FirstOrDefault(e => string.Equals(e.Title.Trim(), unit.Title.Trim(), StringComparison.OrdinalIgnoreCase)) is { } clash)
                return WriteResult<TrashItemDto>.Conflict(
                    $"Det finns redan en enhet som heter {clash.Title} i {clash.Area.Title}. Byt namn på den först.");
        }

        if (contents.Area is not null) contents.Area.TrashBatchId = null;
        foreach (var e in contents.Equipment) e.TrashBatchId = null;
        foreach (var a in contents.Alarms) a.TrashBatchId = null;
        foreach (var s in contents.Solutions) s.TrashBatchId = null;
        await repo.SaveChangesAsync();

        var dto = ToDto(batch);
        await repo.EraseAsync(batch); // nothing is marked any more, so this only removes the batch row
        return WriteResult<TrashItemDto>.Ok(dto);
    }

    public async Task<bool> EraseAsync(int batchId)
    {
        var batch = await repo.GetBatchAsync(batchId);
        if (batch is null) return false;
        await EraseBatchAsync(batch);
        return true;
    }

    public async Task<int> EraseExpiredAsync(DateTime now)
    {
        var cutoff = now.AddDays(-TrashBatch.RetentionDays);
        var erased = 0;
        foreach (var batch in (await repo.GetBatchesAsync()).Where(b => b.DeletedAt <= cutoff).OrderBy(b => b.DeletedAt))
        {
            // An earlier batch may already have gone with its avdelning below.
            if (await repo.GetBatchAsync(batch.Id) is null) continue;
            await EraseBatchAsync(batch);
            erased++;
        }
        return erased;
    }

    private async Task EraseBatchAsync(TrashBatch batch)
    {
        // Units removed on their own earlier still point at this avdelning: they have to go first.
        if (batch.Kind == TrashBatch.KindArea)
            foreach (var unitBatch in await repo.GetUnitBatchesInAreaAsync(batch.ItemId))
                await repo.EraseAsync(unitBatch);
        await repo.EraseAsync(batch);
    }

    private static (string Kind, int Id, string Title) Describe(TrashContents c)
        => c.Area is { } area
            ? (TrashBatch.KindArea, area.Id, area.Title)
            : (TrashBatch.KindEquipment, c.Equipment[0].Id, c.Equipment[0].Title);

    private static TrashItemDto ToDto(TrashBatch b) => new(
        b.Id, b.Kind, b.ItemId, b.Title, b.AreaTitle, b.EquipmentCount, b.AlarmCount, b.SolutionCount,
        b.DeletedAt, b.DeletedAt.AddDays(TrashBatch.RetentionDays));
}
