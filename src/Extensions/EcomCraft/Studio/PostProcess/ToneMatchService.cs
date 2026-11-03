using System.IO;
using EcomCraftStudio.Studio.Export;
using EcomCraftStudio.Studio.Storage;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using ISImage = SixLabors.ImageSharp.Image;
using SixLabors.ImageSharp.PixelFormats;
using SwarmUI.Accounts;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio.PostProcess;

/// <summary>"Match the tone" for a set (F78, T101): the user picks one of their works as the base, and every other picked work gets a local colour transfer
/// toward it (<see cref="ToneMatchRules"/>) — no generation, the product's area untouched. Each result is saved as a new work next to the original, which is
/// never changed; pictures that cannot be matched safely are skipped with a reason.</summary>
[API.APIClass("EcomCraft tone matching: move a set's colour tone toward one picked work, locally and without touching the product.")]
public static class ToneMatchService
{
    [API.APIDescription("Match the colour tone of the current account's works to one of them (T101): base (a gallery path or result URL of the account's own work), images (up to 60 of the account's own works) and optional strength (0–1, default 0.6). Each image becomes a new work with its own metadata plus a tone-match marker and the base's URL; the originals are unchanged. The product's area (from the work's placement metadata) is never changed; works without a known product position, made without generation, or carrying drawn text are skipped with a reason code. rawInput is the root request JSON.", "results: [{ image: \"…\", url: \"View/user/ecomcraft/tone/…png\", skipped: null }]")]
    public static async Task<JObject> EcomCraftStudioToneMatch(Session session, JObject rawInput)
    {
        if (!StudioApi.CanView(session) || !session.User.HasPermission(Permissions.BasicImageGeneration))
        {
            return StudioApi.Error("此账号没有生成权限。", "bad_permissions");
        }
        string baseUrl = OwnUrl(session, rawInput["base"]);
        string basePath = baseUrl is null ? null : WorkSync.LocalPathFor(session.User, baseUrl, out _);
        if (basePath is null || !File.Exists(basePath))
        {
            return StudioApi.Error("基准图不存在或不属于当前账号，请刷新后重新选择。", "invalid_reference");
        }
        JArray items = rawInput["images"] as JArray ?? new JArray();
        if (items.Count == 0 || items.Count > ToneMatchRules.MaxImages)
        {
            return StudioApi.Error($"请选择 1–{ToneMatchRules.MaxImages} 张要统一色调的作品。", "invalid_parameters");
        }
        double? requested = rawInput["strength"]?.Type is JTokenType.Float or JTokenType.Integer ? rawInput.Value<double>("strength") : null;
        if (requested is double asked && (asked < 0 || asked > 1))
        {
            return StudioApi.Error("强度须在 0–1 之间。", "invalid_parameters");
        }
        double strength = ToneMatchRules.Strength(requested);
        List<(string Url, string Path)> targets = [];
        foreach (JToken item in items)
        {
            string url = OwnUrl(session, item);
            string path = url is null ? null : WorkSync.LocalPathFor(session.User, url, out _);
            if (path is null || !File.Exists(path))
            {
                return StudioApi.Error("有作品不存在或不属于当前账号，请刷新后重新选择。", "invalid_reference");
            }
            targets.Add((url, path));
        }
        try
        {
            JArray results = await Task.Run(() => Run(session.User, baseUrl, basePath, targets, strength));
            if (results is null)
            {
                return StudioApi.Error("基准图上没有足够的背景可以取色（商品占满了画面），请换一张基准图。", "invalid_reference");
            }
            JArray made = [.. results.OfType<JObject>().Where(item => item["url"]?.Type == JTokenType.String).Select(item => item["url"])];
            // The matched copies are candidates like any other result (F39); the base is clearly wanted, so it is kept.
            CandidateCleanup.Register(session.User, made);
            CandidateCleanup.MarkKept(session.User, new JArray(baseUrl));
            return new JObject() { ["results"] = results };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            Logs.Warning($"EcomCraft could not match a tone: {ex.Message}");
            return StudioApi.Error("统一色调没有完成，请稍后重试。", "generation_failed");
        }
    }

    /// <summary>A request entry as the account's own work URL, or null when it is not a string.</summary>
    private static string OwnUrl(Session session, JToken token)
    {
        string url = token?.Type == JTokenType.String ? token.ToString() : null;
        if (url is not null && !url.StartsWith("View/", StringComparison.Ordinal) && !url.StartsWith("Output/", StringComparison.Ordinal))
        {
            url = $"View/{session.User.UserID}/{url}";
        }
        return url;
    }

    /// <summary>Matches every target to the base's tone; one entry per target in request order, each with the new work's URL or the reason it was skipped
    /// (one picture failing never stops the others). Null when the base itself has too little background to measure.</summary>
    private static JArray Run(User user, string baseUrl, string basePath, List<(string Url, string Path)> targets, double strength)
    {
        ToneMatchRules.LabStats baseStats;
        using (Image<Rgba32> basePicture = ISImage.Load<Rgba32>(basePath))
        {
            Placement baseProduct = ComplianceRules.ProductPlacement(ExportService.MetadataText(basePicture));
            Rectangle box = baseProduct is null ? Rectangle.Empty : ToneMatchRules.ProductBox(baseProduct, basePicture.Width, basePicture.Height);
            baseStats = ToneMatchRules.Stats(basePicture, box, ToneMatchRules.Feather(basePicture.Width, basePicture.Height));
        }
        if (baseStats is null)
        {
            return null;
        }
        JArray results = [];
        foreach ((string url, string path) in targets)
        {
            JObject entry = new() { ["image"] = url, ["url"] = null, ["skipped"] = null };
            results.Add(entry);
            if (url == baseUrl)
            {
                entry["skipped"] = "is_base";
                continue;
            }
            try
            {
                using Image<Rgba32> picture = ISImage.Load<Rgba32>(path);
                string metadata = ExportService.MetadataText(picture);
                if (ToneMatchRules.SkipReason(metadata) is string reason)
                {
                    entry["skipped"] = reason;
                    continue;
                }
                Placement product = ComplianceRules.ProductPlacement(metadata);
                Rectangle box = ToneMatchRules.ProductBox(product, picture.Width, picture.Height);
                double feather = ToneMatchRules.Feather(picture.Width, picture.Height);
                ToneMatchRules.LabStats stats = ToneMatchRules.Stats(picture, box, feather);
                if (stats is null)
                {
                    entry["skipped"] = "no_background";
                    continue;
                }
                ToneMatchRules.Apply(picture, stats, baseStats, strength, box, feather);
                picture.Metadata.ExifProfile = null;
                picture.Metadata.GetPngMetadata().TextData.Clear();
                entry["url"] = SellingImageService.SaveWork(user, picture, Marked(metadata, baseUrl), "tone");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
            {
                Logs.Warning($"EcomCraft could not match the tone of one work: {ex.Message}");
                entry["skipped"] = "unreadable";
            }
        }
        return results;
    }

    /// <summary>The work's own metadata with the tone-match markers: that it was matched, and the base it was matched to (its gallery URL, never a file path).</summary>
    private static string Marked(string original, string baseUrl)
    {
        string metadata = SellingImageService.Metadata(original, "ecomcraft_tone_match");
        JObject parsed = JObject.Parse(metadata);
        ((JObject)parsed["sui_extra_data"])["ecomcraft_tone_base"] = baseUrl;
        return parsed.ToString(Formatting.None);
    }
}
