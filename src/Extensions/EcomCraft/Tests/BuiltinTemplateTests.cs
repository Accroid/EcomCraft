using System.IO;
using EcomCraftStudio.Studio.Export;
using EcomCraftStudio.Studio.Templates;
using Newtonsoft.Json.Linq;
using Xunit;

namespace EcomCraftStudio.Tests;

/// <summary>The built-in templates (T04) parse, name both languages, and report a missing workflow instead of failing.</summary>
public class BuiltinTemplateTests
{
    /// <summary>The built-in template files copied next to the test assembly.</summary>
    private static string[] Files() => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "builtin"), "*.json");

    /// <summary>A published snapshot with one visible image parameter, standing in for an expert-made workflow.</summary>
    private static JObject Snapshot() => JObject.Parse("""{ "n1_image": { "type": "image" }, "n2_steps": { "type": "integer", "min": 1, "max": 100 } }""");

    [Fact]
    public void FourBuiltinTemplatesParseWithBothLanguages()
    {
        string[] files = Files();
        Assert.Equal(4, files.Length);
        foreach (string file in files)
        {
            List<string> errors = [];
            EcomTemplate template = TemplateValidator.Parse(JObject.Parse(File.ReadAllText(file)), errors);
            Assert.True(errors.Count == 0, $"{Path.GetFileName(file)}: {string.Join(" ", errors)}");
            Assert.False(string.IsNullOrWhiteSpace(EcomTemplate.Pick(template.Name, "en")));
            Assert.False(string.IsNullOrWhiteSpace(template.Name.GetValueOrDefault("zh-CN")));
            Assert.False(string.IsNullOrWhiteSpace(template.Description.GetValueOrDefault("en")));
            Assert.StartsWith(TemplateValidator.AnyServerPrefix, template.LocalPlan.Workflow);
            Assert.Contains(template.Kind, ExportSlot.Kinds);
        }
    }

    [Fact]
    public void MissingWorkflowIsReportedNotThrown()
    {
        foreach (string file in Files())
        {
            List<string> errors = [];
            EcomTemplate template = TemplateValidator.Parse(JObject.Parse(File.ReadAllText(file)), errors);
            List<string> problems = TemplateValidator.CheckPlan(template.LocalPlan, null, template.PreserveProduct);
            Assert.Single(problems);
            Assert.Contains("缺少工作流", problems[0]);
        }
    }

    [Fact]
    public void ImagePlaceholderResolvesAgainstThePublishedSnapshot()
    {
        foreach (string file in Files())
        {
            List<string> errors = [];
            EcomTemplate template = TemplateValidator.Parse(JObject.Parse(File.ReadAllText(file)), errors);
            Assert.Empty(TemplateValidator.CheckPlan(template.LocalPlan, Snapshot(), template.PreserveProduct));
            Assert.Equal(new List<string> { "n1_image" }, TemplateValidator.ExpandFields(template.LocalPlan.UserFields, Snapshot()));
        }
    }
}
