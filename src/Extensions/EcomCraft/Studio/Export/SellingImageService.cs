using System.IO;
using EcomCraftStudio.Studio.Storage;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using ISImage = SixLabors.ImageSharp.Image;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio.Export;

/// <summary>Selling-point images (F47): the user's title and selling points are drawn by the server onto one of the account's works in a fixed layout,
/// with the bundled Noto Sans SC fonts (SIL Open Font License 1.1, Assets/fonts). No AI writes or changes the text. The result is saved as a new work;
/// the original is left unchanged and kept.</summary>
[API.APIClass("EcomCraft selling-point images: draw the user's title and selling points onto a work in a fixed layout.")]
public static class SellingImageService
{
    /// <summary>Longest side of a work accepted as the base picture.</summary>
    private const int MaxSide = 8192;

    /// <summary>Folder holding the bundled fonts, set at startup.</summary>
    private static string FontDir;

    /// <summary>The loaded font family, or null before loading or when the fonts are missing.</summary>
    private static FontFamily? Family;

    /// <summary>Protects font loading.</summary>
    private static readonly object FontLock = new();

    /// <summary>Sets the extension folder that holds Assets/fonts.</summary>
    public static void Init(string extensionFolder)
    {
        FontDir = System.IO.Path.Combine(extensionFolder, "Assets", "fonts");
    }

    /// <summary>The bundled font family (regular and bold), loaded once; null when the font files are missing or unreadable.</summary>
    private static FontFamily? Fonts()
    {
        lock (FontLock)
        {
            if (Family is FontFamily loaded)
            {
                return loaded;
            }
            try
            {
                FontCollection collection = new();
                FontFamily family = collection.Add(System.IO.Path.Combine(FontDir ?? "", "NotoSansSC-Regular.otf"));
                collection.Add(System.IO.Path.Combine(FontDir ?? "", "NotoSansSC-Bold.otf"));
                Family = family;
                return family;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidFontFileException or NotSupportedException)
            {
                Logs.Error($"EcomCraft could not load the selling-image fonts: {ex.Message}");
                return null;
            }
        }
    }

    [API.APIDescription("Make a selling-point image (F47) from one of the current account's works: image (gallery path or result URL), layout (\"banner\", \"side\" or \"badges\"), title (up to 20 characters), points (up to 3, each up to 16 characters) and optional accent (\"#RRGGBB\"). The text is drawn by the server, never by AI; the result is saved as a new work. rawInput is the root request JSON.", "images: [\"View/user/ecomcraft/selling/…png\"]")]
    public static async Task<JObject> EcomCraftStudioSellingImage(Session session, JObject rawInput)
    {
        if (!StudioApi.CanView(session) || !session.User.HasPermission(Permissions.BasicImageGeneration))
        {
            return StudioApi.Error("此账号没有生成权限。", "bad_permissions");
        }
        string layout = rawInput["layout"]?.Type == JTokenType.String ? rawInput.Value<string>("layout") : "";
        if (!SellingImageRules.Layouts.Contains(layout))
        {
            return StudioApi.Error("未知的卖点图版式。", "invalid_parameters");
        }
        string title = SellingImageRules.CleanText(rawInput["title"]?.Type == JTokenType.String ? rawInput.Value<string>("title") : "");
        List<string> points = [.. (rawInput["points"] as JArray ?? new JArray()).Select(item => SellingImageRules.CleanText(item.Type == JTokenType.String ? item.ToString() : ""))
            .Where(point => point.Length > 0)];
        if (SellingImageRules.Problem(title, points) is string problem)
        {
            return StudioApi.Error(problem, "invalid_selling_text");
        }
        string accent = SellingImageRules.Accent(rawInput["accent"]?.Type == JTokenType.String ? rawInput.Value<string>("accent") : null);
        string url = rawInput["image"]?.Type == JTokenType.String ? rawInput.Value<string>("image") : null;
        if (url is not null && !url.StartsWith("View/", StringComparison.Ordinal) && !url.StartsWith("Output/", StringComparison.Ordinal))
        {
            url = $"View/{session.User.UserID}/{url}";
        }
        // Only the account's own works can be used (the same check as exports).
        string source = url is null ? null : WorkSync.LocalPathFor(session.User, url, out _);
        if (source is null || !File.Exists(source))
        {
            return StudioApi.Error("作品不存在或不属于当前账号，请刷新后重新选择。", "invalid_reference");
        }
        FontFamily? loadedFamily = Fonts();
        if (loadedFamily is not FontFamily family)
        {
            return StudioApi.Error("卖点图字体缺失：请管理员确认扩展的 Assets/fonts 文件夹完整。", "selling_font_missing");
        }
        try
        {
            JObject result = await Task.Run(() => Render(session.User, source, family, layout, title, points, accent));
            // The base work is clearly wanted, so it is kept forever (F39).
            CandidateCleanup.MarkKept(session.User, new JArray(url));
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            Logs.Warning($"EcomCraft could not make a selling image: {ex.Message}");
            return StudioApi.Error("卖点图没有生成，请稍后重试。", "generation_failed");
        }
    }

    /// <summary>Draws the layout onto a copy of the work and saves it as a new PNG work with the original's metadata plus a selling-image marker.</summary>
    private static JObject Render(User user, string sourcePath, FontFamily family, string layout, string title, List<string> points, string accent)
    {
        using Image<Rgba32> image = ISImage.Load<Rgba32>(sourcePath);
        if (Math.Max(image.Width, image.Height) > MaxSide)
        {
            return StudioApi.Error($"作品太大（超过 {MaxSide} 像素），无法做成卖点图。", "invalid_reference");
        }
        string metadata = Metadata(ExportService.MetadataText(image));
        image.Metadata.ExifProfile = null;
        image.Metadata.GetPngMetadata().TextData.Clear();
        SellingImageRules.Plan plan = SellingImageRules.Layout(layout, image.Width, image.Height);
        Color accentColor = Color.ParseHex(accent);
        Color onAccent = Color.ParseHex(SellingImageRules.TextOn(accent));
        image.Mutate(context =>
        {
            Font titleFont = Fit(family, FontStyle.Bold, title, plan.Title.Width, plan.TitleSize);
            Font pointFont = family.CreateFont(plan.PointSize, FontStyle.Regular);
            if (layout == "side")
            {
                context.Fill(Color.White.WithAlpha(0.9f), Rect(plan.Panel));
                context.Fill(accentColor, new RectangularPolygon(plan.Panel.X + plan.Panel.Width - plan.Panel.Width * 0.02f, 0, plan.Panel.Width * 0.02f, plan.Panel.Height));
                Write(context, title, titleFont, plan.Title, accentColor);
                float y = plan.Points.Y;
                foreach (string point in points)
                {
                    Font font = Fit(family, FontStyle.Regular, $"• {point}", plan.Points.Width, plan.PointSize);
                    Write(context, $"• {point}", font, new SellingImageRules.Box(plan.Points.X, y, plan.Points.Width, font.Size * 1.6f), Color.ParseHex("#222222"));
                    y += plan.PointSize * 1.8f;
                }
            }
            else if (layout == "badges")
            {
                context.Fill(accentColor.WithAlpha(0.88f), Rect(plan.Panel));
                Write(context, title, titleFont, plan.Title, onAccent);
                float y = plan.Points.Y + plan.Points.Height;
                foreach (string point in Enumerable.Reverse(points))
                {
                    Font font = Fit(family, FontStyle.Bold, point, plan.Points.Width * 0.85f, plan.PointSize);
                    FontRectangle size = TextMeasurer.MeasureSize(point, new TextOptions(font));
                    float padding = font.Size * 0.5f;
                    float badgeWidth = size.Width + padding * 2, badgeHeight = font.Size * 1.8f;
                    y -= badgeHeight + font.Size * 0.4f;
                    float x = plan.Points.X + plan.Points.Width - badgeWidth;
                    context.Fill(accentColor, new RectangularPolygon(x, y, badgeWidth, badgeHeight));
                    Write(context, point, font, new SellingImageRules.Box(x + padding, y + (badgeHeight - font.Size * 1.2f) / 2, size.Width + 1, font.Size * 1.4f), onAccent);
                }
            }
            else
            {
                context.Fill(accentColor.WithAlpha(0.85f), Rect(plan.Panel));
                Write(context, title, titleFont, plan.Title, onAccent);
                if (points.Count > 0)
                {
                    string line = string.Join("  ｜  ", points);
                    Write(context, line, Fit(family, FontStyle.Regular, line, plan.Points.Width, plan.PointSize), plan.Points, onAccent);
                }
            }
        });
        image.Metadata.GetPngMetadata().TextData.Add(new SixLabors.ImageSharp.Formats.Png.Chunks.PngTextData("parameters", metadata, "", ""));
        string relative = $"ecomcraft/selling/{DateTime.Now:yyyy-MM-dd}/{DateTime.Now:HHmmss}-{Guid.NewGuid().ToString("N")[..8]}.png";
        string path = System.IO.Path.GetFullPath(UserImageHistoryHelper.GetRealPathFor(user, $"{user.OutputDirectory}/{relative}"));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
        using (FileStream stream = File.Create(path))
        {
            image.Save(stream, new PngEncoder());
        }
        string prefix = Program.ServerSettings.Paths.AppendUserNameToOutputPath ? $"View/{user.UserID}/" : "Output/";
        return new JObject() { ["images"] = new JArray(prefix + relative) };
    }

    /// <summary>The original work's metadata with a marker that this is a selling-point image (F47), so exports keep the AI provenance of the base picture.</summary>
    private static string Metadata(string original)
    {
        JObject metadata;
        try
        {
            metadata = string.IsNullOrWhiteSpace(original) ? new JObject() : JObject.Parse(original);
        }
        catch (JsonException)
        {
            metadata = new JObject() { ["original_parameters"] = original };
        }
        if (metadata["sui_extra_data"] is not JObject extra)
        {
            extra = new JObject();
            metadata["sui_extra_data"] = extra;
        }
        extra["ecomcraft_selling_image"] = "true";
        return metadata.ToString(Formatting.None);
    }

    /// <summary>A rectangle path for a layout box.</summary>
    private static RectangularPolygon Rect(SellingImageRules.Box box) => new(box.X, box.Y, box.Width, box.Height);

    /// <summary>The font of this style at the largest size (down to half the start) at which the text fits the width on one line.</summary>
    private static Font Fit(FontFamily family, FontStyle style, string text, float width, float start)
    {
        float size = SellingImageRules.FitSize(fontSize => TextMeasurer.MeasureSize(text, new TextOptions(family.CreateFont(fontSize, style))).Width, width, start, start * 0.5f);
        return family.CreateFont(size, style);
    }

    /// <summary>Draws text into a box, wrapping at its width.</summary>
    private static void Write(IImageProcessingContext context, string text, Font font, SellingImageRules.Box box, Color color)
    {
        RichTextOptions options = new(font) { Origin = new PointF(box.X, box.Y), WrappingLength = box.Width };
        context.DrawText(options, text, color);
    }
}
