using EcomCraftStudio.Studio;
using Newtonsoft.Json.Linq;
using Xunit;

namespace EcomCraftStudio.Tests;

/// <summary>Uploaded own workflows skip review, so code-running nodes and path inputs are refused (F60, decision U6).</summary>
public class OwnWorkflowRulesTests
{
    [Fact]
    public void RefusesNodesThatRunCodeButNotOrdinaryOnes()
    {
        Assert.True(OwnWorkflowRules.IsDeniedType("PythonScriptNode", null));
        Assert.True(OwnWorkflowRules.IsDeniedType("ExecuteCommand", null));
        Assert.True(OwnWorkflowRules.IsDeniedType("Eval Code", null));
        Assert.True(OwnWorkflowRules.IsDeniedType("shell_exec", null));
        Assert.False(OwnWorkflowRules.IsDeniedType("VAEDecode", null), "decode is not code");
        Assert.False(OwnWorkflowRules.IsDeniedType("CLIPTextEncode", null));
        Assert.False(OwnWorkflowRules.IsDeniedType("KSampler", null));
        Assert.True(OwnWorkflowRules.IsDeniedType("SaveTextFile", ["savetextfile"]), "administrators can add node types");
    }

    [Fact]
    public void RefusesInputsThatLookLikePaths()
    {
        Assert.True(OwnWorkflowRules.IsPathLike(@"C:\Windows\win.ini"));
        Assert.True(OwnWorkflowRules.IsPathLike("/etc/passwd"));
        Assert.True(OwnWorkflowRules.IsPathLike(@"\\server\share\a.png"));
        Assert.True(OwnWorkflowRules.IsPathLike("../../secret.json"));
        Assert.True(OwnWorkflowRules.IsPathLike(@"models\..\..\x"));
        Assert.False(OwnWorkflowRules.IsPathLike("sdxl/juggernaut.safetensors"), "model names in subfolders are fine");
        Assert.False(OwnWorkflowRules.IsPathLike("a red mug on a wooden table"));
        Assert.False(OwnWorkflowRules.IsPathLike("https://example.com/a.png"));
        Assert.False(OwnWorkflowRules.IsPathLike("example.png"));
    }

    [Fact]
    public void ReportsEachProblemOnce()
    {
        JObject prompt = JObject.Parse("""
            {
              "1": { "class_type": "PythonExec", "inputs": { "code": "print(1)" } },
              "2": { "class_type": "PythonExec", "inputs": {} },
              "3": { "class_type": "LoadImage", "inputs": { "image": "C:/Users/a.png", "upload": "image" } },
              "4": { "class_type": "KSampler", "inputs": { "model": ["5", 0], "seed": 1 } }
            }
            """);
        List<OwnWorkflowRules.Problem> problems = OwnWorkflowRules.Problems(prompt, null);
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, problem => problem.Code == "denied_node" && problem.Node == "PythonExec");
        Assert.Contains(problems, problem => problem.Code == "path_value" && problem.Node == "LoadImage" && problem.Input == "image");
    }

    [Fact]
    public void TellsTheApiExportFromASavedCanvas()
    {
        Assert.True(OwnWorkflowRules.IsApiFormat(JObject.Parse("""{ "3": { "class_type": "KSampler", "inputs": {} } }""")));
        Assert.False(OwnWorkflowRules.IsApiFormat(JObject.Parse("""{ "nodes": [], "links": [] }""")));
        Assert.False(OwnWorkflowRules.IsApiFormat(new JObject()));
    }

    [Fact]
    public void CleansDisplayNames()
    {
        Assert.Equal("主图 工作流", OwnWorkflowRules.CleanName("  主图 工作流.json "));
        Assert.Equal("", OwnWorkflowRules.CleanName("\u0007"));
        Assert.Equal(OwnWorkflowRules.MaxNameLength, OwnWorkflowRules.CleanName(new string('a', 100)).Length);
    }
}
