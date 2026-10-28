using EcomCraftStudio.Studio;
using Newtonsoft.Json.Linq;
using Xunit;

namespace EcomCraftStudio.Tests;

/// <summary>Secret redaction of the diagnostics package (F44): API keys, passwords and tokens never leave the server, ordinary values stay.</summary>
public class DiagnosticsTests
{
    [Fact]
    public void JsonSecretValuesAreRemovedByName()
    {
        JObject config = new()
        {
            ["backend"] = "openai",
            ["api_key"] = "sk-secret-value",
            ["model"] = "gpt-test",
            ["nested"] = new JObject() { ["password"] = "hunter2", ["url"] = "http://local:1234" } ,
            ["list"] = new JArray("a", new JObject() { ["token"] = "abc" })
        };
        JObject clean = (JObject)DiagnosticsSanitizer.RedactJson(config);
        Assert.Equal("openai", clean["backend"]);
        Assert.Equal("[removed]", clean["api_key"]);
        Assert.Equal("gpt-test", clean["model"]);
        Assert.Equal("[removed]", clean["nested"]["password"]);
        Assert.Equal("http://local:1234", clean["nested"]["url"]);
        Assert.Equal("[removed]", clean["list"][1]["token"]);
        Assert.Equal("a", clean["list"][0]);
        // The original object is untouched.
        Assert.Equal("sk-secret-value", config["api_key"]);
    }

    [Fact]
    public void TextSecretsAreRemovedButOrdinaryLinesStay()
    {
        string raw = "EcomCraft export failed: timeout\nAuthorization: Bearer abcdef1234567890\napi_key=sk-1234567890 model=x\npassword: hunter2\nplain line stays";
        string clean = DiagnosticsSanitizer.RedactText(raw);
        Assert.DoesNotContain("abcdef1234567890", clean);
        Assert.DoesNotContain("sk-1234567890", clean);
        Assert.DoesNotContain("hunter2", clean);
        Assert.Contains("Authorization: [removed]", clean);
        Assert.Contains("api_key=[removed]", clean);
        Assert.Contains("plain line stays", clean);
    }
}
