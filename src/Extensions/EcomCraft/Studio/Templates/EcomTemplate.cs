using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace EcomCraftStudio.Studio.Templates;

/// <summary>An e-commerce scene template (F01): a published review snapshot plus shop information. It never contains its own workflow graph;
/// each execution plan points at a published snapshot by ID, fixes some of its whitelisted parameters and lets users change others.</summary>
public sealed class EcomTemplate
{
    /// <summary>Stable ID: lowercase letters, digits and hyphens.</summary>
    public string Id;

    /// <summary>Display name by language code ("zh-CN", "en").</summary>
    public Dictionary<string, string> Name = [];

    /// <summary>Short description by language code.</summary>
    public Dictionary<string, string> Description = [];

    /// <summary>Example image file name, relative to the template's image folder, or null.</summary>
    public string ExampleImage;

    /// <summary>Platform IDs the template suits, or "*" for any.</summary>
    public List<string> Platforms = [];

    /// <summary>IDs of the image sets the template belongs to (for one-click sets, F03).</summary>
    public List<string> Sets = [];

    /// <summary>Text added before the user's prompt.</summary>
    public string PromptPrefix = "";

    /// <summary>Text added after the user's prompt.</summary>
    public string PromptSuffix = "";

    /// <summary>Negative prompt added before the user's negative prompt.</summary>
    public string Negative = "";

    /// <summary>Reserved for category presets (F26); stored but not used yet.</summary>
    public List<string> Categories = [];

    /// <summary>Reserved for brand kits (F27); must be null in this phase.</summary>
    public JObject BrandKit;

    /// <summary>Ways to run the template; this phase accepts exactly one local plan (R10.2).</summary>
    public List<ExecutionPlan> ExecutionPlans = [];

    /// <summary>Whether users can see and run it.</summary>
    public bool Enabled = true;

    /// <summary>Product preservation (F02): the product photo, which must have a transparent background, is placed on the input canvas and its original
    /// pixels are pasted back onto every result, so logos, text and shapes stay unchanged.</summary>
    public bool PreserveProduct;

    /// <summary>Platforms where the template skips generation and only centers the product cut-out on pure white (F22), for example Amazon main images.</summary>
    public List<string> CutoutOnlyPlatforms = [];

    /// <summary>Models the template needs (F08), with commercial-use license status; installed by administrators by hand.</summary>
    public List<ModelRequirement> RequiredModels = [];

    /// <summary>ComfyUI node class names the template needs.</summary>
    public List<string> RequiredNodes = [];

    /// <summary>"builtin" or "custom"; set by the store, never read from the file.</summary>
    public string Source;

    /// <summary>The local plan, or null.</summary>
    public ExecutionPlan LocalPlan => ExecutionPlans.FirstOrDefault(plan => plan.Kind == ExecutionPlan.Local);

    /// <summary>Text in the requested language, falling back to Chinese, then English.</summary>
    public static string Pick(Dictionary<string, string> texts, string language)
    {
        if (texts.TryGetValue(language ?? "", out string text) && !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }
        return texts.GetValueOrDefault("zh-CN") ?? texts.GetValueOrDefault("en") ?? "";
    }
}

/// <summary>One way to run a template. Only <see cref="Local"/> plans run in this phase; cloud kinds are reserved and rejected (D11).</summary>
public sealed class ExecutionPlan
{
    /// <summary>Runs a published snapshot on registered execution servers.</summary>
    public const string Local = "local";

    /// <summary>Reserved: a cloud ComfyUI service.</summary>
    public const string CloudComfy = "cloud_comfy";

    /// <summary>Reserved: a cloud image model API.</summary>
    public const string CloudModelApi = "cloud_model_api";

    /// <summary>Plan kind.</summary>
    public string Kind;

    /// <summary>ID of the published snapshot the plan runs.</summary>
    public string Workflow;

    /// <summary>Fixed values for snapshot parameters, by parameter ID.</summary>
    public JObject Presets = new();

    /// <summary>Snapshot parameter IDs users may change.</summary>
    public List<string> UserFields = [];
}

/// <summary>Checks templates without touching SwarmUI, so the rules can be unit tested. Reasons are short Chinese sentences for administrators.</summary>
public static class TemplateValidator
{
    /// <summary>Pattern for template, platform and set IDs.</summary>
    private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9-]{0,47}$", RegexOptions.CultureInvariant);

    /// <summary>Keys a template file may contain; anything else is rejected so typos do not silently disable a setting.</summary>
    private static readonly HashSet<string> TemplateKeys = ["id", "name", "description", "example_image", "platforms", "sets", "prompt", "categories", "brand_kit", "execution_plans", "enabled", "preserve_product", "cutout_only_platforms", "requirements"];

    /// <summary>Keys an execution plan may contain.</summary>
    private static readonly HashSet<string> PlanKeys = ["kind", "workflow", "presets", "user_fields"];

    /// <summary>Snapshot parameters bound to these page fields cannot be preset: the prompt comes from the user and the prompt service.</summary>
    private static readonly HashSet<string> UnpresettableForms = ["prompt", "negativeprompt"];

    /// <summary>Longest name.</summary>
    public const int MaxNameLength = 60;

    /// <summary>Longest description or prompt fragment.</summary>
    public const int MaxTextLength = 1000;

    /// <summary>Reads a template from JSON and checks its structure. Returns null with reasons when it is not valid.</summary>
    public static EcomTemplate Parse(JObject json, List<string> errors)
    {
        if (json is null)
        {
            errors.Add("模板不是 JSON 对象。");
            return null;
        }
        foreach (JProperty property in json.Properties())
        {
            if (!TemplateKeys.Contains(property.Name))
            {
                errors.Add($"不认识的字段“{property.Name}”。");
            }
        }
        EcomTemplate template = new() { Id = Text(json["id"]) };
        if (template.Id is null || !IdPattern.IsMatch(template.Id))
        {
            errors.Add("id 须为 1–48 个小写字母、数字或连字符，且以字母或数字开头。");
        }
        template.Name = Localized(json["name"], "name", MaxNameLength, true, errors);
        template.Description = Localized(json["description"], "description", MaxTextLength, false, errors);
        if (json["example_image"] is JToken image && image.Type != JTokenType.Null)
        {
            template.ExampleImage = Text(image);
            if (template.ExampleImage is null || !Regex.IsMatch(template.ExampleImage, @"^[A-Za-z0-9_-]{1,64}\.(png|jpg|jpeg|webp)$", RegexOptions.CultureInvariant))
            {
                errors.Add("example_image 须为同一目录下的 png、jpg 或 webp 文件名。");
            }
        }
        template.Platforms = IdList(json["platforms"], "platforms", true, errors);
        if (template.Platforms.Count == 0)
        {
            errors.Add("platforms 至少需要一项（任何平台都适用时填 \"*\"）。");
        }
        template.Sets = IdList(json["sets"], "sets", false, errors);
        template.Categories = IdList(json["categories"], "categories", false, errors);
        template.CutoutOnlyPlatforms = IdList(json["cutout_only_platforms"], "cutout_only_platforms", false, errors);
        (template.RequiredModels, template.RequiredNodes) = RequirementRules.Parse(json["requirements"], errors);
        if (json["prompt"] is JToken prompt && prompt.Type != JTokenType.Null)
        {
            if (prompt is not JObject parts || parts.Properties().Any(part => part.Name is not ("prefix" or "suffix" or "negative")))
            {
                errors.Add("prompt 只能包含 prefix、suffix 和 negative。");
            }
            else
            {
                template.PromptPrefix = Fragment(parts["prefix"], "prompt.prefix", errors);
                template.PromptSuffix = Fragment(parts["suffix"], "prompt.suffix", errors);
                template.Negative = Fragment(parts["negative"], "prompt.negative", errors);
            }
        }
        if (json["brand_kit"] is JToken brand && brand.Type != JTokenType.Null)
        {
            errors.Add("brand_kit 是预留字段，本期须为 null。");
        }
        if (json["enabled"] is JToken enabled && enabled.Type != JTokenType.Null)
        {
            if (enabled.Type != JTokenType.Boolean)
            {
                errors.Add("enabled 须为 true 或 false。");
            }
            else
            {
                template.Enabled = enabled.Value<bool>();
            }
        }
        if (json["preserve_product"] is JToken preserve && preserve.Type != JTokenType.Null)
        {
            if (preserve.Type != JTokenType.Boolean)
            {
                errors.Add("preserve_product 须为 true 或 false。");
            }
            else
            {
                template.PreserveProduct = preserve.Value<bool>();
            }
        }
        if (json["execution_plans"] is not JArray plans || plans.Count == 0)
        {
            errors.Add("execution_plans 至少需要一个本地方案。");
        }
        else
        {
            foreach (JToken token in plans)
            {
                ExecutionPlan plan = ParsePlan(token, errors);
                if (plan is not null)
                {
                    template.ExecutionPlans.Add(plan);
                }
            }
            if (template.ExecutionPlans.Count(plan => plan.Kind == ExecutionPlan.Local) != 1)
            {
                errors.Add("本期每个模板必须有且只有一个本地执行方案（kind 为 local）。");
            }
        }
        return errors.Count == 0 ? template : null;
    }

    /// <summary>Reads one execution plan.</summary>
    private static ExecutionPlan ParsePlan(JToken token, List<string> errors)
    {
        if (token is not JObject json)
        {
            errors.Add("execution_plans 的每一项须为对象。");
            return null;
        }
        foreach (JProperty property in json.Properties())
        {
            if (!PlanKeys.Contains(property.Name))
            {
                errors.Add($"执行方案中不认识的字段“{property.Name}”。");
            }
        }
        ExecutionPlan plan = new() { Kind = Text(json["kind"]), Workflow = Text(json["workflow"]) };
        if (plan.Kind is ExecutionPlan.CloudComfy or ExecutionPlan.CloudModelApi)
        {
            errors.Add("云端执行方案是预留类型，本期不能使用。");
            return null;
        }
        if (plan.Kind != ExecutionPlan.Local)
        {
            errors.Add($"未知的执行方案类型“{plan.Kind}”。");
            return null;
        }
        if (string.IsNullOrWhiteSpace(plan.Workflow) || plan.Workflow.Length > 300)
        {
            errors.Add("本地执行方案需要 workflow（已发布工作流的 ID）。");
        }
        if (json["presets"] is JToken presets && presets.Type != JTokenType.Null)
        {
            if (presets is JObject values && values.Count <= 60)
            {
                plan.Presets = values;
            }
            else
            {
                errors.Add("presets 须为参数 ID 到值的对象，最多 60 项。");
            }
        }
        if (json["user_fields"] is JToken fields && fields.Type != JTokenType.Null)
        {
            if (fields is JArray list && list.Count <= 60 && list.All(field => field.Type == JTokenType.String))
            {
                plan.UserFields = [.. list.Values<string>().Distinct(StringComparer.Ordinal)];
            }
            else
            {
                errors.Add("user_fields 须为参数 ID 数组，最多 60 项。");
            }
        }
        return plan;
    }

    /// <summary>Placeholder in user_fields for the snapshot's first visible image parameter, so templates can be written before the workflow exists.</summary>
    public const string ImageFieldToken = "@image";

    /// <summary>Prefix of a placeholder workflow name: "*/path" means the published snapshot at that path on any execution server.</summary>
    public const string AnyServerPrefix = "*/";

    /// <summary>User fields with <see cref="ImageFieldToken"/> replaced by the snapshot's first visible image parameter; the token is dropped when there is none.</summary>
    public static List<string> ExpandFields(IEnumerable<string> fields, JObject parameters)
    {
        string image = parameters?.Properties().FirstOrDefault(property => property.Value["type"]?.ToString() == "image" && property.Value.Value<bool?>("visible") != false)?.Name;
        List<string> result = [];
        foreach (string field in fields)
        {
            string id = field == ImageFieldToken ? image : field;
            if (id is not null && !result.Contains(id))
            {
                result.Add(id);
            }
        }
        return result;
    }

    /// <summary>Checks a plan against the published snapshot's parameter definitions (parameter ID to definition), or null when the snapshot is not published.
    /// Presets must name whitelisted parameters with valid values and may not set the prompt or an image; user fields must be visible, unfixed parameters.</summary>
    public static List<string> CheckPlan(ExecutionPlan plan, JObject parameters, bool requireImageField = false)
    {
        List<string> errors = [];
        if (parameters is null)
        {
            errors.Add(plan.Workflow.StartsWith(AnyServerPrefix, StringComparison.Ordinal)
                ? $"缺少工作流：请在执行服务器的 ComfyUI 中按约定名称“{plan.Workflow[AnyServerPrefix.Length..]}”保存工作流并通过审核。"
                : $"工作流“{plan.Workflow}”没有发布，或已被下架。");
            return errors;
        }
        if (plan.UserFields.Contains(ImageFieldToken) && !ExpandFields([ImageFieldToken], parameters).Any())
        {
            errors.Add("user_fields 中的 @image 找不到可用的图片参数：工作流没有可见的图片输入。");
        }
        List<string> userFields = ExpandFields(plan.UserFields, parameters);
        foreach (JProperty preset in plan.Presets.Properties())
        {
            if (parameters[preset.Name] is not JObject definition)
            {
                errors.Add($"预设参数“{preset.Name}”不是该工作流的参数。");
                continue;
            }
            string form = definition["form"]?.ToString() ?? "";
            string type = definition["type"]?.ToString() ?? "";
            if (UnpresettableForms.Contains(form))
            {
                errors.Add($"参数“{preset.Name}”是提示词，不能预设；请用 prompt.prefix / suffix / negative。");
            }
            else if (type == "image")
            {
                errors.Add($"参数“{preset.Name}”是图片，不能预设默认图片。");
            }
            else if (CheckValue(definition, preset.Value) is string problem)
            {
                errors.Add($"预设参数“{preset.Name}”的值无效：{problem}");
            }
        }
        if (requireImageField && !userFields.Any(field => parameters[field]?["type"]?.ToString() == "image"))
        {
            errors.Add("开启商品保真的模板必须把一个图片参数开放给用户（user_fields），用来接收商品图。");
        }
        foreach (string field in userFields)
        {
            if (parameters[field] is not JObject definition)
            {
                errors.Add($"用户可调参数“{field}”不是该工作流的参数。");
            }
            else if (definition.Value<bool?>("visible") == false)
            {
                errors.Add($"参数“{field}”在审核时被隐藏，不能开放给用户。");
            }
            else if (plan.Presets.ContainsKey(field))
            {
                errors.Add($"参数“{field}”已有预设值，不能同时开放给用户。");
            }
        }
        return errors;
    }

    /// <summary>Basic check of a value against a parameter definition; the full check, including model permissions, runs again for every generation.</summary>
    public static string CheckValue(JObject definition, JToken value)
    {
        string type = definition["type"]?.ToString() ?? "";
        if (value is null || value.Type == JTokenType.Null)
        {
            return "缺少值。";
        }
        switch (type)
        {
            case "integer":
            case "decimal":
                if (value.Type is not (JTokenType.Integer or JTokenType.Float) || !double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                {
                    return "须为数字。";
                }
                if (type == "integer" && number != Math.Floor(number))
                {
                    return "须为整数。";
                }
                double min = definition.Value<double?>("min") ?? 0;
                double max = definition.Value<double?>("max") ?? 0;
                if ((min != 0 || max != 0) && (number < min || number > max))
                {
                    return $"须在 {min.ToString(CultureInfo.InvariantCulture)}–{max.ToString(CultureInfo.InvariantCulture)} 之间。";
                }
                return null;
            case "boolean":
                return value.Type == JTokenType.Boolean ? null : "须为 true 或 false。";
            case "dropdown":
            case "model":
                if (value.Type != JTokenType.String || string.IsNullOrWhiteSpace(value.ToString()))
                {
                    return "须为文字。";
                }
                if (definition["values"] is JArray choices && choices.Count > 0
                    && !choices.Any(choice => choice.ToString() == value.ToString() || choice.ToString().Split("///")[0] == value.ToString()))
                {
                    return "不在可选项中。";
                }
                return null;
            case "text":
                return value.Type == JTokenType.String && value.ToString().Length <= 2000 ? null : "须为不超过 2000 字的文字。";
            default:
                return $"不支持预设 {type} 类型的参数。";
        }
    }

    /// <summary>A trimmed string, or null when the token is not a string.</summary>
    private static string Text(JToken token) => token?.Type == JTokenType.String ? token.Value<string>().Trim() : null;

    /// <summary>Reads a {"zh-CN": "...", "en": "..."} object; names need both languages.</summary>
    private static Dictionary<string, string> Localized(JToken token, string field, int maxLength, bool required, List<string> errors)
    {
        Dictionary<string, string> result = [];
        if (token is null || token.Type == JTokenType.Null)
        {
            if (required)
            {
                errors.Add($"{field} 需要中文（zh-CN）和英文（en）两种文字。");
            }
            return result;
        }
        if (token is not JObject texts)
        {
            errors.Add($"{field} 须为 {{\"zh-CN\": \"…\", \"en\": \"…\"}} 形式。");
            return result;
        }
        foreach (string language in new[] { "zh-CN", "en" })
        {
            string text = Text(texts[language]);
            if (string.IsNullOrEmpty(text))
            {
                if (required)
                {
                    errors.Add($"{field} 缺少 {language} 文字。");
                }
                continue;
            }
            if (text.Length > maxLength)
            {
                errors.Add($"{field} 的 {language} 文字超过 {maxLength} 个字符。");
                continue;
            }
            result[language] = text;
        }
        if (texts.Properties().Any(property => property.Name is not ("zh-CN" or "en")))
        {
            errors.Add($"{field} 只支持 zh-CN 和 en。");
        }
        return result;
    }

    /// <summary>Reads a list of IDs; "*" is allowed only where noted.</summary>
    private static List<string> IdList(JToken token, string field, bool allowAny, List<string> errors)
    {
        if (token is null || token.Type == JTokenType.Null)
        {
            return [];
        }
        if (token is not JArray list || list.Count > 50)
        {
            errors.Add($"{field} 须为最多 50 项的数组。");
            return [];
        }
        List<string> ids = [];
        foreach (JToken item in list)
        {
            string id = Text(item);
            if (id is null || (!IdPattern.IsMatch(id) && !(allowAny && id == "*")))
            {
                errors.Add($"{field} 中的“{item}”不是有效的 ID。");
                continue;
            }
            ids.Add(id);
        }
        return [.. ids.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>Reads a prompt fragment; native prompt syntax is refused, as in the workspace.</summary>
    private static string Fragment(JToken token, string field, List<string> errors)
    {
        if (token is null || token.Type == JTokenType.Null)
        {
            return "";
        }
        string text = Text(token);
        if (text is null || text.Length > MaxTextLength)
        {
            errors.Add($"{field} 须为不超过 {MaxTextLength} 个字符的文字。");
            return "";
        }
        if (text.Contains('<') || text.Contains('>') || text.Contains("__"))
        {
            errors.Add($"{field} 不能包含 <…> 指令或 __通配符__。");
            return "";
        }
        return text;
    }
}
