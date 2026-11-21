using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Larmcentralen.Api.Security;

/// <summary>
/// Puts the <see cref="Policies.Edit"/> policy on every controller action that isn't a plain read
/// (anything but GET/HEAD/OPTIONS, or an action with no HTTP method at all). New endpoints are
/// admin-only by default, so nobody can forget the attribute. Actions marked
/// <see cref="AllowAnonymousAttribute"/> (the SMS webhook) are left alone.
/// </summary>
public sealed class RequireAdminForWritesConvention : IApplicationModelConvention
{
    private static readonly HashSet<string> ReadMethods = new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS" };

    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        foreach (var action in controller.Actions)
        {
            if (controller.Attributes.OfType<IAllowAnonymous>().Any() || action.Attributes.OfType<IAllowAnonymous>().Any())
                continue;

            foreach (var selector in action.Selectors)
            {
                var methods = selector.ActionConstraints
                    .OfType<HttpMethodActionConstraint>()
                    .SelectMany(c => c.HttpMethods)
                    .ToList();
                if (methods.Count == 0 || methods.Any(m => !ReadMethods.Contains(m)))
                    selector.EndpointMetadata.Add(new AuthorizeAttribute(Policies.Edit));
            }
        }
    }
}
