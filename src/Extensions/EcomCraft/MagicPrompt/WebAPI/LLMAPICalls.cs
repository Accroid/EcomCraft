using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using SwarmUI.Utils;
using SwarmUI.WebAPI;
using SwarmUI.Core;
using SwarmUI.Accounts;
using System.Net.Http;
using EcomCraftStudio.MagicPrompt.WebAPI.Models;

using static EcomCraftStudio.MagicPrompt.BackendSchema;
using SwarmUI.Backends;

namespace EcomCraftStudio.MagicPrompt.WebAPI;

public class LLMAPICalls : MagicPromptAPI
{
    protected static readonly HttpClient HttpClient = CreateHttpClient();

    /// <summary>Backends that may run locally and benefit from a fast reachability check before API calls.</summary>
    private static readonly HashSet<string> LocalBackends = new(StringComparer.OrdinalIgnoreCase) { "ollama", "openaiapi" };

    private static readonly object _reachabilityLock = new();
    private static readonly Dictionary<string, (bool reachable, DateTime checkedUtc)> _reachabilityCache = new();
    private static readonly TimeSpan ReachabilityCacheTtlSuccess = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ReachabilityCacheTtlFailure = TimeSpan.FromSeconds(30);
    private const int ReachabilityTimeoutSeconds = 3;

    // Disable global timeout; per-request CancellationTokenSource controls timeout instead
    private static HttpClient CreateHttpClient()
    {
        var client = NetworkBackendUtils.MakeHttpClient();
        client.Timeout = Timeout.InfiniteTimeSpan;
        return client;
    }

    /// <summary>Checks whether a loopback backend server is reachable with a fast probe.
    /// Remote OpenAI-compatible endpoints must be called directly: their root URL may not serve GET requests.
    /// Results are cached: 10s on success, 30s on failure to avoid repeated probes to a dead server.</summary>
    public static async Task<bool> IsServerReachable(string backend, JObject settings)
    {
        backend = backend?.ToLower();
        if (!LocalBackends.Contains(backend))
        {
            return true; // Cloud backends don't need a connectivity probe
        }
        // Extract the base URL from settings
        string baseUrl = (settings["backends"] as JObject)?[backend]?["baseurl"]?.ToString();
        if (string.IsNullOrEmpty(baseUrl))
        {
            return false;
        }
        baseUrl = baseUrl.TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri serverUri) || !serverUri.IsLoopback)
        {
            return true;
        }
        // Check the cache first — failures are cached longer to avoid repeated probes
        lock (_reachabilityLock)
        {
            if (_reachabilityCache.TryGetValue(baseUrl, out var cached))
            {
                var ttl = cached.reachable ? ReachabilityCacheTtlSuccess : ReachabilityCacheTtlFailure;
                if (DateTime.UtcNow - cached.checkedUtc < ttl)
                {
                    return cached.reachable;
                }
            }
        }
        // Probe the server root with a short timeout
        bool reachable = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(ReachabilityTimeoutSeconds));
            using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl);
            HttpResponseMessage response = await HttpClient.SendAsync(request, cts.Token);
            // Any HTTP response (even 4xx/5xx) means the server is running
            reachable = true;
        }
        catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException or HttpRequestException)
        {
            Logs.Warning($"[MagicPrompt] {backend} server at {baseUrl} is not reachable: {ex.GetType().Name}");
            reachable = false;
        }
        // Cache the result
        lock (_reachabilityLock)
        {
            _reachabilityCache[baseUrl] = (reachable, DateTime.UtcNow);
        }
        return reachable;
    }

    /// <summary>Fetches available models from the LLM API endpoint.</summary>
    /// <returns>A JSON object containing the models or an error message.</returns>
    public static async Task<JObject> GetMagicPromptModels(Session session)
    {
        try
        {
            // Get current settings
            JObject sessionSettings = await SessionSettings.GetMagicPromptSettings();
            if (!sessionSettings["success"].Value<bool>())
            {
                return CreateErrorResponse(sessionSettings["error"]?.ToString() ?? "Failed to load settings");
            }
            JObject settings = sessionSettings["settings"] as JObject;
            if (settings == null)
            {
                return CreateErrorResponse("Configuration not found. Please check your settings.");
            }
            string chatBackend = settings["backend"]?.ToString()?.ToLower() ?? "openrouter";
            string visionBackend = settings["visionbackend"]?.ToString()?.ToLower() ?? "openrouter";
            // Get chat models
            List<ModelData> chatModels = await GetModelsForBackend(chatBackend, settings, false, session);
            if (chatModels == null || !chatModels.Any())
            {
                return CreateErrorResponse($"Failed to fetch models for {chatBackend}. Please check your API key and try again.");
            }
            // Get vision models
            List<ModelData> visionmodels = await GetModelsForBackend(visionBackend, VisionSettings(settings), true, session, VisionKeyOverride(settings, session));
            if (visionmodels == null || !visionmodels.Any())
            {
                return CreateErrorResponse($"Failed to fetch vision models for {visionBackend}. Please check your API key and try again.");
            }
            // Create combined response
            return JObject.FromObject(new
            {
                success = true,
                models = chatModels,
                visionmodels,
                settings
            });
        }
        catch (Exception ex)
        {
            Logs.Error($"EcomCraft.MagicPrompt: Exception occurred: {ex.Message}");
            return CreateErrorResponse(
                "Unexpected error while fetching models:\n" +
                $"{ex.Message}\n\n" +
                "Common solutions:\n" +
                "1. Restart your LLM backend\n" +
                "2. Check your system resources\n" +
                "3. Verify your configuration\n\n" +
                "Need help? Visit: https://github.com/HartsyAI/SwarmUI-MagicPromptExtension"
            );
        }
    }

    public static async Task<List<ModelData>> GetModelsForBackend(string backend, JObject settings, bool isVision = false, Session session = null, string apiKeyOverride = null)
    {
        // Fast-fail for unreachable local backends instead of waiting for the full timeout
        if (!await IsServerReachable(backend, settings))
        {
            string baseUrl = (settings["backends"] as JObject)?[backend]?["baseurl"]?.ToString() ?? "unknown";
            Logs.Error($"[MagicPrompt] {backend} server at {baseUrl} is not reachable. Skipping model fetch.");
            return [];
        }
        // Get endpoint based on backend
        string endpoint = GetEndpoint(backend, settings, "models");
        if (string.IsNullOrEmpty(endpoint))
        {
            string errorMessage = $"Failed to get endpoint for backend: {backend}";
            Logs.Error($"EcomCraft.MagicPrompt: {errorMessage}");
            return [];
        }
        // Create and configure request
        HttpRequestMessage request = new(HttpMethod.Get, endpoint);
        if (!ConfigureRequest(request, backend, settings, session, out string error, apiKeyOverride))
        {
            Logs.Error($"EcomCraft.MagicPrompt: {error}");
            return [];
        }

        // Send request and handle response
        try
        {
            // Log the target endpoint and backend for diagnostics
            Logs.Debug($"[MagicPrompt] Fetching models | backend={backend} | endpoint={endpoint}");
            // Get timeout from backend settings, defaulting to 20 seconds for model listing
            int timeoutSec = GetBackendTimeout(settings, backend, defaultTimeout: 20);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
            HttpResponseMessage response = await HttpClient.SendAsync(request, cts.Token);
            string responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                List<ModelData> models = MagicPromptAPI.DeserializeModels(responseContent, backend);
                return models ?? [];
            }
            else
            {
                // Process error using the ErrorHandler
                string errorMessage = ErrorHandler.ProcessErrorResponse(responseContent, response.StatusCode, backend);
                Logs.Error($"EcomCraft.MagicPrompt: Error fetching models for {backend}: {errorMessage}");
                return [];
            }
        }
        catch (System.Threading.Tasks.TaskCanceledException)
        {
            Logs.Error($"EcomCraft.MagicPrompt: Model listing request timed out for backend {backend} ({endpoint})");
            return [];
        }
        catch (OperationCanceledException)
        {
            Logs.Error($"EcomCraft.MagicPrompt: Model listing request was canceled (timeout) for backend {backend} ({endpoint})");
            return [];
        }
        catch (HttpRequestException ex)
        {
            // Handle network connectivity issues
            Logs.Error($"EcomCraft.MagicPrompt: HTTP request error fetching models for {backend}: {ex.Message}");
            return [];
        }
        catch (Exception ex)
        {
            Logs.Error($"EcomCraft.MagicPrompt: Exception fetching models for {backend}: {ex.Message}");
            return [];
        }
    }

    /// <summary>Gets the appropriate endpoint for model listing based on the backend.</summary>
    /// <param name="backend">The LLM backend being used.</param>
    /// <param name="settings">The current settings object.</param>
    /// <param name="endpointType">The type of endpoint (chat, vision, etc.)</param>
    /// <returns>The endpoint URL for model listing.</returns>
    public static string GetEndpoint(string backend, JObject settings, string endpointType)
    {
        JObject backends = settings["backends"] as JObject;
        if (backends == null)
        {
            return string.Empty;
        }
        JObject backendConfig = backends[backend] as JObject;
        if (backendConfig == null)
        {
            return string.Empty;
        }
        string baseUrl = backendConfig["baseurl"]?.ToString();
        JObject endpoints = backendConfig["endpoints"] as JObject;
        if (string.IsNullOrEmpty(baseUrl) || endpoints == null)
        {
            Logs.Error($"EcomCraft.MagicPrompt: Invalid configuration for backend {backend}: BaseUrl={baseUrl}, Endpoints={endpoints}");
            return string.Empty;
        }
        string endpoint;
        // Special handling for vision endpoints
        if (endpointType == "vision")
        {
            endpoint = backend.ToLower() switch
            {
                "openrouter" or "openai" or "openaiapi" or "grok" => endpoints["chat"]?.ToString(),// OpenAI, Grok, and OpenRouter use the chat endpoint for vision
                "anthropic" => endpoints["messages"]?.ToString() ?? endpoints["chat"]?.ToString(),// Anthropic uses the messages endpoint for both chat and vision
                "ollama" => endpoints["vision"]?.ToString() ?? endpoints["chat"]?.ToString(),// Ollama has a dedicated vision endpoint
                _ => endpoints[endpointType]?.ToString() ?? endpoints["chat"]?.ToString(),
            };
        }
        // Special handling for models endpoints
        else if (endpointType == "models")
        {
            endpoint = backend.ToLower() switch
            {
                "anthropic" => "/v1/models", // Anthropic has a standardized models endpoint now
                _ => endpoints[endpointType]?.ToString()
            };
        }
        else
        {
            endpoint = endpoints[endpointType]?.ToString();
        }
        if (string.IsNullOrEmpty(endpoint))
        {
            Logs.Error($"{endpointType} endpoint not found for backend {backend}");
            return string.Empty;
        }
        // Ensure the base URL doesn't end with a slash and the endpoint starts with one
        baseUrl = baseUrl.TrimEnd('/');
        endpoint = endpoint.StartsWith($"/") ? endpoint : "/" + endpoint;
        if (backend == "openaiapi" && baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            && endpoint.StartsWith("/v1/", StringComparison.OrdinalIgnoreCase))
        {
            endpoint = endpoint[3..];
        }
        string fullUrl = $"{baseUrl}{endpoint}";
        return fullUrl;
    }

    /// <summary>The key type of the vision service's own API key (F68), kept apart from the prompt service's key.</summary>
    public const string VisionKeyType = "openaiapi_vision";

    /// <summary>The vision backend type from the settings, lowercase.</summary>
    public static string VisionBackend(JObject settings)
    {
        return (settings["visionbackend"]?.ToString() ?? settings["backend"]?.ToString() ?? "").ToLower();
    }

    /// <summary>The settings as the vision service sees them (F68): a vision address saved in "visionbaseurl" replaces the shared address
    /// of the vision backend type, so the prompt and vision services can each use their own server. Without one the shared address applies.</summary>
    public static JObject VisionSettings(JObject settings)
    {
        string backend = VisionBackend(settings);
        string url = settings["visionbaseurl"]?.ToString()?.Trim();
        if (string.IsNullOrEmpty(url) || backend is not ("ollama" or "openaiapi") || settings["backends"]?[backend] is not JObject)
        {
            return settings;
        }
        JObject view = (JObject)settings.DeepClone();
        view["backends"][backend]["baseurl"] = url.TrimEnd('/');
        return view;
    }

    /// <summary>The API key override for a vision request (F68): the vision service's own key when one is saved; otherwise the shared key only while
    /// the vision service uses the shared address, and no key for a different address, so a prompt service key never reaches another server.</summary>
    public static string VisionKeyOverride(JObject settings, Session session)
    {
        if (VisionBackend(settings) != "openaiapi")
        {
            return null;
        }
        string own = session?.User?.GetGenericData(VisionKeyType, "key") ?? Program.Sessions.GenericSharedUser.GetGenericData(VisionKeyType, "key");
        if (!string.IsNullOrEmpty(own))
        {
            return own;
        }
        string url = settings["visionbaseurl"]?.ToString()?.Trim().TrimEnd('/') ?? "";
        string shared = settings["backends"]?["openaiapi"]?["baseurl"]?.ToString()?.Trim().TrimEnd('/') ?? "";
        return url.Length == 0 || string.Equals(url, shared, StringComparison.OrdinalIgnoreCase) ? null : "";
    }

    /// <summary>Configures the HTTP request with appropriate headers based on the backend.</summary>
    /// <param name="request">The HTTP request to configure.</param>
    /// <param name="backend">The LLM backend being used.</param>
    /// <param name="settings">The current settings object.</param>
    /// <param name="session">The current user session.</param>
    /// <param name="error">Output parameter for any error message.</param>
    /// <returns>True if configuration was successful, false otherwise.</returns>
    public static bool ConfigureRequest(HttpRequestMessage request, string backend, JObject settings, Session session, out string error, string apiKeyOverride = null)
    {
        // Initialize error message as empty
        error = string.Empty;
        // Validate input parameters
        if (request == null)
        {
            error = ErrorHandler.FormatErrorMessage(ErrorType.Generic, "HTTP request object is null");
            return false;
        }
        if (string.IsNullOrEmpty(backend))
        {
            error = ErrorHandler.FormatErrorMessage(ErrorType.Generic, "Backend name is null or empty");
            return false;
        }
        // Get backends configuration
        JObject backends = settings["backends"] as JObject;
        if (backends == null)
        {
            error = ErrorHandler.FormatErrorMessage(ErrorType.Generic, "Backend configuration not found");
            return false;
        }
        // Normalize backend name for consistency
        backend = backend.ToLower();
        // Process based on backend type
        switch (backend)
        {
            case "openai":
                string apiKey = apiKeyOverride ?? session.User.GetGenericData("openai_api", "key") ?? Program.Sessions.GenericSharedUser.GetGenericData("openai_api", "key");
                if (string.IsNullOrEmpty(apiKey))
                {
                    error = ErrorHandler.FormatErrorMessage(ErrorType.Authentication, "OpenAI API Key not found", "openai");
                    return false;
                }
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
                break;
            case "grok":
                string grokKey = apiKeyOverride ?? session?.User?.GetGenericData("grok_api", "key") ?? Program.Sessions.GenericSharedUser.GetGenericData("grok_api", "key");
                if (string.IsNullOrEmpty(grokKey))
                {
                    error = ErrorHandler.FormatErrorMessage(ErrorType.Authentication, "Grok API Key not found", "grok");
                    return false;
                }
                request.Headers.Add("Authorization", $"Bearer {grokKey}");
                break;
            case "openrouter":
                string openRouterKey = apiKeyOverride ?? session?.User?.GetGenericData("openrouter_api", "key") ?? Program.Sessions.GenericSharedUser.GetGenericData("openrouter_api", "key");
                if (string.IsNullOrEmpty(openRouterKey))
                {
                    error = ErrorHandler.FormatErrorMessage(ErrorType.Authentication, "OpenRouter API Key not found", "openrouter");
                    return false;
                }
                request.Headers.Add("Authorization", $"Bearer {openRouterKey}");
                request.Headers.Add("HTTP-Referer", "https://github.com/HartsyAI/SwarmUI-MagicPromptExtension");
                request.Headers.Add("X-Title", "EcomCraft");
                break;
            case "anthropic":
                string anthropicKey = apiKeyOverride ?? session?.User?.GetGenericData("anthropic_api", "key") ?? Program.Sessions.GenericSharedUser.GetGenericData("anthropic_api", "key");
                if (string.IsNullOrEmpty(anthropicKey))
                {
                    error = ErrorHandler.FormatErrorMessage(ErrorType.Authentication, "Anthropic API Key not found", "anthropic");
                    return false;
                }
                request.Headers.Add("x-api-key", anthropicKey);
                request.Headers.Add("anthropic-version", "2023-06-01");
                break;
            case "openaiapi":
                string openaiApiKey = apiKeyOverride ?? session?.User?.GetGenericData("openaiapi_local", "key") ?? Program.Sessions.GenericSharedUser.GetGenericData("openaiapi_local", "key");
                if (!string.IsNullOrEmpty(openaiApiKey))
                {
                    request.Headers.Add("Authorization", $"Bearer {openaiApiKey}");
                }
                break;
            case "ollama":
                string ollamaKey = apiKeyOverride ?? session?.User?.GetGenericData("ollama_api", "key") ?? Program.Sessions.GenericSharedUser.GetGenericData("ollama_api", "key");
                if (!string.IsNullOrEmpty(ollamaKey))
                {
                    request.Headers.Add("Authorization", $"Bearer {ollamaKey}");
                }
                break;
            default:
                // Handle unsupported backend
                error = ErrorHandler.FormatErrorMessage(ErrorType.Generic,
                    $"Unsupported LLM backend: {backend}. Please select one of the supported backends: Ollama, OpenAI, OpenRouter, or Anthropic.");
                return false;
        }
        return true;
    }

    public static int GetChatBackendTimeoutMs()
    {
        try
        {
            JObject sessionSettings = SessionSettings.GetMagicPromptSettings().GetAwaiter().GetResult();
            JObject settings = sessionSettings?["settings"] as JObject;
            string backend = settings?["backend"]?.ToString();
            return (GenerationTimeout.Seconds(settings, backend) + 30) * 1000;
        }
        catch
        {
            return (GenerationTimeout.DefaultSeconds + 30) * 1000;
        }
    }

    /// <summary>Gets the timeout value from backend settings, with a fallback default.</summary>
    public static int GetBackendTimeout(JObject settings, string backend, int defaultTimeout = 20)
    {
        if (settings == null || string.IsNullOrEmpty(backend))
        {
            return defaultTimeout;
        }
        JObject backends = settings["backends"] as JObject;
        if (backends == null)
        {
            return defaultTimeout;
        }
        JObject backendConfig = backends[backend.ToLower()] as JObject;
        if (backendConfig == null)
        {
            return defaultTimeout;
        }
        int? timeout = backendConfig["timeout"]?.Value<int?>();
        if (timeout.HasValue && timeout.Value >= 0)
        {
            return timeout.Value;
        }
        return defaultTimeout;
    }

    /// <summary>What the "auto" device setting learned since start (F71), by vision endpoint and model: true for the CPU after the GPU hung, false once the GPU answered.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> OllamaVisionOnCpu = new();

    /// <summary>The key <see cref="OllamaVisionOnCpu"/> uses for a vision endpoint and model.</summary>
    private static string PlacementKey(string endpoint, string model) => $"{endpoint}|{model}";

    /// <summary>Whether the configured Ollama vision model was moved to the CPU since start because the GPU hung (F71); false for other services.</summary>
    /// <param name="settings">The MagicPrompt settings as saved (not the vision view).</param>
    public static bool VisionMovedToCpu(JObject settings)
    {
        if (settings is null || VisionBackend(settings) != "ollama")
        {
            return false;
        }
        string endpoint = GetEndpoint("ollama", VisionSettings(settings), "vision");
        return OllamaVisionOnCpu.TryGetValue(PlacementKey(endpoint, settings["visionmodel"]?.ToString() ?? ""), out bool onCpu) && onCpu;
    }

    /// <summary>Forgets what "auto" learned since start for the configured Ollama vision model, so the saved self-test result (F71 P2) applies from the next request.</summary>
    /// <param name="settings">The MagicPrompt settings as saved (not the vision view).</param>
    public static void ForgetLearnedVisionDevice(JObject settings)
    {
        if (settings is null || VisionBackend(settings) != "ollama")
        {
            return;
        }
        string endpoint = GetEndpoint("ollama", VisionSettings(settings), "vision");
        OllamaVisionOnCpu.TryRemove(PlacementKey(endpoint, settings["visionmodel"]?.ToString() ?? ""), out _);
    }

    /// <summary>The version an Ollama server reports, or null when it does not answer within a few seconds.</summary>
    /// <param name="settings">The vision view of the settings.</param>
    public static async Task<string> OllamaVersionAsync(JObject settings, string keyOverride)
    {
        string baseUrl = (settings["backends"] as JObject)?["ollama"]?["baseurl"]?.ToString()?.TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl))
        {
            return null;
        }
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(Program.GlobalProgramCancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using HttpRequestMessage request = new(HttpMethod.Get, $"{baseUrl}/api/version");
            ConfigureRequest(request, "ollama", settings, null, out _, keyOverride);
            using HttpResponseMessage response = await HttpClient.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            string version = JObject.Parse(await response.Content.ReadAsStringAsync(timeout.Token))["version"]?.ToString()?.Trim();
            return string.IsNullOrEmpty(version) || version.Length > 64 ? null : version;
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or Newtonsoft.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Sends one image to an Ollama vision model on the GPU or the CPU and returns how many seconds the answer took, or null when there was no answer
    /// within the limit (F71 P2 self-test). The model is unloaded afterwards, which also clears the state a hung GPU run leaves behind.</summary>
    /// <param name="settings">The vision view of the settings.</param>
    public static async Task<double?> TimeOllamaVisionAsync(JObject settings, string model, MessageContent content, bool cpu, TimeSpan limit, string keyOverride)
    {
        string endpoint = GetEndpoint("ollama", settings, "vision");
        if (string.IsNullOrEmpty(endpoint))
        {
            return null;
        }
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(Program.GlobalProgramCancel);
        deadline.CancelAfter(limit);
        System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Post, endpoint);
            ConfigureRequest(request, "ollama", settings, null, out _, keyOverride);
            request.Content = new StringContent(JsonSerializer.Serialize(GetSchemaType("ollama", content, model, MessageType.Vision, -1, cpu)), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await HttpClient.SendAsync(request, deadline.Token);
            await response.Content.LoadIntoBufferAsync();
            if (!response.IsSuccessStatusCode)
            {
                Logs.Warning($"EcomCraft.MagicPrompt: the vision self-test on the {(cpu ? "CPU" : "GPU")} got HTTP {(int)response.StatusCode}.");
                return null;
            }
            string answer = await DeserializeResponse(response, "ollama");
            return string.IsNullOrWhiteSpace(answer) ? null : elapsed.Elapsed.TotalSeconds;
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or ObjectDisposedException)
        {
            Logs.Warning($"EcomCraft.MagicPrompt: the vision self-test on the {(cpu ? "CPU" : "GPU")} gave no answer within {limit.TotalSeconds:F0} seconds ({ex.GetType().Name}).");
            return null;
        }
        finally
        {
            await UnloadOllamaModel(settings, null, model, keyOverride);
        }
    }

    /// <summary>Asks Ollama to unload a model (keep_alive 0), which clears the state a hung run leaves behind; failures are only logged.</summary>
    private static async Task UnloadOllamaModel(JObject settings, Session session, string model, string keyOverride)
    {
        string baseUrl = (settings["backends"] as JObject)?["ollama"]?["baseurl"]?.ToString()?.TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl))
        {
            return;
        }
        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
            using HttpRequestMessage request = new(HttpMethod.Post, $"{baseUrl}/api/generate");
            ConfigureRequest(request, "ollama", settings, session, out _, keyOverride);
            request.Content = new StringContent(JsonSerializer.Serialize(new { model, keep_alive = 0 }), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await HttpClient.SendAsync(request, timeout.Token);
            Logs.Info($"EcomCraft.MagicPrompt: asked Ollama to unload {model} (HTTP {(int)response.StatusCode}).");
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException)
        {
            Logs.Warning($"EcomCraft.MagicPrompt: could not unload Ollama model {model}: {ex.GetType().Name}");
        }
    }

    /// <summary>Sends the prompt to the LLM API and processes the response.</summary>
    /// <returns>Returns a JSON object with success and a rewritten prompt or an error.</returns>
    [API.APIDescription("Returns a JSON object containing the response from the language model API or an error message.",
        """
        {
            "success": true,
            "response": string,
            "error": string
        }
        """)]
    public static async Task<JObject> MagicPromptPhoneHome(JObject requestData, Session session = null, HttpContext context = null)
    {
        try
        {
            if (requestData == null)
            {
                return CreateErrorResponse("Request data is null");
            }
            long seed = requestData["seed"]?.Value<long>() ?? -1;
            // Safely parse message content
            JToken messageContentToken = requestData["messageContent"];
            if (messageContentToken == null)
            {
                return CreateErrorResponse("Message content is missing");
            }
            // Create message content with explicit parsing
            MessageContent messageContent = new()
            {
                Text = messageContentToken["text"]?.ToString(),
                KeepAlive = messageContentToken["KeepAlive"]?.Value<int?>()
            };
            // Safely parse media content if it exists
            JToken mediaToken = messageContentToken["media"];
            if (mediaToken != null && mediaToken.Type == JTokenType.Array)
            {
                try
                {
                    messageContent.Media = mediaToken.ToObject<List<MediaContent>>();
                }
                catch (Exception ex)
                {
                    Logs.Error($"EcomCraft.MagicPrompt: Failed to parse media content: {ex.Message}");
                    return CreateErrorResponse("Failed to parse media content");
                }
            }
            // Parse model ID
            string modelId = requestData["modelId"]?.ToString();
            // Parse message type with validation
            MessageType messageType = MessageType.Text;
            string messageTypeStr = requestData["messageType"]?.ToString();
            if (!string.IsNullOrEmpty(messageTypeStr))
            {
                if (!Enum.TryParse(messageTypeStr, true, out messageType))
                {
                    messageType = MessageType.Text;
                }
            }
            string action = requestData["action"]?.ToString()?.ToLower() ?? "chat";
            if (string.IsNullOrEmpty(modelId))
            {
                return CreateErrorResponse("Model ID is missing");
            }
            // A vision request can carry its content as the image, so empty text is only a problem without media.
            if (action != "random-prompt" && string.IsNullOrEmpty(messageContent.Text) && (messageContent.Media is null || messageContent.Media.Count == 0))
            {
                return CreateErrorResponse("Message content is missing");
            }
            // Get current settings
            JObject sessionSettings = await SessionSettings.GetMagicPromptSettings();
            if (!sessionSettings["success"].Value<bool>())
            {
                Logs.Error($"EcomCraft.MagicPrompt: {sessionSettings["error"]?.ToString() ?? "Failed to load settings"}");
                return CreateErrorResponse(sessionSettings["error"]?.ToString() ?? "Failed to load settings");
            }
            JObject settings = sessionSettings["settings"] as JObject;
            if (settings == null)
            {
                Logs.Error($"EcomCraft.MagicPrompt: Configuration not found. Please check your settings.");
                return CreateErrorResponse("Configuration not found. Please check your settings.");
            }
            // Get appropriate backend based on message type; a vision request uses the vision service's own address and key (F68)
            string backend = messageType == MessageType.Vision
                ? settings["visionbackend"]?.ToString()?.ToLower() ?? settings["backend"].ToString().ToLower()
                : settings["backend"].ToString().ToLower();
            string keyOverride = null;
            if (messageType == MessageType.Vision)
            {
                keyOverride = VisionKeyOverride(settings, session);
                settings = VisionSettings(settings);
            }
            // Get appropriate endpoint
            string endpoint = GetEndpoint(backend, settings, messageType == MessageType.Vision ? "vision" : "chat");
            if (string.IsNullOrEmpty(endpoint))
            {
                return CreateErrorResponse($"Failed to get endpoint for {backend}");
            }
            // Fast-fail for unreachable local backends instead of waiting for the full timeout
            if (!await IsServerReachable(backend, settings))
            {
                string baseUrl = (settings["backends"] as JObject)?[backend]?["baseurl"]?.ToString() ?? "unknown";
                return CreateErrorResponse(ErrorHandler.FormatErrorMessage(ErrorType.Connectivity,
                    $"{backend} server at {baseUrl} is not reachable. Please ensure the server is running.", backend));
            }
            // Get the instructions from the request if provided
            string clientProvidedInstructions = messageContentToken["instructions"]?.ToString();

            // Only perform server-side lookup if client didn't provide instructions
            if (string.IsNullOrEmpty(clientProvidedInstructions))
            {
                clientProvidedInstructions = action switch
                {
                    "vision" => settings["instructions"]?["vision"]?.ToString() ?? "",
                    "prompt" => settings["instructions"]?["prompt"]?.ToString() ?? "",
                    "caption" => settings["instructions"]?["caption"]?.ToString() ?? "",
                    "generate-instruction" => settings["instructions"]?["instructiongen"]?.ToString() ?? "",
                    "random-prompt" => settings["instructions"]?["randomprompt"]?.ToString() ?? "",
                    _ => settings["instructions"]?["chat"]?.ToString() ?? ""
                };
            }
            messageContent.Instructions = clientProvidedInstructions;
            messageContent.Text = $"{messageContent.Text}";
            // Create request with proper headers and body
            using HttpRequestMessage request = new(HttpMethod.Post, endpoint);
            if (!ConfigureRequest(request, backend, settings, session, out string error, keyOverride))
            {
                Logs.Error($"EcomCraft.MagicPrompt: {error}");
                return CreateErrorResponse(error);
            }
            // Ollama vision on a GPU it cannot drive (seen with Vulkan on older AMD cards) can hang with no output and leave the model reported as loaded
            // while holding no memory (F71). With the "auto" device the first request per server and model gets a limited time on the GPU; past it the
            // model is unloaded and the request retried on the CPU, which is remembered until restart.
            string placementKey = backend == "ollama" && messageType == MessageType.Vision ? PlacementKey(endpoint, modelId) : null;
            bool onCpu = false;
            bool probeGpu = false;
            // The saved self-test result for this Ollama version, model and graphics (F71 P2), so a restart does not wait on a hung GPU again.
            string deviceKey = null;
            VisionDeviceRules.Record saved = null;
            if (placementKey is not null)
            {
                bool auto = VisionDeviceRules.Normalize(settings["visiondevice"]?.ToString()) == VisionDeviceRules.Auto;
                if (auto)
                {
                    deviceKey = await VisionDeviceCheck.KeyAsync(settings, modelId, keyOverride);
                    saved = VisionDeviceCheck.Find(deviceKey);
                }
                bool? remembered = null;
                if (OllamaVisionOnCpu.TryGetValue(placementKey, out bool knownCpu))
                {
                    remembered = knownCpu;
                }
                else if (saved is not null)
                {
                    remembered = saved.Device == VisionDeviceRules.Cpu;
                }
                (onCpu, probeGpu) = VisionDeviceRules.Plan(settings["visiondevice"]?.ToString(), remembered);
                if (saved is not null && saved.Device != (onCpu ? VisionDeviceRules.Cpu : VisionDeviceRules.Gpu))
                {
                    saved = null;
                }
            }
            // The device this request learns by trying the GPU first, saved with its time once the answer is complete.
            string learned = null;
            System.Diagnostics.Stopwatch learnedTime = null;
            // Add request body using BackendSchema
            object requestBody;
            try
            {
                requestBody = GetSchemaType(backend, messageContent, modelId, messageType, seed, onCpu);
            }
            catch (ArgumentException ex)
            {
                // Typically thrown for validation issues (e.g., Grok vision requires direct JPG/PNG URLs)
                Logs.Error($"EcomCraft.MagicPrompt: Request build error for {backend}: {ex.Message}");
                return CreateErrorResponse(ErrorHandler.FormatErrorMessage(ErrorType.UnsupportedParameterImage, ex.Message, backend));
            }
            string jsonContent = JsonSerializer.Serialize(requestBody);
            request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            int timeoutSec = GenerationTimeout.Seconds(settings, backend);
            // A server-side caller may ask for a shorter limit, so a request it stops waiting for does not keep running upstream; never a longer one.
            if (int.TryParse(requestData["timeout"]?.ToString(), out int requestedLimit) && requestedLimit > 0)
            {
                timeoutSec = Math.Min(timeoutSec, requestedLimit);
            }
            if (saved is not null)
            {
                // With a measured speed, a hung run is noticed sooner.
                timeoutSec = VisionDeviceRules.TimeoutFor(saved.Seconds, timeoutSec);
            }
            using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                context?.RequestAborted ?? CancellationToken.None, session?.SessInterrupt.Token ?? CancellationToken.None,
                Program.GlobalProgramCancel);
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
            System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // Send request and handle response
                // Detailed diagnostics to help trace hanging requests
                Logs.Debug($"[MagicPrompt] Sending request | backend={backend} | type={(messageType == MessageType.Vision ? "vision" : "chat")} | endpoint={endpoint} | model={modelId}");
                Logs.Debug($"[MagicPrompt] Using generation timeout of {timeoutSec} seconds for {backend}");
                HttpResponseMessage sent;
                if (probeGpu)
                {
                    using CancellationTokenSource probe = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                    probe.CancelAfter(VisionDeviceRules.GpuProbe);
                    learnedTime = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        sent = await HttpClient.SendAsync(request, probe.Token);
                        OllamaVisionOnCpu[placementKey] = false;
                        learned = VisionDeviceRules.Gpu;
                    }
                    catch (OperationCanceledException) when (!deadline.IsCancellationRequested)
                    {
                        Logs.Warning($"EcomCraft.MagicPrompt: Ollama vision model {modelId} gave no answer on the GPU within {VisionDeviceRules.GpuProbe.TotalSeconds:F0} seconds; unloading it and using the CPU.");
                        OllamaVisionOnCpu[placementKey] = true;
                        learned = VisionDeviceRules.Cpu;
                        await UnloadOllamaModel(settings, session, modelId, keyOverride);
                        learnedTime.Restart();
                        using HttpRequestMessage retry = new(HttpMethod.Post, endpoint);
                        ConfigureRequest(retry, backend, settings, session, out _, keyOverride);
                        retry.Content = new StringContent(JsonSerializer.Serialize(GetSchemaType(backend, messageContent, modelId, messageType, seed, true)), Encoding.UTF8, "application/json");
                        sent = await HttpClient.SendAsync(retry, deadline.Token);
                    }
                }
                else
                {
                    sent = await HttpClient.SendAsync(request, deadline.Token);
                }
                using HttpResponseMessage response = sent;
                string responseContent = await response.Content.ReadAsStringAsync(deadline.Token);
                // Handle API errors indicated by status code
                if (!response.IsSuccessStatusCode)
                {
                    // Use the improved error handler to process response
                    string formattedError = ErrorHandler.ProcessErrorResponse(responseContent, response.StatusCode, backend);
                    Logs.Error($"EcomCraft.MagicPrompt: HTTP {(int)response.StatusCode} error from {backend}: {responseContent}");
                    return CreateErrorResponse(formattedError);
                }
                // Process successful response
                try
                {
                    string llmResponse = await DeserializeResponse(response, backend);
                    if (!string.IsNullOrEmpty(llmResponse))
                    {
                        if (learned is not null && deviceKey is not null)
                        {
                            VisionDeviceCheck.Save(deviceKey, learned, learnedTime.Elapsed.TotalSeconds);
                        }
                        if (action == "prompt")
                        {
                            // How long the professional-mode enhancement takes, for its waiting view (EcomCraft F70); seconds only.
                            EcomCraftStudio.Studio.Generation.TaskTiming.Record(EcomCraftStudio.Studio.Generation.TaskTiming.Enhance, elapsed.Elapsed.TotalSeconds);
                        }
                        return CreateSuccessResponse(llmResponse);
                    }
                }
                catch (Exception ex)
                {
                    // Error during successful response deserialization
                    string errorType = ErrorHandler.DetectErrorType(ex.Message, response.StatusCode, backend);
                    string formattedError = ErrorHandler.FormatErrorMessage(errorType, ex.Message, backend);
                    Logs.Error($"EcomCraft.MagicPrompt: Error deserializing response: {ex.Message}");
                    return CreateErrorResponse(formattedError);
                }
                // If we reach here, something went wrong with a seemingly successful response
                return CreateErrorResponse(
                    ErrorHandler.FormatErrorMessage(ErrorType.Generic,
                    "Response could not be processed properly",
                    backend)
                );
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return CreateErrorResponse("提示词优化请求已取消。未自动重试。");
            }
            catch (OperationCanceledException)
            {
                Logs.Warning($"EcomCraft.MagicPrompt: Generation timed out | backend={backend} | elapsed_seconds={elapsed.Elapsed.TotalSeconds:F1} | limit_seconds={timeoutSec}");
                if (placementKey is not null)
                {
                    // A hung vision run leaves Ollama blocked for later requests until the model is unloaded.
                    await UnloadOllamaModel(settings, session, modelId, keyOverride);
                    if (saved is not null)
                    {
                        // The saved device no longer answers in time, so the next request decides again.
                        VisionDeviceCheck.Forget(deviceKey);
                    }
                }
                return CreateErrorResponse($"提示词优化已等待 {timeoutSec} 秒，模型服务仍未返回完整结果。请检查服务状态或选择响应更快的模型。本次请求未自动重试。");
            }
            catch (HttpRequestException ex)
            {
                Logs.Error($"EcomCraft.MagicPrompt: HTTP request error: {ex.Message}");
                return CreateErrorResponse(ErrorHandler.FormatErrorMessage(ErrorType.HttpRequestError, ex.Message, backend));
            }
            catch (Exception ex)
            {
                Logs.Error($"EcomCraft.MagicPrompt: Error in MagicPromptPhoneHome: {ex.Message}");
                return CreateErrorResponse(ErrorHandler.FormatErrorMessage(ErrorType.GenericException, ex.Message, backend));
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"EcomCraft.MagicPrompt: Error in MagicPromptPhoneHome: {ex.Message}");
            return CreateErrorResponse(ErrorHandler.FormatErrorMessage(ErrorType.GenericException, ex.Message, "unknown"));
        }
    }
}
