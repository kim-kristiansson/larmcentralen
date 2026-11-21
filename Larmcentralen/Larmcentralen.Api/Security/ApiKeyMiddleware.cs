using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Larmcentralen.Api.Security;

public class SecurityOptions
{
    /// <summary>Shared key the desktop app sends in the <c>X-Api-Key</c> header on every API call.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Secret 46elks sends as <c>?secret=</c> on the SMS webhook URL.</summary>
    public string SmsWebhookSecret { get; set; } = "";
}

/// <summary>
/// Guards everything under <c>/api</c>. The 46elks webhook can't set headers, so it is checked
/// against a separate secret in the query string instead. Keys come from configuration
/// (user-secrets locally, App Service settings on Azure), never from appsettings.json.
/// An unset key turns the check off, which <see cref="ApiKeyMiddlewareExtensions"/> only
/// allows in Development.
/// </summary>
public class ApiKeyMiddleware(RequestDelegate next, IOptions<SecurityOptions> options)
{
    public const string HeaderName = "X-Api-Key";
    private const string WebhookPrefix = "/api/webhooks";

    private readonly SecurityOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (!path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        var isWebhook = path.StartsWithSegments(WebhookPrefix);
        var expected = isWebhook ? _options.SmsWebhookSecret : _options.ApiKey;
        var supplied = isWebhook
            ? context.Request.Query["secret"].ToString()
            : context.Request.Headers[HeaderName].ToString();

        if (expected.Length > 0 && !FixedTimeEquals(supplied, expected))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }

    private static bool FixedTimeEquals(string supplied, string expected) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(expected));
}

public static class ApiKeyMiddlewareExtensions
{
    /// <summary>
    /// Adds the key check. Outside Development the API refuses to start without both keys,
    /// so a deployment can't go live unprotected by mistake.
    /// </summary>
    public static WebApplication UseApiKeys(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<SecurityOptions>>().Value;
        var missing = new List<string>();
        if (options.ApiKey.Length == 0) missing.Add("Security:ApiKey");
        if (options.SmsWebhookSecret.Length == 0) missing.Add("Security:SmsWebhookSecret");

        if (missing.Count > 0)
        {
            var names = string.Join(" and ", missing);
            if (!app.Environment.IsDevelopment())
                throw new InvalidOperationException(
                    $"{names} must be set (App Service settings: Security__ApiKey, Security__SmsWebhookSecret).");
            app.Logger.LogWarning("{Names} not set: those endpoints accept requests without a key. Development only.", names);
        }

        app.UseMiddleware<ApiKeyMiddleware>();
        return app;
    }
}
