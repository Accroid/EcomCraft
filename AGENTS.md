# Agents 文件：自动化编程代理须知

本文件定义在 SwarmUI 仓库中工作的自动化编程代理必须遵循的基本规则、指南和建议。本文档会随着时间逐步扩充。

SwarmUI 是一个模块化 AI 媒体生成平台，由三个主要部分组成：
- JavaScript/HTML 前端，在用户的浏览器中打开
- C# 服务器，是主要工作核心
- Python 后端，负责实际的内部 AI 执行，主要由上游项目及其依赖（包括 ComfyUI）驱动

本仓库是在 SwarmUI 基础上开发的 EcomCraft Studio。除 SwarmUI 原有功能外，还包含面向电商团队的本地创作工作台、已发布 ComfyUI 工作流调用、MagicPrompt 配置和 EcomCraft 桌面启动入口。


## 本 Agents 文件

开发时必须始终将本文件 [AGENTS.md](/AGENTS.md) 作为重要参考，并将其内容纳入上下文。

如果开发过程中了解到新的核心仓库规则，应将其加入本文件，并确保不会与现有内容重复或冗余。

本文档适用于 SwarmUI 核心仓库和本仓库直接维护的 EcomCraft 扩展。`src/Extensions/EcomCraft/` 是本项目的一部分，不应按外部下载扩展处理；只有明确属于其他外部扩展的任务，才可以不采用本文件中与核心仓库无关的扩展规则。

## 技能文件

如果你了解到适用于特定仓库任务的开发技巧，应将其添加到 `.agents/skills/(skill-name)/SKILL.md`。例如：`.agents/skills/new-api-route/SKILL.md`。每个文件应采用以下格式：

```md
---
name: my-skill
description: 简要说明技能的用途及适用场景。
---

# 我的技能

面向代理的详细说明。

## 适用场景

- 在……时使用此技能
- 此技能适用于……

## 操作说明

- 代理应遵循的分步指南
- 特定领域的约定
- 最佳实践
- 如果需要澄清需求，使用提问工具向用户确认
```

注意：直接创建文件即可，无需先执行 `mkdir` 等命令。

避免让技能文件的适用范围过窄。例如，新增 API 路由的技能应说明通用规则，不应只描述某个具体 API 路由的实现细节。

如果相关系统发生变化，导致技能文件内容过时，应同步更新该技能文件。

开始任务时，务必检查是否存在相关技能文件并遵循其要求。


## 分支规则

所有代码修改只能在 `dev` 分支上进行。除非用户明确要求，不得直接修改 `main` 分支，也不得在 `dev` 之外的分支上提交改动。开始任务前应确认当前处于 `dev` 分支；若不在，先切换或建好本地 `dev` 跟踪 `origin/dev`。

## 构建与启动

已获授权的代理可以在本地构建和启动项目。构建前检查是否有正在运行的 `SwarmUI` 进程，必要时先停止旧进程以释放输出文件锁；不得停止与本项目无关的进程。

- Windows 开发启动：`launch-windows-dev.ps1`
- Windows 常规启动：`launch-windows.bat`
- Linux 开发启动：`launch-linux-dev.sh`
- Linux/macOS 常规启动：`launch-linux.sh` 或 `launch-macos.sh`
- 服务默认地址：`http://127.0.0.1:7801`

常规构建应使用仓库现有的 .NET 项目和启动脚本，不要手工复制发布文件。启动后至少检查 HTTP 状态码、进程是否持续运行和日志中的致命错误。若使用了旧的 `src/bin/live_release`，必须确认其已由当前源码重新生成。

Release 模式下，C# 扩展程序集按 Git 提交短哈希缓存。修改 `src/Extensions/EcomCraft/` 中的 C# 源码但未改变提交时，必须按 `ExtensionsManager.BuildExtension` 的输出目录与 `TargetName` 重新构建扩展；只构建主项目或重启会继续加载旧程序集。Windows 服务若因沙箱权限出现 DPAPI、WMI 或文件访问拒绝，应经执行权限审批后在正常用户环境启动，并确保同一数据目录只有一个服务实例。

构建和启动不得修改 `Data/`、`Output/`、`Models/` 中的用户数据；不要提交 `src/bin`、`src/obj` 或其他构建产物。

## 测试

本仓库结构复杂，各部分紧密关联，不容易进行模拟测试，并且高度依赖缓慢且成本高昂的 GPU 操作。

NUnit 单元测试位于 `SwarmUITests` 项目中。在 Windows 上使用 `launchtools/run_tests.bat` 运行，在 Linux/macOS 上使用 `launchtools/run_tests.sh` 运行。GitHub Actions 会在推送和拉取请求时运行这些测试。

这些测试覆盖独立的 C# 逻辑，不覆盖依赖真实 GPU 的行为。开发者仍须手动运行软件进行实际验证。代理无法运行大多数形式的测试。在开发者投入时间测试之前，代理应尽力通过静态分析和逐步跟踪执行逻辑来验证修改。

## 通用编辑策略

始终尽量用最小改动完成目标。SwarmUI 结构复杂，每项修改都可能产生多种副作用，因此应将修改范围控制在尽可能小的区域内。

涉及 EcomCraft 工作流时，客户端只能提交工作流 ID 和服务端发布的参数白名单；不得把任意 ComfyUI JSON、未校验的节点图、后端选择器或管理员默认图片直接暴露给普通客户机。生成请求必须限制在已登记的 ComfyUI 执行服务器（见下方 EcomCraft 规则），不得隐式回退到未登记地址、远程 SwarmUI 或云端。

修改桌面入口时，同时检查 `Desktop/`、`src/Core/Installation.cs`、启动脚本和 `src/wwwroot/ecomcraft.ico` 的名称与路径是否一致，避免重新生成 SwarmUI 旧桌面程序或快捷方式。

修改文档时，英文原文和 `.zh-CN.md` 中文文档应保持语言入口一致；不要编辑 `docs/APIRoutes/` 自动生成文档。中文导读如果不是完整翻译，必须明确标注为导读并链接英文原文。

提交前应检查 `git status --short`、`git diff --stat` 和 `git diff --check`。不要覆盖工作区中已有的用户修改；不要把仅由构建、启动或测试产生的文件加入提交。

## EcomCraft 规则

- EcomCraft 工作台只能接受工作流 ID、服务端发布的参数白名单和经过服务端校验的普通生成参数。客户端不得提交或展示任意 ComfyUI JSON、未校验的节点图、后端选择器、管理员默认图片或内部会话标识。
- `EcomCraftStudioGenerate` 只能使用 `ExecutionServers.IsExecutionServer` 认可的执行服务器：本机自启动 ComfyUI，以及地址为回环、私有网段或局域网主机名的 `ComfyUIAPIBackend`。不得连接未登记地址、远程 SwarmUI 或公网地址；云端对接完成并经确认之前，不得向任何云端生成服务发送请求。新增后端匹配逻辑时，应同步检查 `ExecutionServers`、`StudioApi.ValidateExecutionBackend` 和生成 API 的权限校验。
- `EcomCraftStudioCatalog`、`EcomCraftStudioQueue` 和 `EcomCraftStudioCancelTask` 返回的数据应遵守最小披露原则：队列接口可以返回执行服务器的 ID、显示名称、状态、排队数、排队位置和节点进度，但不得返回服务器地址、提示词、原始工作流、其他用户的任务 ID、会话 ID 或密钥。取消任务前必须验证当前账号拥有该任务。
- 原生 ComfyUI 工作流发现由 `WorkflowDiscovery` 在后台执行，接口请求只读缓存。本机只读取已配置本机后端、`Installation.FindComfyScripts` 验证的本机安装中的标准 `workflows` / `blueprints` 目录，或 `Settings.EcomCraft.ComfyWorkflowSearchPaths` 指定的目录；局域网执行服务器只通过其 ComfyUI `userdata` 接口读取 `workflows` 目录。不得全盘扫描任意 JSON 或扫描局域网网段。导入前必须验证工作流结构、子图连接和正负提示词输入，导入后仍使用已发布工作流和参数白名单边界；工作流的来源服务器不决定执行服务器。
- EcomCraft 进度观测不得长期保存提示词、工作流输入或生成结果。进度缓存应在任务结束或超时后清理，并在扩展卸载时解除 `ComfyUIAPIAbstractBackend.QueueProgressObserved` 事件订阅。
- 修改桌面入口或首页跳转时，应同时检查 `Desktop/`、`src/Core/Installation.cs`、`src/Core/Program.cs`、启动脚本、`src/Pages/Text2Image.cshtml` 和 `src/wwwroot/ecomcraft.ico` 的名称与路径一致性。
- 修改 EcomCraft 功能后，应同步检查根目录的 `README.md`、`README.en.md`、`docs/README.zh-CN.md` 以及对应功能文档；API 名称、配置键和安全边界必须保持一致。
- 局域网多服务器平台的开发以 [`docs/EcomCraft 局域网多服务器平台需求.zh-CN.md`](docs/EcomCraft%20局域网多服务器平台需求.zh-CN.md) 为准；需求变更先改该文档再改代码。

## 项目结构

仓库包含多个不同部分。修改时应遵守各部分特有的要求。

- `*.sh`、`*.bat`、`*.ps1`、`launchtools/`：仓库根目录附近的基础启动器，修改时必须格外谨慎。
- `Data/`、`Output/`、`Models/`：本地用户数据目录，无论何种情况都不得修改其中的内容。
- `src/bin`、`src/obj`、`.vs/`、`.git/`：自动生成内容，禁止修改。
- `docs/`：通用用户文档。
- `docs/APIRoutes`：SwarmUI API 路由文档，由源码自动生成，不要编辑。
- `dlbackend/`、`src/BuiltinExtensions/ComfyUIBackend/DLNodes`：自动下载的上游 Python 仓库。可在编写 Python 代码或参考 Comfy 后端 API 时查看，但不得修改。
- `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes`：SwarmUI 直接维护的 Python 代码。它是 SwarmUI 唯一直接维护 Python 代码的目录。
- `src/wwwroot/js`：浏览器前端 JavaScript。
- `src/wwwroot/css`：浏览器前端 CSS 样式表。
- `src/Pages`：`.cshtml` C# Razor Pages，包含浏览器前端 HTML。
- `src/*.cs`：C# 主服务器通用代码。
- `SwarmUITests/`：NUnit 测试项目，不包含在常规构建中。通过 `launchtools/run_tests.bat` 或 `launchtools/run_tests.sh` 运行。
- `src/BuiltinExtensions`：内置于主仓库的扩展。
- `src/Extensions/EcomCraft`：本仓库直接维护的 EcomCraft 工作台、MagicPrompt 和工作流接口；修改时可同时更新其必要的核心入口、页面和文档。
- `src/Extensions`：其他外部下载的扩展。如果任务未明确指定某个外部扩展，不得修改其内容。

## CSS 说明

本节适用于 `src/wwwroot/css`。通常也需要同时修改对应的 `src/Pages` 和 `src/wwwroot/js` 文件。

所有前端代码必须兼容常见现代浏览器的当前版本，包括 Chrome、Firefox 和 Safari，也必须能在现代移动浏览器（Android Chrome、iOS Safari）上正常工作。

### 语法

- 保持代码整洁规范。
- 尽量使用标准 CSS 语法。
- 类名选择器必须使用 `.`，不得使用 ID 选择器 `#`。

## JavaScript 说明

本节适用于 `src/wwwroot/js`。通常也需要同时修改对应的 `src/Pages` 和 `src/wwwroot/css` 文件。

所有前端代码必须兼容常见现代浏览器的当前版本，包括 Chrome、Firefox 和 Safari，也必须能在现代移动浏览器（Android Chrome、iOS Safari）上正常工作。

### 语法

- 保持代码整洁规范。
- 尽量使用标准 JavaScript 语法。
- 始终使用 `let`，不得使用 `var` 或 `const`。
- 除非逻辑上确有必要，否则避免使用 `===` 或 `!==`。
- 始终使用完整的大括号代码块，不要省略大括号编写单行 `if` 等语句。
- `else {` 应另起一行，不要写成 `} else {`。
- 函数顶部应有 `/** ... */` 文档注释。
- 使用标准 `for (...)` 循环，不要使用 `arr.forEach`。
- 适当使用 `async` 函数。现有代码对异步函数的使用不足，修改时应谨慎处理。
- 新增 API、队列或权限相关代码时，必须处理请求失败、超时、取消、并发访问和用户可见错误；不要把异常堆栈、密钥、提示词或原始节点图直接写入响应。

### 结构

旧代码中常见独立函数，但新代码应封装在类中。全局或单例逻辑通常使用单例类，例如：`class MyThingHelper { ... } myThingHelper = new MyThingHelper();`

仓库中有许多工具函数，尤其是在 `util.js` 和 `site.js` 中。重新实现已有行为前，务必先检查是否已有合适的函数。如果需要某种可复用的通用功能，不要将其内联实现，而应添加为工具函数。

### 脚本加载

仅供生成页面使用的新 JavaScript 文件，必须添加到 `src/Pages/Text2Image.cshtml` 的 `@section Scripts` 区块中。脚本加载顺序很重要：依赖项必须先于依赖它们的脚本加载。

### 重要工具函数

重新实现常见行为之前，请先检查以下函数：
- `util.js` 中的 `createDiv(id, classes, html)`：创建 DOM 元素。
- `util.js` 中的 `escapeHtml(text)`：转义 HTML。

（待补充更多 JavaScript 说明）

## C# 说明

本节适用于通用的 `src/*.cs` 文件。

### 语法

- 保持代码整洁规范。
- 尽量使用标准 C# 语法。
- 始终使用明确类型，禁止使用 `var`。
- 始终使用完整的大括号代码块，不要省略大括号编写单行 `if` 等语句。
- 当前使用 C# 12 和 .NET 8。
- 所有字段都必须有 `///` XML 文档注释。
- 禁止使用 `{ get; set; }` 形式。字段应声明为字段；只有在属性具有独特行为时才使用属性。
- 处理取消令牌时，区分会话级取消和单个任务取消；链接的 `CancellationTokenSource` 使用完毕后必须释放，事件订阅必须在扩展卸载时移除。
- 访问本机文件系统、ComfyUI 队列和外部进程时，应设置超时并处理 `IOException`、`UnauthorizedAccessException`、网络错误和 `ObjectDisposedException` 等可预期异常。

### 结构

仓库有许多内部工具，也依赖 [FreneticUtilities](https://github.com/FreneticLLC/FreneticUtilities) 提供额外的通用功能。例如，将字符串转为小写时始终使用 FreneticUtilities 的 `ToLowerFast`。重新实现功能前，务必先检查是否已有合适的工具函数。

（待补充更多 C# 说明）

## Python 说明

本节适用于 `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes`。

### Comfy 上游参考代码

可参考 `dlbackend/` 中的 ComfyUI 源码。不得修改这些文件，但在编写 Swarm 的 Python Comfy 节点时，它们能提供重要的上游实现和示例。

### 语法

- 保持代码整洁规范。
- 尽量使用标准 Python 语法。
- 大多数代码采用 Comfy 节点格式，也有少量独立的 `def` 函数。

（待补充更多 Python 说明）
