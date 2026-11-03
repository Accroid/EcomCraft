using EcomCraftStudio.Studio.Products;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SwarmUI.Accounts;

namespace EcomCraftStudio.Studio.Export;

/// <summary>Detail-page screens (F80, T100): the server lays out the product's own saved info (T99) — selling points, parameters, size — onto screens of the
/// platform's detail-screen size, with the bundled fonts, around a picture of the product (the batch's fusion picture, or the product's own photo).
/// No AI writes text; screens are saved as new works carrying the detail-page marker, and the export stitches them into long images when asked.</summary>
public static class DetailPageService
{
    /// <summary>A parameter row's height for the table, as a multiple of the body font size per line, plus padding.</summary>
    private const float RowPadding = 0.9f;

    /// <summary>Lays out every screen the product's info makes and saves each as a new work (T100). <paramref name="picture"/> fills the picture areas
    /// (center-cropped, never stretched); <paramref name="baseMetadata"/> is the picture's own metadata, so exports keep its AI provenance — null for
    /// a picture that is the user's own photo. Returns the saved URLs, or a plain-language problem (font missing, text too long).</summary>
    public static (List<string> Images, string Problem, string ErrorId) Render(User user, Image<Rgba32> picture, string baseMetadata, ProductSpecs specs, string name,
        int width, int height, string language, string accent)
    {
        if (SellingImageService.SharedFonts() is not FontFamily family)
        {
            return (null, "卖点图字体缺失：请管理员确认扩展的 Assets/fonts 文件夹完整。", "selling_font_missing");
        }
        List<string> images = [];
        Color accentColor = Color.ParseHex(SellingImageRules.Accent(accent));
        Color ink = Color.ParseHex("#222222");
        Color soft = Color.ParseHex("#666666");
        string metadata = baseMetadata is null
            ? SellingImageService.Metadata(null, "ecomcraft_detail_page", "ecomcraft_layout_only")
            : SellingImageService.Metadata(baseMetadata, "ecomcraft_detail_page");
        string textMetadata = SellingImageService.Metadata(null, "ecomcraft_detail_page", "ecomcraft_layout_only");
        foreach (string kind in DetailPageRules.ScreenKinds(specs))
        {
            DetailPageRules.Areas areas = DetailPageRules.Layout(kind, width, height);
            if (kind == DetailPageRules.ScreenParams)
            {
                (List<List<(SellingImageRules.FittedText Name, SellingImageRules.FittedText Value, float Height)>> pages, string tooLong) = ParamPages(family, specs, areas);
                if (tooLong is not null)
                {
                    return (null, $"参数“{tooLong}”在详情屏里放不下（已缩小字号并换行），请删短后重试。", "invalid_selling_text");
                }
                foreach (List<(SellingImageRules.FittedText Name, SellingImageRules.FittedText Value, float Height)> rows in pages)
                {
                    using Image<Rgba32> screen = new(width, height, Color.White);
                    screen.Mutate(context =>
                    {
                        DrawHeading(context, family, DetailPageRules.Heading(kind, language), areas, accentColor);
                        float y = areas.Body.Y;
                        foreach ((SellingImageRules.FittedText nameFit, SellingImageRules.FittedText valueFit, float rowHeight) in rows)
                        {
                            SellingImageService.DrawLines(context, nameFit.Lines, family.CreateFont(nameFit.Size, FontStyle.Regular), areas.Body.X, y + areas.BodySize * 0.4f, soft);
                            SellingImageService.DrawLines(context, valueFit.Lines, family.CreateFont(valueFit.Size, FontStyle.Regular), areas.Body.X + areas.Body.Width * 0.36f,
                                y + areas.BodySize * 0.4f, ink);
                            y += rowHeight;
                            context.Fill(Color.ParseHex("#E5E5E5"), new RectangularPolygon(areas.Body.X, y - 1, areas.Body.Width, 1));
                        }
                    });
                    images.Add(SellingImageService.SaveWork(user, screen, textMetadata, "detail-page"));
                }
                continue;
            }
            string headingText = kind == DetailPageRules.ScreenPoints ? (string.IsNullOrWhiteSpace(specs.DisplayName) ? name ?? "" : specs.DisplayName)
                : DetailPageRules.Heading(kind, language);
            SellingImageRules.FittedText heading = SellingImageService.Fit(family, FontStyle.Bold, SellingImageRules.CleanText(headingText), areas.Heading.Width,
                areas.Heading.Height, areas.HeadingSize);
            if (heading is null)
            {
                return (null, $"“{headingText}”在详情屏里放不下（已缩小字号并换行），请删短后重试。", "invalid_selling_text");
            }
            List<string> bodyTexts = kind == DetailPageRules.ScreenPoints ? [.. specs.Points.Select(point => $"• {SellingImageRules.CleanText(point)}")]
                : [SellingImageRules.CleanText(specs.Size)];
            List<SellingImageRules.FittedText> body = [];
            float used = 0;
            foreach (string text in bodyTexts)
            {
                SellingImageRules.FittedText fit = SellingImageService.Fit(family, FontStyle.Regular, text, areas.Body.Width, areas.Body.Height, areas.BodySize);
                if (fit is null)
                {
                    return (null, $"“{text.TrimStart('•', ' ')}”在详情屏里放不下（已缩小字号并换行），请删短后重试。", "invalid_selling_text");
                }
                body.Add(fit);
                used += fit.Lines.Count * fit.Size * SellingImageRules.LineHeight + fit.Size * 0.4f;
            }
            if (used > areas.Body.Height + areas.BodySize)
            {
                return (null, "卖点太多或太长，一屏放不下，请删短卖点后重试。", "invalid_selling_text");
            }
            using (Image<Rgba32> screen = new(width, height, Color.White))
            {
                screen.Mutate(context =>
                {
                    DrawPicture(context, picture, areas.Image);
                    SellingImageService.DrawLines(context, heading.Lines, family.CreateFont(heading.Size, FontStyle.Bold), areas.Heading.X, areas.Heading.Y, accentColor);
                    float y = areas.Body.Y;
                    foreach (SellingImageRules.FittedText fit in body)
                    {
                        SellingImageService.DrawLines(context, fit.Lines, family.CreateFont(fit.Size, FontStyle.Regular), areas.Body.X, y, ink);
                        y += fit.Lines.Count * fit.Size * SellingImageRules.LineHeight + fit.Size * 0.4f;
                    }
                });
                images.Add(SellingImageService.SaveWork(user, screen, metadata, "detail-page"));
            }
        }
        return (images, null, null);
    }

    /// <summary>The parameter rows fitted and paged into screens (T100): names take a third of the width, values the rest, each shrinking and wrapping once;
    /// returns the pages, or the first text that does not fit.</summary>
    private static (List<List<(SellingImageRules.FittedText Name, SellingImageRules.FittedText Value, float Height)>> Pages, string TooLong) ParamPages(FontFamily family,
        ProductSpecs specs, DetailPageRules.Areas areas)
    {
        List<(SellingImageRules.FittedText Name, SellingImageRules.FittedText Value, float Height)> rows = [];
        foreach (ProductParam param in specs.Params)
        {
            string nameText = SellingImageRules.CleanText(param.Name);
            string valueText = SellingImageRules.CleanText(param.Value);
            SellingImageRules.FittedText nameFit = SellingImageService.Fit(family, FontStyle.Regular, nameText, areas.Body.Width * 0.32f, areas.Body.Height, areas.BodySize);
            if (nameFit is null)
            {
                return (null, nameText);
            }
            SellingImageRules.FittedText valueFit = SellingImageService.Fit(family, FontStyle.Regular, valueText, areas.Body.Width * 0.62f, areas.Body.Height, areas.BodySize);
            if (valueFit is null)
            {
                return (null, valueText);
            }
            float lines = Math.Max(nameFit.Lines.Count * nameFit.Size, valueFit.Lines.Count * valueFit.Size);
            rows.Add((nameFit, valueFit, lines * SellingImageRules.LineHeight + areas.BodySize * RowPadding));
        }
        List<List<(SellingImageRules.FittedText, SellingImageRules.FittedText, float)>> pages = [];
        foreach ((int first, int count) in DetailPageRules.PageRows([.. rows.Select(row => row.Height)], areas.Body.Height))
        {
            pages.Add(rows.GetRange(first, count));
        }
        return (pages, null);
    }

    /// <summary>Draws a screen heading in the accent color, with a short accent rule under it.</summary>
    private static void DrawHeading(IImageProcessingContext context, FontFamily family, string text, DetailPageRules.Areas areas, Color accent)
    {
        Font font = family.CreateFont(areas.HeadingSize, FontStyle.Bold);
        SellingImageService.DrawLines(context, [text], font, areas.Heading.X, areas.Heading.Y, accent);
        context.Fill(accent, new RectangularPolygon(areas.Heading.X, areas.Heading.Y + font.Size * 1.45f, font.Size * 1.6f, Math.Max(2, font.Size * 0.12f)));
    }

    /// <summary>Fills a box with the picture, center-cropped to the box's aspect (never stretched); nothing when the box is empty.</summary>
    private static void DrawPicture(IImageProcessingContext context, Image<Rgba32> picture, SellingImageRules.Box box)
    {
        if (picture is null || box.Width < 1 || box.Height < 1)
        {
            return;
        }
        int width = (int)Math.Round(box.Width), height = (int)Math.Round(box.Height);
        (int x, int y, int cropWidth, int cropHeight) = Generation.EmbedResizeRules.CenterCrop(picture.Width, picture.Height, width, height);
        using Image<Rgba32> part = picture.Clone(inner => inner.Crop(new Rectangle(x, y, cropWidth, cropHeight)).Resize(width, height));
        context.DrawImage(part, new Point((int)Math.Round(box.X), (int)Math.Round(box.Y)), 1f);
    }
}
