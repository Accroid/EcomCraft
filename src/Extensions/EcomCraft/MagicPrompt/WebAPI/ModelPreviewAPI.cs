using FreneticUtilities.FreneticExtensions;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Core;
using System.Net;
using System.Net.Http;

namespace EcomCraftStudio.MagicPrompt.WebAPI;

/// <summary>Lists models using an administrator's unsaved form without persisting configuration or credentials.</summary>
public class ModelPreviewAPI : LLMAPICalls
{
    /// <summary>Previews only the selected text backend; a vision backend failure must not block text model selection.</summary>
    public static async Task<JObject> PreviewMagicPromptModels(Session session, HttpContext context,
        string backend, string baseurl = "", string api_key = "")
    {
        if (!session.User.HasPermission(MagicPromptPermissions.PermGetModels))
        {
            return Failure("此账号没有拉取模型列表的权限。");
        }
        backend = backend?.Trim().ToLowerFast();
        if (backend is not ("ollama" or "openaiapi" or "openai" or "anthropic" or "openrouter" or "grok"))
        {
            return Failure("请选择支持的模型后端。");
        }
        api_key = api_key?.Trim() ?? "";
        if (api_key.Length > 8192 || api_key.Any(char.IsControl))
        {
            return Failure("密钥格式不正确，请重新填写。");
        }
        try
        {
            JObject config = await SessionSettings.GetMagicPromptSettings();
            if (config["settings"] is not JObject saved)
            {
                return Failure("无法读取后端配置，请稍后重试。");
            }
            JObject settings = (JObject)saved.DeepClone();
            JObject backendConfig = settings["backends"]?[backend] as JObject;
            if (backendConfig is null)
            {
                return Failure("后端配置不可用，请重新选择。");
            }
            string savedEndpoint = GetEndpoint(backend, settings, "models");
            if (backend is "ollama" or "openaiapi")
            {
                if (!Uri.TryCreate(baseurl?.Trim(), UriKind.Absolute, out Uri address)
                    || address.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(address.UserInfo)
                    || !string.IsNullOrEmpty(address.Query) || !string.IsNullOrEmpty(address.Fragment))
                {
                    return Failure("请填写有效的服务地址，不要在地址中包含密钥、查询参数或片段。");
                }
                backendConfig["baseurl"] = address.AbsoluteUri.TrimEnd('/');
            }
            string endpoint = GetEndpoint(backend, settings, "models");
            // Never forward a saved credential to a different unsaved destination. A newly entered key is explicit.
            string keyOverride = api_key.Length > 0 ? api_key
                : Uri.TryCreate(savedEndpoint, UriKind.Absolute, out Uri savedAddress)
                    && savedAddress == new Uri(endpoint) ? null : "";
            using HttpRequestMessage request = new(HttpMethod.Get, endpoint);
            if (!ConfigureRequest(request, backend, settings, session, out _, keyOverride))
            {
                return Failure("请填写此后端的密钥后再拉取模型。");
            }
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, Program.GlobalProgramCancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using HttpResponseMessage response = await HttpClient.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                // Upstream bodies can echo credentials; never include them in errors or logs.
                return Failure(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "身份验证失败，请检查当前填写的密钥和账号权限。",
                    HttpStatusCode.NotFound => "未找到模型列表接口，请检查服务地址。",
                    HttpStatusCode.TooManyRequests => "请求过于频繁或额度不足，请稍后重试。",
                    _ => "模型服务返回错误，请检查服务状态后重试。"
                });
            }
            JObject result = JObject.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            if (result[backend == "ollama" ? "models" : "data"] is not JArray entries)
            {
                return Failure("模型列表格式不兼容，请检查服务是否支持所选接口。");
            }
            JArray models = new();
            foreach (JObject entry in entries.OfType<JObject>())
            {
                string id = entry[backend == "ollama" ? "model" : "id"]?.Value<string>();
                if (backend == "ollama" && string.IsNullOrWhiteSpace(id))
                {
                    id = entry["name"]?.Value<string>();
                }
                if (!string.IsNullOrWhiteSpace(id))
                {
                    models.Add(new JObject { ["model"] = id, ["name"] = entry["name"]?.Value<string>() ?? id });
                }
            }
            return new JObject { ["success"] = true, ["models"] = models };
        }
        catch (OperationCanceledException)
        {
            return Failure("模型列表请求已取消或超时，请稍后重试。");
        }
        catch (HttpRequestException)
        {
            return Failure("无法连接模型服务，请检查服务地址和网络。");
        }
        catch (Exception)
        {
            return Failure("无法读取模型列表，请检查后端配置和返回格式。");
        }
    }

    /// <summary>Returns only a controlled message, never upstream bodies, settings, or keys.</summary>
    private static JObject Failure(string message) => new() { ["success"] = false, ["error"] = message };
}
