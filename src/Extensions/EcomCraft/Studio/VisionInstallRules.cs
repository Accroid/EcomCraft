namespace EcomCraftStudio.Studio;

/// <summary>Pure rules of the one-click local vision model install (F69): which models the button may install, and how the install
/// script's output is reduced to plain lines the progress view can show.</summary>
public static class VisionInstallRules
{
    /// <summary>The models the button may install (the documented set, in the docs' recommendation order); other models stay with running the script by hand.</summary>
    public static readonly string[] KnownModels = ["qwen3-vl:8b", "qwen3-vl:4b", "qwen3-vl:2b", "qwen3-vl:30b-a3b", "llama3.2-vision:latest"];

    /// <summary>Longest output line kept, so a stray data blob cannot flood the progress view.</summary>
    public const int MaxLineLength = 300;

    /// <summary>Whether the model is one the button may install; the only names the install API accepts.</summary>
    public static bool IsKnownModel(string model)
    {
        return KnownModels.Contains((model ?? "").Trim());
    }

    /// <summary>Normalizes one raw script line for the progress view: carriage-return progress updates become their latest segment,
    /// empty parts are dropped and the rest is capped at <see cref="MaxLineLength"/> characters.</summary>
    public static string CleanLine(string raw)
    {
        string text = (raw ?? "").Replace('\r', '\n');
        string latest = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault("") ?? "";
        return latest.Length > MaxLineLength ? latest[..MaxLineLength] : latest;
    }
}
