using EcomCraftStudio.Studio.PostProcess;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace EcomCraftStudio.Studio.Export;

/// <summary>One of the four corners of the canvas a label can sit in (F49).</summary>
public enum LabelCorner
{
    /// <summary>Top left.</summary>
    TopLeft,

    /// <summary>Top right.</summary>
    TopRight,

    /// <summary>Bottom left.</summary>
    BottomLeft,

    /// <summary>Bottom right.</summary>
    BottomRight
}

/// <summary>Where one label ended up (F49): its corner, its font size in pixels, whether the font had to shrink below the rule's share and whether it
/// still sits on the product after every corner was tried.</summary>
public sealed record class LabelPlacement(LabelCorner Corner, float FontSize, bool Shrunk, bool OnProduct);

/// <summary>Placing and drawing the visible AI label (F49) on an export copy — never on the stored work. Corner choice and shrinking are pure so they are
/// unit tested; the text is drawn with the same bundled Noto Sans SC fonts as selling-point images (F47) and is never generated or rewritten by AI.</summary>
public static class VisibleLabel
{
    /// <summary>Font-size shares tried when every corner sits on the product: the rule's own share first, then smaller steps down to about a third.</summary>
    public static readonly double[] ShrinkSteps = [1.0, 0.7, 0.5, 0.35];

    /// <summary>Smallest font drawn at all; below this the text stops being readable, so it is better to overlap than to shrink further.</summary>
    public const float MinFontSize = 8f;

    /// <summary>Empty space between the label and the canvas edges, in font sizes.</summary>
    public const double MarginFontSizes = 0.6;

    /// <summary>The label's outcome codes (F49), reported per slot with a count: drawn as ruled ("label_added"), drawn smaller to clear the product
    /// ("label_shrunk"), drawn on the least-overlapping corner after shrinking ("label_on_product"), not drawn because the fonts are missing
    /// ("label_font_missing"), not drawn because a pure-white slot would lose its white border ("label_white_skipped"), and not drawn because the
    /// rule forbids it while the user asked for it ("label_not_allowed").</summary>
    public const string
        Added = "label_added",
        ShrunkCode = "label_shrunk",
        OnProductCode = "label_on_product",
        FontMissing = "label_font_missing",
        WhiteSkipped = "label_white_skipped",
        NotAllowed = "label_not_allowed";

    /// <summary>The corner a position name means; "auto" and anything else give null.</summary>
    public static LabelCorner? CornerOf(string position) => position switch
    {
        "top_left" => LabelCorner.TopLeft,
        "top_right" => LabelCorner.TopRight,
        "bottom_left" => LabelCorner.BottomLeft,
        "bottom_right" => LabelCorner.BottomRight,
        _ => null
    };

    /// <summary>The label's rectangle at one corner: the measured text plus an inner margin, kept MarginFontSizes font sizes from the canvas edges.</summary>
    public static RectangleF Rect(LabelCorner corner, float fontSize, SizeF textSize, int width, int height)
    {
        float w = textSize.Width + fontSize;
        float h = textSize.Height + fontSize * 0.4f;
        float margin = (float)(fontSize * MarginFontSizes);
        float x = corner is LabelCorner.TopLeft or LabelCorner.BottomLeft ? margin : Math.Max(0, width - margin - w);
        float y = corner is LabelCorner.TopLeft or LabelCorner.TopRight ? margin : Math.Max(0, height - margin - h);
        return new RectangleF(x, y, w, h);
    }

    /// <summary>The product's bounding box on the canvas (F49): a square around the placement's center, its side the placement's longer side — the product's own
    /// aspect ratio is not kept in metadata, so the square is larger than the product can be and never smaller. Null without a placement.</summary>
    public static RectangleF? ProductRect(Placement product, int width, int height)
    {
        if (product is null)
        {
            return null;
        }
        float side = (float)(product.Size * Math.Min(width, height));
        PointF center = new((float)(product.CenterX * width), (float)(product.CenterY * height));
        return new RectangleF(center.X - side / 2, center.Y - side / 2, side, side);
    }

    /// <summary>Chooses where the label goes (F49): the rule's corner when it names one, else the corner farthest from the product; a corner that overlaps the
    /// product is only accepted after the font shrinks, and the corner with the least overlap wins when none clears. The label rectangle comes back in
    /// <paramref name=\"labelRect\"/>.</summary>
    public static LabelPlacement Place(int width, int height, string position, double fontRatio, SizeF textSize, Placement product)
    {
        LabelCorner[] corners = [LabelCorner.TopLeft, LabelCorner.TopRight, LabelCorner.BottomLeft, LabelCorner.BottomRight];
        RectangleF? productRect = ProductRect(product, width, height);
        foreach (double share in ShrinkSteps)
        {
            float fontSize = MathF.Max(MinFontSize, (float)(fontRatio * Math.Min(width, height) * share));
            SizeF scaled = new((float)(textSize.Width * share), (float)(textSize.Height * share));
            foreach (LabelCorner corner in Ordered(corners, position, productRect, width, height, fontSize, scaled))
            {
                RectangleF rect = Rect(corner, fontSize, scaled, width, height);
                if (!productRect.HasValue || !rect.IntersectsWith(productRect.Value))
                {
                    return new LabelPlacement(corner, fontSize, share < 1.0, false);
                }
            }
        }
        // Every corner sits on the product even at the smallest share: take the least-overlapping corner there and say so.
        float smallest = MathF.Max(MinFontSize, (float)(fontRatio * Math.Min(width, height) * ShrinkSteps[^1]));
        SizeF smallestText = new((float)(textSize.Width * ShrinkSteps[^1]), (float)(textSize.Height * ShrinkSteps[^1]));
        LabelCorner best = corners[0];
        double bestArea = double.MaxValue;
        foreach (LabelCorner corner in Ordered(corners, position, productRect, width, height, smallest, smallestText))
        {
            RectangleF rect = Rect(corner, smallest, smallestText, width, height);
            if (!productRect.HasValue)
            {
                best = corner;
                break;
            }
            RectangleF overlap = RectangleF.Intersect(rect, productRect.Value);
            double area = (double)overlap.Width * overlap.Height;
            if (area < bestArea)
            {
                bestArea = area;
                best = corner;
            }
        }
        return new LabelPlacement(best, smallest, true, true);
    }

    /// <summary>The corners in the order to try them: the rule's corner first when it names one, then the rest; "auto" takes the corner farthest from the
    /// product, or the bottom right when the work says nothing about where its product sits.</summary>
    private static List<LabelCorner> Ordered(LabelCorner[] corners, string position, RectangleF? productRect, int width, int height, float fontSize, SizeF textSize)
    {
        List<LabelCorner> order = [.. corners];
        if (CornerOf(position) is LabelCorner preferred)
        {
            return [.. order.OrderBy(corner => corner == preferred ? 0 : 1)];
        }
        if (productRect is RectangleF product)
        {
            PointF middle = new(product.X + product.Width / 2, product.Y + product.Height / 2);
            return [.. order.OrderByDescending(corner =>
            {
                RectangleF rect = Rect(corner, fontSize, textSize, width, height);
                PointF center = new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
                double dx = center.X - middle.X, dy = center.Y - middle.Y;
                return dx * dx + dy * dy;
            })];
        }
        order.Remove(LabelCorner.BottomRight);
        order.Insert(0, LabelCorner.BottomRight);
        return order;
    }

    /// <summary>Draws the label onto one export copy (F49). Returns null when the label is off (the rule's default and the request agree, so the picture is not
    /// touched at all), or one of the outcome codes; <see cref=\"WhiteSkipped\"/> leaves the picture untouched too, so a pure-white slot keeps its white border.</summary>
    public static string Draw(Image<Rgb24> image, ComplianceRules.VisibleLabelRule rule, bool? requested, string language, Placement product, bool requireWhite, FontFamily? family)
    {
        if (rule is null)
        {
            return null;
        }
        if (rule.ForcedOff)
        {
            return requested == true ? NotAllowed : null;
        }
        if (!rule.ForcedOn && (requested ?? rule.Enabled) != true)
        {
            return null;
        }
        if (family is not FontFamily fonts)
        {
            return FontMissing;
        }
        string text = rule.TextFor(language);
        float baseFont = MathF.Max(MinFontSize, (float)(rule.FontRatio * Math.Min(image.Width, image.Height)));
        Font font = fonts.CreateFont(baseFont, FontStyle.Regular);
        SixLabors.Fonts.FontRectangle measured = TextMeasurer.MeasureSize(text, new TextOptions(font));
        SizeF textSize = new(measured.Width, measured.Height);
        LabelPlacement placement = Place(image.Width, image.Height, rule.Position, rule.FontRatio, textSize, product);
        RectangleF rect = Rect(placement.Corner, placement.FontSize, new SizeF(textSize.Width * placement.FontSize / baseFont, textSize.Height * placement.FontSize / baseFont), image.Width, image.Height);
        Color color = Color.FromPixel(new Rgba32(63, 63, 63, (byte)Math.Round(rule.Opacity * 255)));
        void draw(Image<Rgb24> target)
        {
            Font drawFont = fonts.CreateFont(placement.FontSize, FontStyle.Regular);
            target.Mutate(context => context.DrawText(new RichTextOptions(drawFont) { Origin = new PointF(rect.X + placement.FontSize / 2, rect.Y + placement.FontSize * 0.2f) }, text, color));
        }
        if (requireWhite)
        {
            // A white slot's label would break its pure-white border check (F22): try on a copy first and leave the picture untouched when it does.
            using Image<Rgb24> test = image.Clone();
            draw(test);
            if (ComplianceRules.NonWhiteBorderFraction(test) > 0)
            {
                return WhiteSkipped;
            }
        }
        draw(image);
        return placement.OnProduct ? OnProductCode : placement.Shrunk ? ShrunkCode : Added;
    }
}
