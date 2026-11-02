using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EcomCraftStudio.Studio.Setup;

/// <summary>Hardware tiering (F76 / T91): pure rules that map a hardware snapshot to a tier, so the boundaries can be unit-tested.
/// The tier only guides recommendations and notices; it never blocks anything (A19). Thresholds and the ROCm card list come from
/// hardware-tiers.json and will be re-tuned from real measurements (G1); nothing here is written for one specific machine.</summary>
public static class HardwareRules
{
    /// <summary>Full effects: NVIDIA with ample video memory, or Apple silicon with ample unified memory.</summary>
    public const string Recommended = "recommended";

    /// <summary>Runs with quantized models; slower than recommended.</summary>
    public const string Minimum = "minimum";

    /// <summary>AMD cards on the ROCm list with enough video memory: works, but effects and speed may be limited.</summary>
    public const string Conditional = "conditional";

    /// <summary>Below the published limits; an administrator may still force a run (A19).</summary>
    public const string Unsupported = "unsupported";

    /// <summary>Something could not be read; never judged unsupported just because a value is missing.</summary>
    public const string Unknown = "unknown";

    /// <summary>Reason code: no graphics adapter was found at all (the enumeration itself worked).</summary>
    public const string NoGpu = "no_gpu";

    /// <summary>Reason code: the graphics adapters could not be read.</summary>
    public const string GpuUnknown = "gpu_unknown";

    /// <summary>Reason code: the adapter was identified but its video memory was not.</summary>
    public const string GpuVramUnknown = "gpu_vram_unknown";

    /// <summary>Reason code: integrated graphics (a chip's built-in GPU, not a discrete card).</summary>
    public const string GpuIntegrated = "gpu_integrated";

    /// <summary>Reason code: a vendor EcomCraft does not run on (A15: NVIDIA, listed AMD or Apple silicon only).</summary>
    public const string GpuVendorUnsupported = "gpu_vendor_unsupported";

    /// <summary>Reason code: NVIDIA card at or above the recommended video memory.</summary>
    public const string NvidiaRecommendedMemory = "nvidia_recommended";

    /// <summary>Reason code: NVIDIA card between the minimum and recommended video memory.</summary>
    public const string NvidiaMinimumMemory = "nvidia_minimum";

    /// <summary>Reason code: NVIDIA card under the minimum video memory.</summary>
    public const string NvidiaBelowMinimum = "nvidia_below_minimum";

    /// <summary>Reason code: AMD card on the ROCm list with enough video memory (conditional tier).</summary>
    public const string AmdRocmListed = "amd_rocm_listed";

    /// <summary>Reason code: AMD card on the ROCm list but under the minimum video memory.</summary>
    public const string AmdRocmVramBelow = "amd_rocm_vram_below";

    /// <summary>Reason code: AMD card not on the ROCm list.</summary>
    public const string AmdNotInRocmList = "amd_not_in_rocm_list";

    /// <summary>Reason code: Mac with an Intel chip (A15: Apple silicon only).</summary>
    public const string MacIntelChip = "mac_intel_chip";

    /// <summary>Reason code: Apple silicon at or above the recommended unified memory.</summary>
    public const string MacRecommendedMemory = "mac_apple_silicon_recommended";

    /// <summary>Reason code: Apple silicon between the minimum and recommended unified memory.</summary>
    public const string MacMinimumMemory = "mac_apple_silicon_minimum";

    /// <summary>Reason code: Apple silicon under the minimum unified memory.</summary>
    public const string MacMemoryBelow = "mac_memory_below";

    /// <summary>Reason code: unified memory could not be read on Apple silicon.</summary>
    public const string MemoryUnknown = "memory_unknown";

    /// <summary>Reason code: free disk space under the hint value (a notice only; downloads and generation are not blocked).</summary>
    public const string DiskLow = "disk_low";

    /// <summary>Thresholds and the ROCm card list. Values are megabytes (rounded the way card makers count: 1 MB = 2^20 bytes) and gigabytes.</summary>
    /// <param name="NvidiaRecommendedMb">NVIDIA video memory at or above this is the recommended tier.</param>
    /// <param name="NvidiaMinimumMb">NVIDIA video memory at or above this (under recommended) is the minimum tier.</param>
    /// <param name="MacRecommendedMb">Apple silicon unified memory at or above this is the recommended tier.</param>
    /// <param name="MacMinimumMb">Apple silicon unified memory at or above this (under recommended) is the minimum tier.</param>
    /// <param name="AmdMinimumMb">AMD video memory a ROCm-listed card needs for the conditional tier.</param>
    /// <param name="DiskHintGb">Free disk space under this adds the disk_low reason; 0 disables the check.</param>
    /// <param name="VramTolerancePercent">How many percent a card's reading may fall short of a video memory threshold and still qualify: cards report
    /// a little less than their stated size (a "6 GB" card may read 6128 MB while 6 GB is 6144 MB). Only the NVIDIA and AMD video memory thresholds
    /// use it; Mac unified memory reads exactly and never does.</param>
    /// <param name="RocmGpus">Names (or name fragments) of AMD cards treated as ROCm-capable; matched case-insensitively inside the adapter name.</param>
    public sealed record class Rules(long NvidiaRecommendedMb, long NvidiaMinimumMb, long MacRecommendedMb, long MacMinimumMb, long AmdMinimumMb, long DiskHintGb, int VramTolerancePercent, List<string> RocmGpus);

    /// <summary>One graphics adapter as seen by the rules: its name, vendor (from the name or, on Linux, the PCI IDs) and its video memory in bytes when it was read.</summary>
    /// <param name="Name">The adapter name, or a stand-in such as "pci 10de:2684" when only IDs are known.</param>
    /// <param name="Vendor">"nvidia", "amd", "intel", "apple", "other" or "unknown".</param>
    /// <param name="VramBytes">Video memory in bytes, or null when it could not be read reliably.</param>
    public sealed record class Gpu(string Name, string Vendor, long? VramBytes);

    /// <summary>The hardware facts the rules decide on. Everything nullable except the operating system and architecture: a missing value is shown as unknown, never guessed.</summary>
    /// <param name="Os">"windows", "macos", "linux" or "other".</param>
    /// <param name="Arch">The OS architecture, lower-case ("x64", "arm64", ...).</param>
    /// <param name="MemoryBytes">Total system (on Mac: unified) memory in bytes, or null.</param>
    /// <param name="DiskFreeBytes">Free space of the server's main volume in bytes, or null.</param>
    /// <param name="Gpus">The graphics adapters found; may be empty.</param>
    /// <param name="GpusRead">Whether the adapter enumeration itself worked (true with an empty list means there really is no adapter).</param>
    public sealed record class Snapshot(string Os, string Arch, long? MemoryBytes, long? DiskFreeBytes, List<Gpu> Gpus, bool GpusRead);

    /// <summary>The decision: the tier, and machine-readable reason codes explaining it.</summary>
    public sealed record class Verdict(string Tier, List<string> Reasons);

    /// <summary>Initial threshold values; the shipped hardware-tiers.json matches them and administrators can override both (re-tuning from G1 measurements is expected).
    /// The tolerance covers cards reporting slightly under their stated size (T91 review).</summary>
    public static Rules Default() => new(16 * 1024, 8 * 1024, 32 * 1024, 24 * 1024, 8 * 1024, 50, 3, [.. DefaultRocmGpus.Select(Normalize)]);

    /// <summary>The initial ROCm card list: current consumer Radeon cards that run ComfyUI through ROCm/ZLUDA on Windows or Linux. A starting point only, to be re-tuned (G1).</summary>
    public static readonly string[] DefaultRocmGpus =
    [
        "Radeon RX 7900 XTX", "Radeon RX 7900 XT", "Radeon RX 7900 GRE", "Radeon RX 7800 XT", "Radeon RX 7700 XT", "Radeon RX 7600",
        "Radeon RX 6950 XT", "Radeon RX 6900 XT", "Radeon RX 6800 XT", "Radeon RX 6800", "Radeon RX 6750 XT", "Radeon RX 6700 XT",
        "Radeon RX 6700", "Radeon RX 6650 XT", "Radeon RX 6600 XT", "Radeon RX 6600",
        "Radeon RX 5700 XT", "Radeon RX 5700", "Radeon RX 5600 XT", "Radeon RX 5600", "Radeon RX 5500 XT", "Radeon RX 5500",
        "Radeon VII", "Radeon Pro W7900", "Radeon Pro W7800", "Radeon Pro W6800"
    ];

    /// <summary>Upper bound for a threshold in megabytes, so a mistyped config cannot produce nonsense tiers.</summary>
    private const long MaxMb = 1024L * 1024;

    /// <summary>Upper bound of the video memory tolerance in percent: values outside 0–10 fall back to the default instead of being clamped.</summary>
    private const int MaxTolerancePercent = 10;

    /// <summary>Reads the rules from hardware-tiers.json text; unreadable text or missing values fall back to <see cref="Default"/>. A missing or empty
    /// ROCm list also falls back to the default list (use an impossible entry such as "none" to keep a custom file from matching any card).</summary>
    public static Rules Parse(string json)
    {
        Rules fallback = Default();
        if (string.IsNullOrWhiteSpace(json))
        {
            return fallback;
        }
        try
        {
            JObject root = JObject.Parse(json);
            long Mb(string key, long fallbackMb)
            {
                // A missing or nonsensical value falls back to the default instead of producing nonsense tiers.
                long? value = WholeNumber(root, key);
                if (value is null || value <= 0)
                {
                    return fallbackMb;
                }
                return Math.Min(value.Value, MaxMb);
            }
            long diskGb = WholeNumber(root, "disk_hint_gb") is long disk and >= 0 ? Math.Min(disk, 1024 * 1024) : fallback.DiskHintGb;
            // Only a small tolerance is meaningful; anything outside 0–10 keeps the default instead of being clamped (T91 review).
            long tolerance = WholeNumber(root, "vram_tolerance_percent") ?? fallback.VramTolerancePercent;
            int tolerancePercent = tolerance is >= 0 and <= MaxTolerancePercent ? (int)tolerance : fallback.VramTolerancePercent;
            List<string> rocm = [.. (root["rocm_gpus"] as JArray ?? new JArray()).Select(item => item?.ToString() ?? "").Select(Normalize)
                .Where(name => name.Length > 0)];
            return new Rules(
                Mb("nvidia_recommended_mb", fallback.NvidiaRecommendedMb),
                Mb("nvidia_minimum_mb", fallback.NvidiaMinimumMb),
                Mb("mac_recommended_mb", fallback.MacRecommendedMb),
                Mb("mac_minimum_mb", fallback.MacMinimumMb),
                Mb("amd_minimum_mb", fallback.AmdMinimumMb),
                diskGb,
                tolerancePercent,
                rocm.Count == 0 ? fallback.RocmGpus : rocm);
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    /// <summary>One config value as a whole number, or null when it is missing, null, a decimal, a string, a nested object or an integer too large
    /// for a long: a mistyped entry falls back to its default on its own, so the rest of the file keeps working and the API never errors on a bad
    /// config (T91 review).</summary>
    private static long? WholeNumber(JObject root, string key)
    {
        if (root[key] is not JValue value || value.Type != JTokenType.Integer || value.Value is not long number)
        {
            return null;
        }
        return number;
    }

    /// <summary>The vendor an adapter name suggests, or "unknown". The probe may supply a better answer from PCI IDs; this is the name-based fallback.</summary>
    public static string VendorOf(string name)
    {
        string lower = Normalize(name);
        if (lower.Length == 0)
        {
            return "unknown";
        }
        if (lower.Contains("nvidia") || lower.Contains("geforce") || lower.Contains("quadro") || lower.Contains("tesla"))
        {
            return "nvidia";
        }
        if (lower.Contains("radeon") || lower.Contains("amd") || lower.Contains("ati"))
        {
            return "amd";
        }
        if (lower.Contains("intel"))
        {
            return "intel";
        }
        if (lower.Contains("apple"))
        {
            return "apple";
        }
        return "other";
    }

    /// <summary>Lower-cases and collapses whitespace, for name comparisons.</summary>
    public static string Normalize(string name) => string.Join(' ', (name ?? "").ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Whether an AMD adapter name matches an entry of the ROCm list (case-insensitive fragment match).</summary>
    public static bool InRocmList(string name, Rules rules)
    {
        string lower = Normalize(name);
        foreach (string entry in rules?.RocmGpus ?? [])
        {
            if (entry.Length > 0 && lower.Contains(Normalize(entry)))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Video memory rounded to whole megabytes the way memory sizes are usually stated.</summary>
    public static long Megabytes(long bytes) => (bytes + (1024 * 1024) / 2) / (1024 * 1024);

    /// <summary>The direct-fusion trial's pixel budget from the hardware tier (F84, T123): 1024 on any tier the hardware can run on, 512 on the
    /// unsupported tier or when the probe failed, so a weak card still finishes. The tier only guides this default; it never blocks a run (A19).</summary>
    public static int ReferenceEditAutoResolution(string tier) => tier is Recommended or Minimum or Conditional ? 1024 : 512;

    /// <summary>The direct-fusion trial's pixel budget as configured (F84): a positive value is used clamped to 256–2048, and 0 or less means automatic
    /// by <see cref="ReferenceEditAutoResolution"/> of the given tier.</summary>
    public static int ReferenceEditResolution(int configured, string tier) =>
        configured > 0 ? (int)Math.Clamp(configured, 256, 2048) : ReferenceEditAutoResolution(tier);

    /// <summary>A video memory threshold with the tolerance applied: cards report a little less than their stated size (an "8 GB" card may read
    /// 8176 MB), so the boundary shifts down by the configured percent instead of rejecting such cards (T91 review).</summary>
    private static long WithTolerance(long thresholdMb, Rules rules) => thresholdMb - thresholdMb * Math.Max(0, rules.VramTolerancePercent) / 100;

    /// <summary>Rank of a tier for picking the best card of a machine: a card that could not be judged never sinks an otherwise-supported machine.</summary>
    private static int Rank(string tier) => tier switch
    {
        Recommended => 4,
        Minimum => 3,
        Conditional => 2,
        Unknown => 1,
        _ => 0
    };

    /// <summary>The tier of a whole machine: on Mac by chip and unified memory, elsewhere by its best graphics adapter. A disk-space shortage only adds a notice.</summary>
    public static Verdict Classify(Snapshot snapshot, Rules rules)
    {
        rules ??= Default();
        List<string> reasons = [];
        string tier;
        if (snapshot is null)
        {
            tier = Unknown;
            reasons.Add(GpuUnknown);
        }
        else if (Normalize(snapshot.Os) == "macos")
        {
            tier = MacTier(snapshot, rules, reasons);
        }
        else
        {
            tier = GpuTier(snapshot, rules, reasons);
        }
        if (rules.DiskHintGb > 0 && snapshot?.DiskFreeBytes is long disk && disk < rules.DiskHintGb * 1024L * 1024 * 1024)
        {
            reasons.Add(DiskLow);
        }
        return new Verdict(tier, reasons);
    }

    /// <summary>The Mac rules (A15): Apple silicon by unified memory; Intel chips are not supported.</summary>
    private static string MacTier(Snapshot snapshot, Rules rules, List<string> reasons)
    {
        if (Normalize(snapshot.Arch) != "arm64")
        {
            reasons.Add(MacIntelChip);
            return Unsupported;
        }
        if (snapshot.MemoryBytes is not long memory)
        {
            reasons.Add(MemoryUnknown);
            return Unknown;
        }
        long megabytes = Megabytes(memory);
        // Unified memory reads exactly, so the Mac thresholds are compared without the video memory tolerance.
        if (megabytes >= rules.MacRecommendedMb)
        {
            reasons.Add(MacRecommendedMemory);
            return Recommended;
        }
        if (megabytes >= rules.MacMinimumMb)
        {
            reasons.Add(MacMinimumMemory);
            return Minimum;
        }
        reasons.Add(MacMemoryBelow);
        return Unsupported;
    }

    /// <summary>The Windows and Linux rules: each adapter gets its own verdict and the best one counts.</summary>
    private static string GpuTier(Snapshot snapshot, Rules rules, List<string> reasons)
    {
        if (!snapshot.GpusRead)
        {
            reasons.Add(GpuUnknown);
            return Unknown;
        }
        if (snapshot.Gpus is null || snapshot.Gpus.Count == 0)
        {
            reasons.Add(NoGpu);
            return Unsupported;
        }
        Verdict best = null;
        foreach (Gpu gpu in snapshot.Gpus)
        {
            Verdict verdict = ClassifyGpu(gpu, rules);
            if (best is null || Rank(verdict.Tier) > Rank(best.Tier))
            {
                best = verdict;
            }
        }
        reasons.AddRange(best.Reasons);
        return best.Tier;
    }

    /// <summary>The verdict for one adapter.</summary>
    private static Verdict ClassifyGpu(Gpu gpu, Rules rules)
    {
        string vendor = Normalize(gpu?.Vendor ?? "") is "" ? VendorOf(gpu?.Name) : Normalize(gpu.Vendor);
        return vendor switch
        {
            "nvidia" => ClassifyNvidia(gpu, rules),
            "amd" => ClassifyAmd(gpu, rules),
            // Intel: built-in graphics; Intel Arc is discrete but EcomCraft does not run on it either way (A15).
            "intel" => new Verdict(Unsupported, [Normalize(gpu?.Name ?? "").Contains("arc") ? GpuVendorUnsupported : GpuIntegrated]),
            // Mac decides by chip and unified memory, never by this list.
            "apple" => new Verdict(Unknown, [GpuUnknown]),
            "unknown" => new Verdict(Unknown, [GpuUnknown]),
            _ => new Verdict(Unsupported, [GpuVendorUnsupported])
        };
    }

    /// <summary>The NVIDIA rules: the tier follows video memory only.</summary>
    private static Verdict ClassifyNvidia(Gpu gpu, Rules rules)
    {
        if (gpu.VramBytes is not long vram)
        {
            return new Verdict(Unknown, [GpuVramUnknown]);
        }
        long megabytes = Megabytes(vram);
        if (megabytes >= WithTolerance(rules.NvidiaRecommendedMb, rules))
        {
            return new Verdict(Recommended, [NvidiaRecommendedMemory]);
        }
        if (megabytes >= WithTolerance(rules.NvidiaMinimumMb, rules))
        {
            return new Verdict(Minimum, [NvidiaMinimumMemory]);
        }
        return new Verdict(Unsupported, [NvidiaBelowMinimum]);
    }

    /// <summary>The AMD rules: only ROCm-listed cards with enough video memory reach the conditional tier; a chip's built-in Radeon graphics never does.</summary>
    private static Verdict ClassifyAmd(Gpu gpu, Rules rules)
    {
        string lower = Normalize(gpu.Name);
        if (lower.Contains("radeon graphics") || (lower.Contains("vega") && lower.Contains("graphics")))
        {
            return new Verdict(Unsupported, [GpuIntegrated]);
        }
        if (!InRocmList(gpu.Name, rules))
        {
            return new Verdict(Unsupported, [AmdNotInRocmList]);
        }
        if (gpu.VramBytes is not long vram)
        {
            return new Verdict(Unknown, [GpuVramUnknown]);
        }
        if (Megabytes(vram) >= WithTolerance(rules.AmdMinimumMb, rules))
        {
            return new Verdict(Conditional, [AmdRocmListed]);
        }
        return new Verdict(Unsupported, [AmdRocmVramBelow]);
    }
}
