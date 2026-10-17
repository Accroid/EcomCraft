# 商创工坊 · EcomCraft — M0 节点诊断

这是基于需求正文 **V0.2** 的第一个独立扩展切片，不是完整 MVP，也不代表 M0 六项 Spike 已通过。

## 当前可用功能

- SwarmUI 主页面的 **EcomCraft** 管理员标签。
- `POST /API/EcomCraftGetDiagnostics`：读取主控已有的配置和引擎状态，不发起远程连接。
- 检查认证、本机登录绕过、用户输出目录隔离、自动拉取更新、入口端口与远程继续转发配置。
- 列出已登记后端及关联远程执行子后端、引擎状态和槽位信号。
- 展示六项待实测的 Spike，并下载当前诊断 JSON；失败刷新会清除旧报告。

**不包含**：节点新增/修改、主动连接测试、定时心跳、持久化 Node/Task/Asset、逐任务路由/云端授权、队列接管、生成、归档确认、素材发布或升级自动化。现有生成流程不受本扩展影响；现有接口的越权和出网边界也不会因此自动解决。

## 安装与用户执行的验证

目录应保持为 `src/Extensions/EcomCraft/`，主类名和文件名都是 `EcomCraft`。宿主会发现并编译扩展。

1. 在**独立测试副本**中准备 SwarmUI 官方要求的 .NET SDK/运行环境。当前宿主目标 net8.0；上游 README 还要求准备 .NET 10，按固定版本说明配置，不在生产机临时升级。
2. 从仓库根目录，由开发者构建并以开发模式启动，以免未提交扩展代码被 Git 提交哈希缓存遮蔽：

   ```bash
   dotnet build src/SwarmUI.csproj --configuration Debug -o src/bin/live_release
   dotnet src/bin/live_release/SwarmUI.dll --environment dev --launch_mode none --host localhost --port 7801
   ```

   以上命令供用户执行，本轮代理未执行。启动会按宿主行为创建/读取运行数据，因此必须使用测试副本，不要指向生产数据。不要将 Development 环境直接暴露给员工或公网。
3. 尚未安装的副本先完成原生安装。按 `docs/Sharing Your Swarm.md` 设置 Owner 密码、账号认证及角色；共享环境应关闭 localhost 绕过，确认后再对内开放。
4. 使用 Owner/Admin 登录，打开主页面 **EcomCraft** 标签。普通 User/PowerUser 默认不应看到标签，也不应获取 API 数据。
5. 在原生 `Server → Backends` 管理测试后端，再点击 **刷新配置快照**。本扩展不会擅自修改节点或关闭 AllowForwarding。

不要把纯 C# 测试工程当作宿主或插件启动器：它不包含 API、UI 和宿主适配器。

## 权限与 API

专属权限为 `view_extension_tab_ecomcraft`，默认 `ADMINS`，API 与标签共用。扩展先注册此权限，防止宿主将标签权限自动设为普通用户可访问。

请求遵循宿主 API 协议：JSON POST、有效 `session_id`；启用账号认证后先通过合法账号/令牌建立会话。也可以在已登录管理员页面的开发者控制台执行：

```javascript
genericRequest('EcomCraftGetDiagnostics', {}, data => console.log(data));
```

- 顶层字段：`schema_version`、`extension_version`、`scope`、`sampled_at`、`m0_status`、`checks`、`nodes`、`spikes`。
- `scope` 固定为 `configuration_snapshot_only`。
- `m0_status` 只可能是 `blocked` 或 `pending_verification`，没有 ready。
- 未授权：宿主权限拒绝或处理器的 `bad_permissions`；读取失败：`ecomcraft_diagnostics_unavailable`。预期错误包含 `error`，不返回原始异常文本。
- JSON 字段白名单中没有后端地址、设置对象、Header、令牌、提示词、文件路径或日志。友好名称由管理员设置，仍不应往名称里填写秘密；下载报告应作为内部诊断资料保管。

## 如何解读报告

| 字段/结果 | 含义 | 不能推断 |
| --- | --- | --- |
| sampled_at | 主控读取内存快照的时间 | 成功连接、最近成功心跳 |
| configured_backend_id | 宿主后端配置引用，可编辑/重用 | 稳定 EcomCraft 业务节点 ID |
| host_backend_id / remote_backend_id | 执行子后端的主控临时引用/远端引用 | 不随重连变化的永久身份 |
| engine.status | 宿主已知引擎状态，可能滞后 | 此刻网络一定畅通 |
| engine_reports_capacity | 已知直接执行端在引擎层报告有空位 | 已通过能力验证、仅本地、安全可调度 |
| usages / max_usages | 引擎槽位占用/上限 | 物理 GPU 数量或实际运行任务数 |
| 配置通过 | 某个本机设置值符合检查 | 对应 FR、AT 或 Spike 已通过 |
| approved_capabilities 为空 | 本切片没有批准能力的功能 | 节点不能生成任何内容 |

心跳、最近错误、后端版本、环境和能力审批均没有可靠证据，保持 null/unverified；两个远程配置不能证明有两台独立 PC。只对本机 Comfy Self-Starting 与远程 Swarm 的直接 Comfy 执行端解释容量信号，其他类型未知即不报容量。即使远程关闭继续转发，远程直接 Comfy API 仍可能指向外部，必须验证实际数据流。

## 测试

在仓库根目录执行：

```bash
# 无第三方包：运行真实前端脚本的隔离行为测试 + 源码契约检查
node --check src/Extensions/EcomCraft/Assets/ecomcraft.js
node --test src/Extensions/EcomCraft/Tests/*.test.js

# 纯判定器：不启动宿主、不连网络、不读用户数据；需要 .NET 8 SDK
# 用户执行，会在 Tests/bin 和 Tests/obj 生成普通构建产物
dotnet run --project src/Extensions/EcomCraft/Tests/EcomCraft.Diagnostics.Tests.csproj
```

Node 的 VM/DOM 替身不是浏览器测试；源码契约检查不是 C# 编译或真实接口鉴权测试。C# 测试也不覆盖宿主适配器。运行证据、尚未执行的检查和双机操作清单见 [M0-verification.md](docs/M0-verification.md)。

## 代码与版本管理

上游 `.gitignore` 默认忽略整个 `src/Extensions/`。本批不修改根忽略规则；扩展代码可以单独建库，或在当前 fork 中使用 `git add -f <逐个审核的扩展源码文件>` 跟踪。不要对整个目录盲目强制添加，尤其是在运行测试后；不要加入其他扩展、密钥、运行数据、Tests/bin 或 Tests/obj。

本轮工作在 `feature/ecomcraft-m0-diagnostics`；不自动提交、推送、修改远端或重命名主分支。设计、实施清单和验证记录都随扩展交付，遵循仓库 MIT 许可。
