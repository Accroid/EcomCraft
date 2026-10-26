using EcomCraftStudio.MagicPrompt.WebAPI;
using Newtonsoft.Json.Linq;

/// <summary>Exercises migration and explicit limits without contacting providers or modifying user settings.</summary>
internal static class GenerationTimeoutTests
{
    /// <summary>Checks the effective limit and preservation of the caller's configuration.</summary>
    private static void Check(string json, int expected)
    {
        JObject settings = JObject.Parse(json);
        string original = settings.ToString();
        if (GenerationTimeout.Seconds(settings, "OpenAIAPI") != expected || settings.ToString() != original)
        {
            throw new Exception("Generation timeout policy or non-mutating configuration behavior failed.");
        }
    }

    /// <summary>Verifies legacy defaults, overrides, invalid settings and bounded cancellation deadlines.</summary>
    public static void Main()
    {
        Check("{}", 120);
        Check("""{"backends":{"openaiapi":{"timeout":20}}}""", 120);
        Check("""{"backends":{"openaiapi":{"timeout":90}}}""", 90);
        Check("""{"backends":{"openaiapi":{"timeout":20,"generation_timeout":180}}}""", 180);
        Check("""{"backends":{"openaiapi":{"timeout":20,"generation_timeout":20}}}""", 20);
        Check("""{"backends":{"openaiapi":{"generation_timeout":999999}}}""", 600);
        Check("""{"backends":{"openaiapi":{"generation_timeout":0}}}""", 120);
        Check("""{"backends":{"openaiapi":{"generation_timeout":-1}}}""", 120);
        Check("""{"backends":{"openaiapi":{"generation_timeout":"invalid"}}}""", 120);
        Console.WriteLine("Generation timeout policy: 9 checks passed; saved settings unchanged.");
    }
}
