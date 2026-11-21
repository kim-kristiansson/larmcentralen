using System.Globalization;

namespace Larmcentralen.Client.Core.Storage;

/// <summary>The alarms this user opened last, newest first. Kept in the local database.</summary>
public class RecentAlarms(LocalStore store)
{
    private const string Key = "RecentAlarmIds";
    public const int MaxCount = 10;

    public async Task<IReadOnlyList<int>> GetAsync(CancellationToken ct = default)
    {
        var stored = await store.GetSettingAsync(Key, ct);
        if (string.IsNullOrEmpty(stored)) return [];
        return stored.Split(',')
            .Select(s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : (int?)null)
            .OfType<int>()
            .ToList();
    }

    public async Task AddAsync(int alarmId, CancellationToken ct = default)
    {
        var ids = (await GetAsync(ct)).ToList();
        ids.Remove(alarmId);
        ids.Insert(0, alarmId);
        await store.SetSettingAsync(Key, string.Join(',', ids.Take(MaxCount)), ct);
    }

    public async Task RemoveAsync(int alarmId, CancellationToken ct = default)
    {
        var ids = (await GetAsync(ct)).Where(id => id != alarmId);
        await store.SetSettingAsync(Key, string.Join(',', ids), ct);
    }
}
