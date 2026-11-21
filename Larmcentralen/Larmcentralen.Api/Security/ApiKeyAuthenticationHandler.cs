using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Larmcentralen.Api.Security;

/// <summary>
/// Signs the caller in from the <c>X-Api-Key</c> header: the admin key gives the Admin role,
/// the read key gives the User role. No header means anonymous (401 on protected endpoints),
/// a wrong key fails. When no key is configured at all (Development only, enforced at start-up)
/// every caller is Admin so local development works without setup.
/// </summary>
public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<SecurityOptions> security)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var keys = security.Value;
        if (keys.KeysDisabled)
            return Task.FromResult(Success(Roles.Admin));

        var supplied = Request.Headers[HeaderName].ToString();
        if (supplied.Length == 0)
            return Task.FromResult(AuthenticateResult.NoResult());

        if (Matches(supplied, keys.AdminApiKey))
            return Task.FromResult(Success(Roles.Admin));
        if (Matches(supplied, keys.ApiKey))
            return Task.FromResult(Success(Roles.User));

        return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
    }

    private AuthenticateResult Success(string role)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, role), new Claim(ClaimTypes.Role, role)], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    /// <summary>Constant-time compare. Hashing first also hides the key's length.</summary>
    internal static bool Matches(string supplied, string expected) =>
        expected.Length > 0 &&
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
