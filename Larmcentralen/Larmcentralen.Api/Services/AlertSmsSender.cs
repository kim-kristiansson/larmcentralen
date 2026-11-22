using Larmcentralen.Application.Interfaces;

namespace Larmcentralen.Api.Services;

/// <summary>
/// Server settings for 46elks (config section "Elks"). The login, the numbers and who gets alerts are not here:
/// admins set them under SMS-larm.
/// </summary>
public class ElksOptions
{
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
    ISmsSettingsService settings,
    ILogger<ElksAlertSmsSender> logger) : IAlertSmsSender
{
    /// <summary>Text sender: at most 11 letters and digits, starting with a letter (46elks' rule).</summary>
    public const string Sender = "Larmcentral";

    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10)];

    public async Task<bool> SendAsync(string message, CancellationToken ct = default)
    {
        if ((await settings.GetAsync()).AlertRecipient is not { } recipient)
        {
            logger.LogWarning("No alert number set under SMS-larm: no alert sent.");
            return false;
        }

        // 5xx or no answer: try again a couple of times. 4xx never: the request itself is wrong.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (await account.GetLoginAsync() is not { } login)
                {
                    logger.LogWarning("No 46elks login saved under SMS-larm: no alert sent.");
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
