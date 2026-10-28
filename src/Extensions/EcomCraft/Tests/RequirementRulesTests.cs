using EcomCraftStudio.Studio.Templates;
using Newtonsoft.Json.Linq;
using Xunit;

namespace EcomCraftStudio.Tests;

/// <summary>Template model and node requirements (T14): license fields are validated and missing items are found per server.</summary>
public class RequirementRulesTests
{
    [Fact]
    public void ValidRequirementsAreRead()
    {
        List<string> errors = [];
        (List<ModelRequirement> models, List<string> nodes) = RequirementRules.Parse(JObject.Parse("""
            { "models": [
                { "name": "sdxl/base.safetensors", "folder": "checkpoints", "commercial": "allowed", "license_url": "https://example.com/license" },
                { "name": "detail.safetensors", "folder": "loras" } ],
              "nodes": ["SwarmRemBg", "KSampler"] }
            """), errors);
        Assert.Empty(errors);
        Assert.Equal(2, models.Count);
        Assert.Equal("allowed", models[0].Commercial);
        Assert.Equal("unknown", models[1].Commercial);
        Assert.Null(models[1].LicenseUrl);
        Assert.Equal(new List<string> { "SwarmRemBg", "KSampler" }, nodes);
    }

    [Theory]
    [InlineData("""{ "models": [{ "name": "a.safetensors", "folder": "checkpoints", "commercial": "maybe" }] }""")]
    [InlineData("""{ "models": [{ "name": "a.safetensors", "folder": "C:/models", "commercial": "allowed" }] }""")]
    [InlineData("""{ "models": [{ "name": "../a.safetensors", "folder": "checkpoints" }] }""")]
    [InlineData("""{ "models": [{ "name": "a.safetensors", "folder": "loras", "license_url": "javascript:alert(1)" }] }""")]
    [InlineData("""{ "models": [{ "name": "a.safetensors", "folder": "loras", "download_url": "file:///etc/passwd" }] }""")]
    [InlineData("""{ "nodes": ["<script>"] }""")]
    [InlineData("""{ "extra": [] }""")]
    public void InvalidRequirementsAreRejected(string json)
    {
        List<string> errors = [];
        RequirementRules.Parse(JObject.Parse(json), errors);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void TemplateWithoutRequirementsIsValid()
    {
        List<string> errors = [];
        (List<ModelRequirement> models, List<string> nodes) = RequirementRules.Parse(null, errors);
        Assert.Empty(errors);
        Assert.Empty(models);
        Assert.Empty(nodes);
    }

    [Fact]
    public void MissingItemsAreFoundFromObjectInfo()
    {
        JObject objectInfo = JObject.Parse("""
            { "CheckpointLoaderSimple": { "input": { "required": { "ckpt_name": [["sdxl\\base.safetensors", "other.safetensors"]] } } },
              "LoraLoader": { "input": { "required": { "lora_name": [["styles/detail.safetensors"]], "strength": ["FLOAT", { "default": 1 }] } } } }
            """);
        HashSet<string> offered = RequirementRules.OfferedModels(objectInfo);
        List<ModelRequirement> models =
        [
            new("sdxl/base.safetensors", "checkpoints", "allowed", null, null),
            new("detail.safetensors", "loras", "unknown", null, null),
            new("absent.safetensors", "vae", "forbidden", null, null)
        ];
        Assert.Equal(new List<string> { "absent.safetensors" }, RequirementRules.MissingModels(models, offered).Select(model => model.Name).ToList());
        HashSet<string> nodeTypes = [.. objectInfo.Properties().Select(property => property.Name)];
        Assert.Equal(new List<string> { "SwarmRemBg" }, RequirementRules.MissingNodes(["LoraLoader", "SwarmRemBg"], nodeTypes));
    }
}
