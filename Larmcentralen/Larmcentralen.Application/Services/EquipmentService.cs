using Larmcentralen.Application.DTOs;
using Larmcentralen.Application.Interfaces;
using Larmcentralen.Domain.Entities;
using Larmcentralen.Domain.Interfaces;

namespace Larmcentralen.Application.Services;

public class EquipmentService(IEquipmentRepository repo, IAreaRepository areas) : IEquipmentService
{
    public const int TitleMaxLength = 200;
    public const int TypeMaxLength = 50;

    public async Task<List<EquipmentDto>> GetAllAsync(int? areaId)
    {
        var items = areaId.HasValue
            ? await repo.GetByAreaIdAsync(areaId.Value)
            : await repo.GetAllAsync();

        return items.Select(ToDto).ToList();
    }

    public async Task<EquipmentDto?> GetByIdAsync(int id)
    {
        var e = await repo.GetByIdWithDetailsAsync(id);
        return e is null ? null : ToDto(e);
    }

    public async Task<WriteResult<EquipmentDto>> CreateAsync(CreateEquipmentDto dto)
    {
        var title = dto.Title?.Trim() ?? "";
        var type = NormaliseType(dto.EquipmentType);
        if (await ValidateAsync(title, type, dto.AreaId, null) is { } invalid) return invalid;

        var equipment = new Equipment
        {
            Title = title,
            DisplayName = AreaService.Clean(dto.DisplayName),
            Category = AreaService.Clean(dto.Category),
            Description = AreaService.Clean(dto.Description),
            AreaId = dto.AreaId,
            EquipmentType = type
        };

        await repo.AddAsync(equipment);
        await repo.SaveChangesAsync();

        var created = await repo.GetByIdWithDetailsAsync(equipment.Id);
        return WriteResult<EquipmentDto>.Ok(ToDto(created!));
    }

    public async Task<WriteResult<EquipmentDto>> UpdateAsync(int id, UpdateEquipmentDto dto)
    {
        var equipment = await repo.GetByIdAsync(id);
        if (equipment is null) return WriteResult<EquipmentDto>.NotFound();

        var title = dto.Title?.Trim() ?? "";
        // Null leaves the type as it is (older clients don't send it); an empty string clears it.
        var type = dto.EquipmentType is null ? equipment.EquipmentType : NormaliseType(dto.EquipmentType);
        if (await ValidateAsync(title, type, dto.AreaId, id) is { } invalid) return invalid;

        equipment.Title = title;
        equipment.DisplayName = AreaService.Merge(dto.DisplayName, equipment.DisplayName);
        equipment.Category = AreaService.Merge(dto.Category, equipment.Category);
        equipment.Description = AreaService.Merge(dto.Description, equipment.Description);
        equipment.AreaId = dto.AreaId;
        equipment.EquipmentType = type;
        equipment.UpdatedAt = DateTime.UtcNow;

        await repo.SaveChangesAsync();
        var updated = await repo.GetByIdWithDetailsAsync(id);
        return WriteResult<EquipmentDto>.Ok(ToDto(updated!));
    }

    /// <summary>
    /// Refuses (409) while the unit still has alarms. Removing them along with the unit would also lose their
    /// solutions, so the alarms have to be moved to another unit or removed first.
    /// </summary>
    public async Task<WriteResult<EquipmentDto>> DeleteAsync(int id)
    {
        var equipment = await repo.GetByIdWithDetailsAsync(id);
        if (equipment is null) return WriteResult<EquipmentDto>.NotFound();

        if (equipment.Alarms.Count > 0)
            return WriteResult<EquipmentDto>.Conflict(
                $"{equipment.Title} har {equipment.Alarms.Count} larm. Flytta larmen till en annan enhet eller ta bort dem först.");

        var dto = ToDto(equipment);
        repo.Remove(equipment);
        await repo.SaveChangesAsync();
        return WriteResult<EquipmentDto>.Ok(dto);
    }

    private async Task<WriteResult<EquipmentDto>?> ValidateAsync(string title, string? type, int areaId, int? id)
    {
        if (title.Length == 0) return WriteResult<EquipmentDto>.Invalid("Ange enhetens beteckning, t.ex. T101.");
        if (title.Length > TitleMaxLength) return WriteResult<EquipmentDto>.Invalid($"Beteckningen får vara högst {TitleMaxLength} tecken.");
        if (type is { Length: > TypeMaxLength }) return WriteResult<EquipmentDto>.Invalid($"Typen får vara högst {TypeMaxLength} tecken.");
        if (await areas.GetByIdAsync(areaId) is null) return WriteResult<EquipmentDto>.Invalid("Välj en avdelning.");

        // The import finds a unit by its title within the area, so two units in one area may not share a title.
        var existing = await repo.GetByAreaIdAsync(areaId);
        var clash = existing.FirstOrDefault(e => e.Id != id && string.Equals(e.Title.Trim(), title, StringComparison.OrdinalIgnoreCase));
        if (clash is not null)
            return WriteResult<EquipmentDto>.Conflict($"Det finns redan en enhet som heter {clash.Title} i {clash.Area.Title}.");
        return null;
    }

    private static EquipmentDto ToDto(Equipment e) => new(
        e.Id, e.Title, e.DisplayName, e.Category, e.Description,
        e.AreaId, e.Area.Title, e.Alarms.Count, e.EquipmentType);

    private static string? NormaliseType(string? type) => string.IsNullOrWhiteSpace(type) ? null : type.Trim();
}
