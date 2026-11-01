using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using EcomCraftStudio.MagicPrompt.WebAPI;
using EcomCraftStudio.Studio.Generation;
using EcomCraftStudio.Studio.Storage;
using EcomCraftStudio.Studio.Templates;
using EcomCraftStudio.Studio.Prompting;
using FreneticUtilities.FreneticExtensions;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Backends;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Core;
using SwarmUI.Text2Image;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio;


/// <summary>Validation helpers for published-workflow parameters. Part of the StudioApi partial class (R07).</summary>
public static partial class StudioApi
{
    internal static bool ValidatePublishedWorkflowInput(Session session, string id, JToken value, JObject definition, out string error)
    {
        error = null;
        string type = definition["type"]?.ToString()?.ToLowerInvariant() ?? "";
        if (value.Type == JTokenType.Null)
        {
            return true;
        }
        string raw = value.Type == JTokenType.String ? value.Value<string>() ?? "" : value.ToString();
        double min = ReadWorkflowBound(definition["min"]);
        double max = ReadWorkflowBound(definition["max"]);
        bool hasBounds = min != 0 || max != 0;
        switch (type)
        {
            case "text":
                if (value.Type != JTokenType.String || raw.Length > 12000 || raw.Contains('<') || raw.Contains('>') || raw.Contains("__"))
                {
                    error = $"工作流参数“{id}”必须是普通文本，不能超过 12000 个字符，也不能包含原生指令语法。";
                    return false;
                }
                return true;
            case "integer":
                if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integerValue)
                    || (hasBounds && (integerValue < min || integerValue > max))
                    || (definition["native_node"] is not null && ReadWorkflowBound(definition["step"]) > 1
                        && integerValue % ReadWorkflowBound(definition["step"]) != 0))
                {
                    error = $"工作流参数“{id}”的整数值无效或超出允许范围。";
                    return false;
                }
                return true;
            case "decimal":
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double decimalValue)
                    || !double.IsFinite(decimalValue) || (hasBounds && (decimalValue < min || decimalValue > max)))
                {
                    error = $"工作流参数“{id}”的数值无效或超出允许范围。";
                    return false;
                }
                return true;
            case "boolean":
                if (!bool.TryParse(raw, out _))
                {
                    error = $"工作流参数“{id}”必须是布尔值。";
                    return false;
                }
                return true;
            case "dropdown":
                if (definition.Value<bool?>("native_model") == true && !session.User.IsAllowedModel(raw))
                {
                    error = "此账号无权访问工作流选择的模型。";
                    return false;
                }
                if (value.Type != JTokenType.String || definition["values"] is not JArray choices || choices.Count == 0
                    || !choices.Any(choice => choice.ToString() == raw || choice.ToString().Before("///") == raw))
                {
                    error = $"工作流参数“{id}”不是当前工作流允许的选项。";
                    return false;
                }
                return true;
            case "model":
                string subtype = definition["subtype"]?.ToString() ?? "Stable-Diffusion";
                if (value.Type != JTokenType.String || !Program.T2IModelSets.TryGetValue(subtype, out T2IModelHandler handler)
                    || T2IParamTypes.GetBestModelInList(raw, handler.ListModelNamesFor(session)) is null)
                {
                    error = $"工作流参数“{id}”引用了不存在或当前账号无权访问的模型。";
                    return false;
                }
                return true;
            case "image":
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return true;
                }
                if (raw.StartsWith("inputs/", StringComparison.Ordinal) || raw.StartsWith("raw/", StringComparison.Ordinal)
                    || raw.StartsWith("Starred/", StringComparison.Ordinal))
                {
                    return true; // SwarmUI validates and resolves these within the user's own output directory.
                }
                if (!ValidReference(raw))
                {
                    error = $"工作流图片参数“{id}”必须是 PNG、JPEG 或 WebP，且不超过 5 MB。";
                    return false;
                }
                try
                {
                    SwarmUI.Media.ImageFile decoded = SwarmUI.Media.ImageFile.FromDataString(raw);
                    (int imageWidth, int imageHeight) = decoded.GetResolution();
                    if (imageWidth > 8192 || imageHeight > 8192 || (long)imageWidth * imageHeight > 40_000_000)
                    {
                        error = $"工作流图片参数“{id}”尺寸过大。";
                        return false;
                    }
                }
                catch (Exception)
                {
                    error = $"无法解码工作流图片参数“{id}”。";
                    return false;
                }
                return true;
            case "list":
                if (value.Type != JTokenType.String || raw.Length > 4000 || raw.Contains('<') || raw.Contains('>') || raw.Contains("__"))
                {
                    error = $"工作流列表参数“{id}”格式无效，或包含不支持的原生指令语法。";
                    return false;
                }
                return true;
            default:
                error = $"工作流参数“{id}”的数据类型“{type}”暂不支持由网页修改，请调整工作流的默认值或输入类型。";
                return false;
        }
    }

    private static double ReadWorkflowBound(JToken token)
    {
        return token is not null && double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            && double.IsFinite(value) ? value : 0;
    }

    private static bool TryBuildLoras(JArray loraArray, JToken weightToken, List<string> allowed, out JArray loras, out JArray weights, out string error)
    {
        loras = new JArray();
        weights = new JArray();
        error = null;
        if (loraArray.Count > MaxLoras)
        {
            error = $"LoRA 最多 {MaxLoras} 个。";
            return false;
        }
        double[] providedWeights = new double[loraArray.Count];
        if (weightToken is not null && weightToken.Type != JTokenType.Null)
        {
            if (weightToken.Type != JTokenType.Array || weightToken.Count() != loraArray.Count)
            {
                error = "LoRA 权重数量必须与 LoRA 数量一致。";
                return false;
            }
            for (int i = 0; i < loraArray.Count; i++)
            {
                if (!double.TryParse(weightToken[i]?.ToString(), out double w) || !double.IsFinite(w) || w < -10 || w > 10)
                {
                    error = "LoRA 权重须为 -10 到 10 的数字。";
                    return false;
                }
                providedWeights[i] = w;
            }
        }
        HashSet<string> seen = new();
        for (int i = 0; i < loraArray.Count; i++)
        {
            string name = loraArray[i]?.ToString();
            if (string.IsNullOrWhiteSpace(name) || !allowed.Contains(name) || !seen.Add(name))
            {
                error = "LoRA 不存在、无权限或重复选择，请刷新后重试。";
                return false;
            }
            loras.Add(name);
            weights.Add(weightToken is null ? "1" : providedWeights[i].ToString());
        }
        return true;
    }

    private static bool MatchOption(JObject source, string key, List<string> allowed, out string error)
    {
        error = null;
        JToken token = source[key];
        if (token is null || token.Type == JTokenType.Null || token.ToString() == "")
        {
            return true;
        }
        string value = token.ToString();
        HashSet<string> ids = new(allowed.Select(v => v.Before("///")));
        if (!ids.Contains(value))
        {
            error = $"{key} 取值不受支持，请使用列表中的选项。";
            return false;
        }
        return true;
    }

    private static bool Integer(JObject input, string key, int min, int max, out int value)
    {
        value = 0;
        return input[key]?.Type == JTokenType.Integer && int.TryParse(input[key].ToString(), out value) && value >= min && value <= max;
    }

    private static bool CopyOptionalNumber(JObject source, JObject target, string key, double min, double max, bool integral)
    {
        JToken token = source[key];
        if (token is null || token.Type == JTokenType.Null)
        {
            return true;
        }
        if (token.Type is not (JTokenType.Integer or JTokenType.Float))
        {
            return false;
        }
        double value = token.Value<double>();
        if (!double.IsFinite(value) || value < min || value > max || (integral && value != Math.Truncate(value)))
        {
            return false;
        }
        target[key] = token.DeepClone();
        return true;
    }

    /// <summary>Whether an image given to a standard workflow can be its product canvas (N10): the workflow generates only where the image is transparent,
    /// so an opaque photo would come back unchanged. Images from the user's own outputs ("inputs/", "raw/", "Starred/") are resolved later and allowed.</summary>
    private static bool StandardCanvasUsable(string data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return false;
        }
        if (!data.StartsWith("data:", StringComparison.Ordinal))
        {
            return true;
        }
        try
        {
            using SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> image =
                SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(Convert.FromBase64String(data[(data.IndexOf(',') + 1)..]));
            return PostProcess.ProductComposite.HasTransparency(image);
        }
        catch (Exception ex) when (ex is FormatException or SixLabors.ImageSharp.UnknownImageFormatException or SixLabors.ImageSharp.InvalidImageContentException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool ValidReference(string data)
    {
        int comma = data.IndexOf(',');
        if (comma < 0 || comma > 30 || data.Length > (MaxReferenceBytes + 2) / 3 * 4 + 40
            || data[..comma] is not ("data:image/png;base64" or "data:image/jpeg;base64" or "data:image/webp;base64"))
        {
            return false;
        }
        byte[] buffer = new byte[(data.Length - comma) * 3 / 4];
        return Convert.TryFromBase64String(data[(comma + 1)..], buffer, out int count) && count > 0 && count <= MaxReferenceBytes;
    }

    private static JObject GenerationError(JObject result, bool workflow)
    {
        string detail = result["error"]?.ToString() ?? "";
        string requestId = result["request_id"]?.ToString();
        Logs.Warning($"EcomCraft {(workflow ? "workflow" : "standard")} generation failed{(string.IsNullOrWhiteSpace(requestId) ? "" : $" (request {requestId})")}: {detail}");
        string message = detail switch
        {
            "Timeout! All backends are occupied with other tasks." => "等待 ComfyUI 执行服务器超时，请检查服务器状态和队列。",
            "Generation session interrupted." => "生成任务已被中断。请到“队列”查看，需要时重新提交。",
            "No images were generated (all refused, or failed - check server logs for details)." => "引擎没有生成可保存的结果，请检查工作流输出节点、模型依赖和服务器日志。",
            "Server failed to save an image." => "引擎生成了结果，但服务器保存文件失败，请检查输出目录和磁盘空间。",
            _ => $"引擎未完整返回结果。请在原生引擎检查{(workflow ? "工作流" : "模型")}依赖、参数和个人历史；可能已有部分输出，请勿直接重复提交。"
        };
        string errorId = "generation_failed";
        if (detail.StartsWith("Too many gens in queue.", StringComparison.Ordinal))
        {
            // R9.2: the per-role MaxQueued limit; reassigning would not help, so this gets its own code.
            message = "你的排队任务已达到账号角色允许的上限（MaxQueued），本次没有提交。请等待已有任务完成，或在“队列”中结束部分任务后再提交；需要更高上限请联系管理员。";
            errorId = "queue_limit";
        }
        else if (detail.StartsWith("ComfyUI errored:", StringComparison.Ordinal))
        {
            message = "ComfyUI 拒绝了这个工作流：节点或参数校验没有通过，任务没有开始执行。请在原生引擎打开该工作流检查节点和参数，或请管理员查看服务器日志。";
        }
        else if (detail.StartsWith("No backends match the settings of the request given!", StringComparison.Ordinal))
        {
            message = "没有执行服务器能运行这个任务：所需的节点或模型不全，或服务器不在线。请在“队列”查看服务器状态，或请管理员检查服务器上的模型和节点。";
        }
        Match failedNode = Regex.Match(detail, @"^ComfyUI execution error \(Node ([0-9]+): ([A-Za-z0-9_]+)\):");
        if (failedNode.Success)
        {
            message = $"ComfyUI 节点 {failedNode.Groups[1].Value}（{failedNode.Groups[2].Value}）执行失败。请在原生工作流中检查该节点及其模型依赖，并核对个人历史。";
        }
        if (detail.Contains("out of memory", StringComparison.OrdinalIgnoreCase))
        {
            message = "生成时显存不足。请在原生引擎核对任务结果，并尝试减小尺寸或使用更轻量的模型。";
        }
        if (detail.Contains("miopenStatusInternalError", StringComparison.OrdinalIgnoreCase))
        {
            message = "本机 ROCm/MIOpen 运行失败。请检查 ComfyUI 进程对 MIOpen 缓存目录的访问权限，并查看服务器日志中的原始错误。";
        }
        if (detail.Contains("hipErrorInvalidValue", StringComparison.OrdinalIgnoreCase))
        {
            message = "本机 AMD/ROCm 显存操作失败。请在任务结束后重启本机后端，并检查是否已禁用固定内存（--disable-pinned-memory）；已完成的图片请在个人历史中查看。";
        }
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            message += $" 请求编号：{requestId}。";
        }
        return Error(message, errorId);
    }
}
