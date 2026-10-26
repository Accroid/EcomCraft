using Newtonsoft.Json.Linq;
using System.Globalization;

namespace EcomCraftStudio.MagicPrompt.WebAPI;

/// <summary>Separates completion latency from short model-list requests without rewriting saved settings.</summary>
public static class GenerationTimeout
{
    /// <summary>Allows time for a complete non-streaming response, including provider queueing.</summary>
    public const int DefaultSeconds = 120;

    /// <summary>Uses an explicit generation limit or a customized legacy limit; replaces the old shared 20-second default.</summary>
    public static int Seconds(JObject settings, string backend)
    {
        JObject config = (settings?["backends"] as JObject)?.GetValue(backend ?? "", StringComparison.OrdinalIgnoreCase) as JObject;
        JToken explicitLimit = config?["generation_timeout"];
        JToken limit = explicitLimit ?? config?["timeout"];
        if (limit is null || !int.TryParse(limit.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds)
            || seconds <= 0 || (explicitLimit is null && seconds == 20))
        {
            return DefaultSeconds;
        }
        return Math.Clamp(seconds, 1, 600);
    }
}
