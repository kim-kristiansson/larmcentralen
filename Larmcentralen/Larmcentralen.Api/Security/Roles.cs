namespace Larmcentralen.Api.Security;

/// <summary>
/// The two roles. Today a role comes from which API key the caller sent; with Entra ID sign-in
/// later it would come from the app roles in the token instead, and nothing else has to change.
/// </summary>
public static class Roles
{
    /// <summary>Can search and read the knowledge base.</summary>
    public const string User = "User";

    /// <summary>Can also create, edit and delete.</summary>
    public const string Admin = "Admin";
}

public static class Policies
{
    /// <summary>Required by every endpoint that changes data. See <see cref="RequireAdminForWritesConvention"/>.</summary>
    public const string Edit = "Edit";
}
