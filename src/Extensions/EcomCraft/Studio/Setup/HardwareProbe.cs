using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using EcomCraftStudio.Studio.Storage;
using FreneticUtilities.FreneticExtensions;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio.Setup;

/// <summary>Reads this platform server's hardware for the tier decision (F76 / T91): operating system and chip architecture, graphics adapters with
/// video memory, total (on Mac unified) memory and free disk space, then classified by <see cref="HardwareRules"/>. Only this server is probed; other
/// execution servers are judged by their nodes and models. The tier only guides recommendations and notices, never blocks anything (A19).
/// The per-system reading of the graphics identity is shared with the vision device self-test (F71), which caches results under it.</summary>
[API.APIClass("EcomCraft hardware detection and tiers (F76): facts about this platform server's hardware and its recommended tier.")]
public static class HardwareProbe
{
    /// <summary>The extension folder's Templates folder, set on init.</summary>
    private static string BuiltinDir;

    /// <summary>Remembers the extension folder holding the built-in hardware-tiers.json.</summary>
    public static void Init(string extensionFolder)
    {
        BuiltinDir = Path.Combine(extensionFolder, "Templates");
    }

    /// <summary>The shipped tier thresholds and ROCm list, or null before init.</summary>
    private static string BuiltinRulesFile => BuiltinDir is null ? null : Path.Combine(BuiltinDir, "hardware-tiers.json");

    /// <summary>An administrator's override of the tier thresholds and ROCm list (same format as the built-in file).</summary>
    private static string CustomRulesFile => Path.Combine(Program.DataDir, "EcomCraft", "hardware-tiers.json");

    /// <summary>The tier rules in force: the administrator's file when present, otherwise the shipped one, otherwise the built-in defaults.</summary>
    public static HardwareRules.Rules LoadRules()
    {
        try
        {
            string file = File.Exists(CustomRulesFile) ? CustomRulesFile : BuiltinRulesFile;
            if (file is null || !File.Exists(file))
            {
                return HardwareRules.Default();
            }
            return HardwareRules.Parse(File.ReadAllText(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Logs.Warning($"EcomCraft could not read the hardware tier rules; using the built-in defaults: {ex.GetType().Name}");
            return HardwareRules.Default();
        }
    }

    /// <summary>One graphics adapter as probed: name, vendor, video memory in bytes when read reliably, and where the memory figure came from.</summary>
    /// <param name="Name">The adapter name, or a stand-in such as "pci 0x1002:731f" when only IDs are known.</param>
    /// <param name="Vendor">"nvidia", "amd", "intel", "apple" or "unknown".</param>
    /// <param name="VramBytes">Video memory in bytes, or null when it could not be read reliably.</param>
    /// <param name="VramSource">"registry" (64-bit), "registry_dword" (32-bit, below the 4 GiB wrap), "sysfs", "nvidia-smi", "unified" or "unknown".</param>
    public sealed record class ProbedGpu(string Name, string Vendor, long? VramBytes, string VramSource);

    /// <summary>One Windows adapter entry of the display class registry key.</summary>
    /// <param name="Name">The adapter description (DriverDesc).</param>
    /// <param name="DriverVersion">The driver version, or null.</param>
    /// <param name="VramBytes">Video memory in bytes, or null.</param>
    /// <param name="VramSource">Where <paramref name="VramBytes"/> came from.</param>
    private sealed record class WindowsAdapter(string Name, string DriverVersion, long? VramBytes, string VramSource);

    /// <summary>The names and driver versions of this computer's graphics, sorted; "unknown" when they cannot be read. The exact identity the vision
    /// device self-test (F71) caches its results under, moved here from <see cref="MagicPrompt.VisionDeviceCheck"/> so both features share one reading;
    /// the format is unchanged so saved results keep matching.</summary>
    public static string GraphicsKey()
    {
        List<string> cards = [];
        try
        {
            if (OperatingSystem.IsWindows())
            {
                foreach (WindowsAdapter adapter in ReadWindowsAdapters())
                {
                    cards.Add($"{adapter.Name} {adapter.DriverVersion ?? ""}".Trim());
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                foreach ((string vendor, string device) in ReadLinuxCardIds())
                {
                    cards.Add($"{vendor}:{device}");
                }
                string nvidia = ReadFirstLine("/proc/driver/nvidia/version");
                if (nvidia.Length > 0)
                {
                    cards.Add(nvidia);
                }
                // AMD and Intel drivers come with the kernel.
                cards.Add($"kernel {Environment.OSVersion.Version}");
            }
            else if (OperatingSystem.IsMacOS())
            {
                cards.Add($"{RuntimeInformation.OSArchitecture} macOS {Environment.OSVersion.Version}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Logs.Debug($"EcomCraft could not read the graphics of this computer: {ex.GetType().Name}");
        }
        cards.Sort(StringComparer.Ordinal);
        return cards.Count == 0 ? "unknown" : string.Join(", ", cards);
    }

    /// <summary>Every adapter entry of the Windows display class key: real cards, chips' built-in graphics and virtual display adapters alike,
    /// so the identity string keeps its old shape; callers filter as they need.</summary>
    private static List<WindowsAdapter> ReadWindowsAdapters()
    {
        List<WindowsAdapter> cards = [];
        using RegistryKey adapters = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
        foreach (string name in adapters?.GetSubKeyNames() ?? Array.Empty<string>())
        {
            // Only the numbered adapter entries; "Properties" cannot be opened.
            if (!int.TryParse(name, out _))
            {
                continue;
            }
            using RegistryKey card = adapters.OpenSubKey(name);
            string description = (card?.GetValue("DriverDesc") as string)?.Trim();
            if (string.IsNullOrWhiteSpace(description))
            {
                continue;
            }
            (long? vram, string source) = ReadWindowsVram(card);
            cards.Add(new WindowsAdapter(description, card.GetValue("DriverVersion") as string, vram, source));
        }
        return cards;
    }

    /// <summary>A card's video memory from the driver's registry values. The 64-bit "qwMemorySize" is trusted; the 32-bit "MemorySize" is only used
    /// below the ceiling where a larger card wraps or caps (WMI's AdapterRAM has the same limit), and above it the memory is reported as unknown
    /// rather than guessed.</summary>
    private static (long? Bytes, string Source) ReadWindowsVram(RegistryKey card)
    {
        long? qword = AsInt64(card.GetValue("HardwareInformation.qwMemorySize"));
        if (qword is > 0)
        {
            return (qword, "registry");
        }
        long? dword = AsInt64(card.GetValue("HardwareInformation.MemorySize"));
        if (dword is > 0 and < 4L * 1024 * 1024 * 1024 - 64L * 1024 * 1024)
        {
            return (dword, "registry_dword");
        }
        return (null, "unknown");
    }

    /// <summary>A registry value as a long, or null: QWORD values arrive as long, DWORD as int and some drivers store them as byte arrays.</summary>
    private static long? AsInt64(object value)
    {
        return value switch
        {
            long l => l,
            int i => i,
            byte[] bytes when bytes.Length == 8 => BitConverter.ToInt64(bytes, 0),
            byte[] bytes when bytes.Length == 4 => BitConverter.ToInt32(bytes, 0),
            _ => null
        };
    }

    /// <summary>The PCI vendor and device IDs of the Linux graphics cards ("card0" and the like, not their outputs).</summary>
    private static List<(string Vendor, string Device)> ReadLinuxCardIds()
    {
        List<(string Vendor, string Device)> cards = [];
        if (!Directory.Exists("/sys/class/drm"))
        {
            return cards;
        }
        foreach (string card in Directory.GetDirectories("/sys/class/drm", "card*"))
        {
            // card0 is the device; card0-HDMI-A-1 and the like are its outputs.
            if (Path.GetFileName(card).Contains('-'))
            {
                continue;
            }
            string vendor = ReadFirstLine(Path.Combine(card, "device", "vendor"));
            if (vendor.Length > 0)
            {
                cards.Add((vendor, ReadFirstLine(Path.Combine(card, "device", "device"))));
            }
        }
        return cards;
    }

    /// <summary>The trimmed first line of a small system file, or an empty string when it does not exist.</summary>
    private static string ReadFirstLine(string path)
    {
        if (!File.Exists(path))
        {
            return "";
        }
        using StreamReader reader = new(path);
        return reader.ReadLine()?.Trim() ?? "";
    }

    /// <summary>Whether an adapter is a virtual or indirect display device rather than hardware images run on.</summary>
    private static bool IsVirtualAdapter(string name)
    {
        string lower = (name ?? "").ToLowerFast();
        return lower.Contains("virtual") || lower.Contains("idd") || lower.Contains("basic display") || lower.Contains("basic render");
    }

    /// <summary>The graphics adapters that matter for images: real cards and chips' built-in graphics; virtual display adapters are left out.
    /// Windows reads the display registry key, Linux the sysfs device files plus nvidia-smi, macOS reports its (integrated) chip.</summary>
    private static async Task<List<ProbedGpu>> ReadGpusAsync(string arch)
    {
        List<ProbedGpu> gpus = [];
        if (OperatingSystem.IsWindows())
        {
            foreach (WindowsAdapter adapter in ReadWindowsAdapters())
            {
                if (IsVirtualAdapter(adapter.Name))
                {
                    continue;
                }
                gpus.Add(new ProbedGpu(adapter.Name, HardwareRules.VendorOf(adapter.Name), adapter.VramBytes, adapter.VramSource));
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            bool nvidiaCard = false;
            if (!Directory.Exists("/sys/class/drm"))
            {
                return gpus;
            }
            foreach (string card in Directory.GetDirectories("/sys/class/drm", "card*"))
            {
                // card0 is the device; card0-HDMI-A-1 and the like are its outputs.
                if (Path.GetFileName(card).Contains('-'))
                {
                    continue;
                }
                string vendor = ReadFirstLine(Path.Combine(card, "device", "vendor"));
                if (vendor.Length == 0)
                {
                    continue;
                }
                string device = ReadFirstLine(Path.Combine(card, "device", "device"));
                if (vendor == "0x10de")
                {
                    // Names and memory come from nvidia-smi below; the card is only remembered in case that fails.
                    nvidiaCard = true;
                }
                else if (vendor == "0x1002")
                {
                    string total = ReadFirstLine(Path.Combine(card, "device", "mem_info_vram_total"));
                    long? vram = long.TryParse(total, out long bytes) && bytes > 0 ? bytes : null;
                    gpus.Add(new ProbedGpu($"pci {vendor}:{device}", "amd", vram, vram is null ? "unknown" : "sysfs"));
                }
                else if (vendor == "0x8086")
                {
                    gpus.Add(new ProbedGpu($"pci {vendor}:{device}", "intel", null, "unknown"));
                }
            }
            if (nvidiaCard)
            {
                NvidiaUtil.NvidiaInfo[] cards = await Task.Run(NvidiaUtil.QueryNvidia);
                if (cards is not null && cards.Length > 0)
                {
                    foreach (NvidiaUtil.NvidiaInfo cardInfo in cards)
                    {
                        gpus.Add(new ProbedGpu(cardInfo.GPUName, "nvidia", cardInfo.TotalMemory.InBytes, "nvidia-smi"));
                    }
                }
                else
                {
                    gpus.Add(new ProbedGpu("pci 0x10de", "nvidia", null, "unknown"));
                }
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            // The GPU is part of the chip; its memory is the unified memory reported separately.
            string chip = await RunCaptureAsync("sysctl", "-n machdep.cpu.brand_string", TimeSpan.FromSeconds(5));
            gpus.Add(new ProbedGpu(chip, arch == "arm64" ? "apple" : "intel", null, "unified"));
        }
        return gpus;
    }

    /// <summary>Total system memory: Hardware.Info on Windows and Linux, sysctl on macOS.</summary>
    private static async Task<long?> ReadTotalMemoryAsync()
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                string raw = await RunCaptureAsync("sysctl", "-n hw.memsize", TimeSpan.FromSeconds(5));
                return long.TryParse(raw, out long bytes) && bytes > 0 ? bytes : null;
            }
            Hardware.Info.HardwareInfo info = new();
            info.RefreshMemoryStatus();
            return info.MemoryStatus.TotalPhysical > 0 ? (long)info.MemoryStatus.TotalPhysical : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            Logs.Debug($"EcomCraft could not read the total memory: {ex.GetType().Name}");
            return null;
        }
    }

    /// <summary>Runs a command and returns its first line of output, or an empty string when it fails or does not answer in time.</summary>
    private static async Task<string> RunCaptureAsync(string fileName, string arguments, TimeSpan timeout)
    {
        try
        {
            using Process process = new();
            process.StartInfo = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = false,
                CreateNoWindow = true
            };
            process.Start();
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            if (await Task.WhenAny(output, Task.Delay(timeout, Program.GlobalProgramCancel)) != output || !process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Already gone.
                }
                return "";
            }
            return (await output)?.Trim() ?? "";
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or InvalidOperationException or ObjectDisposedException or OperationCanceledException)
        {
            return "";
        }
    }

    /// <summary>The hardware of this platform server and its tier (F76): facts and the classification, with anything unreadable reported as null
    /// (shown as unknown) instead of guessed. The tier only guides recommendations; every tier may still run (A19).</summary>
    public static async Task<JObject> StatusAsync()
    {
        HardwareRules.Rules rules = LoadRules();
        string os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : OperatingSystem.IsLinux() ? "linux" : "other";
        string arch = RuntimeInformation.OSArchitecture.ToString().ToLowerFast();
        List<ProbedGpu> adapters = [];
        bool gpusRead = true;
        try
        {
            adapters = await ReadGpusAsync(arch);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException or ObjectDisposedException)
        {
            Logs.Debug($"EcomCraft could not read the graphics adapters: {ex.GetType().Name}");
            gpusRead = false;
        }
        long? memory = await ReadTotalMemoryAsync();
        long? disk = StorageProbe.FreeSpace(Environment.CurrentDirectory);
        HardwareRules.Snapshot snapshot = new(os, arch, memory, disk,
            [.. adapters.Select(adapter => new HardwareRules.Gpu(adapter.Name, adapter.Vendor, adapter.VramBytes))], gpusRead);
        HardwareRules.Verdict verdict = HardwareRules.Classify(snapshot, rules);
        JArray gpus = new();
        foreach (ProbedGpu adapter in adapters)
        {
            gpus.Add(new JObject()
            {
                ["name"] = adapter.Name,
                ["vram_mb"] = adapter.VramBytes is long bytes ? HardwareRules.Megabytes(bytes) : null,
                ["vram_source"] = adapter.VramSource
            });
        }
        return new JObject()
        {
            ["os"] = os,
            ["os_version"] = Environment.OSVersion.VersionString,
            ["arch"] = arch,
            ["gpus"] = gpus,
            ["memory_mb"] = memory is long total ? HardwareRules.Megabytes(total) : null,
            ["disk_free_gb"] = disk is long free ? Math.Round(free / (1024.0 * 1024 * 1024), 1) : null,
            ["tier"] = verdict.Tier,
            ["reasons"] = new JArray(verdict.Reasons),
            // A19: the tier never blocks; an administrator may force any recipe on any machine (T112).
            ["force_allowed"] = true
        };
    }

    [API.APIDescription("The hardware of this platform server and its tier (F76, administrators): system, chip architecture, graphics card names with "
        + "video or unified memory, total memory, free disk space, and the tier (recommended, minimum, conditional, unsupported or unknown) with reason "
        + "codes. Values that could not be read come back as null and never make the machine unsupported, and the tier only guides recommendations: "
        + "force_allowed is always true (A19). Only this server is probed (never paths or addresses of anything); other execution servers are judged by "
        + "their nodes and models. Thresholds come from Templates/hardware-tiers.json, overridable by administrators.",
        "tier: \"conditional\", os: \"windows\", arch: \"x64\", gpus: [{ name: \"AMD Radeon RX 5600 XT\", vram_mb: 6128, vram_source: \"registry\" }], "
        + "memory_mb: 16312, disk_free_gb: 245.3, reasons: [\"amd_rocm_listed\"], force_allowed: true")]
    public static async Task<JObject> EcomCraftStudioHardwareStatus(Session session)
    {
        if (!StudioApi.CanView(session) || !session.User.HasPermission(Permissions.EditServerSettings))
        {
            return StudioApi.Error("只有管理员可以查看硬件检测。", "bad_permissions");
        }
        return await StatusAsync();
    }
}
