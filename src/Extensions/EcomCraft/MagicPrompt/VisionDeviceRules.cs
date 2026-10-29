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
