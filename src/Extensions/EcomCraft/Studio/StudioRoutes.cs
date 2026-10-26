using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SwarmUI.Accounts;
using SwarmUI.Core;
using System.IO;

namespace EcomCraftStudio.Studio;

/// <summary>Extension-owned page; does not replace the original engine or change its login flow.</summary>
public static class StudioRoutes
{
    /// <summary>Page assets served next to the page and read from disk on every request, like the page itself.
    /// SwarmUI's /ExtensionFile route caches files in memory until restart, which mixes a fresh page with a stale script after an update.</summary>
    private static readonly (string Route, string File, string ContentType)[] Assets =
    [
        ("/EcomCraft/studio.js", "studio.js", "text/javascript; charset=utf-8"),
        ("/EcomCraft/studio.css", "studio.css", "text/css; charset=utf-8")
    ];

    /// <summary>Maps the workspace page and its assets.</summary>
    public static void Map(Extension extension)
    {
        foreach ((string route, string file, string contentType) in Assets)
        {
            string path = Path.Combine(extension.FilePath, "Studio", file);
            WebServer.WebApp.MapGet(route, async (HttpContext context) =>
            {
                context.Response.Headers.CacheControl = "no-cache";
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                string content;
                try
                {
                    content = await File.ReadAllTextAsync(path, context.RequestAborted);
                }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException or IOException)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
                context.Response.ContentType = contentType;
                await context.Response.WriteAsync(content, context.RequestAborted);
            });
        }
        WebServer.WebApp.MapGet("/EcomCraft", async (HttpContext context) =>
        {
            User user = WebServer.GetUserFor(context);
            context.Response.Headers.CacheControl = "no-store";
            if (user is null)
            {
                context.Response.Redirect("/Login");
                return;
            }
            if (!user.HasPermission(Permissions.FundamentalGenerateTabAccess)
                || !user.HasPermission(Permissions.FundamentalModelAccess))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("此账号没有创作工作台访问权限。请联系管理员。");
                return;
            }
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "same-origin";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data: blob:; media-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'self'; form-action 'self'";
            await context.Response.WriteAsync(await File.ReadAllTextAsync(Path.Combine(extension.FilePath, "Studio", "index.html"), context.RequestAborted));
        });
    }
}
