using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using EcomCraftStudio.MagicPrompt.WebAPI;
using FreneticUtilities.FreneticExtensions;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SwarmUI.Core;
using SwarmUI.Utils;

namespace EcomCraftStudio.MagicPrompt;

/// <summary>The vision device self-test (F71 P2): runs a built-in test image on the GPU and then the CPU with time limits, keeps the faster device that answered,
/// and saves it with the measured seconds by "Ollama version | model | graphics", so restarts and later requests use it without waiting on a hung GPU again.
/// The saved file holds only those keys, devices, seconds and times: no images, prompts or addresses (a LAN Ollama is told apart by a one-way digest of its address).</summary>
public static class VisionDeviceCheck
{
    /// <summary>The saved results.</summary>
    private static string RecordsFile => Path.Combine(Program.DataDir, "EcomCraft", "vision-device.json");

    /// <summary>Protects <see cref="Records"/> and the file.</summary>
    private static readonly object RecordsLock = new();

    /// <summary>The saved results, read on first use.</summary>
    private static Dictionary<string, VisionDeviceRules.Record> Records;

    /// <summary>Whether the file existed but could not be read: results are then kept in memory only, so the file is never overwritten.</summary>
    private static bool RecordsDamaged;

    /// <summary>The key last worked out per Ollama address and model, and when, so the Ollama version is not asked on every request.</summary>
    private static readonly ConcurrentDictionary<string, (DateTime Checked, string Key)> Keys = new();

    /// <summary>How long a worked-out key is reused.</summary>
    private static readonly TimeSpan KeyTtl = TimeSpan.FromMinutes(1);

    /// <summary>The graphics of this computer, read once.</summary>
    private static readonly Lazy<string> LocalGraphics = new(ReadLocalGraphics);

    /// <summary>The built-in test image as base64 PNG, drawn once.</summary>
    private static readonly Lazy<string> TestImage = new(DrawTestImage);

    /// <summary>Protects <see cref="RunningKey"/> and <see cref="FailedKey"/>.</summary>
    private static readonly object RunLock = new();

    /// <summary>The key being tested now, or null.</summary>
    private static string RunningKey;

    /// <summary>The key whose last test got no answer on either device; it is not tested again automatically until an administrator asks.</summary>
    private static string FailedKey;

    /// <summary>The instruction for the test image: close to a real reference description, so the measured time is representative.</summary>
    private const string TestInstruction = "You write prompts for a text-to-image model. Describe how this product photo is set up: shot type and camera angle, "
        + "where the product sits in the frame, the background, lighting and mood. Reply with one English paragraph of about 80 words.";

    /// <summary>The key results are saved under for this vision service and model, or null when the Ollama version cannot be read.</summary>
    /// <param name="settings">The vision view of the settings.</param>
    public static async Task<string> KeyAsync(JObject settings, string model, string keyOverride)
    {
        string baseUrl = (settings?["backends"] as JObject)?["ollama"]?["baseurl"]?.ToString()?.TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrWhiteSpace(model))
        {
            return null;
        }
        string cacheKey = $"{baseUrl}|{model}";
        if (Keys.TryGetValue(cacheKey, out (DateTime Checked, string Key) cached) && DateTime.UtcNow - cached.Checked < KeyTtl)
        {
            return cached.Key;
        }
        string version = await LLMAPICalls.OllamaVersionAsync(settings, keyOverride);
        string key = version is null ? null : VisionDeviceRules.Key(version, model, Graphics(baseUrl));
        Keys[cacheKey] = (DateTime.UtcNow, key);
        return key;
    }

    /// <summary>The graphics part of the key: this computer's graphics when Ollama runs here, otherwise a digest of the address, since another computer's graphics cannot be read.</summary>
    private static string Graphics(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri uri))
        {
            return "unknown";
        }
        if (uri.IsLoopback)
        {
            return LocalGraphics.Value;
        }
        return $"lan:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.Authority.ToLowerFast())))[..16]}";
    }

    /// <summary>The names and driver versions of this computer's graphics, sorted; "unknown" when they cannot be read.</summary>
    private static string ReadLocalGraphics()
    {
        List<string> cards = [];
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using RegistryKey adapters = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                foreach (string name in adapters?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    // Only the numbered adapter entries; "Properties" cannot be opened.
                    if (!int.TryParse(name, out _))
                    {
                        continue;
                    }
                    using RegistryKey card = adapters.OpenSubKey(name);
                    string description = card?.GetValue("DriverDesc") as string;
                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        cards.Add($"{description.Trim()} {card.GetValue("DriverVersion") as string}".Trim());
                    }
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                if (Directory.Exists("/sys/class/drm"))
                {
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
                            cards.Add($"{vendor}:{ReadFirstLine(Path.Combine(card, "device", "device"))}");
                        }
                    }
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

    /// <summary>Reads the saved results on first use; call inside <see cref="RecordsLock"/>.</summary>
    private static void EnsureLoaded()
    {
        if (Records is not null)
        {
            return;
        }
        try
        {
            Records = File.Exists(RecordsFile) ? VisionDeviceRules.Parse(File.ReadAllText(RecordsFile)) : new Dictionary<string, VisionDeviceRules.Record>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Logs.Warning($"EcomCraft could not read the saved vision device results; new ones are kept until restart and the file is left as it is: {ex.GetType().Name}");
            Records = [];
            RecordsDamaged = true;
        }
    }

    /// <summary>The saved result for a key, or null.</summary>
    public static VisionDeviceRules.Record Find(string key)
    {
        if (key is null)
        {
            return null;
        }
        lock (RecordsLock)
        {
            EnsureLoaded();
            return Records.TryGetValue(key, out VisionDeviceRules.Record record) ? record : null;
        }
    }

    /// <summary>Saves the device and seconds measured for a key.</summary>
    public static void Save(string key, string device, double seconds)
    {
        if (key is null || seconds <= 0)
        {
            return;
        }
        lock (RecordsLock)
        {
            EnsureLoaded();
            VisionDeviceRules.Put(Records, key, new VisionDeviceRules.Record(device, seconds, DateTime.UtcNow));
            WriteRecords();
        }
    }

    /// <summary>Removes the saved result for a key, so the next request or check decides again.</summary>
    public static void Forget(string key)
    {
        if (key is null)
        {
            return;
        }
        lock (RecordsLock)
        {
            EnsureLoaded();
            if (Records.Remove(key))
            {
                WriteRecords();
            }
        }
    }

    /// <summary>Writes the results through a temporary file; call inside <see cref="RecordsLock"/>. Skipped when the file could not be read.</summary>
    private static void WriteRecords()
    {
        if (RecordsDamaged)
        {
            return;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RecordsFile));
            string temp = $"{RecordsFile}.tmp";
            File.WriteAllText(temp, VisionDeviceRules.Serialize(Records));
            File.Move(temp, RecordsFile, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logs.Warning($"EcomCraft could not save the vision device results: {ex.Message}");
        }
    }

    /// <summary>Whether the self-test applies to these settings: a local Ollama vision model in "auto" mode.</summary>
    /// <param name="settings">The MagicPrompt settings as saved.</param>
    private static bool Applies(JObject settings) => settings is not null && LLMAPICalls.VisionBackend(settings) == "ollama"
        && !string.IsNullOrWhiteSpace(settings["visionmodel"]?.ToString()) && VisionDeviceRules.Normalize(settings["visiondevice"]?.ToString()) == VisionDeviceRules.Auto;

    /// <summary>The self-test state for the configured vision model: "none" (not tested or not applicable), "running", "done" or "failed", with the saved device and seconds.</summary>
    /// <param name="settings">The MagicPrompt settings as saved.</param>
    public static async Task<JObject> StatusAsync(JObject settings)
    {
        if (!Applies(settings))
        {
            return new JObject() { ["state"] = "none" };
        }
        string key = await KeyAsync(LLMAPICalls.VisionSettings(settings), settings["visionmodel"].ToString(), LLMAPICalls.VisionKeyOverride(settings, null));
        VisionDeviceRules.Record record = Find(key);
        bool running, failed;
        lock (RunLock)
        {
            running = key is not null && RunningKey == key;
            failed = key is not null && FailedKey == key;
        }
        string state = running ? "running" : record is not null ? "done" : failed ? "failed" : "none";
        return new JObject()
        {
            ["state"] = state,
            ["device"] = record?.Device,
            ["seconds"] = record is null ? null : Math.Round(record.Seconds, 1)
        };
    }

    /// <summary>Starts the self-test in the background when it applies: automatically only when the current combination has no result yet and did not just fail,
    /// or always when <paramref name="force"/> is set (the administrator's "test again"). Returns whether a test is now running for it.</summary>
    /// <param name="settings">The MagicPrompt settings as saved.</param>
    public static async Task<bool> StartAsync(JObject settings, bool force)
    {
        if (!Applies(settings))
        {
            return false;
        }
        JObject view = LLMAPICalls.VisionSettings(settings);
        string keyOverride = LLMAPICalls.VisionKeyOverride(settings, null);
        string model = settings["visionmodel"].ToString();
        string key = await KeyAsync(view, model, keyOverride);
        if (key is null)
        {
            return false;
        }
        lock (RunLock)
        {
            if (RunningKey is not null)
            {
                return RunningKey == key;
            }
            if (!force && (Find(key) is not null || FailedKey == key))
            {
                return false;
            }
            RunningKey = key;
        }
        _ = Task.Run(() => RunAsync(settings, view, model, key, keyOverride));
        return true;
    }

    /// <summary>Runs the test image on the GPU, then the CPU, and saves the faster device that answered.</summary>
    private static async Task RunAsync(JObject settings, JObject view, string model, string key, string keyOverride)
    {
        try
        {
            Logs.Info($"EcomCraft is testing whether the vision model {model} runs faster on the GPU or the CPU.");
            BackendSchema.MessageContent content = new()
            {
                Text = "Describe the setup of this product photo.",
                Instructions = TestInstruction,
                Media = [new BackendSchema.MediaContent() { Type = "base64", Data = TestImage.Value, MediaType = "image/png" }]
            };
            double? gpu = await LLMAPICalls.TimeOllamaVisionAsync(view, model, content, false, VisionDeviceRules.GpuProbe, keyOverride);
            double? cpu = await LLMAPICalls.TimeOllamaVisionAsync(view, model, content, true, VisionDeviceRules.CpuTest, keyOverride);
            string device = VisionDeviceRules.Choose(gpu, cpu);
            Logs.Info($"EcomCraft vision self-test for {model}: GPU {(gpu is null ? "no answer" : $"{gpu:F1} s")}, CPU {(cpu is null ? "no answer" : $"{cpu:F1} s")}; using {device?.ToUpperInvariant() ?? "neither"}.");
            if (device is null)
            {
                lock (RunLock)
                {
                    FailedKey = key;
                }
                return;
            }
            Save(key, device, device == VisionDeviceRules.Gpu ? gpu.Value : cpu.Value);
            // What a request learned earlier in this run gives way to the measured result.
            LLMAPICalls.ForgetLearnedVisionDevice(settings);
            lock (RunLock)
            {
                if (FailedKey == key)
                {
                    FailedKey = null;
                }
            }
        }
        catch (Exception ex)
        {
            Logs.Warning($"EcomCraft vision self-test failed: {ex.GetType().Name}");
            lock (RunLock)
            {
                FailedKey = key;
            }
        }
        finally
        {
            lock (RunLock)
            {
                RunningKey = null;
            }
        }
    }

    /// <summary>Draws the test image: a plain bottle with a cap and a soft shadow on a light backdrop, like a simple product photo. It holds no user data.</summary>
    private static string DrawTestImage()
    {
        const int size = 512;
        using Image<Rgba32> image = new(size, size);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Warm backdrop that gets darker towards the bottom, like a sweep.
                byte shade = (byte)(236 - y * 40 / size);
                Rgba32 color = new(shade, (byte)(shade - 8), (byte)(shade - 20), 255);
                double shadowX = (x - 256) / 120.0;
                double shadowY = (y - 432) / 18.0;
                if (shadowX * shadowX + shadowY * shadowY < 1)
                {
                    color = new Rgba32((byte)(color.R - 50), (byte)(color.G - 50), (byte)(color.B - 50), 255);
                }
                if (x >= 196 && x < 316 && y >= 170 && y < 430)
                {
                    // The bottle, lit from the left.
                    byte light = (byte)(170 - (x - 196) * 70 / 120);
                    color = new Rgba32(30, light, (byte)(light - 20), 255);
                }
                else if (x >= 226 && x < 286 && y >= 120 && y < 170)
                {
                    color = new Rgba32(245, 245, 240, 255);
                }
                image[x, y] = color;
            }
        }
        using MemoryStream stream = new();
        image.SaveAsPng(stream);
        return Convert.ToBase64String(stream.ToArray());
    }
}
