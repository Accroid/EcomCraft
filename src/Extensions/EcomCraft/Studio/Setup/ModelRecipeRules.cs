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

    /// <summary>File role reserved for a future upscaler model (T114): parseable and unit-tested already, but no business code uses it yet.</summary>
    public const string RoleUpscale = "upscale";

    /// <summary>Upper bound of sampling steps, so a mistyped recipe cannot ask for endless sampling.</summary>
    private const int MaxSteps = 150;

    /// <summary>Upper bound of the second pass's steps.</summary>
    private const int MaxSecondPassSteps = 50;

    /// <summary>Default denoise of the second (harmonize) pass.</summary>
    private const double DefaultSecondPassDenoise = 0.35;

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

    /// <summary>Bounds of the longest-edge cap: a recipe may lower it, never raise it past the 2K of A24.</summary>
    private const int MinMaxEdge = 256, MaxMaxEdge = 2048;

    /// <summary>Default encoder pixel budget (F73: about one million pixels).</summary>
    private const long DefaultResolutionBudget = 1024 * 1024;

    /// <summary>Bounds of the encoder pixel budget.</summary>
    private const long MinResolutionBudget = 256 * 256, MaxResolutionBudget = 4096 * 4096;

    /// <summary>Default and bounds of how many images one request may feed the encoder.</summary>
    private const int DefaultMaxInputImages = 1, MaxMaxInputImages = 16;

    /// <summary>Upper bound of a variant's disk hint in gigabytes; 0 means unknown.</summary>
    private const long MaxDiskGbHint = 10240;

    /// <summary>The recipe the server falls back to when the current-recipe setting is empty, invalid or unresolvable (T118): the default
    /// model, the Turbo distillation since T118b (the user's 2026-11-03 decision: the program ships on Qwen-Image 2.1 Turbo).</summary>
    public const string DefaultRecipeId = "qwen-image-2.1-turbo", DefaultVariantId = "q4-gguf";

    /// <summary>The pre-T118b default (the base model's UC quantization), still recognized by the startup migration: a server that never chose
    /// a model of its own — the setting empty, or exactly this pair written by the T118 migration of the factory file names — moves to the new
    /// default, while an administrator's own choice stays untouched (T118b).</summary>
    public const string OldDefaultRecipeId = "qwen-image-2.1", OldDefaultVariantId = "uc-q4-gguf";

    /// <summary>The factory values of the three legacy file-name settings (the Settings.cs defaults, the UC trio): the startup migration reads
    /// them as "never changed by an administrator" and moves such a server to the default recipe (T118b).</summary>
    public const string FactoryUnet = "qwen-image-2.1-UC-Q4_K_M.gguf", FactoryTextEncoder = "qwen3vl_8b_int8_convrot.safetensors",
        FactoryVae = "qwen_image_2.1_vae_bf16.safetensors";

    /// <summary>The prompt image-tag syntax used when a recipe declares none (T118): the <c>&lt;image1&gt;</c> style of the current model.</summary>
    public const string DefaultPromptSyntax = "<image1>";

    /// <summary>Default auto pixel budget of the direct-fusion trial per hardware tier, used when no recipe declares one (T118 moved the values here).</summary>
    private const int DefaultEditResolution = 1024, DefaultEditResolutionWeak = 512;

    /// <summary>Bounds of the direct-fusion trial's pixel budget.</summary>
    private const int MinEditResolution = 256, MaxEditResolution = 2048;

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
    /// <param name="Steps">The recipe's step count (the Qwen base model uses 25); null when the model has no step count of its own and samples
    /// over a fixed sigma table instead (the Turbo distillation), so nothing offers or links steps.</param>
    /// <param name="Cfg">The cfg rule with bounds and hint.</param>
    /// <param name="NegativeHint">The note about the negative prompt at cfg 1.</param>
    /// <param name="Sampler">The sampler name, such as "euler"; "" when the file does not give one.</param>
    /// <param name="Scheduler">The scheduler name, such as "simple"; "" when the file does not give one. A fixed sigma table has none.</param>
    /// <param name="Sigmas">The fixed sigma table the model samples over, as the file writes it (comma-separated decimals); "" when the model
    /// samples by steps and scheduler instead.</param>
    public sealed record class SamplingRule(int? Steps, CfgRule Cfg, LocalizedText NegativeHint, string Sampler, string Scheduler, string Sigmas);

    /// <summary>How a converted graph is recognized as this recipe (T118): the encoder node classes declared under <c>match.encoder_classes</c>.
    /// A graph containing such an encoder follows the recipe's cfg bounds, hints and size step; empty classes never match.</summary>
    /// <param name="EncoderClasses">Encoder class names, normalized to lower case.</param>
    public sealed record class MatchRule(List<string> EncoderClasses);

    /// <summary>The second repainting pass (the standard workflows' harmonize pass over the whole picture). A KSampler-based graph reads the
    /// steps and denoise; a fixed sigma-table graph (SamplerCustomAdvanced with ManualSigmas) reads the tail table instead — one of the two
    /// applies, the recipe declares the kind its blueprints use (T118b).</summary>
    /// <param name="Steps">The pass's step count; null when the model has no step count.</param>
    /// <param name="Denoise">The pass's denoise strength (0–1).</param>
    /// <param name="Sigmas">The pass's sigma table (a tail of the official table for a low-strength pass); "" when the pass runs by steps.</param>
    public sealed record class SecondPassRule(int? Steps, double Denoise, string Sigmas);

    /// <summary>One workflow-kind blueprint entry of a recipe (T118): the shipped blueprint file for that kind, plus — for kinds the standard workflows
    /// repaint around the product — the blueprint nodes to wire in at (canvas node IDs, resolved through the converter's ID map). A plain string entry
    /// (the T117 form) means no declared access points: the wiring finds the graph's single sampler and decoder itself.</summary>
    /// <param name="File">The blueprint file name inside Templates/blueprints.</param>
    /// <param name="InpaintSampler">The blueprint node ID of the sampler to rewire; "" to look for the single KSampler.</param>
    /// <param name="InpaintDecoder">The blueprint node ID of the decoder whose picture is repainted; "" to look for the single VAEDecode.</param>
    public sealed record class BlueprintEntry(string File, string InpaintSampler, string InpaintDecoder);

    /// <summary>The direct-fusion trial's wiring as the recipe declares it (T118): which encoder node class carries the pixel budget and in which input,
    /// and the automatic pixel budget per hardware tier. A missing block means the recipe cannot run the trial.</summary>
    /// <param name="EncoderClass">The encoder node class, such as the edit encoder of the current model; "" only on the fallback rule.</param>
    /// <param name="ResolutionInput">The encoder input carrying the pixel budget, "resolution" when the file does not name one.</param>
    /// <param name="AutoDefault">The automatic pixel budget for tiers <see cref="AutoByTier"/> does not list.</param>
    /// <param name="AutoByTier">Per-tier automatic budgets (HardwareRules tier names to values).</param>
    public sealed record class ReferenceEditRule(string EncoderClass, string ResolutionInput, int AutoDefault, Dictionary<string, int> AutoByTier)
    {
        /// <summary>The automatic pixel budget of one hardware tier ("" or an unlisted tier uses <see cref="AutoDefault"/>; a missing tier
        /// reading counts as unknown); the tier only guides this default and never blocks a run (A19).</summary>
        public int AutoResolution(string tier) => AutoByTier.TryGetValue(
            HardwareRules.Normalize(string.IsNullOrWhiteSpace(tier) ? HardwareRules.Unknown : tier), out int value) ? value : AutoDefault;
    }

    /// <summary>The three model file names the standard workflows fill into their loaders.</summary>
    /// <param name="Unet">The diffusion model file; "" keeps the blueprint's own saved value.</param>
    /// <param name="TextEncoder">The text encoder file; "" keeps the blueprint's own value.</param>
    /// <param name="Vae">The VAE file; "" keeps the blueprint's own value.</param>
    public sealed record class ModelFiles(string Unet, string TextEncoder, string Vae)
    {
        /// <summary>Whether any file name is set.</summary>
        public bool Any() => (Unet ?? "").Length > 0 || (TextEncoder ?? "").Length > 0 || (Vae ?? "").Length > 0;
    }

    /// <summary>One resolved recipe choice: the recipe, one of its variants, and a plain-language problem when the choice fell back (T118).</summary>
    /// <param name="Recipe">The chosen recipe; null when the book has nothing usable.</param>
    /// <param name="Variant">The chosen variant; null when the recipe has none or the recipe itself is null.</param>
    /// <param name="Problem">Why the choice fell back, in words for the administrator's status list; null when the choice is exact.</param>
    public sealed record class CurrentChoice(Recipe Recipe, Variant Variant, string Problem);

    /// <summary>The startup migration's decision when the default recipe changes (T118b): the recipe id to write into the setting (null leaves
    /// everything as it is) and whether the legacy file names still carry values to clear.</summary>
    /// <param name="Recipe">The "model id / variant id" to write; null when nothing should change.</param>
    /// <param name="ClearLegacy">Whether the three legacy file-name settings hold factory values that should be cleared with the write.</param>
    public sealed record class RecipeMigration(string Recipe, bool ClearLegacy);

    /// <summary>Everything the standard workflows need to build and publish: the resolved recipe with its variant, the effective model file names
    /// (the recipe's, or the administrator's legacy override), and problems to show in the status (T118).</summary>
    /// <param name="Recipe">The current recipe; null when no recipe resolves.</param>
    /// <param name="Variant">The current recipe's variant; null when none.</param>
    /// <param name="Files">The model file names to fill into the loaders.</param>
    /// <param name="LegacyUnmatched">Whether non-empty legacy file-name settings are in effect but match no recipe variant (administrator override, A19).</param>
    /// <param name="Problems">Plain-language notes for the administrator's status list.</param>
    public sealed record class EffectiveModels(Recipe Recipe, Variant Variant, ModelFiles Files, bool LegacyUnmatched, List<string> Problems);

    /// <summary>What the model can do; the page hides entries a recipe does not support.</summary>
    /// <param name="Edit">Whether the model can edit from reference images.</param>
    /// <param name="AlphaChannel">Whether the model can output an alpha channel.</param>
    /// <param name="MaxInputImages">How many images one request may feed the encoder (a reference plus angle photos).</param>
    public sealed record class CapabilityRule(bool Edit, bool AlphaChannel, int MaxInputImages);

    /// <summary>One model recipe: everything about running one model, plus its hardware variants.</summary>
    /// <param name="Id">The recipe id, unique in the book (the first entry wins when a file repeats one).</n    /// <param name="Name">The display name.</param>
    /// <param name="License">The license note shown to administrators; EcomCraft itself claims nothing beyond it (A31).</param>
    /// <param name="MinComfyUI">The lowest ComfyUI version the recipe is known to work on; "" when unknown.</param>
    /// <param name="Nodes">The ComfyUI node classes the recipe needs.</param>
    /// <param name="Match">How a converted graph is recognized as this recipe (T118); never null.</param>
    /// <param name="Blueprints">Workflow kind ("scene", "detail", "reference-edit", ...) to blueprint entry; kinds without a blueprint cannot run that way.</param>
    /// <param name="Sampling">The sampling rule.</param>
    /// <param name="SecondPass">The second pass rule.</param>
    /// <param name="ReferenceEdit">The direct-fusion trial's wiring as declared; null when the recipe cannot run the trial (T118).</param>
    /// <param name="ResolutionBudget">The encoder's pixel budget per image.</param>
    /// <param name="SizeStep">Widths and heights must be multiples of this.</param>
    /// <param name="MaxEdge">The longest edge generated and exported images may have; never above 2048 (A24).</param>
    /// <param name="PromptSyntax">How images are referenced in prompts, such as "&lt;image1&gt;"; "" when plain text.</param>
    /// <param name="Capabilities">The capability flags.</param>
    /// <param name="Variants">The hardware variants, in file order; the first matching one wins.</param>
    public sealed record class Recipe(string Id, LocalizedText Name, string License, string MinComfyUI, List<string> Nodes, MatchRule Match,
        Dictionary<string, BlueprintEntry> Blueprints, SamplingRule Sampling, SecondPassRule SecondPass, ReferenceEditRule ReferenceEdit,
        long ResolutionBudget, int SizeStep, int MaxEdge,
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

    /// <summary>The recipe and variant a "model id / variant id" setting picks (T118): an empty setting takes the default recipe, an unresolvable one
    /// also takes the default with a plain-language problem, and a variant id that no longer exists falls back to the recipe's first variant.</summary>
    public static CurrentChoice ResolveCurrent(IEnumerable<Recipe> book, string setting)
    {
        List<Recipe> recipes = [.. (book ?? [])];
        string problem = null;
        Recipe recipe = null;
        Variant variant = null;
        string value = (setting ?? "").Trim();
        if (value.Length > 0)
        {
            string[] parts = value.Split('/', StringSplitOptions.TrimEntries);
            recipe = parts.Length == 2 ? Find(recipes, parts[0]) : null;
            if (recipe is not null)
            {
                variant = recipe.Variants.FirstOrDefault(item => item.Id == HardwareRules.Normalize(parts[1]));
            }
            if (recipe is null || variant is null)
            {
                problem = $"当前配方设置“{value}”无效，已回退默认配方。";
                recipe = null;
            }
        }
        if (recipe is null)
        {
            recipe = Find(recipes, DefaultRecipeId);
            if (recipe is not null)
            {
                variant = recipe.Variants.FirstOrDefault(item => item.Id == DefaultVariantId) ?? recipe.Variants.FirstOrDefault();
                if (variant is null)
                {
                    problem = $"默认配方 {DefaultRecipeId} 没有可用变体。";
                }
            }
            else
            {
                problem = "没有可用的模型配方。";
            }
        }
        return new CurrentChoice(recipe, variant, problem);
    }

    /// <summary>The first variant whose files match every non-empty legacy file-name setting, case-insensitively (T118): how the startup migration
    /// recognizes which recipe the old settings already point at. Null when nothing is set or nothing matches.</summary>
    public static CurrentChoice MatchLegacy(IEnumerable<Recipe> book, ModelFiles legacy)
    {
        if (legacy?.Any() != true)
        {
            return null;
        }
        foreach (Recipe recipe in book ?? [])
        {
            foreach (Variant variant in recipe.Variants)
            {
                if (Fits(legacy.Unet, FileFor(variant, RoleUnet)) && Fits(legacy.TextEncoder, FileFor(variant, RoleTextEncoder))
                    && Fits(legacy.Vae, FileFor(variant, RoleVae)))
                {
                    return new CurrentChoice(recipe, variant, null);
                }
            }
        }
        return null;
    }

    /// <summary>Whether one legacy file name fits a variant file: an unset legacy name sets no constraint, a set one must equal the variant's file name
    /// for that role (case-insensitively, the way model folders work).</summary>
    private static bool Fits(string legacyName, FileSpec file)
    {
        if (string.IsNullOrWhiteSpace(legacyName))
        {
            return true;
        }
        return file is not null && string.Equals(file.Filename, legacyName.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether the legacy file-name settings still carry their factory state (T118b): nothing at all, or exactly the UC trio the
    /// settings shipped with — an administrator's own name (any difference) is a choice to keep (A19).</summary>
    private static bool IsFactoryNames(ModelFiles legacy)
    {
        if (legacy is null || !legacy.Any())
        {
            return true;
        }
        return string.Equals(legacy.Unet?.Trim(), FactoryUnet, StringComparison.OrdinalIgnoreCase)
            && string.Equals(legacy.TextEncoder?.Trim(), FactoryTextEncoder, StringComparison.OrdinalIgnoreCase)
            && string.Equals(legacy.Vae?.Trim(), FactoryVae, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The startup migration when the default recipe changes (T118b): a server still on the factory state — the setting empty, with the
    /// old file names empty or the factory trio — moves to the current default. An administrator's own setting or file names keep everything as it
    /// is, including a deliberate choice of the old default pair (M1 review: this server's T118 migration never wrote that pair, so treating it as
    /// factory state would undo a later deliberate switch back on every restart, against A19); null means no change. The new pair is written
    /// only when the book actually resolves it, so a custom book without the default never stores an unusable value.</summary>
    public static RecipeMigration MigrateDefault(IEnumerable<Recipe> book, string setting, ModelFiles legacy)
    {
        string current = (setting ?? "").Trim();
        if (current.Length > 0)
        {
            return null;
        }
        ModelFiles names = new((legacy?.Unet ?? "").Trim(), (legacy?.TextEncoder ?? "").Trim(), (legacy?.Vae ?? "").Trim());
        if (!IsFactoryNames(names))
        {
            return null;
        }
        Recipe recipe = Find(book, DefaultRecipeId);
        if (recipe is null || recipe.Variants.All(variant => variant.Id != DefaultVariantId))
        {
            return null;
        }
        return new RecipeMigration($"{DefaultRecipeId}/{DefaultVariantId}", names.Any());
    }

    /// <summary>The loader node class a diffusion-model file needs (T118b): ".gguf" quantizations load through <c>UnetLoaderGGUF</c> and
    /// safetensors through <c>UNETLoader</c> — a file-format distinction, not a model rule, so the same recipe may declare both kinds of file
    /// (the official int8 safetensors and the community GGUF quantization) and the standard workflows' graph follows the file in effect.</summary>
    public static string UnetLoaderClass(string filename) =>
        (filename ?? "").Trim().EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) ? "UnetLoaderGGUF" : "UNETLoader";

    /// <summary>Everything the standard workflows need (T118): non-empty legacy file-name settings keep acting as the administrator's override (A19) —
    /// matching a variant or not — while the recipe itself comes from the current-recipe setting with its default fallback. Never throws and never
    /// blocks a run; problems are words for the administrator's status list.</summary>
    public static EffectiveModels Resolve(IEnumerable<Recipe> book, string setting, ModelFiles legacy)
    {
        List<string> problems = [];
        if (legacy is not null && legacy.Any())
        {
            CurrentChoice match = MatchLegacy(book, legacy);
            if (match is not null)
            {
                return new EffectiveModels(match.Recipe, match.Variant, legacy, false, problems);
            }
            problems.Add("旧的模型文件名设置未匹配任何配方，已按这些文件名继续（管理员覆盖）。");
            CurrentChoice fallback = ResolveCurrent(book, "");
            if (fallback.Problem is not null)
            {
                problems.Add(fallback.Problem);
            }
            return new EffectiveModels(fallback.Recipe, fallback.Variant, legacy, true, problems);
        }
        CurrentChoice current = ResolveCurrent(book, setting);
        if (current.Problem is not null)
        {
            problems.Add(current.Problem);
        }
        return new EffectiveModels(current.Recipe, current.Variant, FilesOf(current.Variant), false, problems);
    }

    /// <summary>The model file names of one variant, by role; "" for roles the variant does not declare.</summary>
    public static ModelFiles FilesOf(Variant variant) => new(
        FileFor(variant, RoleUnet)?.Filename ?? "",
        FileFor(variant, RoleTextEncoder)?.Filename ?? "",
        FileFor(variant, RoleVae)?.Filename ?? "");

    /// <summary>The image tag of one picture under a prompt syntax (T118): the syntax's digit run becomes the picture's index, so "<c>&lt;image1&gt;</c>"
    /// makes "<c>&lt;image2&gt;</c>" and so on. Null when the syntax has no index to replace.</summary>
    public static string ImageTag(string promptSyntax, int index)
    {
        (int start, int end) = DigitRun(promptSyntax);
        if (start < 0)
        {
            return null;
        }
        string syntax = promptSyntax.Trim();
        return syntax[..start] + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + syntax[end..];
    }

    /// <summary>The prompt syntax a request path uses: a declared one with a replaceable index, or the default style when the recipe has none.</summary>
    public static string EffectivePromptSyntax(string promptSyntax) => DigitRun(promptSyntax).Start >= 0 ? promptSyntax.Trim() : DefaultPromptSyntax;

    /// <summary>The regex matching every image tag of a prompt syntax (T118): pictures 1–10, as the server-written edit prompt may carry them. The
    /// default syntax yields exactly the pattern the checks used before recipes existed.</summary>
    public static string TagPattern(string promptSyntax)
    {
        string syntax = EffectivePromptSyntax(promptSyntax);
        (int start, int end) = DigitRun(syntax);
        return System.Text.RegularExpressions.Regex.Escape(syntax[..start]) + "(?:[1-9]|10)" + System.Text.RegularExpressions.Regex.Escape(syntax[end..]);
    }

    /// <summary>Where the digit run of a prompt syntax sits; start -1 when there is none.</summary>
    private static (int Start, int End) DigitRun(string promptSyntax)
    {
        string syntax = (promptSyntax ?? "").Trim();
        for (int i = 0; i < syntax.Length; i++)
        {
            if (char.IsDigit(syntax[i]))
            {
                int end = i;
                while (end < syntax.Length && char.IsDigit(syntax[end]))
                {
                    end++;
                }
                return (i, end);
            }
        }
        return (-1, -1);
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
        JObject secondPass = entry["second_pass"] as JObject ?? new JObject();
        return new Recipe(
            id,
            ParseText(entry["name"]),
            (entry["license"]?.ToString() ?? "").Trim(),
            (entry["min_comfyui"]?.ToString() ?? "").Trim(),
            ParseList(entry["nodes"]),
            new MatchRule(ParseList((entry["match"] as JObject ?? new JObject())["encoder_classes"])),
            ParseBlueprints(entry["blueprints"]),
            new SamplingRule(
                OptionalStepCount(samplingEntry["steps"], MaxSteps),
                new CfgRule(cfgDefault, cfgMin, cfgMax, ParseText(cfgEntry["hint"])),
                ParseText(samplingEntry["negative_hint"]),
                (samplingEntry["sampler"]?.ToString() ?? "").Trim(),
                (samplingEntry["scheduler"]?.ToString() ?? "").Trim(),
                SigmaTable(samplingEntry["sigmas"])),
            new SecondPassRule(
                OptionalStepCount(secondPass["steps"], MaxSecondPassSteps),
                BoundedDecimal(secondPass["denoise"], DefaultSecondPassDenoise, 0, 1),
                SigmaTable(secondPass["sigmas"])),
            ParseReferenceEdit(entry["reference_edit"]),
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

    /// <summary>A workflow-kind to blueprint-entry map (T118): a plain string is the file alone (the T117 form, no declared access points); an
    /// object adds the <c>inpaint</c> access points. Entries without a usable file name are dropped.</summary>
    private static Dictionary<string, BlueprintEntry> ParseBlueprints(JToken token)
    {
        Dictionary<string, BlueprintEntry> map = [];
        if (token is not JObject obj)
        {
            return map;
        }
        foreach (JProperty property in obj.Properties())
        {
            string kind = HardwareRules.Normalize(property.Name);
            if (kind.Length == 0)
            {
                continue;
            }
            if (property.Value?.Type == JTokenType.String)
            {
                string file = property.Value.ToString().Trim();
                if (file.Length > 0)
                {
                    map[kind] = new BlueprintEntry(file, "", "");
                }
                continue;
            }
            if (property.Value is not JObject entry)
            {
                continue;
            }
            string entryFile = (entry["file"]?.ToString() ?? "").Trim();
            if (entryFile.Length == 0)
            {
                continue;
            }
            JObject inpaint = entry["inpaint"] as JObject ?? new JObject();
            map[kind] = new BlueprintEntry(entryFile,
                (inpaint["sampler"]?.ToString() ?? "").Trim(),
                (inpaint["decoder"]?.ToString() ?? "").Trim());
        }
        return map;
    }

    /// <summary>The direct-fusion trial's declaration (T118): a block without an encoder class cannot run the trial; the auto pixel budgets are
    /// clamped to their bounds per value.</summary>
    private static ReferenceEditRule ParseReferenceEdit(JToken token)
    {
        if (token is not JObject obj)
        {
            return null;
        }
        string encoderClass = (obj["encoder_class"]?.ToString() ?? "").Trim();
        if (encoderClass.Length == 0)
        {
            return null;
        }
        string resolutionInput = (obj["resolution_input"]?.ToString() ?? "").Trim();
        JObject auto = obj["auto_resolution"] as JObject ?? new JObject();
        Dictionary<string, int> byTier = [];
        foreach (JProperty property in (auto["by_tier"] as JObject ?? new JObject()).Properties())
        {
            string tier = HardwareRules.Normalize(property.Name);
            if (tier.Length > 0 && property.Value is JValue value && value.Type == JTokenType.Integer)
            {
                byTier[tier] = (int)Math.Clamp(value.Value<long>(), MinEditResolution, MaxEditResolution);
            }
        }
        return new ReferenceEditRule(encoderClass,
            resolutionInput.Length > 0 ? resolutionInput : "resolution",
            (int)BoundedWhole(auto["default"], DefaultEditResolution, MinEditResolution, MaxEditResolution),
            byTier);
    }

    /// <summary>The auto pixel budgets used when no recipe declares a reference-edit block: today's values, kept so a missing recipe never breaks
    /// the administrator override of <c>EcomCraft.ReferenceEditResolution</c> (T118).</summary>
    public static ReferenceEditRule FallbackReferenceEdit() => new("", "resolution", DefaultEditResolution,
        new Dictionary<string, int>() { [HardwareRules.Unsupported] = DefaultEditResolutionWeak, [HardwareRules.Unknown] = DefaultEditResolutionWeak });

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

    /// <summary>A step count the file may leave out (T118b): a positive whole number clamped to its upper bound, and null when the file gives
    /// none — a model that samples over a fixed sigma table has no step count, and nothing should fall back to a number it does not use.</summary>
    private static int? OptionalStepCount(JToken token, int max)
    {
        if (token is not JValue value || value.Type != JTokenType.Integer || value.Value is not long number || number <= 0)
        {
            return null;
        }
        return (int)Math.Min(number, max);
    }

    /// <summary>A sigma table as the file writes it (comma-separated decimals, at least two values) — the fixed sampling path of models without
    /// step counts, and of their second pass; "" when the file does not give a usable table.</summary>
    private static string SigmaTable(JToken token)
    {
        string text = (token?.Type == JTokenType.String ? token.ToString() : "").Trim();
        if (text.Length == 0)
        {
            return "";
        }
        string[] values = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (values.Length < 2 || values.Any(part => !double.TryParse(part, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _)))
        {
            return "";
        }
        return text;
    }
}
