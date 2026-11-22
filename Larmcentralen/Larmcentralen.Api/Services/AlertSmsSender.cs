using Microsoft.Extensions.Options;

namespace Larmcentralen.Api.Services;

/// <summary>
/// The 46elks account and who gets alerts (config section "Email" for historical reasons). From user-secrets or
/// environment variables, never appsettings.json.
/// </summary>
public class EmailOptions
{
    public string ElksUsername { get; set; } = "";
    public string ElksPassword { get; set; } = "";

    /// <summary>Who gets the alert SMS for Hög and Kritisk alarms (+46…).</summary>
    public string AlertRecipient { get; set; } = "";

    /// <summary>How often new SMS are fetched from 46elks' SMS history, in seconds. 0 turns fetching off (webhook only).</summary>
    public int FetchIntervalSeconds { get; set; } = 15;
}

/// <summary>Sends the alert SMS for a serious alarm. Replaced in tests.</summary>
public interface IAlertSmsSender
{
    /// <summary>False when it could not be sent (not configured, or 46elks said no).</summary>
    Task<bool> SendAsync(string message, CancellationToken ct = default);
}

public sealed class ElksAlertSmsSender(HttpClient http, IOptions<EmailOptions> options, ILogger<ElksAlertSmsSender> logger)
    : IAlertSmsSender
{
    public const string ApiUrl = "https://api.46elks.com/a1/sms";

    public async Task<bool> SendAsync(string message, CancellationToken ct = default)
    {
        var o = options.Value;
        if (o.ElksUsername.Length == 0 || o.ElksPassword.Length == 0 || o.AlertRecipient.Length == 0)
        {
            logger.LogWarning("Email:ElksUsername, Email:ElksPassword or Email:AlertRecipient not set: no alert sent.");
            return false;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["from"] = "Larmcentral",
                ["to"] = o.AlertRecipient,
                ["message"] = message,
            })
        };
        var auth = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{o.ElksUsername}:{o.ElksPassword}"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);

        try
        {
            using var response = await http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogInformation("46elks response: {Status} {Body}", response.StatusCode, body);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Could not reach 46elks");
            return false;
        }
    }
}
