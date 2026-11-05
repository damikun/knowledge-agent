namespace KnowledgeAgent.Infrastructure.Security;

public static class AuthRoles
{
    public const string Admin = "admin";
    public const string Support = "support";
}

public static class AuthPolicies
{
    public const string Staff = "staff";

    public const string Admin = "admin";
}

public static class AuthSchemes
{
    public const string Cookie = "ka-session";
    public const string Oidc = "oidc";
    public const string Local = "local";
}
