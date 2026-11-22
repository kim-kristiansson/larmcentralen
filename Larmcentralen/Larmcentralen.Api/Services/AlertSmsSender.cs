namespace Larmcentralen.Api.Services;

/// <summary>
/// The server's 46elks settings (config section "Email" for historical reasons). The login can instead be saved
/// by an admin under SMS-larm, which wins. From user-secrets or environment variables, never appsettings.json.
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

public sealed class ElksAlertSmsSender(
    IElksApi api,
    ElksAccount account,
    Microsoft.Extensions.Options.IOptions<EmailOptions> options,
    ILogger<ElksAlertSmsSender> logger) : IAlertSmsSender
{
    /// <summary>Text sender: at most 11 letters and digits, starting with a letter (46elks' rule).</summary>
    public const string Sender = "Larmcentral";

    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10)];

    public async Task<bool> SendAsync(string message, CancellationToken ct = default)
    {
        var recipient = options.Value.AlertRecipient;
        if (recipient.Length == 0)
        {
            logger.LogWarning("Email:AlertRecipient not set: no alert sent.");
            return false;
        }

        // 5xx or no answer: try again a couple of times. 4xx never: the request itself is wrong.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (await account.GetLoginAsync() is not { } login)
                {
                    logger.LogWarning("No 46elks login (SMS-larm or Email:ElksUsername/ElksPassword): no alert sent.");
                    return false;
                }
                var id = await api.SendSmsAsync(login, Sender, recipient, message, ct);
                logger.LogInformation("Alert SMS {Id} handed to 46elks", id);
                return true;
            }
            catch (ElksException ex) when (ex.Transient && attempt < RetryDelays.Length)
            {
                logger.LogWarning("Sending the alert failed ({Reason}), trying again", ex.Message);
                await Task.Delay(RetryDelays[attempt], ct);
            }
            catch (ElksException ex)
            {
                logger.LogError("Sending the alert failed: {Reason}", ex.Message);
                return false;
            }
        }
    }
}
