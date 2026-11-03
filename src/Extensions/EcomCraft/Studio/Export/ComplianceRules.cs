using System.Security;
using EcomCraftStudio.Studio.PostProcess;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace EcomCraftStudio.Studio.Export;

/// <summary>Compliance rules (F22), independent of SwarmUI so they can be unit tested: the pure-white background check for main images and the
/// AI provenance metadata written on export, based on the IPTC digital source type vocabulary.</summary>
public static class ComplianceRules
{
    /// <summary>IPTC digital source type of pictures made by a generative model.</summary>
    public const string TrainedAlgorithmicMedia = "http://cv.iptc.org/newscodes/digitalsourcetype/trainedAlgorithmicMedia";

    /// <summary>IPTC digital source type of pictures that combine captured content (the real product) with generated content.</summary>
    public const string CompositeWithTrainedAlgorithmicMedia = "http://cv.iptc.org/newscodes/digitalsourcetype/compositeWithTrainedAlgorithmicMedia";

    /// <summary>Name written as the creating software.</summary>
    public const string CreatorTool = "EcomCraft Studio";

    /// <summary>How a work was made, from the markers EcomCraft writes into its metadata: "cutout" (no generation — the original photo on white, or a
    /// real crop of the user's own photo, T98), "composite" (original product pixels pasted onto a generated picture) or "generated" (everything else,
    /// the safe default).</summary>
    public static string SourceKind(string metadataText)
    {
        string text = metadataText ?? "";
        if (text.Contains("\"ecomcraft_cutout_only\"", StringComparison.Ordinal) || text.Contains("\"ecomcraft_photo_crop\"", StringComparison.Ordinal))
        {
            return "cutout";
        }
        if (text.Contains("\"ecomcraft_preserve\"", StringComparison.Ordinal))
        {
            return "composite";
        }
        return "generated";
    }

    /// <summary>The IPTC digital source type for a source kind, or null when no AI marking applies.</summary>
    public static string DigitalSourceType(string kind) => kind switch
    {
        "generated" => TrainedAlgorithmicMedia,
        "composite" => CompositeWithTrainedAlgorithmicMedia,
        _ => null
    };

    /// <summary>The human-readable description written with the marking, or null when none applies.</summary>
    public static string Description(string kind) => kind switch
    {
        "generated" => "AI-generated image (EcomCraft Studio)",
        "composite" => "Composite with AI-generated background (EcomCraft Studio)",
        _ => null
    };

    /// <summary>An XMP packet with the digital source type, description and creator tool.</summary>
    public static string Xmp(string digitalSourceType, string description)
    {
        string type = SecurityElement.Escape(digitalSourceType);
        string text = SecurityElement.Escape(description);
        return "<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>"
            + "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">"
            + "<rdf:Description rdf:about=\"\" xmlns:Iptc4xmpExt=\"http://iptc.org/std/Iptc4xmpExt/2008-02-29/\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\""
            + $" Iptc4xmpExt:DigitalSourceType=\"{type}\" xmp:CreatorTool=\"{CreatorTool}\">"
            + $"<dc:description><rdf:Alt><rdf:li xml:lang=\"x-default\">{text}</rdf:li></rdf:Alt></dc:description>"
            + "</rdf:Description></rdf:RDF></x:xmpmeta><?xpacket end=\"w\"?>";
    }

    /// <summary>Fraction of pixels in the outer band of the picture (each side <paramref name="band"/> of the size, at least one pixel) that are not pure white (255, 255, 255).
    /// Main images on platforms such as Amazon need this to be zero.</summary>
    public static double NonWhiteBorderFraction(Image<Rgb24> image, double band = 0.02)
    {
        int bandX = Math.Max(1, (int)(image.Width * band));
        int bandY = Math.Max(1, (int)(image.Height * band));
        long total = 0;
        long nonWhite = 0;
        image.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < rows.Height; y++)
            {
                Span<Rgb24> row = rows.GetRowSpan(y);
                bool edgeRow = y < bandY || y >= rows.Height - bandY;
                for (int x = 0; x < row.Length; x++)
                {
                    if (!edgeRow && x >= bandX && x < row.Length - bandX)
                    {
                        continue;
                    }
                    total++;
                    Rgb24 pixel = row[x];
                    if (pixel.R != 255 || pixel.G != 255 || pixel.B != 255)
                    {
                        nonWhite++;
                    }
                }
            }
        });
        return total == 0 ? 0 : (double)nonWhite / total;
    }

    /// <summary>Fraction of the whole picture that is not pure white (255, 255, 255) — an estimate of how much of a white-background slot the product fills (F58).
    /// A hint only: scene pictures embed the product in scenery, so the check is used for white slots.</summary>
    public static double SubjectFillFraction(Image<Rgb24> image)
    {
        long total = 0;
        long nonWhite = 0;
        image.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < rows.Height; y++)
            {
                Span<Rgb24> row = rows.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    total++;
                    Rgb24 pixel = row[x];
                    if (pixel.R != 255 || pixel.G != 255 || pixel.B != 255)
                    {
                        nonWhite++;
                    }
                }
            }
        });
        return total == 0 ? 0 : (double)nonWhite / total;
    }

    /// <summary>Whether a platform's rules forbid promotional text, so images carrying drawn text (selling-point images, F47) get an export hint (F58).</summary>
    public static bool ForbidsPromoText(JObject platform)
    {
        return (platform?["forbidden"] as JArray)?.Any(item => item?.Type == JTokenType.String && item.ToString() == "promo_text") == true;
    }

    /// <summary>Whether a slot kind's pictures carry server-drawn text by definition (T97): selling-point images do (F47). Detail screens can be textless, so they
    /// warn through their own marker when text is drawn. Together with <see cref="ForbidsPromoText"/> this hints before exporting to platforms that forbid promotional text.</summary>
    public static bool CarriesDrawnText(string slotKind) => slotKind == "selling";

    /// <summary>The share of the canvas's shorter side a cut-out-only white main image gives the product (F22): mirrors CutoutOnly.ProductSize, kept here so the
    /// label rule stays free of SwarmUI dependencies and can be unit tested.</summary>
    public const double CutoutOnlyProductSize = 0.85;

    /// <summary>The visible AI label rule of an export (F49): whether a drawn label is added by default, its text per language, its preferred corner, its font size
    /// as a share of the canvas's shorter side, its opacity and whether the user may turn it off. Every shipped value stays unverified (verified: false) until the
    /// Chinese AI-labelling regulation and each platform's rules are checked against their official texts; the conservative default is off.</summary>
    public sealed record class VisibleLabelRule(bool Enabled, Dictionary<string, string> Text, string Position, double FontRatio, double Opacity, bool UserCanDisable)
    {
        /// <summary>Valid label positions: one of the four corners, or "auto" for the corner farthest from the product.</summary>
        public static readonly string[] Positions = ["auto", "top_left", "top_right", "bottom_left", "bottom_right"];

        /// <summary>The shipped default (F49): off until the rules are checked, a short Chinese and English text, an automatic corner, 3% of the shorter side,
        /// 60% opacity and free for the user to turn off.</summary>
        public static VisibleLabelRule Default { get; } = new(false, new Dictionary<string, string>() { ["zh-CN"] = "AI 生成", ["en"] = "AI generated" }, "auto", 0.03, 0.6, true);

        /// <summary>Whether the rule forces the label on: it is enabled and the user cannot turn it off.</summary>
        public bool ForcedOn => Enabled && !UserCanDisable;

        /// <summary>Whether the rule forces the label off: it is disabled and the user cannot turn it on (for example a slot whose platform rules forbid it).</summary>
        public bool ForcedOff => !Enabled && !UserCanDisable;

        /// <summary>The label text for a language: that language's own text, else the Chinese text, else the first text there is.</summary>
        public string TextFor(string language)
        {
            if (!string.IsNullOrEmpty(language) && Text.TryGetValue(language, out string exact) && !string.IsNullOrWhiteSpace(exact))
            {
                return exact;
            }
            if (Text.TryGetValue("zh-CN", out string chinese) && !string.IsNullOrWhiteSpace(chinese))
            {
                return chinese;
            }
            foreach (string value in Text.Values.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                return value;
            }
            return "AI 生成";
        }
    }

    /// <summary>The visible AI label rule that applies to one slot (F49): the shipped global default overlaid with the platform's "ai_label"."visible" object and
    /// then the slot's own, so a platform can differ from the global default and one slot (a white main image, say) from its platform. Each field of each layer is
    /// checked on its own, so a broken or out-of-range value falls back to the layer below instead of failing the export.</summary>
    public static VisibleLabelRule VisibleLabel(JObject root, JObject platform, JObject slot)
    {
        return Overlay(Overlay(Overlay(VisibleLabelRule.Default, root?["ai_label"] as JObject), platform?["ai_label"] as JObject), slot?["ai_label"] as JObject);
    }

    /// <summary>Applies one layer's "ai_label"."visible" object onto a rule (F49): every field is read on its own, so a broken value keeps the layer below's.
    /// A layer without a "visible" object changes nothing.</summary>
    private static VisibleLabelRule Overlay(VisibleLabelRule current, JObject aiLabel)
    {
        JObject visible = aiLabel?["visible"] as JObject;
        if (visible is null)
        {
            return current;
        }
        bool enabled = visible["enabled"]?.Type == JTokenType.Boolean ? visible.Value<bool>("enabled") : current.Enabled;
        bool userCanDisable = visible["user_can_disable"]?.Type == JTokenType.Boolean ? visible.Value<bool>("user_can_disable") : current.UserCanDisable;
        string position = visible["position"]?.Type == JTokenType.String && VisibleLabelRule.Positions.Contains(visible.Value<string>("position")) ? visible.Value<string>("position") : current.Position;
        double fontRatio = Number(visible, "font_ratio") is double ratio && ratio > 0 && ratio <= 0.2 ? ratio : current.FontRatio;
        double opacity = Number(visible, "opacity") is double alpha && alpha > 0 && alpha <= 1 ? alpha : current.Opacity;
        Dictionary<string, string> text = current.Text;
        if (visible["text"] is JObject rawText)
        {
            Dictionary<string, string> parsed = [];
            foreach (JProperty entry in rawText.Properties())
            {
                string value = entry.Value?.Type == JTokenType.String ? entry.Value.ToString().Trim() : "";
                if (entry.Name.Length > 0 && entry.Name.Length <= 16 && value.Length > 0 && value.Length <= 40)
                {
                    parsed[entry.Name] = value;
                }
            }
            if (parsed.Count > 0)
            {
                text = parsed;
            }
        }
        return current with { Enabled = enabled, UserCanDisable = userCanDisable, Position = position, FontRatio = fontRatio, Opacity = opacity, Text = text };
    }

    /// <summary>A JSON value as a number, or null when it is not one.</summary>
    private static double? Number(JObject obj, string name) => obj[name]?.Type is JTokenType.Float or JTokenType.Integer ? obj.Value<double>(name) : null;

    /// <summary>Placement of the product pasted into a work, read from the work's own markers (F49): the resolved placement a product-preserving generation
    /// kept (sui_extra_data.ecomcraft_placement — a placement object, or the same object as compact JSON text, because SwarmUI stores extra metadata values as
    /// strings), or the centered layout of a cut-out-only white main image. Older works, reference mode and the fusion trial say nothing about where their
    /// product sits and give null, so the label falls back to its rule's corner.</summary>
    public static Placement ProductPlacement(string metadataText)
    {
        bool cutout = metadataText?.Contains("\"ecomcraft_cutout_only\"", StringComparison.Ordinal) == true;
        JObject metadata;
        try
        {
            metadata = string.IsNullOrWhiteSpace(metadataText) ? null : JObject.Parse(metadataText);
        }
        catch (JsonException)
        {
            metadata = null;
        }
        JObject stored = metadata?["sui_extra_data"]?["ecomcraft_placement"] as JObject;
        if (stored is null && metadata?["sui_extra_data"]?["ecomcraft_placement"]?.Type == JTokenType.String)
        {
            try
            {
                stored = JObject.Parse(metadata["sui_extra_data"]["ecomcraft_placement"].ToString());
            }
            catch (JsonException)
            {
                stored = null;
            }
        }
        if (stored is not null && Placement.TryCreate(stored.Value<double?>("x"), stored.Value<double?>("y"), stored.Value<double?>("size"), out Placement placement))
        {
            return placement;
        }
        return cutout ? new Placement(0.5, 0.5, CutoutOnlyProductSize) : null;
    }
}
