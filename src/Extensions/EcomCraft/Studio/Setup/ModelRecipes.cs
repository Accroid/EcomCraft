using System.IO;
using EcomCraftStudio.Studio.Templates;
using SwarmUI.Core;

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
}
