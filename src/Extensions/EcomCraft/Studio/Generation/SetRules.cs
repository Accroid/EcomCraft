using EcomCraftStudio.Studio.Common;

namespace EcomCraftStudio.Studio.Generation;

/// <summary>Rules for one-click sets and batches (F03, R5.7, R9.4), independent of SwarmUI so they can be unit tested.</summary>
public static class SetRules
{
    /// <summary>Most product photos in one batch.</summary>
    public const int MaxProducts = 20;

    /// <summary>Most reference images in reference mode (F59): since T98 enough for a whole set, 5 main images and 10 detail screens.</summary>
    public const int MaxReferences = 15;

    /// <summary>Reference mode's default: follow each reference's own prompt (F59).</summary>
    public const string ReferenceModeComposition = "composition";

    /// <summary>Reference mode's direct-fusion trial (F84): the server writes the prompt, no per-reference prompts are sent.</summary>
    public const string ReferenceModeEdit = "edit";

    /// <summary>Whether a reference_mode value is one reference mode accepts: empty means <see cref="ReferenceModeComposition"/>; anything else is refused.</summary>
    public static bool IsValidReferenceMode(string mode) => string.IsNullOrEmpty(mode) || mode is (ReferenceModeComposition or ReferenceModeEdit);

    /// <summary>Most tasks one batch may expand into.</summary>
    public const int MaxJobs = 80;

    /// <summary>Most images one batch may request in total (F55): products × set types × candidates, shared with single generations. Configurable per
    /// server since T98 (2026-11-03); <see cref="DefaultImageLimit"/> is the value a server uses when its setting is unset or out of range.</summary>
    public const int DefaultImageLimit = 60;

    /// <summary>Largest image total an administrator may configure (T98): far above a sensible set, but bounded so a typo cannot queue thousands of images.</summary>
    public const int MaxImageLimit = 500;

    /// <summary>The image total cap this server enforces (T98): the configured value, or the default when it is unset; never above <see cref="MaxImageLimit"/>.</summary>
    public static int ImageLimit(int configured) => Math.Clamp(configured <= 0 ? DefaultImageLimit : configured, 1, MaxImageLimit);

    /// <summary>Candidates each image slot makes by default in the reference flow's slot modes (T98): users may lower the number to 1 in the page.</summary>
    public const int DefaultSlotCandidates = 2;

    /// <summary>The default candidate count this server offers for slot batches (T98): the configured value, or the default when unset; within 1–<see cref="MaxCandidates"/>.</summary>
    public static int SlotCandidateCount(int configured) => Math.Clamp(configured <= 0 ? DefaultSlotCandidates : configured, 1, MaxCandidates);

    /// <summary>Most candidate pictures per image type; the batch total still stays within the server's image cap (F55).</summary>
    public const int MaxCandidates = 20;

    /// <summary>Largest inline image, as a data URL, that a request may carry.</summary>
    public const int MaxImageDataLength = (5 * 1024 * 1024 + 2) / 3 * 4 + 40;

    /// <summary>Upper bound for one person's tasks running at once, whatever their role allows (R9.4).</summary>
    public const int MaxConcurrent = 4;

    /// <summary>Whether expanded tasks × candidates stays within the image cap this server enforces (F55, T98).</summary>
    public static bool WithinImageTotal(int tasks, int candidates, int limit) => (long)tasks * candidates <= ImageLimit(limit);

    /// <summary>Whether a product photo looks like a data URL of an allowed image type within <see cref="MaxImageDataLength"/>; only the prefix is checked here,
    /// the bytes are decoded later (R10, via ImageDataUrl since R12).</summary>
    public static bool ProductImageLooksValid(string image) =>
        ImageDataUrl.IsValid(image, ImageDataUrl.ImageDataFormats.PngJpegWebp, MaxImageDataLength);

    /// <summary>How long a finished batch stays in memory.</summary>
    public const long BatchKeepMs = 6 * 60 * 60 * 1000;

    /// <summary>How long a batch with unfinished tasks may stay, so a stuck task cannot keep its images in memory forever.</summary>
    public const long ActiveBatchKeepMs = 24 * 60 * 60 * 1000;

    /// <summary>Whether a batch of this age can be dropped from memory: finished batches after <see cref="BatchKeepMs"/>; a batch still running is kept so its owner can follow it, up to <see cref="ActiveBatchKeepMs"/>.</summary>
    public static bool BatchExpired(long ageMs, bool hasUnfinishedTasks) => ageMs > (hasUnfinishedTasks ? ActiveBatchKeepMs : BatchKeepMs);

    /// <summary>A template as the set expansion sees it.</summary>
    public sealed record class TemplateRef(string Id, IReadOnlyList<string> Platforms, IReadOnlyList<string> Sets, string Kind = "");

    /// <summary>Source of a slot's picture in the reference flow's slot modes (T98): the reference and the product fused, the cut-out on white, a real
    /// crop of the product photo, or the server's layout (selling text drawn onto a base picture). Kinds with no source are uncovered, never invented.</summary>
    public const string SourceFusion = "fusion", SourceCutout = "cutout", SourceCrop = "crop", SourceLayout = "layout";

    /// <summary>One slot of the platform as the reference flow's expansion sees it (T98): its ID and kind and, when the kind can be made, the source
    /// that makes it; a slot with no source carries the reason code instead (translated by the page), so the batch can show it as uncovered.</summary>
    public sealed record class SlotPlan(string SlotId, string Kind, string Source, string Reason)
    {
        /// <summary>Whether this slot gets pictures at all.</summary>
        public bool Covered => Source is not null;
    }

    /// <summary>Why a slot kind has no source in the one-reference full set (T98), or null when the kind is made. Kinds never in
    /// <see cref="SetSlotKinds"/> never reach here. "model" has no automatic source yet; detail screens wait for T100's layouts.</summary>
    public static string FullSetSlotSource(string kind, bool detailHighResolution, bool sellingInfoFilled) => kind switch
    {
        "first_main" or "scene_main" => SourceFusion,
        "white_main" => SourceCutout,
        "detail" => detailHighResolution ? SourceCrop : "low_resolution",
        "selling" => sellingInfoFilled ? SourceLayout : "no_selling_info",
        "detail_page" => "layout_pending",
        _ => "unsupported_kind",
    };

    /// <summary>One task: a product photo (by index), a template and, for storyboard sets (F25) or reference mode (F59), the shot or reference it makes (-1 otherwise).
    /// Slot-mode tasks (T98) also carry the platform slot they belong to and the source that makes them.</summary>
    public sealed record class Job(int Product, string Template, int Shot = -1, int Reference = -1, string SlotId = null, string SlotKind = null, string Source = null);

    /// <summary>Expands products × reference images into tasks (F59): one image per reference, all with the same template, product by product.
    /// Slot-less requests keep exactly this expansion (T98).</summary>
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

    /// <summary>Expands products × reference images into slot tasks (T98): the same one-image-per-reference shape, each task carrying the slot its user
    /// chose for that reference (null keeps the slot-less behaviour). Repeated slots are allowed: two references on one slot make two tasks.</summary>
    public static List<Job> ExpandReferenceSlots(int products, string templateId, IReadOnlyList<(string SlotId, string Kind)> referenceSlots)
    {
        List<Job> jobs = [];
        for (int product = 0; product < products; product++)
        {
            for (int reference = 0; reference < referenceSlots.Count; reference++)
            {
                jobs.Add(new Job(product, templateId, -1, reference, referenceSlots[reference].SlotId, referenceSlots[reference].Kind,
                    referenceSlots[reference].SlotId is null ? null : SourceFusion));
            }
        }
        return jobs;
    }

    /// <summary>Expands one reference into the platform's whole set (T98): product by product, one task per covered slot in the platform's slot order,
    /// all fusion tasks using the single reference. Uncovered slots are not tasks; the caller reports them as uncovered.</summary>
    public static List<Job> ExpandFullSet(int products, string fusionTemplateId, IReadOnlyList<SlotPlan> slots)
    {
        List<Job> jobs = [];
        foreach (SlotPlan slot in slots)
        {
            if (!slot.Covered)
            {
                continue;
            }
            string source = slot.Source;
            for (int product = 0; product < products; product++)
            {
                jobs.Add(new Job(product, source == SourceFusion ? fusionTemplateId : "", -1, 0, slot.SlotId, slot.Kind, source));
            }
        }
        return jobs;
    }

    /// <summary>Images one slot task makes (T98): cut-outs are deterministic, so one picture however many candidates were asked for; every other source
    /// makes the chosen candidate count. The page mirrors this for its estimate before submitting.</summary>
    public static int JobImageCount(string source, int candidates) => source == SourceCutout ? 1 : Math.Max(1, candidates);

    /// <summary>Images one task of a slot batch makes (T98): a reference left without a slot keeps the old one picture per reference, the rest follow
    /// <see cref="JobImageCount"/>.</summary>
    public static int SlotJobImageCount(Job job, int candidates) => job.SlotId is null ? 1 : JobImageCount(job.Source, candidates);

    /// <summary>Images a slot batch requests in total (T98): the sum over its tasks, each at its own count. Over the limit the server refuses the batch
    /// outright; nothing is silently truncated.</summary>
    public static int SlotImageTotal(IEnumerable<Job> jobs, int candidates) => jobs.Sum(job => SlotJobImageCount(job, candidates));

    /// <summary>The largest center crop of a photo that matches a slot's aspect (T98): the crop the detail slot takes its close-up from. The wider side
    /// of the photo is kept, the other is centered to the aspect; the result is null-like (false) when either side would come out empty.</summary>
    public static bool DetailCrop(int photoWidth, int photoHeight, int slotWidth, int slotHeight, out int cropWidth, out int cropHeight)
    {
        cropWidth = cropHeight = 0;
        if (photoWidth <= 0 || photoHeight <= 0 || slotWidth <= 0 || slotHeight <= 0)
        {
            return false;
        }
        double aspect = (double)slotWidth / slotHeight;
        if ((double)photoWidth / photoHeight > aspect)
        {
            cropHeight = photoHeight;
            cropWidth = Math.Max(1, (int)Math.Round(photoHeight * aspect));
        }
        else
        {
            cropWidth = photoWidth;
            cropHeight = Math.Max(1, (int)Math.Round(photoWidth / aspect));
        }
        return true;
    }

    /// <summary>Whether a photo has enough pixels for a detail slot (T98): the crop at the slot's aspect must reach half the slot's size on both sides,
    /// so the close-up is enlarged at most twofold and stays clear.</summary>
    public static bool DetailCropSufficient(int photoWidth, int photoHeight, int slotWidth, int slotHeight)
    {
        return DetailCrop(photoWidth, photoHeight, slotWidth, slotHeight, out int cropWidth, out int cropHeight)
            && cropWidth * 2 >= slotWidth && cropHeight * 2 >= slotHeight;
    }

    /// <summary>Linear share of the largest crop that a detail close-up keeps (T98): enough zoom to read as a close-up, enough room to shift.</summary>
    public const double DetailZoom = 0.6;

    /// <summary>The detail close-up's crop size (T98): <see cref="DetailZoom"/> of the largest crop at the slot's aspect, but never smaller than half the
    /// slot's size (the enlargement stays at most twofold, as <see cref="DetailCropSufficient"/> promises) and never larger than the largest crop.</summary>
    public static bool DetailZoomCrop(int photoWidth, int photoHeight, int slotWidth, int slotHeight, out int cropWidth, out int cropHeight)
    {
        if (!DetailCrop(photoWidth, photoHeight, slotWidth, slotHeight, out int maxWidth, out int maxHeight))
        {
            cropWidth = cropHeight = 0;
            return false;
        }
        double scale = Math.Max(DetailZoom, Math.Max((double)slotWidth / 2 / maxWidth, (double)slotHeight / 2 / maxHeight));
        scale = Math.Min(1, scale);
        cropWidth = Math.Max(1, (int)Math.Round(maxWidth * scale));
        cropHeight = Math.Max(1, (int)Math.Round(maxHeight * scale));
        return true;
    }

    /// <summary>Crop positions the detail candidates step through (T98), as fractions (0–1) of the shift the photo allows on each axis: centered first,
    /// then left/right, up/down, and the edges.</summary>
    private static readonly (double X, double Y)[] CropShifts = [(0.5, 0.5), (0.25, 0.5), (0.75, 0.5), (0.5, 0.25), (0.5, 0.75), (0, 0.5), (1, 0.5), (0.5, 0), (0.5, 1)];

    /// <summary>Where the n-th detail crop of a slot sits (T98): the first is centered and later ones (more candidates, or "one more") step through
    /// <see cref="CropShifts"/>, starting over after the last, so every value stays inside the photo.</summary>
    public static (double X, double Y) CropShift(int index) => CropShifts[Math.Max(0, index) % CropShifts.Length];

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
    /// no template for are skipped, and so are kinds that never join sets ("video_cover" only records video specs, T97); templates without a kind do not join
    /// slot-composed sets (they are meant for the old, set-only platforms).</summary>
    public static List<TemplateRef> SlotTemplates(IEnumerable<TemplateRef> templates, string platform, string setId, IReadOnlyList<string> slotKinds)
    {
        List<TemplateRef> result = [];
        foreach (string kind in slotKinds)
        {
            if (!SetSlotKinds.Contains(kind))
            {
                continue;
            }
            result.AddRange(templates.Where(template => template.Sets.Contains(setId) && SuitsPlatform(template, platform) && template.Kind == kind));
        }
        return result;
    }

    /// <summary>The slot kinds one-click sets compose (F58, T97): the main-image kinds, then detail screens. "video_cover" only records video specs and never
    /// joins sets (design decision 3, 2026-10-28). Kinds must also be listed in <see cref="Export.ExportSlot.Kinds"/>; extend both together.</summary>
    public static readonly string[] SetSlotKinds = ["white_main", "first_main", "scene_main", "selling", "detail", "model", "detail_page"];

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
