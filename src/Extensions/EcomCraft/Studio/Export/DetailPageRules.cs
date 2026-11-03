using EcomCraftStudio.Studio.Products;

namespace EcomCraftStudio.Studio.Export;

/// <summary>Rules for detail-page screens (F80, T100), independent of SwarmUI and fonts so they can be unit tested. A screen is laid out by the server from
/// the product's own saved info (T99) and a picture; no AI writes text and nothing is invented — a product without info gets no screen.</summary>
public static class DetailPageRules
{
    /// <summary>The selling-point screen: the picture with the display name and the points under it.</summary>
    public const string ScreenPoints = "points";

    /// <summary>The parameter screen: a two-column table of the saved parameters, continued on further screens when it does not fit one.</summary>
    public const string ScreenParams = "params";

    /// <summary>The size screen: the picture with the saved size line under it.</summary>
    public const string ScreenSize = "size";

    /// <summary>One screen kind with the areas it draws into: the picture (zero size when the screen has none), the heading and the body.</summary>
    public sealed record class Areas(SellingImageRules.Box Image, SellingImageRules.Box Heading, SellingImageRules.Box Body, float HeadingSize, float BodySize);

    /// <summary>Whether a product's info gives detail screens anything to show: selling points, parameters or a size line (the display name alone does not).</summary>
    public static bool HasContent(ProductSpecs specs) =>
        specs is not null && (specs.Points.Count > 0 || specs.Params.Count > 0 || !string.IsNullOrWhiteSpace(specs.Size));

    /// <summary>The screen kinds a product's info makes, in page order (T100): points, then parameters, then size — each only when that info was filled in.</summary>
    public static List<string> ScreenKinds(ProductSpecs specs)
    {
        List<string> kinds = [];
        if (specs is null)
        {
            return kinds;
        }
        if (specs.Points.Count > 0)
        {
            kinds.Add(ScreenPoints);
        }
        if (specs.Params.Count > 0)
        {
            kinds.Add(ScreenParams);
        }
        if (!string.IsNullOrWhiteSpace(specs.Size))
        {
            kinds.Add(ScreenSize);
        }
        return kinds;
    }

    /// <summary>The areas of a screen of the given size. Sizes scale with the screen's width (detail screens are portrait), so every platform looks alike.</summary>
    public static Areas Layout(string kind, int width, int height)
    {
        float w = width, h = height;
        float unit = Math.Min(w, h);
        float margin = unit * 0.06f;
        SellingImageRules.Box none = new(0, 0, 0, 0);
        return kind switch
        {
            ScreenParams => new Areas(none, new(margin, h * 0.04f, w - margin * 2, unit * 0.11f), new(margin, h * 0.04f + unit * 0.15f, w - margin * 2, h * 0.92f - unit * 0.15f),
                unit * 0.065f, unit * 0.036f),
            ScreenSize => new Areas(new(0, 0, w, h * 0.7f), new(margin, h * 0.73f, w - margin * 2, unit * 0.1f), new(margin, h * 0.73f + unit * 0.12f, w - margin * 2, h * 0.24f - unit * 0.12f),
                unit * 0.06f, unit * 0.045f),
            _ => new Areas(new(0, 0, w, h * 0.55f), new(margin, h * 0.58f, w - margin * 2, unit * 0.12f), new(margin, h * 0.58f + unit * 0.14f, w - margin * 2, h * 0.4f - unit * 0.14f),
                unit * 0.065f, unit * 0.042f)
        };
    }

    /// <summary>Splits table rows into screens (T100): rows keep their order and are never split; each screen takes rows while their heights fit the body's
    /// height. A single row taller than a whole body still gets a screen of its own. Returns each screen's first row and row count.</summary>
    public static List<(int First, int Count)> PageRows(IReadOnlyList<float> rowHeights, float available)
    {
        List<(int First, int Count)> pages = [];
        int first = 0;
        float used = 0;
        for (int index = 0; index < rowHeights.Count; index++)
        {
            if (index > first && used + rowHeights[index] > available)
            {
                pages.Add((first, index - first));
                first = index;
                used = 0;
            }
            used += rowHeights[index];
        }
        if (rowHeights.Count > first)
        {
            pages.Add((first, rowHeights.Count - first));
        }
        return pages;
    }

    /// <summary>The fixed screen headings in the page language: labels written by the program, never by AI.</summary>
    public static string Heading(string kind, string language) => (kind, language == "en") switch
    {
        (ScreenParams, true) => "Specifications",
        (ScreenParams, false) => "产品参数",
        (ScreenSize, true) => "Size",
        (ScreenSize, false) => "尺寸",
        _ => ""
    };
}
