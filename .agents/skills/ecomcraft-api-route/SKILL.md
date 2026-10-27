---
name: ecomcraft-api-route
description: 新增或修改 EcomCraft 服务端 API（StudioApi.cs、MagicPrompt/WebAPI 等）时使用，包括注册接口、权限校验、参数校验、返回字段、错误码，以及前端 studio.js 的调用对接。
---

# 新增或修改 EcomCraft API

EcomCraft 的接口直接面向普通员工的浏览器，安全边界比 SwarmUI 原生接口更严：只接受工作流 ID 和白名单参数，只在登记过的执行服务器上生成，只返回必要信息。

## 适用场景

- 新增 `EcomCraftStudio*` 或 MagicPrompt 接口
- 修改已有接口的参数、返回字段、权限或错误处理
- 前端需要调用新的服务端能力

## 操作说明

### 注册

- 处理函数写在 `src/Extensions/EcomCraft/Studio/StudioApi.cs`（MagicPrompt 相关写在 `MagicPrompt/WebAPI/`），签名为 `public static Task<JObject> 名称(Session session, ...)`，并加 `[API.APIDescription("用途", "示例返回")]`。
- 在 `EcomCraft.cs` 的 `OnInit` 中用 `API.RegisterAPICall(方法, isUserUpdate, 权限)` 注册：只读接口 `isUserUpdate` 为 `false`，生成、取消、删除等改变状态的接口为 `true`。权限必须填写，不允许为 `null`。
- 注册了事件、校验器或后台任务的，必须在 `OnShutdown` 中对称地移除或停止。

### 权限

- 接口开头先调用 `CanView(session)`，再检查该操作需要的具体权限（如 `Permissions.BasicImageGeneration`、`ParamVideo`、`UserDeleteImage`、`EcomCraft.PermUsePublishedWorkflows`）。注册时的权限只是第一道门，处理函数里仍要检查。
- 不要为了让功能能用而授予 SwarmUI 的宽泛权限（例如任意 Comfy 工作流权限）；需要新权限时用 `Permissions.Register` 新建一个范围窄的权限。
- 操作某个任务、图片或记录之前，验证它属于当前账号（参考 `EcomCraftStudioCancelTask` 只在 `session.User.CurrentSessions` 中查找）。
- 前端需要据此显示或隐藏按钮时，在 `EcomCraftStudioCatalog` 返回 `can_xxx` 布尔值，而不是让前端猜。

### 输入校验

- 只读取已知字段，枚举值用白名单（参考 `mode is not ("text2img" or ...)`）；模型、LoRA、工作流按 ID 在服务端列表中查找，找不到就报错。
- 不接受客户端提交的 ComfyUI JSON、节点图、后端 ID 或服务器地址。生成只能落到 `ExecutionServers.IsExecutionServer` 认可的服务器；改动后端匹配逻辑时，同时检查 `ExecutionServers`、`StudioApi.ValidateExecutionBackend` 和生成接口的权限校验。
- 云端对接确认前，不向任何云端生成服务发请求，也不在本机失败时自动回退到云端或其他服务器。

### 返回与错误

- 失败用 `Error("中文用户提示", "error_id")` 返回，提示写清原因和下一步。不返回异常堆栈、服务器地址、密钥、提示词、原始工作流、其他用户的任务 ID 或会话 ID。
- 新的 `error_id` 必须加入 `studio.js` 顶部的 `safeErrors`，否则前端只会显示笼统的“服务未正常返回”。结果不确定的错误（可能已经执行）同时加入 `uncertainErrors`。
- 访问文件系统、ComfyUI 或网络时设置超时，按类型捕获 `IOException`、`UnauthorizedAccessException`、`HttpRequestException`、`OperationCanceledException`、`ObjectDisposedException` 等；链接的 `CancellationTokenSource` 用完要释放。
- 进度、队列等缓存不能长期保存提示词、工作流输入或结果，任务结束或超时后清理。

### 前端对接

- 前端统一通过 `studio.js` 的 `request(route, body, signal)` 调用，它会附带会话、处理会话失效和错误码，不要另写 `fetch`。
- 处理请求失败、超时、取消和重复点击（请求进行中禁用按钮）；界面规则见 `ecomcraft-studio-ui` 技能。

### 收尾

- 遵守 `AGENTS.md` 的 C# 规则：显式类型、不用 `var`、完整大括号、字段写 `///` 注释、字符串小写用 `ToLowerFast`。
- 按 `ecomcraft-docs-sync` 技能同步文档；不要编辑自动生成的 `docs/APIRoutes/`。
- 按 `ecomcraft-build-run` 技能编译并重启验证；没有 .NET SDK 时写明未经编译验证。
