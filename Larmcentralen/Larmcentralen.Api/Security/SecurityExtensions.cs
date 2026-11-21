using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Larmcentralen.Api.Security;

public static class SecurityExtensions
{
    /// <summary>
    /// API key sign-in plus two rules: every endpoint needs a valid key (fallback policy), and
    /// endpoints that change data need the Admin role (<see cref="Policies.Edit"/>).
    /// </summary>
    public static IServiceCollection AddApiKeySecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SecurityOptions>(configuration.GetSection("Security"));

        services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Policies.Edit, policy => policy.RequireAuthenticatedUser().RequireRole(Roles.Admin));

        return services;
    }

    /// <summary>
    /// Checks the keys and adds authentication and authorization. Outside Development the API
    /// refuses to start without all three, so a deployment can't go live unprotected by mistake.
    /// </summary>
    public static WebApplication UseApiKeySecurity(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<SecurityOptions>>().Value;

        if (options.ApiKey.Length > 0 && ApiKeyAuthenticationHandler.Matches(options.ApiKey, options.AdminApiKey))
            throw new InvalidOperationException(
                "Security:ApiKey and Security:AdminApiKey must be different, or every reader could edit.");

        var missing = new List<string>();
        if (options.ApiKey.Length == 0) missing.Add("Security:ApiKey");
        if (options.AdminApiKey.Length == 0) missing.Add("Security:AdminApiKey");
        if (options.SmsWebhookSecret.Length == 0) missing.Add("Security:SmsWebhookSecret");

        if (missing.Count > 0)
        {
            var names = string.Join(", ", missing);
            if (!app.Environment.IsDevelopment())
                throw new InvalidOperationException(
                    $"{names} must be set (App Service settings: Security__ApiKey, Security__AdminApiKey, Security__SmsWebhookSecret).");

            if (options.KeysDisabled)
                app.Logger.LogWarning("No API keys set: every caller is admin. Development only.");
            else
                app.Logger.LogWarning("{Names} not set. Development only.", names);
        }

        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}
