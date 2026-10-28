using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EcomCraftStudio.Studio.Projects;

/// <summary>The recipe of a filed work (F04), read by the server from the work's own metadata: enough to make it again with the same template, seed and prompt.
/// It never holds image content, workflow graphs or server addresses.</summary>
public sealed class ProjectRecipe
{
    /// <summary>Template ID, or null when the work was not made from a template.</summary>
    public string Template;

    /// <summary>Seed, or null when unknown.</summary>
    public long? Seed;

    /// <summary>The prompt subject actually used (without the template's own prefix and suffix), or an empty string.</summary>
    public string Prompt = "";

    /// <summary>Display name of the execution server, or null.</summary>
    public string Server;

    /// <summary>Picture width, or 0 when unknown.</summary>
    public int Width;

    /// <summary>Picture height, or 0 when unknown.</summary>
    public int Height;

    /// <summary>The account's saved product the work was made from, or null.</summary>
    public string ProductId;

    /// <summary>The account's brand kit used, or null.</summary>
    public string BrandKitId;

    /// <summary>Target platform ID, or null.</summary>
    public string Platform;
}

/// <summary>One work filed in a project: its path inside the owner's output folder, when it was added and its recipe.</summary>
public sealed class ProjectWork
{
    /// <summary>Path relative to the owner's output folder, as the gallery lists it.</summary>
    public string Path;

    /// <summary>When it was added, Unix milliseconds.</summary>
    public long Added;

    /// <summary>The recipe read from the work's metadata.</summary>
    public ProjectRecipe Recipe = new();
}

/// <summary>One project (F04): a customer or a batch of products, owned by one account.</summary>
public sealed class ProjectRecord
{
    /// <summary>Project ID.</summary>
    public string Id;

    /// <summary>Owner account.</summary>
    public string UserId;

    /// <summary>Display name.</summary>
    public string Name;

    /// <summary>Optional note.</summary>
    public string Note = "";

    /// <summary>Creation time, Unix milliseconds.</summary>
    public long Created;

    /// <summary>Last change, Unix milliseconds.</summary>
    public long Updated;

    /// <summary>Filed works, oldest first.</summary>
    public List<ProjectWork> Works = [];
}

/// <summary>The project index with per-account isolation, independent of SwarmUI so isolation, limits and recipe reading can be unit tested.
/// Every lookup takes the account: another account's project is never found, changed or removed.</summary>
public sealed class ProjectIndex
{
    /// <summary>Longest project name.</summary>
    public const int MaxNameLength = 40;

    /// <summary>Longest note.</summary>
    public const int MaxNoteLength = 200;

    /// <summary>Most projects per account.</summary>
    public const int MaxPerUser = 200;

    /// <summary>Most works per project.</summary>
    public const int MaxWorks = 2000;

    /// <summary>Longest prompt kept in a recipe.</summary>
    public const int MaxPromptLength = 1500;

    /// <summary>All records.</summary>
    public List<ProjectRecord> Items = [];

    /// <summary>The account's projects, most recently changed first.</summary>
    public List<ProjectRecord> ForUser(string userId) => [.. Items.Where(item => item.UserId == userId).OrderByDescending(item => item.Updated)];

    /// <summary>The account's project with this ID, or null; another account's project is never returned.</summary>
    public ProjectRecord Find(string userId, string id) =>
        string.IsNullOrEmpty(id) ? null : Items.FirstOrDefault(item => item.UserId == userId && item.Id == id);

    /// <summary>Adds a project; returns a reason when the account already has the most projects allowed.</summary>
    public string Add(ProjectRecord record)
    {
        if (Items.Count(item => item.UserId == record.UserId) >= MaxPerUser)
        {
            return $"每个账号最多 {MaxPerUser} 个项目，请先删除不用的。";
        }
        Items.Add(record);
        return null;
    }

    /// <summary>Removes the account's project and returns it, or null when there is none.</summary>
    public ProjectRecord Remove(string userId, string id)
    {
        ProjectRecord record = Find(userId, id);
        if (record is not null)
        {
            Items.Remove(record);
        }
        return record;
    }

    /// <summary>Files works into a project, skipping ones already in it; returns how many were added, or -1 when the project would exceed <see cref="MaxWorks"/>.</summary>
    public static int AddWorks(ProjectRecord project, IEnumerable<ProjectWork> works)
    {
        List<ProjectWork> fresh = [.. works.Where(work => project.Works.All(existing => existing.Path != work.Path)).DistinctBy(work => work.Path)];
        if (project.Works.Count + fresh.Count > MaxWorks)
        {
            return -1;
        }
        project.Works.AddRange(fresh);
        return fresh.Count;
    }

    /// <summary>Removes works from a project by path; returns how many were removed.</summary>
    public static int RemoveWorks(ProjectRecord project, IEnumerable<string> paths)
    {
        HashSet<string> remove = [.. paths];
        return project.Works.RemoveAll(work => remove.Contains(work.Path));
    }

    /// <summary>One line of plain text (no control characters or angle brackets), limited in length; null when nothing is left.</summary>
    public static string CleanText(string text, int maxLength)
    {
        string cleaned = Regex.Replace(text ?? "", @"[\p{C}<>]", " ", RegexOptions.CultureInvariant);
        cleaned = Regex.Replace(cleaned, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
        if (cleaned.Length > maxLength)
        {
            cleaned = cleaned[..maxLength].TrimEnd();
        }
        return cleaned.Length == 0 ? null : cleaned;
    }

    /// <summary>The prompt subject: the full prompt without the template's own prefix and suffix when they surround it.</summary>
    public static string StripTemplate(string prompt, string prefix, string suffix)
    {
        string text = (prompt ?? "").Trim();
        prefix = prefix?.Trim() ?? "";
        suffix = suffix?.Trim() ?? "";
        if (prefix.Length > 0 && text.StartsWith(prefix, StringComparison.Ordinal))
        {
            text = text[prefix.Length..].TrimStart();
        }
        if (suffix.Length > 0 && text.EndsWith(suffix, StringComparison.Ordinal))
        {
            text = text[..^suffix.Length].TrimEnd();
        }
        return text;
    }

    /// <summary>Reads a recipe from a work's SwarmUI metadata text (sui_image_params and sui_extra_data); unreadable metadata gives an empty recipe.
    /// <paramref name="templatePrompt"/> returns the template's prefix and suffix for a template ID, so only the subject is kept.</summary>
    public static ProjectRecipe ParseMetadata(string text, Func<string, (string Prefix, string Suffix)> templatePrompt)
    {
        ProjectRecipe recipe = new();
        JObject metadata;
        try
        {
            metadata = string.IsNullOrWhiteSpace(text) ? null : JObject.Parse(text);
        }
        catch (JsonException)
        {
            metadata = null;
        }
        if (metadata is null)
        {
            return recipe;
        }
        JObject parameters = metadata["sui_image_params"] as JObject ?? new JObject();
        JObject extra = metadata["sui_extra_data"] as JObject ?? new JObject();
        string template = extra["ecomcraft_template"]?.ToString();
        recipe.Template = template is not null && Regex.IsMatch(template, "^[a-z0-9][a-z0-9-]{0,47}$", RegexOptions.CultureInvariant) ? template : null;
        recipe.Seed = parameters["seed"]?.Type == JTokenType.Integer ? parameters.Value<long>("seed") : null;
        recipe.Width = parameters["width"]?.Type == JTokenType.Integer ? parameters.Value<int>("width") : 0;
        recipe.Height = parameters["height"]?.Type == JTokenType.Integer ? parameters.Value<int>("height") : 0;
        recipe.Server = CleanText(extra["ecomcraft_execution_server_name"]?.ToString(), 80);
        string prompt = parameters["prompt"]?.Type == JTokenType.String ? parameters.Value<string>("prompt") : "";
        if (recipe.Template is not null && templatePrompt is not null)
        {
            (string prefix, string suffix) = templatePrompt(recipe.Template);
            prompt = StripTemplate(prompt, prefix, suffix);
        }
        prompt = Regex.Replace(prompt, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
        recipe.Prompt = prompt.Length > MaxPromptLength ? prompt[..MaxPromptLength] : prompt;
        return recipe;
    }
}
