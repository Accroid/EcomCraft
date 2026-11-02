using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EcomCraftStudio.Studio.Setup;

/// <summary>Model recipes (F86 / T117): the "model + hardware variant" two-layer format. A recipe declares how one model runs — blueprint files
/// per workflow kind, sampling (steps, cfg range with hints, sampler), the second repainting pass, resolution budget, size step and edge cap,
/// prompt syntax, capabilities and license — and its variants declare which files (with sources and checksums) serve which operating system,
/// GPU vendor and hardware tier. Pure rules with no file or SwarmUI access, so the unit tests build them alone: everything parses from text and
/// falls back per value (a broken recipe is skipped, a broken number falls back to a safe default) without touching the running configuration.</summary>
public static class ModelRecipeRules
{
    /// <summary>File role: the diffusion model an <c>UnetLoaderGGUF</c>-style loader reads.</summary>
    public const string RoleUnet = "unet";

    /// <summary>File role: the text encoder a <c>CLIPLoader</c>-style loader reads.</summary>
    public const string RoleTextEncoder = "text_encoder";

    /// <summary>File role: the VAE a <c>VAELoader</c>-style loader reads.</summary>
    public const string RoleVae = "vae";

    /// <summary>Default sampling steps when the file does not give a usable whole number.</summary>
    private const int DefaultSteps = 20;

    /// <summary>Upper bound of sampling steps, so a mistyped recipe cannot ask for endless sampling.</summary>
    private const int MaxSteps = 150;

    /// <summary>Default steps of the second (harmonize) pass.</summary>
    private const int DefaultSecondPassSteps = 10;

    /// <summary>Upper bound of the second pass's steps.</summary>
    private const int MaxSecondPassSteps = 50;

    /// <summary>Default cfg bounds when the file does not give usable numbers.</summary>
    private const double DefaultCfg = 1, DefaultCfgMin = 1, DefaultCfgMax = 4;

    /// <summary>Upper bound of cfg values.</summary>
    private const double MaxCfg = 30;

    /// <summary>Default latent size step (multiples of 16 for Qwen-style latents).</summary>
    private const int DefaultSizeStep = 16;

    /// <summary>Bounds of the latent size step.</summary>
    private const int MinSizeStep = 8, MaxSizeStep = 128;

    /// <summary>Default longest-edge cap of generated and exported images (A24: 2K today, changes with the recipe).</summary>
    private const int DefaultMaxEdge = 2048;

    /// <summary>Bounds of the longest-edge cap.</summary>
    private const int MinMaxEdge = 256, MaxMaxEdge = 8192;

    /// <summary>Default encoder pixel budget (F73: about one million pixels).</summary>
    private const long DefaultResolutionBudget = 1024 * 1024;

    /// <summary>Bounds of the encoder pixel budget.</summary>
    private const long MinResolutionBudget = 256 * 256, MaxResolutionBudget = 4096 * 4096;

    /// <summary>Default and bounds of how many images one request may feed the encoder.</summary>
    private const int DefaultMaxInputImages = 1, MaxMaxInputImages = 16;

    /// <summary>Upper bound of a variant's disk hint in gigabytes; 0 means unknown.</summary>
    private const long MaxDiskGbHint = 10240;

    /// <summary>A bilingual label: the Chinese and English text, either may be empty when the file only gives one.</summary>
    /// <param name="ZhCn">The Simplified Chinese text, or "".</param>
    /// <param name="En">The English text, or "".</param>
    public sealed record class LocalizedText(string ZhCn, string En);

    /// <summary>One download location of a file: a region label ("cn" for a mirror reachable in China, "global" for the official source) and its URL.</summary>
    /// <param name="Region">The region label, normalized to lower case; "" when the file does not give one.</param>
    /// <param name="Url">The download URL, or "".</param>
    public sealed record class Source(string Region, string Url);

    /// <summary>One file a variant needs, in the folder ComfyUI reads for its loader.</summary>
    /// <param name="Role">One of <see cref="RoleUnet"/>, <see cref="RoleTextEncoder"/>, <see cref="RoleVae"/>, or a future role.</param>
    /// <param name="Folder">The model folder relative to the model root, such as "unet" or "VAE".</param>
    /// <param name="Filename">The file name inside the folder; never empty on a kept entry.</param>
    /// <param name="Bytes">The expected size in bytes; 0 when unknown.</param>
    /// <param name="Sha256">The expected SHA-256 in lower-case hex; "" when unknown or not 64 hex characters.</param>
    /// <param name="Sources">Download locations; may be empty when the file is already on disk and no source is recorded.</param>
    public sealed record class FileSpec(string Role, string Folder, string Filename, long Bytes, string Sha256, List<Source> Sources);

    /// <summary>One hardware variant of a recipe: which files it uses and which machines it is meant for. The targeting only guides
    /// recommendations (A19): it never blocks a run, and empty lists mean "any".</summary>
    /// <param name="Id">The variant id, unique inside its recipe.</param>
    /// <param name="Name">The variant's display name.</param>
    /// <param name="Os">Operating systems ("windows", "linux", "macos"); empty means any.</param>
    /// <param name="Gpu">GPU vendors ("nvidia", "amd", "apple", ...); empty means any.</param>
    /// <param name="Tier">Hardware tiers the variant is recommended for (HardwareRules tier names); empty means any.</param>
    /// <param name="DiskGbHint">Rough disk space the files need, in gigabytes; 0 means unknown.</param>
    /// <param name="Files">The variant's files, at most one per role (extra files with a repeated role are dropped).</param>
    public sealed record class Variant(string Id, LocalizedText Name, List<string> Os, List<string> Gpu, List<string> Tier, long DiskGbHint, List<FileSpec> Files);

    /// <summary>The cfg (prompt strength) rule: its default and bounds with the hint text the page shows.</summary>
    /// <param name="Default">The default cfg, kept inside [Min, Max].</param>
    /// <param name="Min">The lowest useful cfg.</param>
    /// <param name="Max">The highest useful cfg.</param>
    /// <param name="Hint">Why these values, shown with the control.</param>
    public sealed record class CfgRule(double Default, double Min, double Max, LocalizedText Hint);

    /// <summary>How the model is sampled.</summary>
    /// <param name="Steps">The recipe's step count (the Qwen base model uses 25, its Turbo distillation 8).</param>
    /// <param name="Cfg">The cfg rule with bounds and hint.</param>
    /// <param name="NegativeHint">The note about the negative prompt at cfg 1.</param>
    /// <param name="Sampler">The sampler name, such as "euler"; "" when the file does not give one.</param>
    /// <param name="Scheduler">The scheduler name, such as "simple"; "" when the file does not give one.</param>
    public sealed record class SamplingRule(int Steps, CfgRule Cfg, LocalizedText NegativeHint, string Sampler, string Scheduler);

    /// <summary>The second repainting pass (the standard workflows' harmonize pass over the whole picture).</summary>
    /// <param name="Steps">The pass's step count.</param>
    public sealed record class SecondPassRule(int Steps);

    /// <summary>What the model can do; the page hides entries a recipe does not support.</summary>
    /// <param name="Edit">Whether the model can edit from reference images.</param>
    /// <param name="AlphaChannel">Whether the model can output an alpha channel.</param>
    /// <param name="MaxInputImages">How many images one request may feed the encoder (a reference plus angle photos).</param>
    public sealed record class CapabilityRule(bool Edit, bool AlphaChannel, int MaxInputImages);

    /// <summary>One model recipe: everything about running one model, plus its hardware variants.</summary>
    /// <param name="Id">The recipe id, unique in the book (the first entry wins when a file repeats one).</param>
    /// <param name="Name">The display name.</param>
    /// <param name="License">The license note shown to administrators; EcomCraft itself claims nothing beyond it (A31).</param>
    /// <param name="MinComfyUI">The lowest ComfyUI version the recipe is known to work on; "" when unknown.</param>
    /// <param name="Nodes">The ComfyUI node classes the recipe needs.</param>
    /// <param name="Blueprints">Workflow kind ("scene", "detail", "reference-edit", ...) to blueprint file; kinds without a blueprint cannot run that way.</param>
    /// <param name="Sampling">The sampling rule.</param>
    /// <param name="SecondPass">The second pass rule.</param>
    /// <param name="ResolutionBudget">The encoder's pixel budget per image.</param>
    /// <param name="SizeStep">Widths and heights must be multiples of this.</param>
    /// <param name="MaxEdge">The longest edge generated and exported images may have.</param>
    /// <param name="PromptSyntax">How images are referenced in prompts, such as "&lt;image1&gt;"; "" when plain text.</param>
    /// <param name="Capabilities">The capability flags.</param>
    /// <param name="Variants">The hardware variants, in file order; the first matching one wins.</param>
    public sealed record class Recipe(string Id, LocalizedText Name, string License, string MinComfyUI, List<string> Nodes,
        Dictionary<string, string> Blueprints, SamplingRule Sampling, SecondPassRule SecondPass, long ResolutionBudget, int SizeStep, int MaxEdge,
        string PromptSyntax, CapabilityRule Capabilities, List<Variant> Variants);

    /// <summary>Parses a recipe book from model-recipes.json text. Unreadable text, a missing "models" array, or an entry without an id yields
    /// no recipes; every other bad value falls back on its own, so one broken recipe never hides the rest and the running configuration is
    /// never touched (a corrupt file simply looks like no recipes).</summary>
    public static List<Recipe> Parse(string json)
    {
        List<Recipe> recipes = [];
        if (string.IsNullOrWhiteSpace(json))
        {
            return recipes;
        }
        try
        {
            JObject root = JObject.Parse(json);
            foreach (JToken entry in root["models"] as JArray ?? new JArray())
            {
                Recipe recipe = ParseRecipe(entry as JObject);
                if (recipe is not null && !recipes.Any(item => item.Id == recipe.Id))
                {
                    recipes.Add(recipe);
                }
            }
        }
        catch (JsonException)
        {
            return [];
        }
        return recipes;
    }

    /// <summary>Finds a recipe by id, case-insensitively; null when the book does not have it.</summary>
    public static Recipe Find(IEnumerable<Recipe> recipes, string id)
    {
        string wanted = HardwareRules.Normalize(id ?? "");
        foreach (Recipe recipe in recipes ?? [])
        {
            if (recipe.Id == wanted)
            {
                return recipe;
            }
        }
        return null;
    }

    /// <summary>The first variant of the recipe whose targeting accepts the machine: each list must be empty (any) or contain the given value,
    /// compared case-insensitively. The targeting only guides the choice (A19); null when no variant matches.</summary>
    /// <param name="recipe">The recipe to look in.</param>
    /// <param name="os">The operating system, such as "windows"; ignored when empty.</param>
    /// <param name="gpuVendor">The GPU vendor, such as "nvidia"; ignored when empty.</param>
    /// <param name="tier">The hardware tier (a HardwareRules tier name); ignored when empty.</param>
    public static Variant Match(Recipe recipe, string os, string gpuVendor, string tier)
    {
        foreach (Variant variant in recipe?.Variants ?? [])
        {
            if (Allows(variant.Os, os) && Allows(variant.Gpu, gpuVendor) && Allows(variant.Tier, tier))
            {
                return variant;
            }
        }
        return null;
    }

    /// <summary>The variant's file for one role, or null when it has none.</summary>
    public static FileSpec FileFor(Variant variant, string role)
    {
        string wanted = HardwareRules.Normalize(role ?? "");
        foreach (FileSpec file in variant?.Files ?? [])
        {
            if (HardwareRules.Normalize(file.Role) == wanted)
            {
                return file;
            }
        }
        return null;
    }

    /// <summary>Whether a targeting list accepts a value: an empty list means "any", otherwise the value must appear in it (case-insensitive).</summary>
    private static bool Allows(List<string> list, string value)
    {
        if (list is null || list.Count == 0)
        {
            return true;
        }
        string wanted = HardwareRules.Normalize(value ?? "");
        foreach (string entry in list)
        {
            if (entry == wanted)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Parses one recipe entry; null when it has no usable id. Every other bad value falls back per field.</summary>
    private static Recipe ParseRecipe(JObject entry)
    {
        if (entry is null)
        {
            return null;
        }
        string id = HardwareRules.Normalize(entry["id"]?.ToString() ?? "");
        if (id.Length == 0)
        {
            return null;
        }
        JObject samplingEntry = entry["sampling"] as JObject ?? new JObject();
        JObject cfgEntry = samplingEntry["cfg"] as JObject ?? new JObject();
        double cfgMin = BoundedDecimal(cfgEntry["min"], DefaultCfgMin, 0, MaxCfg);
        double cfgMax = BoundedDecimal(cfgEntry["max"], DefaultCfgMax, 0, MaxCfg);
        if (cfgMin > cfgMax)
        {
            (cfgMin, cfgMax) = (cfgMax, cfgMin);
        }
        double cfgDefault = Math.Clamp(BoundedDecimal(cfgEntry["default"], DefaultCfg, 0, MaxCfg), cfgMin, cfgMax);
        JObject capabilities = entry["capabilities"] as JObject ?? new JObject();
        return new Recipe(
            id,
            ParseText(entry["name"]),
            (entry["license"]?.ToString() ?? "").Trim(),
            (entry["min_comfyui"]?.ToString() ?? "").Trim(),
            ParseList(entry["nodes"]),
            ParseMap(entry["blueprints"]),
            new SamplingRule(
                StepCount(samplingEntry["steps"], DefaultSteps, MaxSteps),
                new CfgRule(cfgDefault, cfgMin, cfgMax, ParseText(cfgEntry["hint"])),
                ParseText(samplingEntry["negative_hint"]),
                (samplingEntry["sampler"]?.ToString() ?? "").Trim(),
                (samplingEntry["scheduler"]?.ToString() ?? "").Trim()),
            new SecondPassRule(StepCount((entry["second_pass"] as JObject ?? new JObject())["steps"], DefaultSecondPassSteps, MaxSecondPassSteps)),
            BoundedWhole(entry["resolution_budget"], DefaultResolutionBudget, MinResolutionBudget, MaxResolutionBudget),
            (int)BoundedWhole(entry["size_step"], DefaultSizeStep, MinSizeStep, MaxSizeStep),
            (int)BoundedWhole(entry["max_edge"], DefaultMaxEdge, MinMaxEdge, MaxMaxEdge),
            (entry["prompt_syntax"]?.ToString() ?? "").Trim(),
            new CapabilityRule(
                capabilities["edit"]?.Value<bool?>() ?? false,
                capabilities["alpha_channel"]?.Value<bool?>() ?? false,
                (int)BoundedWhole(capabilities["max_input_images"], DefaultMaxInputImages, 1, MaxMaxInputImages)),
            ParseVariants(entry["variants"] as JArray));
    }

    /// <summary>Parses the variant list: entries without a usable id are skipped, and a variant keeps at most one file per role.</summary>
    private static List<Variant> ParseVariants(JArray array)
    {
        List<Variant> variants = [];
        foreach (JToken entry in array ?? new JArray())
        {
            if (entry is not JObject variantEntry)
            {
                continue;
            }
            string id = HardwareRules.Normalize(variantEntry["id"]?.ToString() ?? "");
            if (id.Length == 0)
            {
                continue;
            }
            List<FileSpec> files = [];
            foreach (JToken fileEntry in variantEntry["files"] as JArray ?? new JArray())
            {
                if (fileEntry is not JObject fileObject)
                {
                    continue;
                }
                string filename = (fileObject["filename"]?.ToString() ?? "").Trim();
                if (filename.Length == 0)
                {
                    continue;
                }
                FileSpec file = new(
                    HardwareRules.Normalize(fileObject["role"]?.ToString() ?? ""),
                    (fileObject["folder"]?.ToString() ?? "").Trim(),
                    filename,
                    BoundedWhole(fileObject["bytes"], 0, 0, long.MaxValue),
                    ParseSha256(fileObject["sha256"]?.ToString() ?? ""),
                    ParseSources(fileObject["sources"] as JArray));
                if (!files.Any(item => HardwareRules.Normalize(item.Role) == file.Role))
                {
                    files.Add(file);
                }
            }
            variants.Add(new Variant(
                id,
                ParseText(variantEntry["name"]),
                ParseList(variantEntry["os"]),
                ParseList(variantEntry["gpu"]),
                ParseList(variantEntry["tier"]),
                BoundedWhole(variantEntry["disk_gb_hint"], 0, 0, MaxDiskGbHint),
                files));
        }
        return variants;
    }

    /// <summary>Parses a source list: entries without a URL are skipped.</summary>
    private static List<Source> ParseSources(JArray array)
    {
        List<Source> sources = [];
        foreach (JToken entry in array ?? new JArray())
        {
            if (entry is not JObject source)
            {
                continue;
            }
            string url = (source["url"]?.ToString() ?? "").Trim();
            if (url.Length == 0)
            {
                continue;
            }
            sources.Add(new Source(HardwareRules.Normalize(source["region"]?.ToString() ?? ""), url));
        }
        return sources;
    }

    /// <summary>A SHA-256 as 64 lower-case hex characters, or "" for anything else (unknown stays unknown rather than half-valid).</summary>
    private static string ParseSha256(string value)
    {
        string sha = (value ?? "").Trim().ToLowerInvariant();
        return sha.Length == 64 && sha.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f') ? sha : "";
    }

    /// <summary>A bilingual label from an object with "zh-CN" and "en" strings; anything else reads as both empty.</summary>
    private static LocalizedText ParseText(JToken token)
    {
        if (token is not JObject text)
        {
            return new LocalizedText("", "");
        }
        return new LocalizedText((text["zh-CN"]?.ToString() ?? "").Trim(), (text["en"]?.ToString() ?? "").Trim());
    }

    /// <summary>A trimmed, non-empty, lower-cased string list.</summary>
    private static List<string> ParseList(JToken token)
    {
        return [.. (token as JArray ?? new JArray()).Select(item => HardwareRules.Normalize(item?.ToString() ?? "")).Where(item => item.Length > 0)];
    }

    /// <summary>A workflow-kind to blueprint-file map; entries with an empty file name are dropped.</summary>
    private static Dictionary<string, string> ParseMap(JToken token)
    {
        Dictionary<string, string> map = [];
        if (token is not JObject obj)
        {
            return map;
        }
        foreach (JProperty property in obj.Properties())
        {
            string file = (property.Value?.ToString() ?? "").Trim();
            if (file.Length > 0)
            {
                map[HardwareRules.Normalize(property.Name)] = file;
            }
        }
        return map;
    }

    /// <summary>A whole number inside its bounds; a missing, decimal, string or otherwise unusable value — or one outside its bounds — falls
    /// back to the default, the way <see cref="HardwareRules"/> treats thresholds: a mistyped entry never silently clamps into nonsense.</summary>
    private static long BoundedWhole(JToken token, long fallback, long minValue, long maxValue)
    {
        if (token is not JValue value || value.Type != JTokenType.Integer || value.Value is not long number)
        {
            return fallback;
        }
        return number >= minValue && number <= maxValue ? number : fallback;
    }

    /// <summary>A decimal inside its bounds, falling back the same way <see cref="BoundedWhole"/> does.</summary>
    private static double BoundedDecimal(JToken token, double fallback, double minValue, double maxValue)
    {
        if (token is not JValue value || value.Type is not (JTokenType.Integer or JTokenType.Float))
        {
            return fallback;
        }
        double number = value.Value<double>();
        return number >= minValue && number <= maxValue ? number : fallback;
    }

    /// <summary>A step count: a positive whole number clamped to its upper bound; zero, negative or unusable values fall back to the default, so
    /// a corrupt count never quietly becomes the minimum and runs a useless pass.</summary>
    private static int StepCount(JToken token, int fallback, int max)
    {
        if (token is not JValue value || value.Type != JTokenType.Integer || value.Value is not long number || number <= 0)
        {
            return fallback;
        }
        return (int)Math.Min(number, max);
    }
}
