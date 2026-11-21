namespace Larmcentralen.Api.Security;

/// <summary>
/// Keys and secrets for the API. They come from configuration (user-secrets locally,
/// App Service settings on Azure), never from appsettings.json.
/// </summary>
public class SecurityOptions
{
    /// <summary>
    /// Read key for operators, sent in the <c>X-Api-Key</c> header. Can search and read, never change anything.
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Admin key, sent in the same header. Can also create, edit and delete alarms and solutions.
    /// </summary>
    public string AdminApiKey { get; set; } = "";

    /// <summary>Secret 46elks sends as <c>?secret=</c> on the SMS webhook URL.</summary>
    public string SmsWebhookSecret { get; set; } = "";

    /// <summary>True when no API key is configured at all. Only allowed in Development, where everyone is admin.</summary>
    public bool KeysDisabled => ApiKey.Length == 0 && AdminApiKey.Length == 0;
}
