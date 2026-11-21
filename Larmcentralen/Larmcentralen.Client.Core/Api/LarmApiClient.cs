using System.Net.Http.Json;
using Larmcentralen.Client.Core.Models;

namespace Larmcentralen.Client.Core.Api;

/// <summary>
/// Typed HTTP client for the Larmcentralen API. Reads go through the local copy instead;
/// this client is used for sync, writes and Word export, which all need a connection.
/// </summary>
public class LarmApiClient(HttpClient http)
{
    public async Task<SyncSnapshot> GetSnapshotAsync(CancellationToken ct = default)
        => await http.GetFromJsonAsync<SyncSnapshot>("api/sync", ct)
           ?? throw new InvalidOperationException("The API returned an empty sync snapshot.");

    /// <summary>Asks the API what this app's key allows (read only, or edit too).</summary>
    public async Task<AccessInfo> GetAccessAsync(CancellationToken ct = default)
        => await http.GetFromJsonAsync<AccessInfo>("api/me", ct)
           ?? throw new InvalidOperationException("The API returned an empty answer for api/me.");

    public async Task<int> CreateAlarmAsync(SaveAlarmRequest request, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync("api/alarms", request, ct);
        await EnsureSuccessAsync(response, ct);
        var created = await response.Content.ReadFromJsonAsync<CreatedEntity>(ct);
        return created?.Id ?? throw new InvalidOperationException("The API did not return the new alarm's id.");
    }

    public async Task UpdateAlarmAsync(int id, SaveAlarmRequest request, CancellationToken ct = default)
    {
        using var response = await http.PutAsJsonAsync($"api/alarms/{id}", request, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task DeleteAlarmAsync(int id, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync($"api/alarms/{id}", ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<int> CreateSolutionAsync(CreateSolutionRequest request, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync("api/solutions", request, ct);
        await EnsureSuccessAsync(response, ct);
        var created = await response.Content.ReadFromJsonAsync<CreatedEntity>(ct);
        return created?.Id ?? throw new InvalidOperationException("The API did not return the new solution's id.");
    }

    public async Task UpdateSolutionAsync(int id, UpdateSolutionRequest request, CancellationToken ct = default)
    {
        using var response = await http.PutAsJsonAsync($"api/solutions/{id}", request, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task DeleteSolutionAsync(int id, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync($"api/solutions/{id}", ct);
        await EnsureSuccessAsync(response, ct);
    }

    /// <summary>Gives this unit its own alarm definition (a copy with the same solutions), so its text can change alone.</summary>
    public async Task SplitAlarmDefinitionAsync(int alarmId, CancellationToken ct = default)
    {
        using var response = await http.PostAsync($"api/alarms/{alarmId}/own-definition", null, ct);
        await EnsureSuccessAsync(response, ct);
    }

    /// <summary>
    /// Removes a shared solution from one alarm (on a server with definitions: from that alarm's definition,
    /// so every unit sharing it); the others keep it. The API refuses (409) the last link.
    /// </summary>
    public async Task UnlinkSolutionAsync(int alarmId, int solutionId, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync($"api/alarms/{alarmId}/solutions/{solutionId}", ct);
        await EnsureSuccessAsync(response, ct);
    }

    /// <summary>Gives one alarm its own copy of a shared solution. Returns the copy's id.</summary>
    public async Task<int> DetachSolutionAsync(int alarmId, int solutionId, CancellationToken ct = default)
    {
        using var response = await http.PostAsync($"api/alarms/{alarmId}/solutions/{solutionId}/detach", null, ct);
        await EnsureSuccessAsync(response, ct);
        var copy = await response.Content.ReadFromJsonAsync<CreatedEntity>(ct);
        return copy?.Id ?? throw new InvalidOperationException("The API did not return the copy's id.");
    }

    /// <summary>Downloads the alarm and its solutions as a .docx.</summary>
    public async Task<(byte[] Bytes, string FileName)> ExportAlarmAsync(int alarmId, CancellationToken ct = default)
    {
        using var response = await http.GetAsync($"api/alarms/{alarmId}/export", ct);
        await EnsureSuccessAsync(response, ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                       ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                       ?? $"larm-{alarmId}.docx";
        return (bytes, fileName);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct);
        throw new ApiException(response.StatusCode, body);
    }
}

public class ApiException(System.Net.HttpStatusCode status, string body)
    : Exception($"The API answered {(int)status} {status}. {body}".Trim())
{
    public System.Net.HttpStatusCode Status { get; } = status;
}
