using Larmcentralen.Application.DTOs;
using Larmcentralen.Application.Interfaces;
using Larmcentralen.Domain.Entities;
using Larmcentralen.Domain.Interfaces;

namespace Larmcentralen.Application.Services;

public class AreaService(IAreaRepository repo) : IAreaService
{
    public const int TitleMaxLength = 100;

    public async Task<List<AreaDto>> GetAllAsync()
    {
        var areas = await repo.GetAllWithEquipmentAsync();
        return areas.Select(ToDto).ToList();
    }

    public async Task<AreaDto?> GetByIdAsync(int id)
    {
        var area = await repo.GetByIdWithEquipmentAsync(id);
        return area is null ? null : ToDto(area);
    }

    public async Task<WriteResult<AreaDto>> CreateAsync(CreateAreaDto dto)
    {
        var title = dto.Title?.Trim() ?? "";
        if (await ValidateAsync(title, null) is { } invalid) return invalid;

        var area = new Area
        {
            Title = title,
            DisplayName = Clean(dto.DisplayName),
            Description = Clean(dto.Description),
            Location = Clean(dto.Location)
        };

        await repo.AddAsync(area);
        await repo.SaveChangesAsync();
        return WriteResult<AreaDto>.Ok(ToDto(area));
    }

    public async Task<WriteResult<AreaDto>> UpdateAsync(int id, UpdateAreaDto dto)
    {
        var area = await repo.GetByIdWithEquipmentAsync(id);
        if (area is null) return WriteResult<AreaDto>.NotFound();

        var title = dto.Title?.Trim() ?? "";
        if (await ValidateAsync(title, id) is { } invalid) return invalid;

        area.Title = title;
        area.DisplayName = Merge(dto.DisplayName, area.DisplayName);
        area.Description = Merge(dto.Description, area.Description);
        area.Location = Merge(dto.Location, area.Location);
        area.UpdatedAt = DateTime.UtcNow;

        await repo.SaveChangesAsync();
        return WriteResult<AreaDto>.Ok(ToDto(area));
    }

    /// <summary>Refuses (409) while the area still has units: they would be left without an area.</summary>
    public async Task<WriteResult<AreaDto>> DeleteAsync(int id)
    {
        var area = await repo.GetByIdWithEquipmentAsync(id);
        if (area is null) return WriteResult<AreaDto>.NotFound();

        if (area.Equipment.Count > 0)
        {
            var units = area.Equipment.Count == 1 ? "1 enhet" : $"{area.Equipment.Count} enheter";
            return WriteResult<AreaDto>.Conflict($"Avdelningen har {units}. Flytta eller ta bort dem först.");
        }

        repo.Remove(area);
        await repo.SaveChangesAsync();
        return WriteResult<AreaDto>.Ok(ToDto(area));
    }

    private async Task<WriteResult<AreaDto>?> ValidateAsync(string title, int? id)
    {
        if (title.Length == 0) return WriteResult<AreaDto>.Invalid("Ange avdelningens namn.");
        if (title.Length > TitleMaxLength) return WriteResult<AreaDto>.Invalid($"Namnet får vara högst {TitleMaxLength} tecken.");

        var all = await repo.GetAllAsync();
        if (all.Any(a => a.Id != id && string.Equals(a.Title.Trim(), title, StringComparison.CurrentCultureIgnoreCase)))
            return WriteResult<AreaDto>.Conflict($"Det finns redan en avdelning som heter {title}.");
        return null;
    }

    private static AreaDto ToDto(Area a) => new(a.Id, a.Title, a.DisplayName, a.Description, a.Location, a.Equipment.Count);

    internal static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>
    /// For an optional field on update: null leaves the current value (the app doesn't know every field, e.g. an
    /// area's description), an empty string clears it.
    /// </summary>
    internal static string? Merge(string? incoming, string? current) => incoming is null ? current : Clean(incoming);
}
