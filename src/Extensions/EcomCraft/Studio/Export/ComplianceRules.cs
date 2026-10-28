using System.Security;
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

    /// <summary>How a work was made, from the markers EcomCraft writes into its metadata: "cutout" (original photo on white, no generation),
    /// "composite" (original product pixels pasted onto a generated picture) or "generated" (everything else, the safe default).</summary>
    public static string SourceKind(string metadataText)
    {
        string text = metadataText ?? "";
        if (text.Contains("\"ecomcraft_cutout_only\"", StringComparison.Ordinal))
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
}
