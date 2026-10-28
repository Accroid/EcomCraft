namespace EcomCraftStudio.Studio.Generation;

/// <summary>Rules for one-click sets and batches (F03, R5.7, R9.4), independent of SwarmUI so they can be unit tested.</summary>
public static class SetRules
{
    /// <summary>Most product photos in one batch.</summary>
    public const int MaxProducts = 20;

    /// <summary>Most tasks one batch may expand into.</summary>
    public const int MaxJobs = 80;

    /// <summary>Upper bound for one person's tasks running at once, whatever their role allows (R9.4).</summary>
    public const int MaxConcurrent = 4;

    /// <summary>A template as the set expansion sees it.</summary>
    public sealed record class TemplateRef(string Id, IReadOnlyList<string> Platforms, IReadOnlyList<string> Sets);

    /// <summary>One task: a product photo (by index) and a template.</summary>
    public sealed record class Job(int Product, string Template);

    /// <summary>Templates of a set that suit the platform (an empty platform means any; "*" in a template suits every platform), in the given order.</summary>
    public static List<TemplateRef> SetTemplates(IEnumerable<TemplateRef> templates, string platform, string setId) =>
        [.. templates.Where(template => template.Sets.Contains(setId)
            && (string.IsNullOrEmpty(platform) || template.Platforms.Contains("*") || template.Platforms.Contains(platform)))];

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

    /// <summary>How many of one person's tasks may run at once: the role's MaxT2ISimultaneous, at least 1 and at most <see cref="MaxConcurrent"/>.</summary>
    public static int Concurrency(int roleMaxSimultaneous) => Math.Clamp(roleMaxSimultaneous, 1, MaxConcurrent);
}
