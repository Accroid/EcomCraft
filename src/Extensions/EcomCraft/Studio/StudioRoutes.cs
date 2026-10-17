using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SwarmUI.Accounts;
using SwarmUI.Core;
using System.IO;

namespace EcomCraftStudio.Studio;

/// <summary>Extension-owned page; does not replace the original engine or change its login flow.</summary>
public static class StudioRoutes
{
    public static void Map(Extension extension)
    {
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
