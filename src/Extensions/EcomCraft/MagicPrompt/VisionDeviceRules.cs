using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EcomCraftStudio.MagicPrompt;

/// <summary>Which device a local Ollama vision model runs on (F71): some graphics cards (such as older AMD cards running through Vulkan) hang on vision models,
/// so "auto" gives the first request a limited time on the GPU and moves to the CPU when it does not answer.</summary>
public static class VisionDeviceRules
{
    /// <summary>Let Ollama choose, and move to the CPU when the GPU hangs.</summary>
    public const string Auto = "auto";

    /// <summary>Let Ollama choose and never move to the CPU.</summary>
    public const string Gpu = "gpu";

    /// <summary>Always run on the CPU (num_gpu 0).</summary>
    public const string Cpu = "cpu";

    /// <summary>How long the first "auto" request per model may take on the GPU before it is treated as hung.</summary>
    public static readonly TimeSpan GpuProbe = TimeSpan.FromSeconds(60);

    /// <summary>Context length for vision requests: a description needs far less than the model default and a smaller context saves video memory.</summary>
    public const int VisionContext = 4096;

    /// <summary>The saved device setting, with anything unknown read as <see cref="Auto"/>.</summary>
    public static string Normalize(string setting)
    {
        string value = (setting ?? "").Trim().ToLowerInvariant();
        return value is Gpu or Cpu ? value : Auto;
    }

    /// <summary>How long the self-test (F71 P2) waits for the test image on the CPU.</summary>
    public static readonly TimeSpan CpuTest = TimeSpan.FromSeconds(120);

    /// <summary>Most self-test results kept; the oldest are dropped first.</summary>
    public const int MaxRecords = 20;

    /// <summary>One saved self-test result: the device chosen, the seconds one image took on it, and when it was measured.</summary>
    public sealed record class Record(string Device, double Seconds, DateTime Checked);

    /// <summary>The key a result is saved under (F71 P2): the Ollama version, the model and the graphics, so a change of any of them tests again.</summary>
    public static string Key(string ollamaVersion, string model, string graphics) => $"{ollamaVersion?.Trim()}|{model?.Trim()}|{graphics?.Trim()}";

    /// <summary>The device the self-test picks from the seconds each one took (null when it gave no answer): the faster one that answered, or null when neither did.</summary>
    public static string Choose(double? gpuSeconds, double? cpuSeconds)
    {
        if (gpuSeconds is null && cpuSeconds is null)
        {
            return null;
        }
        if (cpuSeconds is null || (gpuSeconds is not null && gpuSeconds <= cpuSeconds))
        {
            return Gpu;
        }
        return Cpu;
    }

    /// <summary>The wait for one image once its speed is known: five times the measured seconds, at least 90 seconds, at most the configured timeout.</summary>
    public static int TimeoutFor(double seconds, int configured)
    {
        int measured = (int)Math.Ceiling(Math.Max(90, seconds * 5));
        return Math.Min(configured, measured);
    }

    /// <summary>Reads the saved results; entries with an unknown device or no time are skipped. Throws on text that is not JSON, so a damaged file is never overwritten.</summary>
    public static Dictionary<string, Record> Parse(string json)
    {
        Dictionary<string, Record> records = [];
        JObject root = JObject.Parse(json);
        foreach (JProperty entry in (root["entries"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
        {
            if (entry.Value is not JObject value)
            {
                continue;
            }
            string device = value["device"]?.ToString();
            double seconds = value["seconds"]?.Type is JTokenType.Float or JTokenType.Integer ? value.Value<double>("seconds") : -1;
            DateTime checkedAt = value["checked"]?.Type == JTokenType.Date ? value.Value<DateTime>("checked") : DateTime.MinValue;
            if (device is not (Gpu or Cpu) || seconds <= 0)
            {
                continue;
            }
            records[entry.Name] = new Record(device, seconds, checkedAt);
        }
        return records;
    }

    /// <summary>The saved results as JSON: only keys, devices, seconds and times.</summary>
    public static string Serialize(Dictionary<string, Record> records)
    {
        JObject entries = new();
        foreach ((string key, Record record) in records)
        {
            entries[key] = new JObject() { ["device"] = record.Device, ["seconds"] = Math.Round(record.Seconds, 1), ["checked"] = record.Checked };
        }
        return new JObject() { ["entries"] = entries }.ToString(Formatting.Indented);
    }

    /// <summary>Adds or replaces one result, then drops the oldest beyond <see cref="MaxRecords"/>.</summary>
    public static void Put(Dictionary<string, Record> records, string key, Record record)
    {
        records[key] = record;
        foreach (string old in records.OrderBy(entry => entry.Value.Checked).Take(Math.Max(0, records.Count - MaxRecords)).Select(entry => entry.Key).ToList())
        {
            records.Remove(old);
        }
    }

    /// <summary>How to send one request: whether to force the CPU, and whether to give the GPU a limited try first.</summary>
    /// <param name="setting">The saved device setting.</param>
    /// <param name="remembered">What "auto" learned for this model since start: true when it moved to the CPU, false when the GPU answered, null when not yet tried.</param>
    public static (bool Cpu, bool ProbeGpu) Plan(string setting, bool? remembered)
    {
        return Normalize(setting) switch
        {
            Cpu => (true, false),
            Gpu => (false, false),
            _ => remembered switch
            {
                true => (true, false),
                false => (false, false),
                null => (false, true)
            }
        };
    }
}
