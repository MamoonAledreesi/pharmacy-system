namespace Pharmacy.Web.Common;

public static class LocalUrl
{
    /// <summary>
    /// True only for app-relative paths such as "/sales". Rejects absolute URLs and protocol-relative
    /// forms ("//host", "/\host") so redirects can never leave the application (open redirect).
    /// </summary>
    public static bool IsLocalPath(string? path) =>
        !string.IsNullOrEmpty(path)
        && path[0] == '/'
        && (path.Length == 1 || (path[1] != '/' && path[1] != '\\'));

    public static string OrRoot(string? path) => IsLocalPath(path) ? path! : "/";
}
