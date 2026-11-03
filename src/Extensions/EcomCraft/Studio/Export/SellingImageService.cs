using System.IO;
using EcomCraftStudio.Studio.PostProcess;
using EcomCraftStudio.Studio.Prompting;
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
/// the original is left unchanged and kept. Since T100 the layout can be chosen automatically to keep clear of the product, text that is too long shrinks
/// and wraps once and is otherwise refused (never cut), and the wording check (F79) comes back with the result.</summary>
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

    /// <summary>The bundled font family for the server's other export drawings (the visible AI label, F49; detail screens, T100), or null while the font
    /// files are missing.</summary>
    internal static FontFamily? SharedFonts() => Fonts();

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

    [API.APIDescription("Make a selling-point image (F47) from one of the current account's works: image (gallery path or result URL), layout (\"auto\" picks the layout that keeps clear of the product, or \"banner\", \"side\", \"right\", \"top\", \"badges\"), title (up to 60 characters) and points (up to 3, each up to 40 characters) — or, without a title, product_id and language to use the product's saved selling info (F79) — and optional accent (\"#RRGGBB\"). Text that does not fit the layout after shrinking and wrapping once is refused, never cut. The text is drawn by the server, never by AI; the result is saved as a new work and comes back with the layout used, notes and the wording check. rawInput is the root request JSON.", "images: [\"View/user/ecomcraft/selling/…png\"], layout: \"side\", notes: [], claims: []")]
    public static async Task<JObject> EcomCraftStudioSellingImage(Session session, JObject rawInput)
    {
        if (!StudioApi.CanView(session) || !session.User.HasPermission(Permissions.BasicImageGeneration))
        {
            return StudioApi.Error("此账号没有生成权限。", "bad_permissions");
        }
        string layout = rawInput["layout"]?.Type == JTokenType.String ? rawInput.Value<string>("layout") : SellingImageRules.AutoLayout;
        if (!SellingImageRules.IsLayout(layout))
        {
            return StudioApi.Error("未知的卖点图版式。", "invalid_parameters");
        }
        string title = SellingImageRules.CleanText(rawInput["title"]?.Type == JTokenType.String ? rawInput.Value<string>("title") : "");
        List<string> points = [.. (rawInput["points"] as JArray ?? new JArray()).Select(item => SellingImageRules.CleanText(item.Type == JTokenType.String ? item.ToString() : ""))
            .Where(point => point.Length > 0)];
        string productId = rawInput["product_id"]?.Type == JTokenType.String ? rawInput.Value<string>("product_id") : null;
        if (title.Length == 0 && points.Count == 0 && productId is not null)
        {
            // The product's own saved words (F79, T100): only read here, never changed.
            string language = rawInput.Value<string>("language") == "en" ? "en" : "zh-CN";
            (Products.ProductSpecs specs, string name) = Products.ProductLibrary.SellingInfoFor(session.User.UserID, productId, language);
            if (specs is null || specs.Points.Count == 0)
            {
                return StudioApi.Error("这个商品还没有填写这个语言的卖点信息，请先填写。", "invalid_selling_text");
            }
            (title, points) = SellingImageRules.FromSpecs(specs, name);
        }
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
        Placement hint = productId is null ? null : Products.ProductLibrary.ForGeneration(session.User.UserID, productId, false).Placement;
        try
        {
            JObject result = await Task.Run(() => Render(session.User, source, family, layout, title, points, accent, hint));
            if (!result.ContainsKey("error"))
            {
                // The base work is clearly wanted, so it is kept forever (F39).
                CandidateCleanup.MarkKept(session.User, new JArray(url));
                result["claims"] = Claims(title, points);
            }
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            Logs.Warning($"EcomCraft could not make a selling image: {ex.Message}");
            return StudioApi.Error("卖点图没有生成，请稍后重试。", "generation_failed");
        }
    }

    /// <summary>The wording check (F79) of the drawn text (T100): every hit with the text it was found in; it only notices and never blocks.</summary>
    private static JArray Claims(string title, List<string> points)
    {
        JArray claims = [];
        List<ClaimRules.ClaimWord> words = ClaimWords.Words();
        foreach (string text in points.Prepend(title))
        {
            JArray hits = ClaimWords.HitsJson(ClaimRules.Check(text, words));
            if (hits.Count > 0)
            {
                claims.Add(new JObject() { ["text"] = text, ["hits"] = hits });
            }
        }
        return claims;
    }

    /// <summary>Draws the given title and points onto one of the account's works from its local file (T98: the full-set batch's selling slot). Same rule
    /// and same result as the page's own selling images (F47): the text is the user's, drawn by the server, saved as a new work.</summary>
    internal static JObject RenderFromWork(User user, string sourcePath, string layout, string title, List<string> points, string accent)
    {
        FontFamily? loadedFamily = Fonts();
        if (loadedFamily is not FontFamily family)
        {
            return StudioApi.Error("卖点图字体缺失：请管理员确认扩展的 Assets/fonts 文件夹完整。", "selling_font_missing");
        }
        try
        {
            return Render(user, sourcePath, family, layout, title, points, accent, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            Logs.Warning($"EcomCraft could not make a selling image: {ex.Message}");
            return StudioApi.Error("卖点图没有生成，请稍后重试。", "generation_failed");
        }
    }

    /// <summary>The automatic layout for one saved work (T100), read from its size and product placement.</summary>
    internal static string AutoLayoutFor(string path)
    {
        using Image<Rgba32> image = ISImage.Load<Rgba32>(path);
        return ResolveLayout(SellingImageRules.AutoLayout, image.Width, image.Height, ExportService.MetadataText(image), null, []);
    }

    /// <summary>The layout for a picture (T100): the requested one, or for "auto" the one that keeps clear of the product — placed by the work's own metadata
    /// (F49), else by <paramref name="hint"/> — with "layout_on_product" in <paramref name="notes"/> when every layout covers part of it.</summary>
    internal static string ResolveLayout(string layout, int width, int height, string metadataText, Placement hint, JArray notes)
    {
        if (layout != SellingImageRules.AutoLayout)
        {
            return layout;
        }
        SellingImageRules.LayoutChoice choice = SellingImageRules.ChooseLayout(width, height, ComplianceRules.ProductPlacement(metadataText) ?? hint);
        if (choice.OverlapsProduct)
        {
            notes.Add("layout_on_product");
        }
        return choice.Layout;
    }

    /// <summary>Draws the layout onto a copy of the work and saves it as a new PNG work with the original's metadata plus a selling-image marker. Returns the
    /// images with the layout used and notes, or an <c>invalid_selling_text</c> error naming the text that does not fit.</summary>
    private static JObject Render(User user, string sourcePath, FontFamily family, string layout, string title, List<string> points, string accent, Placement hint)
    {
        using Image<Rgba32> image = ISImage.Load<Rgba32>(sourcePath);
        if (Math.Max(image.Width, image.Height) > MaxSide)
        {
            return StudioApi.Error($"作品太大（超过 {MaxSide} 像素），无法做成卖点图。", "invalid_reference");
        }
        string original = ExportService.MetadataText(image);
        JArray notes = [];
        layout = ResolveLayout(layout, image.Width, image.Height, original, hint, notes);
        string metadata = Metadata(original, "ecomcraft_selling_image");
        image.Metadata.ExifProfile = null;
        image.Metadata.GetPngMetadata().TextData.Clear();
        SellingImageRules.Plan plan = SellingImageRules.Layout(layout, image.Width, image.Height);
        Color accentColor = Color.ParseHex(accent);
        Color onAccent = Color.ParseHex(SellingImageRules.TextOn(accent));
        Color ink = Color.ParseHex("#222222");
        SellingImageRules.FittedText titleFit = Fit(family, FontStyle.Bold, title, plan.Title.Width, plan.Title.Height, plan.TitleSize);
        if (titleFit is null)
        {
            return TooLong(title);
        }
        List<(SellingImageRules.FittedText Text, FontStyle Style, SellingImageRules.Box Box)> pointDraws = [];
        if (layout is "side" or "right")
        {
            float y = plan.Points.Y;
            foreach (string point in points)
            {
                SellingImageRules.FittedText fit = Fit(family, FontStyle.Regular, $"• {point}", plan.Points.Width, plan.Points.Height, plan.PointSize);
                if (fit is null)
                {
                    return TooLong(point);
                }
                pointDraws.Add((fit, FontStyle.Regular, new SellingImageRules.Box(plan.Points.X, y, plan.Points.Width, 0)));
                y += fit.Lines.Count * fit.Size * SellingImageRules.LineHeight + fit.Size * 0.5f;
            }
            if (y - plan.PointSize * 0.5f > plan.Points.Y + plan.Points.Height)
            {
                return StudioApi.Error("卖点太多或太长，这个版式放不下，请删短卖点或换一种版式后重试。", "invalid_selling_text");
            }
        }
        else if (layout == "badges")
        {
            float y = plan.Points.Y + plan.Points.Height;
            foreach (string point in Enumerable.Reverse(points))
            {
                SellingImageRules.FittedText fit = SellingImageRules.FitText(point, Measurer(family, FontStyle.Bold), plan.Points.Width * 0.85f, float.MaxValue, plan.PointSize,
                    plan.PointSize * SellingImageRules.MinSizeShare, 1);
                if (fit is null)
                {
                    return TooLong(point);
                }
                float badgeHeight = fit.Size * 1.8f;
                y -= badgeHeight + fit.Size * 0.4f;
                pointDraws.Add((fit, FontStyle.Bold, new SellingImageRules.Box(plan.Points.X, y, plan.Points.Width, badgeHeight)));
            }
            if (y < plan.Points.Y)
            {
                return StudioApi.Error("卖点太多或太长，这个版式放不下，请删短卖点或换一种版式后重试。", "invalid_selling_text");
            }
        }
        else if (points.Count > 0)
        {
            string line = string.Join("  ｜  ", points);
            SellingImageRules.FittedText fit = Fit(family, FontStyle.Regular, line, plan.Points.Width, plan.Points.Height, plan.PointSize);
            if (fit is null)
            {
                return StudioApi.Error("卖点合在一起放不下这个横幅，请删短卖点或换成侧边文字栏后重试。", "invalid_selling_text");
            }
            pointDraws.Add((fit, FontStyle.Regular, plan.Points));
        }
        image.Mutate(context =>
        {
            Font titleFont = family.CreateFont(titleFit.Size, FontStyle.Bold);
            if (layout is "side" or "right")
            {
                context.Fill(Color.White.WithAlpha(0.9f), Rect(plan.Panel));
                float barWidth = plan.Panel.Width * 0.02f;
                float barX = layout == "side" ? plan.Panel.X + plan.Panel.Width - barWidth : plan.Panel.X;
                context.Fill(accentColor, new RectangularPolygon(barX, 0, barWidth, plan.Panel.Height));
                DrawLines(context, titleFit.Lines, titleFont, plan.Title.X, plan.Title.Y, accentColor);
                foreach ((SellingImageRules.FittedText fit, FontStyle style, SellingImageRules.Box box) in pointDraws)
                {
                    DrawLines(context, fit.Lines, family.CreateFont(fit.Size, style), box.X, box.Y, ink);
                }
            }
            else if (layout == "badges")
            {
                context.Fill(accentColor.WithAlpha(0.88f), Rect(plan.Panel));
                DrawLines(context, titleFit.Lines, titleFont, plan.Title.X, plan.Title.Y, onAccent);
                foreach ((SellingImageRules.FittedText fit, FontStyle style, SellingImageRules.Box box) in pointDraws)
                {
                    Font font = family.CreateFont(fit.Size, style);
                    FontRectangle size = TextMeasurer.MeasureSize(fit.Lines[0], new TextOptions(font));
                    float padding = font.Size * 0.5f;
                    float badgeWidth = size.Width + padding * 2;
                    float x = box.X + box.Width - badgeWidth;
                    context.Fill(accentColor, new RectangularPolygon(x, box.Y, badgeWidth, box.Height));
                    DrawLines(context, fit.Lines, font, x + padding, box.Y + (box.Height - font.Size * 1.2f) / 2, onAccent);
                }
            }
            else
            {
                context.Fill(accentColor.WithAlpha(0.85f), Rect(plan.Panel));
                DrawLines(context, titleFit.Lines, titleFont, plan.Title.X, plan.Title.Y, onAccent);
                foreach ((SellingImageRules.FittedText fit, FontStyle style, SellingImageRules.Box box) in pointDraws)
                {
                    DrawLines(context, fit.Lines, family.CreateFont(fit.Size, style), box.X, box.Y, onAccent);
                }
            }
        });
        string saved = SaveWork(user, image, metadata, "selling");
        return new JObject() { ["images"] = new JArray(saved), ["layout"] = layout, ["notes"] = notes };
    }

    /// <summary>The plain-language refusal for text that does not fit (T100): it names the text, so the user knows what to shorten.</summary>
    private static JObject TooLong(string text) =>
        StudioApi.Error($"“{text}”在这个版式里放不下（已缩小字号并换行），请删短后重试。", "invalid_selling_text");

    /// <summary>A measure for <see cref="SellingImageRules.FitText"/>: a string's width at a size in this style.</summary>
    internal static Func<float, string, float> Measurer(FontFamily family, FontStyle style) =>
        (size, text) => TextMeasurer.MeasureSize(text, new TextOptions(family.CreateFont(size, style))).Width;

    /// <summary>Fits text into a box from its starting size down to <see cref="SellingImageRules.MinSizeShare"/> of it, on up to
    /// <see cref="SellingImageRules.MaxLines"/> lines; null when it does not fit.</summary>
    internal static SellingImageRules.FittedText Fit(FontFamily family, FontStyle style, string text, float width, float height, float start) =>
        SellingImageRules.FitText(text, Measurer(family, style), width, height, start, start * SellingImageRules.MinSizeShare, SellingImageRules.MaxLines);

    /// <summary>Draws fitted lines from a top-left point, one under the other at <see cref="SellingImageRules.LineHeight"/>.</summary>
    internal static void DrawLines(IImageProcessingContext context, List<string> lines, Font font, float x, float y, Color color)
    {
        foreach (string line in lines)
        {
            context.DrawText(new RichTextOptions(font) { Origin = new PointF(x, y) }, line, color);
            y += font.Size * SellingImageRules.LineHeight;
        }
    }

    /// <summary>Saves a drawn picture as a new PNG work under ecomcraft/&lt;folder&gt;/ with the given metadata; returns its viewable URL.</summary>
    internal static string SaveWork(User user, Image<Rgba32> image, string metadata, string folder)
    {
        image.Metadata.GetPngMetadata().TextData.Add(new SixLabors.ImageSharp.Formats.Png.Chunks.PngTextData("parameters", metadata, "", ""));
        string relative = $"ecomcraft/{folder}/{DateTime.Now:yyyy-MM-dd}/{DateTime.Now:HHmmss}-{Guid.NewGuid().ToString("N")[..8]}.png";
        string path = System.IO.Path.GetFullPath(UserImageHistoryHelper.GetRealPathFor(user, $"{user.OutputDirectory}/{relative}"));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
        using (FileStream stream = File.Create(path))
        {
            image.Save(stream, new PngEncoder());
        }
        string prefix = Program.ServerSettings.Paths.AppendUserNameToOutputPath ? $"View/{user.UserID}/" : "Output/";
        return prefix + relative;
    }

    /// <summary>The original work's metadata with a marker (F47: "ecomcraft_selling_image"; T100: "ecomcraft_detail_page" too), so exports keep the AI
    /// provenance of the base picture and know the picture carries drawn text.</summary>
    internal static string Metadata(string original, params string[] markers)
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
        foreach (string marker in markers)
        {
            extra[marker] = "true";
        }
        return metadata.ToString(Formatting.None);
    }

    /// <summary>A rectangle path for a layout box.</summary>
    internal static RectangularPolygon Rect(SellingImageRules.Box box) => new(box.X, box.Y, box.Width, box.Height);
}
