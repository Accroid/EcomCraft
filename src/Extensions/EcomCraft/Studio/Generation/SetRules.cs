namespace EcomCraftStudio.Studio.Generation;

/// <summary>Rules for one-click sets and batches (F03, R5.7, R9.4), independent of SwarmUI so they can be unit tested.</summary>
public static class SetRules
{
    /// <summary>Most product photos in one batch.</summary>
    public const int MaxProducts = 20;

    /// <summary>Most reference images in reference mode (F59), matching the storyboard shot cap.</summary>
    public const int MaxReferences = 8;

    /// <summary>Most tasks one batch may expand into.</summary>
    public const int MaxJobs = 80;

    /// <summary>Upper bound for one person's tasks running at once, whatever their role allows (R9.4).</summary>
    public const int MaxConcurrent = 4;

    /// <summary>A template as the set expansion sees it.</summary>
    public sealed record class TemplateRef(string Id, IReadOnlyList<string> Platforms, IReadOnlyList<string> Sets, string Kind = "");

    /// <summary>One task: a product photo (by index), a template and, for storyboard sets (F25) or reference mode (F59), the shot or reference it makes (-1 otherwise).</summary>
    public sealed record class Job(int Product, string Template, int Shot = -1, int Reference = -1);

    /// <summary>Whether a template suits the platform (an empty platform means any; "*" in a template suits every platform).</summary>
    public static bool SuitsPlatform(TemplateRef template, string platform) =>
        string.IsNullOrEmpty(platform) || template.Platforms.Contains("*") || template.Platforms.Contains(platform);

    /// <summary>Expands products × storyboard shots (F25) into tasks, product by product; each shot names its template.</summary>
    public static List<Job> ExpandShots(int products, IReadOnlyList<string> shotTemplates)
    {
        List<Job> jobs = [];
        for (int product = 0; product < products; product++)
        {
            for (int shot = 0; shot < shotTemplates.Count; shot++)
            {
                jobs.Add(new Job(product, shotTemplates[shot], shot));
            }
        }
        return jobs;
    }

    /// <summary>Templates of a set that suit the platform (an empty platform means any; "*" in a template suits every platform), in the given order.</summary>
    public static List<TemplateRef> SetTemplates(IEnumerable<TemplateRef> templates, string platform, string setId) =>
        [.. templates.Where(template => template.Sets.Contains(setId) && SuitsPlatform(template, platform))];

    /// <summary>Templates of a set as a platform's slots compose them (F58): in slot order, every template whose kind matches a slot. Slot kinds the set has
    /// no template for are skipped; templates without a kind do not join slot-composed sets (they are meant for the old, set-only platforms).</summary>
    public static List<TemplateRef> SlotTemplates(IEnumerable<TemplateRef> templates, string platform, string setId, IReadOnlyList<string> slotKinds)
    {
        List<TemplateRef> result = [];
        foreach (string kind in slotKinds)
        {
            result.AddRange(templates.Where(template => template.Sets.Contains(setId) && SuitsPlatform(template, platform) && template.Kind == kind));
        }
        return result;
    }

    /// <summary>Expands products × set templates into tasks, product by product, so the first product's set finishes first.</summary>
    public static List<Job> Expand(int products, IReadOnlyList<TemplateRef> setTemplates)
    {
        List<Job> jobs = [];
        for (int product = 0; product < products; product++)
        {
            foreach (TemplateRef template in setTemplates)
            {
                jobs.Add(new Job(product, template.Id));
            }
        }
        return jobs;
    }

    /// <summary>Expands products × reference images into tasks (F59): one image per reference, all with the same template, product by product.</summary>
    public static List<Job> ExpandReferences(int products, string templateId, int references)
    {
        List<Job> jobs = [];
        for (int product = 0; product < products; product++)
        {
            for (int reference = 0; reference < references; reference++)
            {
                jobs.Add(new Job(product, templateId, -1, reference));
            }
        }
        return jobs;
    }

    /// <summary>How many of one person's tasks may run at once: the role's MaxT2ISimultaneous, at least 1 and at most <see cref="MaxConcurrent"/>.</summary>
    public static int Concurrency(int roleMaxSimultaneous) => Math.Clamp(roleMaxSimultaneous, 1, MaxConcurrent);
}
