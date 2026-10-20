# SwarmUI API 使用文档

> **语言：** [English 原文](API.md) · 简体中文

SwarmUI 提供网络 API，可供外部程序调用生成图像等功能，也可用于管理 SwarmUI 实例（例如修改后端）。

## 基本概念

SwarmUI 使用自己的 API。你可以打开网页界面，在浏览器开发者工具中观察页面发起的 API 调用，再根据需要在自己的代码中复现。

大多数 API 使用 `POST` 请求，目标格式为：

```text
http(s)://<服务器地址>/API/<路由>
```

请求体通常是 JSON，响应也通常为 JSON。

## WebSocket

部分以 `WS` 结尾的 API 路由使用 WebSocket。这类接口通常接收一次初始输入，然后随时间返回多条结果。例如 `GenerateText2ImageWS` 可以在生成过程中持续发送进度和预览图像。

## 授权与会话

除了 `GetNewSession` 以外，所有 API 路由都要求 JSON 请求中包含 `session_id`。调用 `GetNewSession` 获取会话 ID 后，再将其用于其他请求。

如果 SwarmUI 配置了账号认证，还需要携带名为 `swarm_token` 的 Cookie，其值为与有效账号关联的令牌。可在 UI 的 **User** 标签页中找到 **Swarm Auth Tokens** 区域并创建令牌。

## 错误处理

API 路由可能返回 `error` 或 `error_id`：

- 如果 `error_id` 为 `invalid_session_id`，需要重新调用 `/API/GetNewSession` 获取会话 ID 并重试。
- 其他 `error_id` 的含义取决于具体路由。
- 在未提供专门错误 ID 的情况下，`error` 通常是适合向终端用户展示的文字。

## 快速示例：生成并下载图片

以下示例使用 Bash 的 curl/wget 命令。假设 SwarmUI 在本机默认端口运行，且未要求账号认证。

```bash
# 第一步：获取会话 ID
curl -H "Content-Type: application/json" -d "{}" -X POST http://localhost:7801/API/GetNewSession

# 示例响应：
# {"session_id":"9D3534E30DA38499DE782BC38211976A58555AA6","user_id":"local","output_append_user":true,"version":"0.6.3.0.GIT-5ee406ba","server_id":"058716b5-c6f5-49ed-9ca3-be20d82e4c5f"}

# 复制上方响应中的 session_id，然后发起生成请求：
curl -H "Content-Type: application/json" -d '{"session_id":"9D3534E30DA38499DE782BC38211976A58555AA6","images":1,"prompt":"a cat","model":"OfficialStableDiffusion/sd_xl_base_1.0","width":1024,"height":1024}' -X POST http://localhost:7801/API/GenerateText2Image

# 示例响应：
# {"images":["View/local/raw/2024-05-19/a cat-OfficialStableDiffusionsd_xl_base_10s-1872258705.png"]}

# 下载返回的图像路径
wget "http://localhost:7801/View/local/raw/2024-05-19/a cat-OfficialStableDiffusionsd_xl_base_10s-1872258705.png"
```

示例中的会话 ID、版本号和输出路径仅用于演示；实际使用时应读取当前 API 的响应，不要硬编码旧会话。

## API 路由目录

路由文档按功能分类：

- [AdminAPI](APIRoutes/AdminAPI.md)：服务器管理 API。
- [BackendAPI](APIRoutes/BackendAPI.md)：后端管理 API。
- [UtilAPI](APIRoutes/UtilAPI.md)：常用工具 API。
- [ModelsAPI](APIRoutes/ModelsAPI.md)：模型相关 API，包括 LoRA、通配符等。
- [T2IAPI](APIRoutes/T2IAPI.md)：文生图及相关生成 API。
- [BasicAPIFeatures](APIRoutes/BasicAPIFeatures.md)：通用 API，主要涉及用户与会话管理。
- 内置扩展的 API：
  - [GridGeneratorExtension](APIRoutes/GridGeneratorExtension.md)
  - [ComfyUIWebAPI](APIRoutes/ComfyUIWebAPI.md)
  - [ImageBatchToolExtension](APIRoutes/ImageBatchToolExtension.md)

## GET 路由

除上述 API 外，还提供以下 GET 路由：

- `/Text2Image`：主界面 HTML 页面。
- `/Install`：安装器 HTML 页面。
- `/Error/404`、`/Error/BasicAPI`、`/Error/Internal`、`/Error/NoGetAPI`：展示常见错误的 HTML 页面。
- `/css/*.css`：样式表和主题文件。
- `/js/*.js`：界面 JavaScript 文件。
- `/fonts/*.woff2`、`/imgs/*.jpg`、`/imgs/*.png`、`/favicon.ico`：界面资源文件。
- `/View/*.*`：返回已保存的输出，通常形如 `/View/(user)/raw/(year-month-day)/(file).(ext)`；具体格式可能可由用户配置。
- `/Output/*.*`：旧输出路由格式，不建议新客户端使用。
- `/ViewSpecial/*.*`：返回特殊资源视图，例如模型预览图标。
- `/Audio/*.*`：返回用户音频文件（位于 `Data/Audio`）。
- `/ExtensionFile/(extension)/*.*`：返回扩展中的网页资源。
- `/ComfyBackendDirect/*.*`：如果启用了 [ComfyUI Backend Extension](/src/BuiltinExtensions/ComfyUIBackend/README.md)，可直接转发到 ComfyUI 实例。

## C# 客户端示例

下面的代码演示获取会话、调用生成接口、处理会话失效并通过 HTTP 发送 JSON。示例依赖 SwarmUI 使用的 Newtonsoft.Json 与 FreneticUtilities。

```cs
using System.Text;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using FreneticUtilities.FreneticToolkit;
using FreneticUtilities.FreneticExtensions;

namespace MySwarmClient;

public static class SwarmAPI
{
    public static HttpClient Client = new();

    public static string Session = "";

    public static string Address => "http://127.0.0.1:7801";

    static SwarmAPI()
    {
        Client.DefaultRequestHeaders.Add("user-agent", "MySwarmClient/1.0");
    }

    public class SessionInvalidException : Exception
    {
    }

    public static async Task GetSession()
    {
        JObject sessData = await Client.PostJson($"{Address}/API/GetNewSession", []);
        Session = sessData["session_id"].ToString();
    }

    public static async Task<string> GenerateAnImage(string prompt)
    {
        return await RunWithSession(async () =>
        {
            JObject request = new()
            {
                ["images"] = 1,
                ["session_id"] = Session,
                ["donotsave"] = true,
                ["prompt"] = prompt,
                ["negativeprompt"] = "",
                ["model"] = "OfficialStableDiffusion/sd_xl_base_1.0",
                ["width"] = 1024,
                ["height"] = 1024,
                ["cfgscale"] = 7.5,
                ["steps"] = 20,
                ["seed"] = -1
            };
            JObject generated = await Client.PostJson($"{Address}/API/GenerateText2Image", request);
            if (generated.TryGetValue("error_id", out JToken errorId) && errorId.ToString() == "invalid_session_id")
            {
                throw new SessionInvalidException();
            }
            else if (generated.TryGetValue("error", out JToken errorMessage))
            {
                throw new Exception($"Swarm API error: {errorMessage}");
            }
            return $"{generated["images"].First()}";
        });
    }

    public static async Task<T> RunWithSession<T>(Func<Task<T>> call)
    {
        if (string.IsNullOrWhiteSpace(Session))
        {
            await GetSession();
        }
        try
        {
            return await call();
        }
        catch (SessionInvalidException)
        {
            await GetSession();
            return await call();
        }
    }

    /// <summary>Sends a JSON object post and receives a JSON object back.</summary>
    public static async Task<JObject> PostJson(this HttpClient client, string url, JObject data)
    {
        ByteArrayContent content = new(data.ToString(Formatting.None).EncodeUTF8());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return JObject.Parse(await (await client.PostAsync(url, content)).Content.ReadAsStringAsync());
    }
}
```