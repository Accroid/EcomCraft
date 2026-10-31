# EcomCraft 重构执行计划

> **版本：** v1.0 · **日期：** 2026-10-31 · **体检基线提交：** `2a7e84ec`（`dev` 分支）
>
> **执行者：** 开发 AI，严格按本文执行，本文可独立阅读。配套文件：[EcomCraft 实施计划](EcomCraft%20实施计划.zh-CN.md)（新功能计划，英文版 [EcomCraft Implementation Plan](EcomCraft%20Implementation%20Plan.md)，与本文放在同一目录）。
>
> **文中行号均为基线提交的行号**，代码变动后会偏移。移动代码时以类名、方法名和区块注释为准。

---

## 1. 重构方针（已定，不要改）

**这是渐进式、保持行为不变的重构，不是重写。** 项目已有约 3.8 万行扩展代码和大量业务规则，重写的风险远高于收益，而且功能尚未通过验收。

| 做 | 不做 |
|---|---|
| 拆大文件、拆长方法 | 重写，或引入 React、TypeScript、打包工具、npm 依赖 |
| 合并真正重复的基础代码 | 改 API 名称、请求和响应字段（R08 的 `raw_prompt` 除外） |
| 修第 2 节确认的缺陷 | 改 `Data/` 下任何文件的格式 |
| 建立测试和 CI 安全网 | 改元素 ID、CSS 视觉效果、界面文字 |
| 为新功能预留接入点 | 趁重构删除或隐藏功能，或实现 `实施计划` 里的新功能 |
| | 重构 `MagicPrompt/`（第三方来源代码，见 2.3） |
| | 批量把 `===` 改成 `==`（类型转换可能改变行为） |
| | 把核心改动搬回上游或大规模移动核心文件 |

---

## 2. 体检结果

### 2.1 已确认的缺陷（R04–R08 修复）

| 级别 | 问题 | 位置 | 后果 |
|---|---|---|---|
| **高** | 索引文件读取失败时从空数据开始，下一次修改就覆盖原文件。`ProjectLibrary` 和 `WorkflowReview` 有保护，其他没有 | `Products/ProductLibrary.cs:33-70`；`Brand/BrandKitLibrary.cs` 注释相同，须核对 | 文件一时读不了（如被占用或损坏），用户保存一个商品就会丢掉整个商品库索引 |
| **中** | 备份恢复的路径可逃逸。`BackupRules.Validate` 只检查整个名称，而 `DestinationFor` 截掉前缀后再 `Path.Combine`；`ecomcraft/C:/x` 或 `ecomcraft//x` 这类名称可写到数据目录之外 | `BackupService.cs:183-190` | 恶意备份包可写任意路径（只有管理员能恢复） |
| **中** | 批次的图片数据只在下一次 `StartSet` 时才清理，没有定时清理 | `Generation/BatchService.cs:173、743` | 商品图和参考图可能一直留在内存，超过文档承诺的 6 小时 |
| **中** | `raw_prompt` 是内部标记，但公开接口也接受，客户端可绕过模板提示词。前端没有发送它（已核对） | `Templates/TemplateApi.cs:312` | 内部标记暴露；新功能还要加内部参数 |

> 以上来自读代码，未实际运行复现。修复时必须先写失败的测试。

### 2.2 结构问题

| 问题 | 数据 | 处理 |
|---|---|---|
| `studio.js` 是单个闭包，内含所有视图 | 6088 行，无法单独加载和测试 | R17–R19 |
| `simple.js` 单个类 | 3712 行，158 个方法，`attach` 230 行 | R15–R16 |
| `StudioApi.cs` 职责过多 | 2106 行，混合接口、校验、队列、进度 | R07 |
| 超长方法 | `EcomCraftStudioGenerate` 约 308 行、`StartSet` 约 308 行、`GeneratePublishedWorkflow` 约 244 行 | R09、R10 |
| `BatchService` 经 `JObject` 调用公开接口 | `BatchService.cs:622` | R08 |
| 出图后处理写死在接口方法内 | `StudioApi.cs:772-795` | R09（T86 要接入） |
| 蓝图写死单个文件 | `StandardWorkflows.Blueprint()` | R13（T84 要接入） |
| 原子写入重复 | 10 处 `{file}.tmp` + `File.Move` | R04 |
| 图片 data URL 解析重复 | 至少 8 处，允许的格式和上限不一 | R12 |
| `Studio/` 根目录杂乱 | 工作流、备份、诊断混放 | R14 |
| 核心改动多 | 不算扩展和 `docs/`，与上游相差 92 个文件（+4561 / −2042），`Installation.cs` +402 行 | R21 |

### 2.3 规范和工程问题

| 问题 | 数据 | 处理 |
|---|---|---|
| `studio.js` 违反 JS 规范 | `const` 165 处、`forEach` 4 处、`} else` 10 处、`===` 146 处 | R20（`===` 不批量改） |
| `MagicPrompt/` 的 `{ get; set; }` | 136 处，集中在第三方来源的模型类，其中有序列化依赖 | **豁免**，写入 AGENTS.md |
| CI 不覆盖扩展 | `build-and-check.yml` 构建 `SwarmUI.sln`，而主项目排除了 `Extensions/**`；不跑扩展单测和前端测试；只有 Ubuntu | R01 |
| 本机无法编译 | 没有 `dotnet` | R00 |
| 前端测试绑定文件名 | 多个测试直接读 `studio.js`、`simple.js` | R02 |
| 验收积压 | 总表 79 个任务：已完成 33、进行中 23、未开始 19、暂缓 3、待验收 1 | 用户在检查点验收 |
| 文档命名不一致 | `Prompt Enhancement.md` 是中文，`.en.md` 是英文，与其他文档相反 | R22 |
| AGENTS.md 过时 | 写着“仓库不包含自动化测试项目”，实际有 30 个测试文件 | R23 |
| 残留 | 已合并的分支 `wt/t56` 和工作树 `.claude/worktrees/t56`；`.claude/` 未忽略 | R22 |

### 2.4 基线

- **前端测试：** 50 个，全部通过（本次实际运行，Node 24）。
- **C# 单元测试：** 本机无 SDK，未运行，数量和结果未知，由 R00 确认。
- **主流程：** 未运行，由 R03 的回归清单确认。

---

## 3. 执行规则（必须遵守）

1. **严格按第 4 节顺序，一个会话一个 R 任务。** 依赖未完成不得开始。**第一件事是先做 `实施计划` 的 T81，把目标写进仓库。之后每个任务开工前，都先读 `docs/EcomCraft Architecture.zh-CN.md` 第 1、2 节和本任务章节，确认没有偏离目标。**
2. **只做任务写明的内容。** 发现其他问题写进完成报告，不要顺手改。
3. **遇到以下情况立刻停止并报告，不要自行变通：**
   - 本文的前提与代码不符（方法不存在、依赖方向相反等）。
   - 完成任务必须改变用户可见行为（第 2.1 节的修复除外）。
   - 检查失败且原因不明，或两次修复仍未通过。
   - 需要安装软件、推送代码或删除分支，而用户尚未同意。
4. **纯移动和逻辑修改分开提交。** 移动提交里不改任何逻辑、不改格式。用 `git diff --color-moved=dimmed-zebra` 自查。
5. **分支和提交：** 只在 `dev` 分支。每个任务至少一个提交，信息格式 `refactor(R07): 中文说明`（缺陷修复用 `fix(R04): …`）。提交前跑 `git status --short`、`git diff --stat`、`git diff --check`。**推送需要用户同意。** 不提交 `.claude/`、`pic/`、构建产物。
6. **遵守 `AGENTS.md`：** 不改 `Data/`、`Output/`、`Models/`、`dlbackend/`；不改 `docs/APIRoutes/`；C# 不用 `var`、字段写 `///`；JS 只用 `let`、函数写 `/** */`。
7. **诚实报告：** 没跑过的检查写“未运行”。任务状态由用户验证后才改为已完成。
8. **改前端后** 更新 `index.html` 中所有 `?v=`；**改 C# 后** 按 `.agents/skills/ecomcraft-build-run` 重新构建扩展。

### 每个任务的检查门

| 改动类型 | 必须通过 |
|---|---|
| 所有任务 | `git diff --check` |
| 前端 | `node --test src/Extensions/EcomCraft/Tests/*.test.cjs`；R02 之后再加 eslint 检查 |
| C# | `dotnet build src/Extensions/EcomCraft/EcomCraft.csproj -c Debug -o <临时目录>`；`dotnet test src/Extensions/EcomCraft/Tests/EcomCraft.Tests.csproj` |
| 改了 `src/` 核心 | 再加 `dotnet build src/SwarmUI.csproj -c Debug -o <临时目录>`（不要输出到 `src/bin/live_release`） |

**C# 任务没有通过编译，不算完成，也不得开始下一个依赖它的任务。**

### 完成报告格式

```
任务：R__
改动：文件和原因
检查：命令和结果（失败原样贴出）
未验证：哪些没跑，用户要怎么验证
行为变化：无 / 具体内容
范围外发现：只列出，未修改
```

---

## 4. 任务总览

| 阶段 | 任务 | 内容 | 依赖 | 规模 |
|---|---|---|---|---|
| A 安全网 | R00 | 编译环境和基线 | — | S |
| | R01 | CI 覆盖扩展 | R00 | S |
| | R02 | 前端测试和静态检查 | — | M |
| | R03 | 接口快照和回归清单 | — | S |
| | **CP0** | **用户跑回归清单，记录基线** | R03 | — |
| B 修缺陷 | R04 | 文件读写统一，读取失败不覆盖 | R00 | M |
| | R05 | 备份恢复路径 | R00 | S |
| | R06 | 批次内存定时释放 | R00 | S |
| | **CP1** | **用户回归** | R04–R06 | — |
| C 后端 | R07 | `StudioApi` 按职责拆成 partial | CP1 | M |
| | R08 | 内部调用接口，收回 `raw_prompt` | R07 | M |
| | R09 | 拆 `EcomCraftStudioGenerate`，抽出后处理流程 | R08 | L |
| | R10 | 拆 `BatchService` 和 `StartSet` | R08 | M |
| | R11 | `PromptService` 按职责拆成 partial | CP1 | S |
| | R12 | 统一图片 data URL 解析 | R07、R10、R11 | M |
| | R13 | 标准工作流支持多蓝图 | CP1 | S |
| | R14 | 整理 `Studio/` 目录 | R07–R13 | S |
| | **CP2** | **用户回归** | R14 | — |
| D 前端 | R15 | `simple.js` 拆文件 | R02、CP0 | L |
| | R16 | `SimpleMode.attach` 按功能拆分 | R15 | S |
| | R17 | `studio.js`：公共层和独立视图 | R02、CP0 | L |
| | R18 | `studio.js`：管理、画廊、队列 | R17 | L |
| | R19 | `studio.js`：创作页和事件绑定 | R18 | L |
| | R20 | 前端规范统一 | R16、R19 | S |
| | **CP3** | **用户回归** | R20 | — |
| E 收尾 | R21 | 核心改动清单，拆出 `Installation` 新增代码 | CP2 | M |
| | R22 | 仓库和文档清理 | — | S |
| | R23 | 更新 AGENTS、技能和总表 | R14、R20 | S |

**并行：** B、C 与 D 改的文件基本不重叠，可并行；同一阶段内按序。

---

## 5. 任务详情

### 阶段 A：安全网

#### R00 编译环境和基线

本机没有 .NET SDK。**先问用户选哪种（不得自行安装）：**

1. **（推荐）** 用户同意后，安装 .NET 8 SDK 到用户目录（不需要管理员权限），用微软官方 `dotnet-install.ps1`，安装到 `%LOCALAPPDATA%\Microsoft\dotnet`。
2. 整个重构改到能编译的电脑上做。

然后：

1. 跑检查门的 C# 命令，记录测试数量和结果到完成报告。
2. 跑前端测试，确认 50 个通过。
3. 现有测试失败时**不要修**，记录下来，作为基线问题交给用户。
4. 打本地标签 `refactor-baseline`（不推送）。

**验收：** 扩展能编译；基线数字明确。

#### R01 CI 覆盖扩展

修改 `.github/workflows/build-and-check.yml`，**只新增 job，不改现有 job**：

```yaml
    ecomcraft:
        strategy:
            fail-fast: false
            matrix:
                os: [windows-latest, macos-latest]
        runs-on: ${{ matrix.os }}
        steps:
        - uses: actions/checkout@v6
        - uses: actions/setup-dotnet@v5
          with:
            dotnet-version: '8.x'
        - uses: actions/setup-node@v4
          with:
            node-version: '22'
        - run: dotnet build src/Extensions/EcomCraft/EcomCraft.csproj -c Debug
        - run: dotnet test src/Extensions/EcomCraft/Tests/EcomCraft.Tests.csproj
        - run: node --test src/Extensions/EcomCraft/Tests/
```

- 扩展依赖主项目的方式要实际验证，`SwarmUI.extension.props` 若要求先构建主项目，就加上这一步。
- `node --test <目录>` 的写法按实际 Node 版本调整，要覆盖全部 `*.test.cjs`。
- 现有 Ubuntu job 的状态本机查不了（没有 `gh`），推送后让用户看结果。现有 job 失败不在本任务范围，记录即可。

**验收：** 需要用户同意推送后确认。未推送时报告写“未验证”。

#### R02 前端测试和静态检查

**目标：** 让拆文件不会破坏测试，并能发现拆分造成的变量缺失。

1. 新建 `Tests/helpers/studio-sources.cjs`，提供：
   - `scriptFiles()`：解析 `index.html` 的 `<script src="/EcomCraft/...">`，按加载顺序返回本地路径。
   - `readAllScripts()`：按顺序拼接所有工作台脚本（不含 `lang/en.js`）。
   - `loadSimpleMode()`：把所有 `simple*.js` 按顺序放进一个 `vm` 上下文，返回 `SimpleMode`。
2. 修改 `simple-mode.test.cjs`、`mobile.test.cjs`、`error-codes.test.cjs`、`studio-i18n.test.cjs`：读 `studio.js`、`simple.js` 的地方改用上述函数。**断言内容不变。**
3. 新增 `Tests/frontend-structure.test.cjs`：
   - `index.html` 里每个脚本都在 `StudioRoutes.cs` 的 `Assets` 中有路由，文件存在。
   - 脚本和样式的 `?v=` 版本相同。
   - 脚本里 `$('xxx')` 引用的静态 ID 都存在于 `index.html`；动态生成的 ID 用明确的白名单列出（首次运行得出，人工核对后写入）。
4. 新增 `Tools/lint/eslint.config.mjs`，只启用 `no-undef`、`no-redeclare`，`sourceType: 'script'`，声明浏览器全局和 `SimpleMode`、`I18n`、`StudioCore`。命令：

   ```
   npx --yes eslint@9 -c src/Extensions/EcomCraft/Tools/lint/eslint.config.mjs src/Extensions/EcomCraft/Studio
   ```

   不提交 `node_modules`，不新增 `package.json`。基线必须 0 错误；有错误先报告，不要改代码。

**验收：** 前端测试通过且数量增加；eslint 0 错误。

#### R03 接口快照和回归清单

1. 新增 `Tests/api-surface.test.cjs` 和快照文件 `Tests/api-surface.json`：从 `EcomCraft.cs` 和 `MagicPromptAPI.cs` 提取所有 `API.RegisterAPICall` 的方法名和权限，排序后与快照比较。之后改接口必须显式更新快照。
2. 新增 `docs/EcomCraft 回归清单.zh-CN.md` 和 `docs/EcomCraft Regression Checklist.md`。每项写明操作和预期：

| # | 流程 |
|---|---|
| 1 | 登录，打开工作台，无控制台错误，中英文切换正常 |
| 2 | 上传商品：本机、商品库、画廊选图、自动抠图、拖动摆放 |
| 3 | 照参考图做：传 2 张参考，描述完成，出图，取消一个任务 |
| 4 | 做一张图：选平台、尺寸和类型，出图 |
| 5 | 出一整套（含白底图）；分镜；重试失败项 |
| 6 | 候选图：收藏、下载、卖点图、在专业模式精修、平台打包 |
| 7 | 专业模式：文生图、图生图、局部重绘、扩图、放大 |
| 8 | 上传自用工作流并运行，提交审核 |
| 9 | 工作流审核：发布、驳回、下架；初始化标准工作流 |
| 10 | 品牌资料包、项目归档、用相同参数重做 |
| 11 | 队列、改派、画廊筛选和批量操作 |
| 12 | 设置：提示词优化、看图模型、存储、向导、诊断包、备份和恢复 |
| 13 | 窄屏：只有简易模式 |
| 14 | 用普通账号确认看不到管理入口和他人任务 |

#### CP0 用户基线回归

用户在能跑 GPU 的电脑上跑一遍清单，记录每项通过与否。**已知不通过的项写入清单作为基线**，之后只判断是否变差。开发 AI 不能代替这一步。

### 阶段 B：修缺陷

#### R04 文件读写统一

1. 新建 `Studio/Common/JsonFile.cs`（只依赖 .NET 和 Newtonsoft，加入测试项目的 `Compile Include`）：

   ```csharp
   /// <summary>Result of reading a data file.</summary>
   public enum JsonFileState { Missing, Ok, Unreadable }

   public static class JsonFile
   {
       public static JsonFileState TryReadObject(string path, out JObject value, out string error);
       /// <summary>Writes through a temporary file next to the target and then replaces it.</summary>
       public static bool TryWriteAtomic(string path, string content, out string error);
   }
   ```

   捕获 `IOException`、`UnauthorizedAccessException`、`JsonException`；日志由调用方写。

2. 先写测试 `JsonFileTests`：缺失、正常、损坏 JSON、写入后内容正确、写入失败时原文件不变。

3. 逐处迁移，**文件格式和缩进不变**：

| 位置 | 读取失败时的要求 |
|---|---|
| `Products/ProductLibrary` | **修复：** 记录 `Unreadable`，`Save()` 拒绝并返回错误 |
| `Brand/BrandKitLibrary` | **修复：** 同上 |
| `Projects/ProjectLibrary` | 已有保护，只换实现 |
| `Workflows`（`WorkflowReview`） | 保留现有“损坏文件改名保存 + `LoadFailed`”行为 |
| `Storage/CandidateCleanup`、`Storage/WorkSync`、`Storage/StorageSettings`、`Templates/TemplateStore` | 注释称不覆盖原文件。**逐个核对 `Save` 是否真有保护，没有就按商品库的方式修**，并写进报告 |
| `Prompting/PromptService`（设置文件） | 保留“读不了算关闭”；第 831 行注释说明它会用默认值替换，保持原样并在报告中指出 |
| `MagicPrompt/VisionDeviceCheck` | 只换写入方法（AGENTS.md 要求读取失败不覆盖，核对后处理） |

4. 商品库和品牌资料包保存失败时，返回错误 `data_file_unreadable`，文案白话：“商品库文件暂时读不了，这次没有保存，原有数据没有动。请稍后重试或联系管理员。”按 `ecomcraft-api-route` 技能加入 `safeErrors` 和英文翻译。

**验收（用测试或手工）：** 把 `products.json` 写成损坏内容后启动，保存商品失败，文件内容未变；修复文件后重启正常。

**行为变化：** 仅限读取失败时。

#### R05 备份恢复路径

1. 在 `BackupRules` 增加纯函数 `ResolveDestination(string entryName, string ecomcraftRoot)`：去掉前缀后，用 `Path.GetFullPath` 解析，必须位于根目录内，否则返回 `null`。
2. `Validate` 也拒绝这类名称：整体校验不通过时不写任何文件。
3. `BackupService.DestinationFor` 改用它。
4. 先写失败测试（`BackupRulesTests`）：`ecomcraft/C:/x`、`ecomcraft//x`、`ecomcraft/\x`、`ecomcraft/a/../../x`、正常名称。
5. 测试需覆盖 Windows 分隔符；在其他系统上行为不同的用例要按系统判断，不要跳过 Windows 用例。

#### R06 批次内存定时释放

1. 参照 `Storage/CandidateCleanup.Start/Shutdown`，给 `BatchService` 加后台循环，每 10 分钟调用 `Cleanup()`。
2. 在 `EcomCraft.cs` 的 `OnInit`、`OnShutdown` 注册和停止。
3. `Cleanup()` 处理前检查有无进行中的任务：**未结束的批次不清理**，避免运行中的任务被清掉（现有逻辑只看时间，这一点要先确认对运行中任务的影响，若已有处理则保持）。
4. 过期时间仍为 6 小时，不改接口。

### 阶段 C：后端结构

#### R07 `StudioApi` 拆成 partial

**只移动，不改逻辑。** 把 `public static class StudioApi` 改为 `partial`，调用方全部不用改（包括 `StudioApi.Error` 的 248 处）。`[API.APIClass]` 只保留一处。文件放在 `Studio/Api/`，**namespace 不变**。

| 文件 | 成员（基线行号） |
|---|---|
| `StudioApi.cs` | 常量、`CanView`、`IsCloudModelConfigured`、模型列表帮助方法（389–458）、`LocalBackendStatus`、`EcomCraftStudioBackendStatus`、`EcomCraftStudioCatalog`（606–661）、`Error` |
| `Api/StudioApi.Progress.cs` | `ComfyProgress`、`ComfyQueueProgress`、`ObserveComfyProgress`、`NativeQueueEntries`（45–187） |
| `Api/StudioApi.Execution.cs` | 工作流参数集合（188–205）、`PrepareNativeWorkflowSeeds`、`ValidateExecutionBackend`、问题检查、`TryReadPinnedServer`、`ScheduleAndSubmit`（206–388） |
| `Api/StudioApi.Queue.cs` | `EcomCraftStudioQueue`、`EcomCraftStudioCancelTask`（459–605） |
| `Api/StudioApi.Generate.cs` | `EcomCraftStudioGenerate`、提示词帮助方法（662–996）、尾部校验帮助方法中只被它使用的部分 |
| `Api/StudioApi.Preparing.cs` | `PreparingTask` 等（997–1134） |
| `Api/StudioApi.Background.cs` | `BackgroundTask`、`SubmitBackgroundGeneration`、`FinishedResponse`（1135–1297） |
| `Api/StudioApi.PublishedCatalog.cs` | 权限覆盖、动态参数、目录、输入、`InferWorkflowKind`、`TryGetPublishedWorkflow`、`ParseWorkflowObject`（1298–1567） |
| `Api/StudioApi.PublishedGenerate.cs` | `GeneratePublishedWorkflow`（1568–1811） |
| `Api/StudioApi.Validation.cs` | `ValidatePublishedWorkflowInput` 及数值、选项、图片校验、`GenerationError`（1812–2104） |

**注意：**
- C# 不保证不同 partial 文件之间静态字段的初始化顺序。互相依赖的静态字段放同一文件，移动前逐个核对。
- 确认 `docs/APIRoutes` 生成逻辑和 `[API.APIDescription]` 不受影响。

**验收：** 编译通过；`api-surface` 快照不变；`git diff --color-moved` 显示几乎全是移动。

#### R08 内部调用接口，收回 `raw_prompt`

**问题：** `BatchService` 把内部标记放进 `JObject` 调公开接口，客户端也能发送。

1. 新增 `internal sealed class GenerateOptions`，字段 `bool RawPrompt`（写 `///`）。
2. 新增 `internal static Task<JObject> GenerateInternal(Session, JObject, GenerateOptions)`，放原逻辑。公开的 `EcomCraftStudioGenerate` 只调用它并传 `new()`。
3. `TemplateApi.Resolve` 改为从参数读取，不再读 `rawInput["raw_prompt"]`，并**移除请求里的同名字段**。检查 `TemplateApi.WithTemplatePrompt`（`StudioApi.cs:1080` 的后台路径）也按相同选项处理，否则参考模式的后台提交会丢失原始提示词。
4. `BatchService.GenerateAsync` 改调 `GenerateInternal`。
5. 增加测试，至少断言：公开路径不再读取 `raw_prompt`。

**行为变化：** 客户端发送 `raw_prompt` 不再生效。前端未使用（已核对）。参考模式行为必须不变，用回归清单第 3 项确认。

#### R09 拆 `EcomCraftStudioGenerate` 和后处理流程

1. 按方法内现有区块注释（`Reference / edit images`、`Mask`、`Upscale`、`LoRAs`、`Sampler / scheduler`）提取私有方法，每个返回错误或 `null`。**顺序和错误文案不变。**
2. 提取 `GeneratePublishedWorkflow` 的长段落。目标：单个方法不超过约 100 行，做不到的写明原因。
3. 新建 `PostProcess/GenerationFinish.cs`，把 `StudioApi.cs:772-795` 附近的出图后步骤（`OutputResize.ApplyAsync`、`ProductPreserve.ApplyAsync`、`Discard`、失败统计）移入，提供一个 `internal static` 入口。**顺序不变：** 先缩放，后贴回（提交 `c2fab63c` 的修复依赖这个顺序）。后台路径（约 1096 行）也要用它。

**这是 T86 的接入点。** 不要提前加定位或校验逻辑。

#### R10 拆 `BatchService`

1. 改为 `partial`，放进 `Studio/Generation/`：`BatchService.cs`（数据类型、接口）、`BatchService.Start.cs`（`StartSet` 及帮助方法）、`BatchService.Run.cs`（`RunJobAsync`、`GenerateAsync`、抠图）。
2. 把 `StartSet` 拆成按步骤的方法：读取商品、读取参考（238–310 行）、选择模板、展开任务、提交。错误文案和顺序不变。
3. 能脱离 SwarmUI 的校验移到 `SetRules`，补测试。
4. `20` 总上限写成 `SetRules.MaxImages` 常量（第 218、365 行和错误文案中）。**值不变。**

**这是 T85、T98 的接入点。**

#### R11 `PromptService` 拆成 partial

只移动。放进 `Studio/Prompting/`：

| 文件 | 内容 |
|---|---|
| `PromptService.cs` | 初始化、数据类型、设置 |
| `PromptService.Catalog.cs` | 风格、产品类型 |
| `PromptService.Compose.cs` | 组合、扩写、翻译 |
| `PromptService.Vision.cs` | 看图状态、模型、设备、`EcomCraftStudioReferenceDescribe` |
| `PromptService.Storyboard.cs` | 分镜 |
| `PromptService.Model.cs` | `AskModelRawAsync` 等底层调用 |

静态字段注意事项同 R07。

#### R12 统一图片 data URL 解析

新建 `Studio/Common/ImageDataUrl.cs`（可单测）：

```csharp
public static bool TryParse(string dataUrl, ImageDataFormats allowed, int maxBytes, out byte[] bytes, out string extension);
public static bool IsValid(string dataUrl, ImageDataFormats allowed, int maxBytes);
public static string ToDataUrl(byte[] bytes, string mime);
```

迁移 `CutoutService`、`CutoutOnly`、`ProductPreserve`、`ProductLibrary`、`BrandKitLibrary`、`TemplateAdminApi`、`TemplateApi`、`BatchService`、`StudioApi.ValidReference`、`PromptService`。

**先列表记录每处现有的允许格式和大小上限，迁移后逐处保持一致，不得统一放宽或收紧。** 列表写进完成报告。错误文案和错误码不变。

#### R13 标准工作流支持多蓝图

修改 `StandardWorkflows.cs`，行为不变：

1. `Kinds` 改成带字段的记录：`Kind`、`Id`、`Description`、`BlueprintFile`、`AddProductInpaint`（布尔）。现有两项都用 `text-to-image.json` 和 `true`。
2. `Blueprint()` 改为接收记录；同一文件可缓存一次转换结果。
3. `Ensure` 逐项调用。

**验收：** 已发布工作流的指纹不变（否则会触发重新发布）。写一个能在单测里运行的比较，或在报告中说明如何验证。**这是 T84 的接入点。**

#### R14 整理 `Studio/` 目录

只移动文件，**namespace 不变**，不改内容。

| 目标目录 | 文件 |
|---|---|
| `Studio/Workflows/` | `WorkflowReview`、`WorkflowReviewApi`、`WorkflowDiscovery`、`WorkflowCatalogFilter`、`NativeWorkflowConverter`、`NativeWorkflowDiscovery`、`NativeWorkflowParameters`、`OwnWorkflowApi`、`OwnWorkflowRules`、`StandardWorkflows`、`ExecutionServers` |
| `Studio/Ops/` | `BackupService`、`BackupRules`、`DiagnosticsService`、`DiagnosticsSanitizer`、`VisionInstallRules`、`VisionModelInstall` |

同步：`Tests/EcomCraft.Tests.csproj` 的链接、`simple-mode.test.cjs:166` 等读取 C# 文件的路径、技能文件、AGENTS.md 中的路径。用 `git mv`。前端文件留在原位。

### 阶段 D：前端

#### R15 `simple.js` 拆文件

**方法：** 用混入，调用方和 `this` 都不变。

在 `simple.js` 的 `SimpleMode` 中增加：

```js
/** Copies the static methods of a part class onto SimpleMode, so simple mode can live in several files while callers and this stay the same. */
static adopt(part) {
    for (let name of Object.getOwnPropertyNames(part)) {
        if (typeof part[name] == 'function' && !['length', 'name', 'prototype'].includes(name)) {
            this[name] = part[name];
        }
    }
}
```

每个文件结构：

```js
/** Simple mode: brand kits (F12). */
class SimpleBrandPart {
    static loadBrandKits() { ... }
}
SimpleMode.adopt(SimpleBrandPart);
```

**规则：** 静态字段全部留在 `simple.js`；已确认文件内没有 `super.` 和私有 `#` 成员。

文件放 `Studio/js/simple/`：

| 文件 | 方法（基线） |
|---|---|
| `simple.js`（原位） | 字段、`adopt`、`viewAccess`、`attach`、`setTask`、`renderTask`、`remember`、`enter`、`refresh`、`load`、`message`、`blocker`、`setBlocker`、`update`、平台和模板渲染（`preferredPlatforms`–`imageField`） |
| `rules.js` | `placementRect`–`cutoutIssues`（103–250）、`compressionScale`、`countValue`、`countProblem`、`qualityText` |
| `prompt.js` | `promptSource`–`renderPromptUsed`（543–677） |
| `export.js` | `loadExportOptions`–`slotName`（727–968） |
| `products.js` | `loadLibrary`–`toDataUrl`（969–1288，除上面已列）、`readFile`、`addPhoto`、`loadImage`、`runCutout`、`clearProduct`、`originText`、`renderProducts`、`selectProduct`、`renderQuality` |
| `placement.js` | `renderCutout`–`endDrag`（1590–1744） |
| `brand.js` | `loadBrandKits`–`deleteBrand`（1976–2234） |
| `storyboard.js` | `storyboardProblem`–`runStoryboard`（2235–2392） |
| `reference.js` | `addReferenceClick`–`generateReference`（2393–2941，含看图） |
| `batch.js` | `setTemplates`–`cancelBatch`（2987–3219） |
| `tasks.js` | `applyRecipe`–`download`（3220–结尾） |

步骤：

1. 每个文件一个提交，从 `brand.js`、`storyboard.js` 这类独立部分开始。
2. 每次同步 `StudioRoutes.Assets`、`index.html`（放在 `simple.js` 之后、`studio-core`/`studio.js` 之前）。
3. 每步跑前端测试和 eslint。

**验收：** 测试全过；`simple.js` 不超过约 1000 行；CP3 回归。

#### R16 拆 `SimpleMode.attach`

把 230 行的 `attach` 拆成 `bindUpload`、`bindReference`、`bindBrand`、`bindStoryboard`、`bindExport`、`bindPicker` 等，放到对应文件，由 `attach` 按原顺序调用。事件绑定的顺序不变。

#### R17 `studio.js`：公共层和独立视图

**方法：** 每个文件自带闭包（经典脚本的顶层 `let` 共享全局作用域，重复声明会报错），通过一个全局对象共享。

新建 `Studio/js/studio-core.js`：

```js
/** Shared helpers and views of the professional workspace; each script adds what it owns. */
let StudioCore = {};
(() => {
    'use strict';
    /* 从 studio.js 移入：$、el、svgIcon、tr、debounce、ApiError、state、request、存储读写、错误文本 */
    Object.assign(StudioCore, { $, el, svgIcon, tr, debounce, ApiError, state, request, ... });
})();
```

其他文件：

```js
(() => {
    'use strict';
    let { $, el, tr, request, state } = StudioCore;
    class Lightbox { ... }
    StudioCore.Lightbox = Lightbox;
})();
```

**规则：**
- 只解构 `studio-core.js` 提供、加载时已存在的成员。
- 其他文件的成员一律运行时用 `StudioCore.X` 访问，不要解构（否则取决于加载顺序）。
- **被多个文件读写的闭包级 `let` 变量，必须移入 `state`**。若 `state` 本身被整体赋值，先报告。
- `studio.js` 留在最后加载，负责注册尚未移走的函数（`Object.assign(StudioCore, { refreshCatalog, ... })`）和启动。

本任务移动（基线行号）：

| 文件（`Studio/js/`） | 内容 |
|---|---|
| `studio-core.js` | Helpers（5–315，不含下两项） |
| `time-estimate.js` | `TimeEstimate`、`TitleProgress` |
| `startup-overlay.js` | `StartupOverlay` |
| `dialogs.js` | `DialogFocus`（5206–5259） |
| `lightbox.js` | `Lightbox`（5260–5675） |

#### R18 `studio.js`：管理、画廊、队列

每行一个提交，按序：

| 文件（`Studio/js/`） | 内容（基线行号） |
|---|---|
| `admin-standard.js` | `StandardWorkflowView`（5018–5094） |
| `admin-menu.js` | `AdminMenu`（5095–5205） |
| `admin-review.js` | `WorkflowReviewView`（4259–4819） |
| `own-workflows.js` | `OwnWorkflows`（4820–5017） |
| `admin-storage.js` | `StorageSettingsView`（3270–3475） |
| `admin-setup.js` | `SetupWizard`、`TemplateRequirementsView`（3476–3763） |
| `gallery.js` | Gallery（2962–3269）、`GallerySelection`、`GalleryProjects`（3764–4258） |
| `queue.js` | `Reassign`、`ExecutionServerPicker`、Queue（2628–2961） |

#### R19 `studio.js`：创作页和事件绑定

| 文件（`Studio/js/`） | 内容（基线行号） |
|---|---|
| `prompt-settings.js` | Prompt enhancement、看图模型安装（1430–2413） |
| `create-catalog.js` | Catalog、`NativeWorkflowForm`、`WorkflowCatalogRefresh`（547–1044） |
| `create-form.js` | 类型和模式、参考图、蒙版、扩图、放大、LoRA、状态（1045–1429） |
| `create-generate.js` | Results、Generate（2414–2627） |
| `studio.js`（保留） | Theme、View routing、Init；`bind()` 按视图拆成各文件的 `bindXxx()`，由 `studio.js` 按原顺序调用 |

`SimpleMode.attach({...})` 的参数不变，来源改为 `StudioCore`。

**验收：** `studio.js` 不超过约 600 行；单文件不超过约 1000 行（`prompt-settings.js` 例外，写明原因）；eslint 0 错误。

#### R20 前端规范统一

只改 `Studio/js/` 下的 `studio*` 和视图文件，分别提交：

1. `const` → `let`。
2. 4 处 `forEach` → 标准 `for`（注意回调里的 `return` 要改成 `continue`）。
3. `} else` → 另起一行。
4. **`===`、`!==` 不批量改**，只在逻辑确实无需严格比较时才动，默认保留。
5. 单行无大括号的 `if` 补上大括号。

更新依赖旧文本的测试断言（如 `mobile.test.cjs:41` 这类正则）。

### 阶段 E：收尾

#### R21 核心改动清单

1. 新增 `docs/EcomCraft Upstream Changes.md` 和 `docs/EcomCraft 上游改动清单.zh-CN.md`：用 `git diff $(git merge-base dev origin/master) dev` 列出所有非扩展文件，每个文件写用途、是否是品牌替换、以后合并上游的风险。
2. 把 `src/Core/Installation.cs` 新增的约 400 行移到新文件 `src/Core/EcomCraftInstallation.cs`：`Installation` 改为 `partial class`，**只移动 EcomCraft 新增的方法**，上游原有代码不动。减少合并冲突，也便于 T95、T96 修改。
3. 其他核心改动**不移动**。

#### R22 仓库和文档清理

**先向用户确认再执行删除：**

1. 删除工作树和已合并分支：`git worktree remove .claude/worktrees/t56`、`git branch -d wt/t56`（`-d` 拒绝时停止）。
2. `.gitignore` 加 `.claude/`。

**不需确认：**

3. 重命名：`EcomCraft Prompt Enhancement.md` → `EcomCraft 提示词优化指南.zh-CN.md`，`EcomCraft Prompt Enhancement.en.md` → `EcomCraft Prompt Enhancement.md`。先后顺序要避免覆盖。更新 `README.md`、`README.en.md`、`docs/README.md`、`docs/README.zh-CN.md` 和两个文档内的链接。
4. `docs/EcomCraft 简易模式改版原型.html` 不动。

#### R23 更新 AGENTS、技能和总表

1. **AGENTS.md：**
   - 修正“仓库不包含自动化测试项目”，写明 C#、前端测试和 eslint 的命令。
   - 增加扩展结构说明：`Studio/Api`、`Common`、`Workflows`、`Ops`、`js/`。
   - 增加规则：
     - 数据文件读写必须用 `JsonFile`。
     - 图片 data URL 必须用 `ImageDataUrl`。
     - 服务端内部选项走 `GenerateOptions`，不放进客户端可发送的 `JObject`。
     - 新脚本同步 `StudioRoutes.Assets` 和 `index.html`。
     - `MagicPrompt/` 的 `{ get; set; }` 豁免（序列化模型）。
2. **技能：** 更新 `ecomcraft-studio-ui`、`ecomcraft-api-route`、`ecomcraft-build-run`、`dev-branch-commit` 里 `studio.js`、`simple.js`、检查命令的说明。
3. **`docs/EcomCraft 工作台界面设计.zh-CN.md`、架构文档第 6.5 节（代码组织）** 中英文同步。
4. **开发总表：** 新增阶段“基础重构”和 R00–R23；编号规则加上 R。用 openpyxl 编辑前先备份，保存后确认公式和下拉还在。

---

## 6. 检查点（用户在能跑 GPU 的电脑上执行）

| 检查点 | 时机 | 内容 | 不通过时 |
|---|---|---|---|
| CP0 | R03 后 | 全部回归清单，建立基线 | — |
| CP1 | R06 后 | 清单第 2、3、5、12 项；损坏文件测试 | 停止，修复 |
| CP2 | R14 后 | 第 3–12、14 项 | 用 `git bisect` 定位提交，修复后继续 |
| CP3 | R20 后 | 全部，加上桌面和手机浏览器 | 同上 |

任一检查点比基线变差，**不得进入下一阶段**。

---

## 7. 与《实施计划》的衔接

| 顺序 | 工作 |
|---|---|
| 1 | **`实施计划` T81：先把目标和两份计划写进仓库** |
| 2 | R00–R03、CP0。T82、N12（路线实测）可与后面的工作并行，都不改平台代码 |
| 3 | R04–R20（CP1–CP3） |
| 4 | **G1 通过后**开始 T83 及后续 |
| 5 | R21–R23 与上面并行，但 R23 要等 R14、R20 |

**G1 不通过时：** 阶段 A、B 仍要做；C、D 也可继续，因为这些问题与路线无关。但要问用户是否继续投入。

**新功能的接入点：**

| 新任务 | 接入点 | 来自 |
|---|---|---|
| T84 融合工作流 | `StandardWorkflows` 多蓝图 | R13 |
| T85 出图流程 | `BatchService.Start/Run`、`GenerateOptions` | R08、R10 |
| T86 商品校验 | `PostProcess/GenerationFinish` | R09 |
| T87 比例适配 | `OutputSizeRules`（无变化） | — |
| T89 单入口 | `simple/reference.js`、`storyboard.js`、`brand.js` 等可分别隐藏 | R15 |
| T103+ 精修台 | `create-form.js`、`create-generate.js` | R19 |
| T109 版本拆分 | `Workflows/`、`Ops/`、调度分离后更易抽接口 | R14 |

---

## 8. 风险

| 风险 | 应对 |
|---|---|
| 前端拆分后变量缺失，只在点击时才报错 | eslint `no-undef`；ID 检查；CP3 手工回归 |
| 跨文件共享的闭包变量被解构成旧值 | 第 R17 节规则：共享变量入 `state`，跨文件成员运行时访问 |
| 脚本加载顺序错误 | `frontend-structure` 测试；`boot-check` 兜底提示还在 |
| partial 文件的静态初始化顺序 | 互相依赖的字段放同一文件 |
| 测试只比对源码文本，难以发现行为错误 | 回归清单；重构不等于没有人工测试 |
| 拖太久影响新功能 | 第 7 节已安排实测并行；后端 C 和前端 D 可分别暂停，每个任务完成后都是可用状态 |
| 无法编译 | R00 是硬前提 |

---

## 9. 全部完成的标准

- 前端测试、C# 单测、eslint、CI（Windows、macOS）全部通过。
- 没有超过约 1000 行的 C# 或 JS 文件（JSON、`lang/en.js`、`MagicPrompt/`、写明原因的例外除外）。
- API 快照（接口名和权限）与 R03 建立时完全相同。
- 第 2.1 节的四个缺陷都有测试。
- CP3 不比 CP0 差。
- AGENTS.md、技能、架构文档和总表都已更新，中英文同步。
