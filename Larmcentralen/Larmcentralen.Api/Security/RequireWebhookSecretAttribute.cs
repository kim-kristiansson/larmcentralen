using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Larmcentralen.Api.Security;

/// <summary>
/// 46elks can't send headers, so the SMS webhook is checked against a secret in the query string
/// (<c>?secret=</c>) instead of an API key. An unset secret turns the check off, which
/// <see cref="SecurityExtensions.UseApiKeySecurity"/> only allows in Development.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireWebhookSecretAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var expected = context.HttpContext.RequestServices.GetRequiredService<IOptions<SecurityOptions>>().Value.SmsWebhookSecret;
        if (expected.Length == 0) return;

        var supplied = context.HttpContext.Request.Query["secret"].ToString();
        if (!ApiKeyAuthenticationHandler.Matches(supplied, expected))
            context.Result = new UnauthorizedResult();
    }
}
