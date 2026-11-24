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

    /// <summary>
    /// A cheap call to see whether the server answers at all. Returns the status code (any answer, even 401, means
    /// the server is there); throws when it cannot be reached.
    /// </summary>
    public async Task<System.Net.HttpStatusCode> PingAsync(CancellationToken ct = default)
    {
        using var response = await http.GetAsync("api/me", HttpCompletionOption.ResponseHeadersRead, ct);
        return response.StatusCode;
    }

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

    /// <summary>Removes a shared solution from one alarm; the other alarms keep it. The API refuses (409) the last link.</summary>
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

    /// <summary>
    /// Adds a code to the shared list and returns its id. A code that is already there (in any case) is not an
    /// error: its id is returned.
    /// </summary>
    public async Task<int> CreateAlarmCodeAsync(string code, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync("api/alarm-codes", new { code }, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var conflict = await response.Content.ReadFromJsonAsync<CodeConflict>(ct);
            if (conflict?.Code is { } existing) return existing.Id;
        }
        await EnsureSuccessAsync(response, ct);
        var created = await response.Content.ReadFromJsonAsync<CreatedEntity>(ct);
        return created?.Id ?? throw new InvalidOperationException("The API did not return the new code's id.");
    }

    private sealed record CodeConflict(string? Error, CreatedEntity? Code);

    public async Task<int> CreateAreaAsync(SaveAreaRequest request, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync("api/areas", request, ct);
        await EnsureSuccessAsync(response, ct);
        var created = await response.Content.ReadFromJsonAsync<CreatedEntity>(ct);
        return created?.Id ?? throw new InvalidOperationException("The API did not return the new area's id.");
    }

    public async Task UpdateAreaAsync(int id, SaveAreaRequest request, CancellationToken ct = default)
    {
        using var response = await http.PutAsJsonAsync($"api/areas/{id}", request, ct);
        await EnsureSuccessAsync(response, ct);
    }

    /// <summary>Moves the avdelning to the bin (Papperskorg) with its units, their alarms and solutions.</summary>
    public async Task DeleteAreaAsync(int id, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync($"api/areas/{id}", ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<int> CreateEquipmentAsync(SaveEquipmentRequest request, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync("api/equipment", request, ct);
        await EnsureSuccessAsync(response, ct);
        var created = await response.Content.ReadFromJsonAsync<CreatedEntity>(ct);
        return created?.Id ?? throw new InvalidOperationException("The API did not return the new unit's id.");
    }

    public async Task UpdateEquipmentAsync(int id, SaveEquipmentRequest request, CancellationToken ct = default)
    {
        using var response = await http.PutAsJsonAsync($"api/equipment/{id}", request, ct);
        await EnsureSuccessAsync(response, ct);
    }

    /// <summary>Moves the unit to the bin (Papperskorg) with its alarms and solutions.</summary>
    public async Task DeleteEquipmentAsync(int id, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync($"api/equipment/{id}", ct);
        await EnsureSuccessAsync(response, ct);
    }

    /// <summary>What removing an avdelning (<paramref name="isArea"/>) or a unit would take with it.</summary>
    public async Task<TrashPreview> PreviewRemovalAsync(bool isArea, int id, CancellationToken ct = default)
        => await http.GetFromJsonAsync<TrashPreview>($"api/trash/preview/{(isArea ? "areas" : "equipment")}/{id}", ct)
           ?? throw new InvalidOperationException("The API returned an empty preview.");

    /// <summary>The bin (admin only). Not part of the sync: it needs a connection.</summary>
    public async Task<List<TrashItem>> GetTrashAsync(CancellationToken ct = default)
        => await http.GetFromJsonAsync<List<TrashItem>>("api/trash", ct) ?? [];

    /// <summary>Brings a removal back. 409 (with a reason) when its avdelning is in the bin too, or the name is taken.</summary>
    public async Task RestoreFromTrashAsync(int id, CancellationToken ct = default)
    {
        using var response = await http.PostAsync($"api/trash/{id}/restore", null, ct);
        await EnsureSuccessAsync(response, ct);
    }

    /// <summary>Erases a removal for good, now.</summary>
    public async Task EraseFromTrashAsync(int id, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync($"api/trash/{id}", ct);
        await EnsureSuccessAsync(response, ct);
    }

    /// <summary>Settings for incoming alarm SMS (admin only). Not part of the sync: it needs a connection.</summary>
    public async Task<SmsSettings> GetSmsSettingsAsync(CancellationToken ct = default)
        => await http.GetFromJsonAsync<SmsSettings>("api/settings/sms", ct)
           ?? throw new InvalidOperationException("The API returned empty SMS settings.");

    /// <summary>Saves them. 400 (with a reason) when a number isn't a Swedish phone number.</summary>
    public async Task<SmsSettings> UpdateSmsSettingsAsync(SmsSettingsRequest request, CancellationToken ct = default)
    {
        using var response = await http.PutAsJsonAsync("api/settings/sms", request, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<SmsSettings>(ct)
               ?? throw new InvalidOperationException("The API returned empty SMS settings.");
    }

    /// <summary>Logs in to 46elks with the saved login and checks the 46elks number. Sends nothing.</summary>
    public async Task<SmsConnectionTest> TestSmsConnectionAsync(CancellationToken ct = default)
    {
        using var response = await http.PostAsync("api/settings/sms/test", null, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<SmsConnectionTest>(ct)
               ?? throw new InvalidOperationException("The API returned an empty test result.");
    }

    /// <summary>Contact people (admin only). Not part of the sync: it needs a connection.</summary>
    public async Task<List<Contact>> GetContactsAsync(CancellationToken ct = default)
        => await http.GetFromJsonAsync<List<Contact>>("api/contacts", ct) ?? [];

    /// <summary>400 (with a Swedish reason) without a name or role, or when the number isn't Swedish.</summary>
    public async Task<Contact> CreateContactAsync(SaveContactRequest request, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync("api/contacts", request, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<Contact>(ct)
               ?? throw new InvalidOperationException("The API did not return the new contact.");
    }

    public async Task<Contact> UpdateContactAsync(int id, SaveContactRequest request, CancellationToken ct = default)
    {
        using var response = await http.PutAsJsonAsync($"api/contacts/{id}", request, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<Contact>(ct)
               ?? throw new InvalidOperationException("The API did not return the contact.");
    }

    public async Task DeleteContactAsync(int id, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync($"api/contacts/{id}", ct);
        await EnsureSuccessAsync(response, ct);
    }

    /// <summary>Roles for contact people (admin only), with how many people have each.</summary>
    public async Task<List<ContactRole>> GetContactRolesAsync(CancellationToken ct = default)
        => await http.GetFromJsonAsync<List<ContactRole>>("api/contact-roles", ct) ?? [];

    /// <summary>409 (with a reason) when another role already has the name.</summary>
    public async Task<ContactRole> CreateContactRoleAsync(string name, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync("api/contact-roles", new { name }, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<ContactRole>(ct)
               ?? throw new InvalidOperationException("The API did not return the new role.");
    }

    public async Task<ContactRole> UpdateContactRoleAsync(int id, string name, CancellationToken ct = default)
    {
        using var response = await http.PutAsJsonAsync($"api/contact-roles/{id}", new { name }, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<ContactRole>(ct)
               ?? throw new InvalidOperationException("The API did not return the role.");
    }

    /// <summary>409 (with a reason naming them) while contacts have the role.</summary>
    public async Task DeleteContactRoleAsync(int id, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync($"api/contact-roles/{id}", ct);
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

    /// <summary>
    /// The reason the API gave, for showing to the user: the "detail" of a problem response (the API writes it in
    /// Swedish), or null when the body has none.
    /// </summary>
    public string? Detail
    {
        get
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                       && doc.RootElement.TryGetProperty("detail", out var detail)
                       && detail.ValueKind == System.Text.Json.JsonValueKind.String
                    ? detail.GetString()
                    : null;
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }
    }
}
