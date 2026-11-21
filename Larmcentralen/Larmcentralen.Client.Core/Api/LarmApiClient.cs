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
