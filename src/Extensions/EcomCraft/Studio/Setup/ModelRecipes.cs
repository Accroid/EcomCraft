using System.IO;
using EcomCraftStudio.Studio.Templates;
using SwarmUI.Core;
using SwarmUI.Utils;

namespace EcomCraftStudio.Studio.Setup;

/// <summary>Loads the model recipe book (F86 / T117): Data/EcomCraft/model-recipes.json when an administrator provided one, otherwise the
/// shipped Templates/model-recipes.json — the same override mechanism as platforms.json. Unreadable or corrupt files read as no recipes and
/// never disturb the running configuration.</summary>
public static class ModelRecipes
{
    /// <summary>Reads the recipe book. Read per call like the platform list, so an administrator's override takes effect without a restart;
    /// unreadable files give no recipes.</summary>
    public static List<ModelRecipeRules.Recipe> Load()
    {
        try
        {
            string custom = Path.Combine(Program.DataDir, "EcomCraft", "model-recipes.json");
            if (File.Exists(custom))
            {
                return ModelRecipeRules.Parse(File.ReadAllText(custom));
            }
            string shipped = Path.Combine(Path.GetDirectoryName(TemplateStore.BuiltinDir) ?? "", "model-recipes.json");
            return File.Exists(shipped) ? ModelRecipeRules.Parse(File.ReadAllText(shipped)) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable recipe book only means no recipe-driven hints; generation itself never depends on it.
            return [];
        }
    }

    /// <summary>Everything the standard workflows need right now (T118): the current recipe (setting or default fallback) with its effective model
    /// file names, honoring a legacy file-name override. Read per call, so a recipe edit takes effect on the next check.</summary>
    public static ModelRecipeRules.EffectiveModels Current()
    {
        Settings.EcomCraftData settings = Program.ServerSettings.EcomCraft;
        return ModelRecipeRules.Resolve(Load(), settings.CurrentRecipe,
            new ModelRecipeRules.ModelFiles(settings.StandardWorkflowUnet?.Trim() ?? "", settings.StandardWorkflowTextEncoder?.Trim() ?? "", settings.StandardWorkflowVae?.Trim() ?? ""));
    }

    /// <summary>One-time migration of the three legacy model file-name settings into the current recipe (T118): when the current recipe is unset and
    /// the legacy names match a recipe variant, the recipe id is written and the legacy names cleared; otherwise (a name of the administrator's own)
    /// everything stays as the override it already is (A19). Failures only log — the server must start whatever the settings contain.</summary>
    public static void MigrateLegacySettings()
    {
        try
        {
            Settings.EcomCraftData settings = Program.ServerSettings.EcomCraft;
            if (!string.IsNullOrWhiteSpace(settings.CurrentRecipe))
            {
                return;
            }
            ModelRecipeRules.ModelFiles legacy = new(settings.StandardWorkflowUnet?.Trim() ?? "", settings.StandardWorkflowTextEncoder?.Trim() ?? "", settings.StandardWorkflowVae?.Trim() ?? "");
            if (!legacy.Any())
            {
                return;
            }
            ModelRecipeRules.CurrentChoice match = ModelRecipeRules.MatchLegacy(Load(), legacy);
            if (match is null)
            {
                Logs.Info("EcomCraft legacy model file names match no recipe; keeping them as the administrator override (A19).");
                return;
            }
            settings.CurrentRecipe = $"{match.Recipe.Id}/{match.Variant.Id}";
            settings.StandardWorkflowUnet = "";
            settings.StandardWorkflowTextEncoder = "";
            settings.StandardWorkflowVae = "";
            Program.SaveSettingsFile();
            Logs.Info($"EcomCraft migrated the legacy model settings to the current recipe {settings.CurrentRecipe} (T118).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Logs.Warning($"EcomCraft could not migrate the legacy model settings into the current recipe: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
