using EcomCraftStudio.MagicPrompt.WebAPI.Models;
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
    /// <summary>Most Ollama models checked for image support in one preview; later ones are kept by name only.</summary>
    private const int MaxCapabilityChecks = 40;

    /// <summary>Name parts of known Ollama vision models, used when the Ollama version does not report model capabilities.</summary>
    private static readonly string[] VisionNameParts = ["vision", "-vl", "llava", "minicpm-v", "moondream", "gemma3", "bakllava"];

    /// <summary>Previews only the selected backend; a vision backend failure must not block text model selection.
    /// With purpose "vision" (F68) the saved vision address and key are the comparison point, and an Ollama listing keeps only models that can read images.</summary>
    public static async Task<JObject> PreviewMagicPromptModels(Session session, HttpContext context,
        string backend, string baseurl = "", string api_key = "", string purpose = "")
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
            bool vision = purpose == "vision";
            JObject settings = (JObject)saved.DeepClone();
            string savedKey = null;
            if (vision)
            {
                // The saved vision address only belongs to the saved vision backend type; another type compares with its shared address.
                if (VisionBackend(settings) != backend)
                {
                    settings.Remove("visionbaseurl");
                }
                settings["visionbackend"] = backend;
                savedKey = VisionKeyOverride(settings, session);
                settings = (JObject)VisionSettings(settings).DeepClone();
            }
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
                    && savedAddress == new Uri(endpoint) ? savedKey : "";
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
            if (vision && backend == "ollama")
            {
                int all = models.Count;
                models = await OllamaVisionModels(models, backendConfig["baseurl"]?.ToString() ?? "", backend, settings, session, keyOverride, timeout.Token);
                return new JObject { ["success"] = true, ["models"] = models, ["all_count"] = all };
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

    /// <summary>The model names the saved vision service offers (F68), for switching the vision model from the reference page:
    /// an Ollama listing keeps only models that can read images. Null when the service cannot be reached; never returns the address or key.</summary>
    public static async Task<List<string>> SavedVisionModels(Session session, CancellationToken cancel)
    {
        if ((await SessionSettings.GetMagicPromptSettings())["settings"] is not JObject saved)
        {
            return null;
        }
        string backend = VisionBackend(saved);
        JObject settings = VisionSettings(saved);
        string keyOverride = VisionKeyOverride(saved, session);
        if (!await IsServerReachable(backend, settings))
        {
            return null;
        }
        JArray models = new();
        foreach (ModelData item in await GetModelsForBackend(backend, settings, true, session, keyOverride) ?? [])
        {
            string id = item.Model ?? item.Id ?? item.Name;
            if (!string.IsNullOrWhiteSpace(id))
            {
                models.Add(new JObject { ["model"] = id });
            }
        }
        if (backend == "ollama")
        {
            models = await OllamaVisionModels(models, settings["backends"]?[backend]?["baseurl"]?.ToString() ?? "", backend, settings, session, keyOverride, cancel);
        }
        return [.. models.Select(item => item["model"].ToString()).Distinct()];
    }

    /// <summary>Keeps the Ollama models that can read images: asks /api/show for each model's capabilities, and falls back to known
    /// vision model names when the Ollama version does not report capabilities or one check fails.</summary>
    private static async Task<JArray> OllamaVisionModels(JArray models, string baseUrl, string backend, JObject settings, Session session,
        string keyOverride, CancellationToken cancel)
    {
        string showUrl = $"{baseUrl.TrimEnd('/')}/api/show";
        List<JObject> entries = [.. models.OfType<JObject>()];
        Task<bool>[] checks = new Task<bool>[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            string id = entries[i]["model"]?.ToString() ?? "";
            checks[i] = i < MaxCapabilityChecks ? CanReadImages(id, showUrl, backend, settings, session, keyOverride, cancel) : Task.FromResult(HasVisionName(id));
        }
        bool[] results = await Task.WhenAll(checks);
        JArray kept = new();
        for (int i = 0; i < entries.Count; i++)
        {
            if (results[i])
            {
                kept.Add(entries[i]);
            }
        }
        return kept;
    }

    /// <summary>Whether one Ollama model reports the "vision" capability; known vision names decide when capabilities are not available.</summary>
    private static async Task<bool> CanReadImages(string model, string showUrl, string backend, JObject settings, Session session, string keyOverride, CancellationToken cancel)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Post, showUrl);
            if (!ConfigureRequest(request, backend, settings, session, out _, keyOverride))
            {
                return HasVisionName(model);
            }
            request.Content = new StringContent(new JObject { ["model"] = model, ["name"] = model }.ToString(), System.Text.Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await HttpClient.SendAsync(request, cancel);
            if (!response.IsSuccessStatusCode)
            {
                return HasVisionName(model);
            }
            JObject info = JObject.Parse(await response.Content.ReadAsStringAsync(cancel));
            if (info["capabilities"] is JArray capabilities)
            {
                return capabilities.Any(item => item.ToString() == "vision");
            }
            return HasVisionName(model);
        }
        catch (Exception ex) when (ex is HttpRequestException or Newtonsoft.Json.JsonException)
        {
            return HasVisionName(model);
        }
    }

    /// <summary>Whether a model name looks like a known vision model.</summary>
    private static bool HasVisionName(string model)
    {
        string name = model.ToLowerFast();
        return VisionNameParts.Any(part => name.Contains(part));
    }

    /// <summary>Returns only a controlled message, never upstream bodies, settings, or keys.</summary>
    private static JObject Failure(string message) => new() { ["success"] = false, ["error"] = message };
}
