using System.Text.RegularExpressions;

namespace EcomCraftStudio.Studio;

/// <summary>Pure rules of the one-click local vision model install (F69): which models the button may install, and how the install
/// script's output is reduced to plain lines the progress view can show.</summary>
public static class VisionInstallRules
{
    /// <summary>The models the button may install (the documented set, in the docs' recommendation order); other models stay with running the script by hand.
    /// Qwen3-VL uses the instruct tags: the plain tags are the thinking variants, which reason before every description and ignore Ollama's think:false (F71).</summary>
    public static readonly string[] KnownModels = ["qwen3-vl:8b-instruct", "qwen3-vl:4b-instruct", "qwen3-vl:2b-instruct", "qwen3-vl:30b-a3b-instruct", "llama3.2-vision:latest"];

    /// <summary>Longest output line kept, so a stray data blob cannot flood the progress view.</summary>
    public const int MaxLineLength = 300;

    /// <summary>Whether the model is one the button may install; the only names the install API accepts.</summary>
    public static bool IsKnownModel(string model)
    {
        return KnownModels.Contains((model ?? "").Trim());
    }

    /// <summary>Terminal control sequences in the script output (Ollama draws its progress with them): CSI sequences such as "ESC[K",
    /// "ESC[?25h" or "ESC[1G", OSC sequences and other two-character escapes.</summary>
    private static readonly Regex ControlSequence = new(@"\x1B\[[0-9;?]*[ -/]*[@-~]|\x1B\][^\x07\x1B]*(?:\x07|\x1B\\)|\x1B[@-_]", RegexOptions.Compiled);

    /// <summary>Progress bar drawing characters (the Unicode block elements).</summary>
    private static readonly Regex Bar = new(@"[▀-▟]+", RegexOptions.Compiled);

    /// <summary>Runs of spaces, such as the ones the removed bar leaves behind.</summary>
    private static readonly Regex Spaces = new(@"\s{2,}", RegexOptions.Compiled);

    /// <summary>Normalizes one raw script line for the progress view: terminal control sequences are removed, carriage returns and cursor moves
    /// split redrawn progress into segments, and the latest segment with a percentage wins (else the latest one). The progress bar characters
    /// are dropped and the rest is capped at <see cref="MaxLineLength"/> characters.</summary>
    public static string CleanLine(string raw)
    {
        // Cursor moves (ESC[A, ESC[1G, …) start a redraw like a carriage return does; other sequences only style the text.
        string text = ControlSequence.Replace(raw ?? "", match => match.Value.StartsWith("\x1B[", StringComparison.Ordinal) && match.Value[^1] is >= 'A' and <= 'H' ? "\n" : "");
        List<string> segments = [];
        foreach (string part in text.Replace('\r', '\n').Split('\n'))
        {
            string plain = Spaces.Replace(string.Concat(Bar.Replace(part, " ").Where(c => !char.IsControl(c))), " ").Trim();
            if (plain.Length > 0)
            {
                segments.Add(plain);
            }
        }
        string latest = segments.LastOrDefault(segment => segment.Contains('%')) ?? segments.LastOrDefault("");
        return latest.Length > MaxLineLength ? latest[..MaxLineLength] : latest;
    }

    /// <summary>Whether a new progress line replaces the previous one instead of being added: the same text again, or a newer percentage
    /// of the same step (the part before the first colon, such as "pulling ee8b93fa272a").</summary>
    public static bool Replaces(string previous, string line)
    {
        if (previous == line)
        {
            return true;
        }
        int colon = line.IndexOf(':');
        return colon > 0 && line.Contains('%') && previous.Contains('%') && previous.StartsWith(line[..(colon + 1)], StringComparison.Ordinal);
    }
}
