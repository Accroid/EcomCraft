using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SwarmUI.Accounts;
using SwarmUI.Core;
using System.IO;
using EcomCraftStudio.Studio.Templates;
using FreneticUtilities.FreneticExtensions;

namespace EcomCraftStudio.Studio;

/// <summary>Extension-owned page; does not replace the original engine or change its login flow.</summary>
public static class StudioRoutes
{
    /// <summary>Page assets served next to the page and read from disk on every request, like the page itself.
    /// SwarmUI's /ExtensionFile route caches files in memory until restart, which mixes a fresh page with a stale script after an update.</summary>
    private static readonly (string Route, string File, string ContentType)[] Assets =
    [
        ("/EcomCraft/studio.js", "studio.js", "text/javascript; charset=utf-8"),
        ("/EcomCraft/i18n.js", "i18n.js", "text/javascript; charset=utf-8"),
        ("/EcomCraft/simple.js", "simple.js", "text/javascript; charset=utf-8"),
        ("/EcomCraft/js/simple/brand.js", "js/simple/brand.js", "text/javascript; charset=utf-8"),
        ("/EcomCraft/js/simple/storyboard.js", "js/simple/storyboard.js", "text/javascript; charset=utf-8"),
        ("/EcomCraft/js/simple/rules.js", "js/simple/rules.js", "text/javascript; charset=utf-8"),
        ("/EcomCraft/js/simple/prompt.js", "js/simple/prompt.js", "text/javascript; charset=utf-8"),
        ("/EcomCraft/js/simple/export.js", "js/simple/export.js", "text/javascript; charset=utf-8"),
        ("/EcomCraft/js/simple/ai-label.js", "js/simple/ai-label.js", "text/javascript; charset=utf-8"),
        ("/EcomCraft/js/simple/tone.js", "js/simple/tone.js", "text/javascript; charset=utf-8"),
        ("/EcomCraft/js/simple/placement.js", "js/simple/placement.js", "text/javascript; charset=utf-8"),
        ("/EcomCraft/js/simple/products.js", "js/simple/products.js", "text/javascript; charset=utf-8"),
        ("/EcomCraft/js/simple/productinfo.js", "js/simple/productinfo.js", "text/javascript; charset=utf-8"),
        ("/EcomCraft/js/simple/reference.js", "js/simple/reference.js", "text/javascript; charset=utf-8"),
        ("/EcomCraft/js/simple/batch.js", "js/simple/batch.js", "text/javascript; charset=utf-8"),
        ("/EcomCraft/js/simple/tasks.js", "js/simple/tasks.js", "text/javascript; charset=utf-8"),
        ("/EcomCraft/lang/en.js", "lang/en.js", "text/javascript; charset=utf-8"),
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
        // Template example images: built-in ones ship in the extension folder, administrator ones live in Data/EcomCraft/template-images.
        WebServer.WebApp.MapGet("/EcomCraft/template-image/{id}", async (HttpContext context, string id) =>
        {
            User user = WebServer.GetUserFor(context);
            TemplateStore.Entry entry = user is not null && user.HasPermission(Permissions.FundamentalGenerateTabAccess) ? TemplateStore.Find(id) : null;
            string file = entry?.Template?.ExampleImage;
            string path = file is null ? null : Path.Combine(TemplateStore.ImageDir(entry.Source), file);
            if (path is null || !File.Exists(path))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            context.Response.Headers.CacheControl = "private, max-age=300";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.ContentType = Path.GetExtension(file).ToLowerFast() switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
            try
            {
                await context.Response.Body.WriteAsync(await File.ReadAllBytesAsync(path, context.RequestAborted), context.RequestAborted);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
            }
        });
        // Product library images (F36): only the owner can read them. The optional ?angle= names one angle photo (F84); without it the main angle serves.
        WebServer.WebApp.MapGet("/EcomCraft/product-image/{id}/{kind}", async (HttpContext context, string id, string kind) =>
        {
            User user = WebServer.GetUserFor(context);
            string angle = context.Request.Query["angle"].ToString();
            (byte[] bytes, string extension) = user is null || !user.HasPermission(Permissions.FundamentalGenerateTabAccess) || kind is not ("original" or "cutout")
                ? (null, null) : Products.ProductLibrary.FileFor(user.UserID, id, angle, kind);
            if (bytes is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            context.Response.Headers.CacheControl = "private, no-cache";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.ContentType = extension switch { "png" => "image/png", "webp" => "image/webp", _ => "image/jpeg" };
            await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
        });
        // Brand kit images (F12): only the owner can read them, and only file names the kit lists.
        WebServer.WebApp.MapGet("/EcomCraft/brand-image/{id}/{file}", async (HttpContext context, string id, string file) =>
        {
            User user = WebServer.GetUserFor(context);
            (byte[] bytes, string extension) = user is null || !user.HasPermission(Permissions.FundamentalGenerateTabAccess)
                ? (null, null) : Brand.BrandKitLibrary.FileFor(user.UserID, id, file);
            if (bytes is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            context.Response.Headers.CacheControl = "private, no-cache";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.ContentType = extension switch { "png" => "image/png", "webp" => "image/webp", _ => "image/jpeg" };
            await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
        });
        // Prepared export zips (F05): only the account that made them can download them.
        WebServer.WebApp.MapGet("/EcomCraft/export/{id}", async (HttpContext context, string id) =>
        {
            User user = WebServer.GetUserFor(context);
            string path = user is null ? null : Export.ExportService.ZipPath(user.UserID, id);
            if (path is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers.ContentDisposition = "attachment; filename=\"ecomcraft-export.zip\"";
            context.Response.ContentType = "application/zip";
            await context.Response.SendFileAsync(path, context.RequestAborted);
        });
        // System-data backups (F45): administrators only, created and downloaded by the same account.
        WebServer.WebApp.MapGet("/EcomCraft/backup/{id}", async (HttpContext context, string id) =>
        {
            User user = WebServer.GetUserFor(context);
            string path = user is null || !user.HasPermission(Permissions.EditServerSettings) ? null : BackupService.ZipPath(user.UserID, id);
            if (path is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers.ContentDisposition = "attachment; filename=\"ecomcraft-backup.zip\"";
            context.Response.ContentType = "application/zip";
            await context.Response.SendFileAsync(path, context.RequestAborted);
        });
        // Diagnostics packages (F44): administrators only, created and downloaded by the same account.
        WebServer.WebApp.MapGet("/EcomCraft/diagnostics/{id}", async (HttpContext context, string id) =>
        {
            User user = WebServer.GetUserFor(context);
            string path = user is null || !user.HasPermission(Permissions.EditServerSettings) ? null : DiagnosticsService.ZipPath(user.UserID, id);
            if (path is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers.ContentDisposition = "attachment; filename=\"ecomcraft-diagnostics.zip\"";
            context.Response.ContentType = "application/zip";
            await context.Response.SendFileAsync(path, context.RequestAborted);
        });
        // ComfyUI editor of a published workflow's source server (administrators): resolves the editor page and redirects to it.
        // Local self-starting servers use the session-checked proxy; LAN servers use their own address.
        WebServer.WebApp.MapGet("/EcomCraft/WorkflowEdit", async (HttpContext context) =>
        {
            User user = WebServer.GetUserFor(context);
            if (user is null || !user.HasPermission(Permissions.EditServerSettings))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync("此账号没有管理服务器的权限。\nThis account cannot manage the server.", context.RequestAborted);
                return;
            }
            string id = context.Request.Query["id"].ToString();
            bool requesterLocal = System.Net.IPAddress.IsLoopback(context.Connection.RemoteIpAddress ?? System.Net.IPAddress.None);
            WorkflowEditLink.Result result = WorkflowEditLink.Resolve(id, requesterLocal);
            if (result.Error is not null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["Referrer-Policy"] = "same-origin";
                await context.Response.WriteAsync($"<!DOCTYPE html>\n<html lang=\"zh-CN\"><head><meta charset=\"utf-\"><title>EcomCraft</title></head>"
                    + $"<body style=\"font-family:system-ui;background:#101010;color:#eee;margin:40px auto;max-width:560px\">"
                    + $"<p>{System.Net.WebUtility.HtmlEncode(result.Error)}</p>"
                    + "<p><a href=\"/EcomCraft\" style=\"color:#7fd0a0\">返回工作台 / Back to the workspace</a></p></body></html>", context.RequestAborted);
                return;
            }
            context.Response.Redirect(result.Url);
        });
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
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync("此账号没有创作工作台访问权限。请联系管理员。\nThis account cannot access the EcomCraft workspace. Contact your administrator.");
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
