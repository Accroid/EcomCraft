using EcomCraftStudio.Studio.Templates;
using Newtonsoft.Json.Linq;
using Xunit;

namespace EcomCraftStudio.Tests;

/// <summary>Template structure and snapshot checks (F01): valid templates parse; invalid ones are rejected with reasons.</summary>
public class TemplateValidatorTests
{
    /// <summary>A valid template referencing workflow "B/white".</summary>
    private static JObject Valid() => JObject.Parse("""
        {
          "id": "white-main",
          "name": { "zh-CN": "白底主图", "en": "White background main image" },
          "description": { "zh-CN": "纯白背景", "en": "Pure white background" },
          "example_image": "white-main.webp",
          "platforms": ["amazon", "taobao"],
          "sets": ["main-set"],
          "prompt": { "prefix": "product photo,", "suffix": "studio light", "negative": "text, watermark" },
          "categories": [],
          "brand_kit": null,
          "execution_plans": [ { "kind": "local", "workflow": "B/white", "presets": { "steps": 30 }, "user_fields": ["strength"] } ]
        }
        """);

    /// <summary>Parameter definitions of the published snapshot "B/white".</summary>
    private static JObject Snapshot() => JObject.Parse("""
        {
          "steps": { "type": "integer", "min": 1, "max": 100 },
          "strength": { "type": "decimal", "min": 0, "max": 1 },
          "hidden_seed": { "type": "integer", "visible": false },
          "positive": { "type": "text", "form": "prompt" },
          "product": { "type": "image" },
          "sampler": { "type": "dropdown", "values": ["euler", "dpmpp_2m"] }
        }
        """);

    /// <summary>Parses a template and returns the errors.</summary>
    private static List<string> Errors(JObject json)
    {
        List<string> errors = [];
        TemplateValidator.Parse(json, errors);
        return errors;
    }

    [Fact]
    public void ValidTemplateParsesWithAllFields()
    {
        List<string> errors = [];
        EcomTemplate template = TemplateValidator.Parse(Valid(), errors);
        Assert.Empty(errors);
        Assert.Equal("white-main", template.Id);
        Assert.Equal("White background main image", EcomTemplate.Pick(template.Name, "en"));
        Assert.Equal("白底主图", EcomTemplate.Pick(template.Name, "fr"));
        Assert.Equal(new List<string> { "amazon", "taobao" }, template.Platforms);
        Assert.Equal("B/white", template.LocalPlan.Workflow);
        Assert.Empty(TemplateValidator.CheckPlan(template.LocalPlan, Snapshot()));
    }

    [Fact]
    public void RejectsBadIdMissingEnglishNameAndUnknownField()
    {
        JObject json = Valid();
        json["id"] = "White Main";
        json["name"] = new JObject { ["zh-CN"] = "白底主图" };
        json["colour"] = "red";
        List<string> errors = Errors(json);
        Assert.Contains(errors, error => error.StartsWith("id "));
        Assert.Contains(errors, error => error.Contains("缺少 en"));
        Assert.Contains(errors, error => error.Contains("colour"));
    }

    [Fact]
    public void RejectsCloudPlansAndMissingLocalPlan()
    {
        JObject json = Valid();
        json["execution_plans"] = JArray.Parse("""[ { "kind": "cloud_comfy", "workflow": "x" } ]""");
        List<string> errors = Errors(json);
        Assert.Contains(errors, error => error.Contains("云端"));
        Assert.Contains(errors, error => error.Contains("有且只有一个本地执行方案"));
    }

    [Fact]
    public void RejectsBrandKitAndNativePromptSyntax()
    {
        JObject json = Valid();
        json["brand_kit"] = new JObject();
        json["prompt"]["prefix"] = "<lora:x>";
        List<string> errors = Errors(json);
        Assert.Contains(errors, error => error.Contains("brand_kit"));
        Assert.Contains(errors, error => error.Contains("prompt.prefix"));
    }

    [Fact]
    public void UnpublishedSnapshotIsReported()
    {
        List<string> errors = [];
        EcomTemplate template = TemplateValidator.Parse(Valid(), errors);
        Assert.Single(TemplateValidator.CheckPlan(template.LocalPlan, null));
    }

    [Fact]
    public void PresetsMustBeWhitelistedValidAndNotPromptOrImage()
    {
        ExecutionPlan plan = new()
        {
            Kind = ExecutionPlan.Local,
            Workflow = "B/white",
            Presets = JObject.Parse("""{ "steps": 500, "unknown": 1, "positive": "x", "product": "data:", "sampler": "ddim" }""")
        };
        List<string> errors = TemplateValidator.CheckPlan(plan, Snapshot());
        Assert.Equal(5, errors.Count);
    }

    [Fact]
    public void UserFieldsMustBeVisibleAndNotPreset()
    {
        ExecutionPlan plan = new()
        {
            Kind = ExecutionPlan.Local,
            Workflow = "B/white",
            Presets = JObject.Parse("""{ "steps": 20 }"""),
            UserFields = ["steps", "hidden_seed", "missing", "product"]
        };
        List<string> errors = TemplateValidator.CheckPlan(plan, Snapshot());
        Assert.Equal(3, errors.Count);
    }
}
