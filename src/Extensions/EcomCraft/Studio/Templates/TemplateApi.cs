using EcomCraftStudio.Studio.PostProcess;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio.Templates;

/// <summary>Template catalog for users (F01) and the conversion of a template request into a published-workflow request.
/// The catalog follows minimal disclosure: template ID, name, description, example image, platforms, sets and the fields users may change.
/// Generation by template still goes through the published-workflow allowlist checks.</summary>
[API.APIClass("EcomCraft templates: list the e-commerce scene templates the current account can run.")]
public static class TemplateApi
{
    /// <summary>Reason code when a template's configuration is invalid.</summary>
    public const string ReasonInvalid = "invalid";

    /// <summary>Reason code when no online execution server can run a template for this account.</summary>
    public const string ReasonUnavailable = "unavailable";

    /// <summary>Snapshot parameters bound to these page fields are filled by the template request itself.</summary>
    private static readonly HashSet<string> PromptForms = ["prompt", "negativeprompt"];

    [API.APIDescription("List the e-commerce scene templates the current account can run, with names and descriptions in the requested language (zh-CN or en). Templates that cannot run are listed under hidden with a reason code; reviewers also get the details.", "templates: [{ id: \"white-main\", name: \"\", fields: [] }], hidden: []")]
    public static Task<JObject> EcomCraftStudioTemplates(Session session, string language = "zh-CN")
    {
        if (!StudioApi.CanView(session) || !session.User.HasPermission(EcomCraft.PermUsePublishedWorkflows))
        {
            return Task.FromResult(StudioApi.Error("此账号没有使用模板的权限。", "bad_permissions"));
        }
        language = language == "en" ? "en" : "zh-CN";
        bool reviewer = session.User.HasPermission(EcomCraft.PermReviewWorkflows);
        Dictionary<string, JObject> runnable = Runnable(session);
        JArray templates = new();
        JArray hidden = new();
        foreach (TemplateStore.Entry entry in TemplateStore.All())
        {
            if (entry.Template is { Enabled: false })
            {
                continue;
            }
            string name = entry.Template is null ? entry.Id : EcomTemplate.Pick(entry.Template.Name, language);
            List<string> problems = TemplateStore.Problems(entry);
            if (problems.Count > 0)
            {
                hidden.Add(Hidden(entry.Id, name, ReasonInvalid, reviewer ? string.Join(" ", problems) : null));
                continue;
            }
            EcomTemplate template = entry.Template;
            if (!runnable.TryGetValue(template.LocalPlan.Workflow, out JObject workflow))
            {
                hidden.Add(Hidden(entry.Id, name, ReasonUnavailable, reviewer ? "没有在线的执行服务器具备该工作流所需的模型和节点，或当前账号无权使用其中的模型。" : null));
                continue;
            }
            HashSet<string> userFields = new(template.LocalPlan.UserFields, StringComparer.Ordinal);
            templates.Add(new JObject()
            {
                ["id"] = template.Id,
                ["name"] = name,
                ["description"] = EcomTemplate.Pick(template.Description, language),
                ["example_image"] = template.ExampleImage is null ? null : $"/EcomCraft/template-image/{Uri.EscapeDataString(template.Id)}",
                ["kind"] = workflow["kind"]?.DeepClone(),
                ["platforms"] = new JArray(template.Platforms),
                ["sets"] = new JArray(template.Sets),
                ["preserve_product"] = template.PreserveProduct,
                ["fields"] = new JArray((workflow["inputs"] as JArray ?? new JArray()).OfType<JObject>().Where(field => userFields.Contains(field["id"]?.ToString() ?? "")).Select(field => field.DeepClone()))
            });
        }
        return Task.FromResult(new JObject() { ["templates"] = templates, ["hidden"] = hidden });
    }

    [API.APIDescription("List the shop platforms templates can target, with names in the requested language (zh-CN or en).", "platforms: [{ id: \"amazon\", name: \"Amazon\", region: \"global\" }]")]
    public static Task<JObject> EcomCraftStudioPlatforms(Session session, string language = "zh-CN")
    {
        if (!StudioApi.CanView(session))
        {
            return Task.FromResult(StudioApi.Error("无权访问创作工作台。", "bad_permissions"));
        }
        language = language == "en" ? "en" : "zh-CN";
        JArray platforms = new();
        foreach (JObject platform in TemplateStore.Platforms())
        {
            Dictionary<string, string> names = platform["name"].ToObject<Dictionary<string, string>>() ?? [];
            platforms.Add(new JObject() { ["id"] = platform["id"]?.ToString(), ["name"] = EcomTemplate.Pick(names, language), ["region"] = platform["region"]?.ToString() ?? "" });
        }
        return Task.FromResult(new JObject() { ["platforms"] = platforms });
    }

    /// <summary>One hidden template: ID, name, reason code, and details for reviewers only.</summary>
    private static JObject Hidden(string id, string name, string code, string detail) => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["reason"] = code,
        ["detail"] = detail
    };

    /// <summary>Published workflows this account can run on an online server now, by ID, as the workspace catalog lists them.</summary>
    private static Dictionary<string, JObject> Runnable(Session session)
    {
        Dictionary<string, JObject> result = new(StringComparer.Ordinal);
        foreach (JObject workflow in StudioApi.PublishedWorkflowsFor(session).OfType<JObject>())
        {
            result[workflow["id"]?.ToString() ?? ""] = workflow;
        }
        return result;
    }

    /// <summary>Turns a template request into a published-workflow request: the template's snapshot, its presets, the user's allowed fields
    /// (template_params, by parameter ID) and the prompt with the template's fragments. Returns null with an error response when the template cannot be used.
    /// Page fields bound to parameters the template does not open to users are removed or overwritten, so only presets, allowed fields and snapshot values apply.</summary>
    public static JObject Resolve(Session session, JObject rawInput, string templateId, out JObject error, out string preserveToken)
    {
        error = null;
        preserveToken = null;
        TemplateStore.Entry entry = TemplateStore.Find(templateId);
        if (entry?.Template is null || !entry.Template.Enabled || TemplateStore.Problems(entry).Count > 0)
        {
            error = StudioApi.Error("模板不存在、已停用或配置无效。请刷新模板列表。", "invalid_template");
            return null;
        }
        EcomTemplate template = entry.Template;
        ExecutionPlan plan = template.LocalPlan;
        if (!Runnable(session).TryGetValue(plan.Workflow, out JObject workflow))
        {
            error = StudioApi.Error("暂时没有执行服务器能运行此模板，或当前账号无权使用其中的模型。", "no_execution_server");
            return null;
        }
        JObject parameters = TemplateStore.SnapshotParameters(plan.Workflow);
        JToken rawParams = rawInput["template_params"];
        if (rawParams is not null && rawParams.Type is not (JTokenType.Object or JTokenType.Null))
        {
            error = StudioApi.Error("模板参数格式无效。", "invalid_workflow_parameters");
            return null;
        }
        JObject userParams = rawParams as JObject ?? new JObject();
        HashSet<string> allowed = new(plan.UserFields, StringComparer.Ordinal);
        foreach (JProperty property in userParams.Properties())
        {
            if (!allowed.Contains(property.Name))
            {
                error = StudioApi.Error($"参数“{property.Name}”不是此模板允许修改的参数。", "invalid_workflow_parameters");
                return null;
            }
        }
        JObject request = (JObject)rawInput.DeepClone();
        request.Remove("template_id");
        request.Remove("template_params");
        request["workflow_id"] = plan.Workflow;
        request["kind"] = workflow["kind"]?.DeepClone();
        JObject workflowParams = (JObject)plan.Presets.DeepClone();
        foreach (JProperty property in userParams.Properties())
        {
            workflowParams[property.Name] = property.Value.DeepClone();
        }
        if (template.PreserveProduct)
        {
            // Local preprocessing (R10.3): the cut-out product is placed on the input canvas; the same placement pastes it back after generation.
            string field = plan.UserFields.FirstOrDefault(id => parameters[id]?["type"]?.ToString() == "image" && workflowParams[id]?.Type == JTokenType.String);
            if (field is null)
            {
                error = StudioApi.Error("这个模板需要商品图。", "invalid_reference");
                return null;
            }
            Placement placement = Placement.Default;
            if (rawInput["placement"] is JObject requested
                && !Placement.TryCreate(requested.Value<double?>("x"), requested.Value<double?>("y"), requested.Value<double?>("size"), out placement))
            {
                error = StudioApi.Error("商品位置无效：中心须在画面内，大小在 0.1–1.5 之间。", "invalid_parameters");
                return null;
            }
            (int width, int height) = TargetSize(parameters, workflowParams, plan.Workflow);
            string problem = ProductPreserve.Prepare(session.User.UserID, workflowParams.Value<string>(field), width, height, placement, out string canvas, out preserveToken);
            if (problem is not null)
            {
                error = StudioApi.Error(problem, "invalid_reference");
                return null;
            }
            workflowParams[field] = canvas;
        }
        request["workflow_params"] = workflowParams;
        foreach (JProperty property in parameters.Properties())
        {
            string form = property.Value["form"]?.ToString();
            if (string.IsNullOrEmpty(form) || PromptForms.Contains(form))
            {
                continue;
            }
            if (workflowParams[property.Name] is JToken value)
            {
                request[form] = value.DeepClone();
            }
            else
            {
                // Not opened to users and not preset: the snapshot's own value applies.
                request.Remove(form);
            }
        }
        request["prompt"] = Join(" ", template.PromptPrefix, rawInput["prompt"]?.Type == JTokenType.String ? rawInput.Value<string>("prompt") : "", template.PromptSuffix);
        request["negativeprompt"] = Join(", ", template.Negative, rawInput["negativeprompt"]?.Type == JTokenType.String ? rawInput.Value<string>("negativeprompt") : "");
        return request;
    }

    /// <summary>Output size of a template run: the preset or user value of the parameters bound to the width and height fields, else the snapshot defaults, else 1024.</summary>
    private static (int Width, int Height) TargetSize(JObject parameters, JObject workflowParams, string workflowId)
    {
        JObject defaults = TemplateStore.SnapshotFormDefaults(workflowId);
        int read(string form)
        {
            JProperty bound = parameters.Properties().FirstOrDefault(property => property.Value["form"]?.ToString() == form);
            JToken value = (bound is null ? null : workflowParams[bound.Name]) ?? defaults[form];
            return value?.Type is JTokenType.Integer or JTokenType.Float ? Math.Clamp((int)value.Value<double>(), 64, 8192) : 1024;
        }
        return (read("width"), read("height"));
    }

    /// <summary>Joins the non-empty parts.</summary>
    private static string Join(string separator, params string[] parts) => string.Join(separator, parts.Select(part => part?.Trim()).Where(part => !string.IsNullOrEmpty(part)));
}
