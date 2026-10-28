# EcomCraft Studio

> **语言：** 简体中文 · [English](README.en.md)

EcomCraft Studio 是基于 [SwarmUI](https://github.com/mcmonkeyprojects/SwarmUI) 的电商创作工作台。本仓库包含 SwarmUI 服务器源码及 EcomCraft 扩展，**无需另行安装上游 SwarmUI**。项目仍在开发中，尚未完成生产环境验收。如果你在使用过程中出现bug，请通过Issues提交给我。

## 效果对比

<!-- 效果对比图位置：左为上传的商品原图，右为平台生成的成品图。图片稍后提供，先保留占位。 -->

| 上传原图 | 生成成品 |
|---|---|
| （图片待补） | （图片待补） |

## 演示

- 演示视频：（稍后提供）
- 常见平台出图流程见上文“快速开始”与[中文文档总览](docs/README.zh-CN.md)。

## 快速开始

### 安装要求

- [Git](https://git-scm.com/downloads) 和 [.NET SDK](https://dotnet.microsoft.com/download)：建议安装 .NET 10 SDK；项目以 .NET 8 为目标框架。运行 `git --version` 和 `dotnet --list-sdks` 可检查安装情况。
- 首次构建需要访问 NuGet；安装 ComfyUI 和下载模型也需要网络连接。
- 若在 **Linux** 上通过首次设置安装本机 ComfyUI，需预先安装 Python（建议 3.10–3.12）及其 `venv` 模块。**Windows** 安装向导使用带 Python 的 ComfyUI 便携包，无需先单独安装 Python。复用已有 ComfyUI 时，需提供可运行的环境与启动脚本。
- 生成图像或视频需要所选模型、相应的 ComfyUI 节点及兼容的计算设备；所需显存取决于模型。仅启动网页服务无需 GPU，也无需 Node.js。

### 获取源码并启动

```bash
git clone https://github.com/Accroid/EcomCraft.git
cd EcomCraft
```

在仓库根目录运行对应命令：

| 系统 | 常规启动 | 开发启动（强制重建） |
|---|---|---|
| Windows（PowerShell） | `.\launch-windows.bat` | `.\launch-windows-dev.ps1` |
| Linux | `./launch-linux.sh` | `./launch-linux-dev.sh` |
| macOS | `./launch-macos.sh` | — |

首次启动会构建服务器并还原 .NET 依赖，可能需要几分钟。开发启动脚本会重新生成构建文件，日常运行使用常规启动脚本。macOS 提供服务器启动脚本，但仓库的 ComfyUI 自动安装流程只覆盖 Windows 及 Linux 的 NVIDIA/AMD 路径；在 macOS 上使用本机生成功能需自行准备兼容的 ComfyUI 环境并验证。

### 首次配置

1. 在本机浏览器打开 `http://127.0.0.1:7801`。如果已修改服务器端口或监听地址，以实际配置为准。
2. 按首次设置页面选择使用范围、安装 **ComfyUI Self-Starting** 后端并下载模型，或跳过后端和模型安装后再配置。默认的“仅自己使用”模式会将本机访问者视为管理员；多人账号需要安装后另行配置。
3. 确认本机 ComfyUI 后端运行后，打开 `http://127.0.0.1:7801/EcomCraft` 进入工作台。工作台需要相应的生成页面和模型访问权限。

安装后若未识别已有 ComfyUI，可在原生引擎的 **Server → Backends** 页面顶部点击 **Scan ComfyUI**；找到多个安装位置时选择要使用的 `main.py`，再选择现有或新建的 **ComfyUI Self-Starting** 后端，最后点击后端卡片上的 Save。扫描只查找路径，不会自动修改后端。

工作台的生成请求只使用已登记的 ComfyUI 执行服务器：本机自启动 ComfyUI，以及在原生引擎 **Server → Backends** 中以 **ComfyUI API By URL** 登记的局域网 ComfyUI（局域网设备需以 `--listen` 启动 ComfyUI，详见[工作流调用文档](docs/EcomCraft%20ComfyUI%20工作流.zh-CN.md#局域网执行服务器)；多台服务器之间的分配规则见同一文档的“执行服务器调度”）。执行服务器不可用时不会改用未登记地址、远程 SwarmUI 或云端服务。配置提示词优化可阅读[提示词优化指南](docs/EcomCraft%20Prompt%20Enhancement.md)。若构建时出现 NuGet 连接错误，检查网络、代理和 `https://api.nuget.org/v3/index.json` 的访问情况。

进入工作台后可以切换到“任务队列”，按执行服务器查看运行任务、排队数（超过 2 条显示“繁忙”）、等待位置和节点进度。拥有生成权限的账号可以结束自己发起的活动任务；队列状态是运行时信息，服务重启后不会保留历史记录。

## 功能状态

当前已实现：

- 三步式简易模式（默认界面）：页面顶部先选要做的事，“照参考图做”（默认）、“做一张图”或“出一整套”，第 2 步只显示该任务需要的选项，视觉风格、模特、品牌资料包、描述与提示词收在“更多选项”里。上传商品图（本机上传、拍照上传、从商品库选择、从画廊选择已生成的作品四个并列入口，画廊图超过 5 MB 时在浏览器里先压缩，缩略图标注来源） → 选平台、图片类型（模板）、产品类型（两级：大类 → 子类）、可选视觉风格和是否需要模特，可写一句描述 → 挑选候选图并下载。描述旁的“AI 扩写”把一句话（不填则按产品类型）写成中英文双语提示词，填入可选的提示词面板，两种语言都可编辑：改中文时提交后由服务端翻译成英文发送，改英文时直接发送，面板为空时由服务端根据产品类型、风格和描述生成提示词；发送给引擎的提示词始终是英文，结果区可展开查看本次实际使用的提示词；候选张数为可输入的 1–20 正整数，单次出图与一键出整套共用 20 张总上限；采样器、LoRA、步数等专业参数只在“专业模式”中出现。平台列表接口为 `EcomCraftStudioPlatforms`，产品类型接口为 `EcomCraftStudioProductTypes`。
- 图片与视频创作工作台（专业模式，所有账号可用，面向设计人员），复用 SwarmUI 的生成能力；可用模式取决于已安装的模型、节点和账号权限。界面支持中文和英文，页头按钮切换，不刷新页面，选择记在浏览器中；首次打开按浏览器语言选择。
- 管理员发布 ComfyUI 工作流供工作台选用；服务端校验工作流 ID 和已发布参数白名单，提供 `EcomCraftStudioCatalog`、`EcomCraftStudioGenerate`、`EcomCraftStudioQueue` 和 `EcomCraftStudioCancelTask` API。
- 工作流审核：自动发现的原生工作流须经审核才能给用户调用。用户可提交自己保存的工作流并查看驳回原因；审核人配置用户可见参数（显示名、默认值、范围、可选项）、查看可运行服务器后发布、驳回或下架。通过时冻结快照，来源文件修改后需重新审核，通过前用户继续使用旧快照。权限为 `ecomcraft_submit_workflows` 和 `ecomcraft_review_workflows`，接口为 `EcomCraftStudioReviewList`、`EcomCraftStudioReviewDetail`、`EcomCraftStudioReviewDecide`、`EcomCraftStudioWorkflowSubmissions` 和 `EcomCraftStudioSubmitWorkflow`。原生工作流批量出图时逐张递增实际采样种子，填写 `-1` 可随机确定本批起始值。
- 自动发现本机 ComfyUI 安装中的 `workflows` 和 `blueprints`，以及局域网执行服务器 ComfyUI 中保存的工作流，支持内嵌子图蓝图；发现在后台进行，约每 30 秒更新。列表按“来源服务器 / 工作流”命名。无需逐个填写路径；`EcomCraft.ComfyWorkflowSearchPaths` 仅用于补充目录。发现的工作流仍须通过结构及正负提示词校验和人工审核，并由具备所需模型和节点的执行服务器执行。可选列表按执行内容合并重复原生工作流，并隐藏所有在线执行服务器都缺少默认模型或节点的项。页面读取工作流内保存的模型和参数，修改值传入对应节点，提交前再次检查依赖。Windows AMD 本机后端默认禁用固定内存（pinned memory），改善量化编码器重新加载时的 ROCm 兼容性。Comfy Desktop 安装的后端会自动追加同一根目录下的 `ComfyUI-Shared/models` 共享模型路径。
- 执行服务器调度：每次提交按“规则 1”在具备所需节点和模型的在线服务器中选择排队最少的一台（排队数包括其他人直接提交到 ComfyUI 的任务），整批图片在同一台执行；用户可在创作区限定只在某台服务器执行，所选服务器繁忙而其他服务器空闲时给出提示；没有可运行的服务器时说明原因且不提交。结果标注实际执行服务器；任务失败时不自动重试，可一键“改派到其他服务器”。作品画廊显示每个作品的工作流和执行服务器。
- 电商场景模板：模板引用已发布的工作流快照，加上中英文名称、示例图、适用平台、所属套图和提示词片段；模板可声明槽位类型（`kind`，如白底主图、场景主图、细节图、模特图），一键套图按平台的图片槽位组合模板（F58）；内置模板与管理员自定义模板分开存放，不合格的模板不提供给用户并说明原因。管理接口为 `EcomCraftTemplateAdminList`、`EcomCraftTemplateSave` 和 `EcomCraftTemplateDelete`；用户通过 `EcomCraftStudioTemplates` 读取可用模板（按语言返回名称），向 `EcomCraftStudioGenerate` 提交 `template_id` 按模板生成，仍经快照白名单校验。
- 模型清单与商用许可：模板声明所需模型和节点及每个模型的商用许可（允许 / 禁止 / 未知），工作流页面按服务器列出缺失项，许可有问题时突出提示。
- 合规：亚马逊白底主图走纯抠图分支，只把抠图居中放到纯白背景上，不调用生成模型；导出时检查主图背景是否纯白，并为 AI 生成或合成的图片写入 IPTC 数字来源类型等元数据。
- 平台导出（槽位化，F58）：每个平台由多个图片槽位组成（白底主图、场景主图、可选的 3:4 无线图等），各有尺寸、格式、单张大小上限、主体占比和禁用内容规则；导出时选择平台和槽位，按槽位规格等比缩放补边，超上限自动压缩，白底槽位检查纯白并提示商品占比，需声明 AI 生成的平台给出提示。打包下载 zip 或导出到共享文件夹，每个商品一个文件夹；预设覆盖国内外平台、可在配置中修改，内置数值标为待核对（N06）。接口为 `EcomCraftStudioExportOptions` 和 `EcomCraftStudioExport`。
- 卖点图：在一张候选图上按固定版式（底部横幅、左侧文字栏、标题加角标）叠加用户填写的标题（≤ 20 字）和最多 3 条卖点（每条 ≤ 16 字），文字由服务端用随附的思源黑体（Noto Sans SC，SIL OFL 1.1，可商用）绘制，不由 AI 生成；强调色默认取所选品牌资料包的第一个品牌色；结果另存为新作品，原图不变，并保留原图的 AI 元数据。不支持 emoji。接口为 `EcomCraftStudioSellingImage`。
- 商品库（简版）：上传的商品自动入库，保存原图、抠图和确认过的位置，下次出图直接选择；按账号隔离，可重命名和删除。接口为 `EcomCraftStudioProducts`、`EcomCraftStudioProductSave`、`EcomCraftStudioProductUpdate` 和 `EcomCraftStudioProductDelete`。
- 项目归档（服务端）：每个账号可建最多 200 个项目（一个客户或一批商品），把画廊作品归入项目；归档时服务端从作品自带的元数据读出配方（模板、种子、实际使用的提示词主体、执行服务器），供“用相同参数重做”；只给本人看、可随时删除，不保存图片、工作流或服务器地址，作品本身留在画廊。接口为 `EcomCraftStudioProjects`、`EcomCraftStudioProjectSave`、`EcomCraftStudioProjectDelete`、`EcomCraftStudioProjectAddWorks`、`EcomCraftStudioProjectRemoveWorks` 和 `EcomCraftStudioProjectWorks`；工作台界面在后续版本（T10）加入。
- 品牌资料包：每个账号可保存最多 20 个资料包（名称、最多 5 个品牌色、偏好背景描述、Logo、最多 4 张风格参考图），只有自己能看到和使用。出图时选中资料包，品牌色（换算成英文颜色词）和偏好背景（中文在保存时翻译一次）加入提示词；Logo 和第一张参考图只传给模板 `brand_kit` 槽位声明的图片参数，不由 AI 生成 Logo。客户端只提交资料包 ID。接口为 `EcomCraftStudioBrandKits`、`EcomCraftStudioBrandKitSave` 和 `EcomCraftStudioBrandKitDelete`。
- 一键套图与批量：多张商品图 × 平台套图展开成独立任务，分配到多台执行服务器并行；平台定义图片槽位时按槽位类型组合（可选槽位勾选后追加），每人同时执行的任务数受限，失败项可单独重试，可取消剩余任务。
- 卖点分析与套图分镜：根据商品描述（或提示词面板）、可选卖点、产品类型和风格，由提示词服务列出最多 5 条卖点，并规划最多 8 张图的分镜（每张 = 当前平台可用的图片类型 + 画面描述 + 用途）；用户可增删改后“按分镜出整套”。服务端只接受已登记模板 ID 和清理后的文字，提交时逐张重新校验；模型不可用时按平台套图类型列出可编辑的分镜。分镜只保存在当前页面。接口为 `EcomCraftStudioStoryboard`；套图接口为 `EcomCraftStudioGenerateSet`、`EcomCraftStudioBatchStatus`、`EcomCraftStudioBatchRetry` 和 `EcomCraftStudioBatchCancel`。
- 参考图（“照参考图做”任务，F59）：上传最多 8 张参考图（如竞品成品图）；每张参考由视觉模型生成英文构图描述，页面逐张展示、英文可修改（以改稿为准），“按参考出图”为每张商品图 × 每张参考各出 1 张，固定使用保真场景模板，提示词按原文发送。看图描述默认开启（只描述参考图，商品图描述仍默认关闭），管理员可关闭；关闭、连不上视觉服务或没有视觉模型时，页面说明缺什么（管理员看到处理步骤，可直接运行一键安装脚本 `launchtools/install-vision-model-windows.ps1` 或 `launchtools/install-vision-model-linux.sh`；其他账号看到请联系管理员），普通出图不受影响；界面提供版权提示，海外平台加强提醒；参考图只在内存中短暂存在，不保存。接口为 `EcomCraftStudioReferenceDescribe` 与 `EcomCraftStudioVisionStatus`，`EcomCraftStudioGenerateSet` 携带 `reference_prompts`。
- 内置 4 个电商场景模板：白底主图、场景图、细节图、模特或手持图；按约定名称引用专家制作的工作流（如 `ecomcraft/white-main`），工作流安装并审核通过前显示“缺少工作流”。
- 商品保真：模板开启 `preserve_product` 后，平台在本地把透明背景的商品图按位置放到输入画布上，生成后再把原商品像素贴回成图，Logo、文字和形状不会被改动。简易模式中可一键抠图（在执行服务器上用 SwarmUI 的去背景节点，接口 `EcomCraftStudioCutout`），并拖动、缩放商品位置。
- 提示词服务：用户选产品类型、可选风格并写一句描述（中文或英文），由已配置的提示词优化模型生成英文提示词，勾选“需要模特”时加入固定的模特描述片段；模型只能填写提示词文字；模型不可用时退回模板和风格的默认描述并说明。把商品图发给视觉模型默认关闭，只有管理员能开启。“AI 扩写”同时生成中英文提示词，修改过的中文提示词在提交时翻译成英文，翻译失败时按产品类型和风格的默认描述出图并说明，不阻塞出图。需要模型写提示词或翻译时，提交立即返回、提示词在后台生成（上限仍为 45 秒），任务依次显示“正在生成提示词 → 排队中 / 执行中（含执行服务器名）→ 已完成”；已扩写未改或改的是英文时不调用模型，直接排队；一键出整套同样先返回再在后台组合提示词。接口为 `EcomCraftStudioStyles`、`EcomCraftStudioProductTypes`、`EcomCraftStudioComposePrompt`、`EcomCraftStudioExpandPrompt`、`EcomCraftStudioPrepareStatus` 和 `EcomCraftPromptSettings`。
- 作品存储：管理员可把作品库设到本机目录、NAS 或共享文件夹（保存时检测可写、空间和速度）。作品先保存在平台服务器，再在后台复制，失败自动重试，画廊显示“待同步”；改位置后可迁移旧作品。接口为 `EcomCraftStorageSettings`、`EcomCraftStorageSave`、`EcomCraftStorageMigrate` 和 `EcomCraftStoragePending`。
- 实时任务队列按执行服务器展示运行任务、排队任务（含平台已分配但尚未交给 ComfyUI 的任务）、空闲 / 忙碌 / 繁忙状态、当前节点和节点进度，并支持结束当前账号自己的活动生成任务。
- 生成结果可直接下载、作为初始图继续创作，或跳到画廊查看；大图预览支持上一张 / 下一张和方向键切换，图片和视频均可放大缩小（滚轮、双击、双指或按钮、`+` / `-` / `0` 键），放大后可拖动查看细节。
- 作品画廊支持收藏、搜索，以及单个或批量删除当前账号的作品。删除需要 `user_delete_image` 权限，删除前需确认；服务器启用回收站（`Paths.RecycleDeletedImages`）时文件移入回收站，否则永久删除。
- MagicPrompt 提示词优化，可配置本地或云端语言模型服务；输入 API Key 后可直接拉取模型，点击保存才会应用配置。提示词生成默认等待 120 秒，模型列表拉取限时 20 秒。
- 上传质量检查：上传商品图时检查分辨率、清晰度（拉普拉斯方差）和抠图后的商品占比，不达标时用白话提示原因和建议，用户可以继续；阈值在 `platforms.json` 的 `upload_quality` 中配置。
- 未选候选图自动清理：简易模式里没被选中、下载或导出的候选图超过保留天数（默认 14 天，管理员可改或填 0 关闭）后自动删除；删除前校验路径在作品目录内且为图片或视频。接口为 `EcomCraftStudioMarkKept`。
- 白话错误提示：全部接口错误带错误码，前端按错误码显示中英文白话提示和下一步建议，不向用户展示异常堆栈、地址、提示词或节点图。
- 管理员首次设置向导：语言 → 存储位置 → 登记服务器 → 检查模型 → 选常用平台，每步可跳过，之后可从“查看配置指引”重新打开；常用平台在简易模式中排在最前并默认选中。
- 手机支持：上传步骤可拍照上传（带 `capture` 属性的输入在 `http://` 局域网地址下也可调起相机），画廊在窄屏下改为单列，不使用需要 HTTPS 的浏览器功能。
- 完成通知：批量任务完成或失败时页面内提示；页面在后台时标签页标题显示“（已完成/总数）”进度，全部结束后恢复。
- 自用工作流（专业模式）：上传 ComfyUI 菜单“保存”得到的工作流文件（不超过 2 MB，每个账号最多 20 个），经服务端结构校验和禁用规则检查后上传者自己马上能用，其他账号看不到；含可执行代码的节点或像文件路径的参数会被拒绝，管理员可在服务器设置 `EcomCraft.DeniedWorkflowNodes` 追加禁用节点。自用工作流可以“提交审核”，管理员通过后复制为 `上传/<名称>` 发布给所有账号，驳回原因只给上传者。接口为 `EcomCraftStudioWorkflowUpload`、`EcomCraftStudioMyWorkflows`、`EcomCraftStudioWorkflowSubmitOwn`、`EcomCraftStudioWorkflowDelete`。
- 画廊可按来源（出图 / 专业模式）筛选，队列把自己的请求放在最前。
- 手机只做简易模式：手机等窄屏上可以出图、看画廊和队列，专业模式请在电脑上使用。
- 候选图收藏（初筛）：第 3 步每张候选图左上角可点收藏，与画廊收藏是同一件事，收藏后的作品出现在画廊“已收藏”中，并且不会被候选图自动清理删除。
- 简易模式到专业模式的单向衔接：第 3 步只选中一张候选图时出现“在专业模式里精修”，把这张图载入为专业模式的图生图初始图，并填入出图时实际发送的提示词（服务端从作品自带的元数据读取，只限本人作品，接口 `EcomCraftStudioWorkRecipe`）；专业模式的修改不回写简易模式，手机上不显示。
- 标准工作流自动生成：内置的场景图、细节图模板使用系统按默认模型（服务器设置 `EcomCraft.StandardWorkflowUnet` / `StandardWorkflowTextEncoder` / `StandardWorkflowVae`）从扩展自带蓝图生成的工作流，执行服务器具备所需节点和模型时自动发布，来源显示“系统生成”，可在审核中下架；管理员在“管理 → 标准工作流”查看状态并初始化。接口为 `EcomCraftStandardWorkflows`。
- 管理功能（工作流审核、首次设置向导、提示词优化设置、存储与备份、诊断包和原生引擎）集中在顶栏右侧只有管理员可见的“管理”菜单中。
- 一键诊断包（管理员）：打包最近的日志、执行服务器状态、版本信息和脱敏后的配置，密钥、密码、提示词、图片和会话信息自动去除。接口为 `EcomCraftDiagnostics`。
- 系统数据备份与恢复（管理员）：把 `Data/EcomCraft` 与账号数据库打包为带 SHA-256 清单的 zip，恢复前逐文件校验并要求确认；升级时数据如何保留见[升级与数据说明](docs/EcomCraft%20Upgrade.zh-CN.md)。接口为 `EcomCraftBackupCreate` 和 `EcomCraftBackupRestore`。
- 磁盘空间监控：后台定期检查平台服务器和作品库的剩余空间，低于警告阈值（默认 10 GB）时提醒管理员，严重不足（默认 2 GB）时拒绝新生成并说明原因；阈值可在存储设置中修改或关闭。
- Windows 首次安装时可创建 EcomCraft Studio 桌面快捷方式。

规划中：团队节点管理、持久化任务历史、公共素材库，以及按任务授权的云端生成。上述规划功能当前不可作为可用功能使用。

## 项目结构

| 位置 | 用途 |
|---|---|
| `src/` | SwarmUI 服务器与浏览器前端 |
| `src/Extensions/EcomCraft/` | EcomCraft 工作台、MagicPrompt 与工作流接口 |
| `Desktop/` | 桌面启动入口 |
| `docs/` | 使用与开发文档 |

## 文档

- [中文文档总览](docs/README.zh-CN.md) · [英文文档总览](docs/README.md)
- [基础使用](docs/Basic%20Usage.zh-CN.md) · [常见问题排查](docs/Troubleshooting.zh-CN.md)
- [EcomCraft ComfyUI 工作流调用](docs/EcomCraft%20ComfyUI%20工作流.zh-CN.md) · [局域网部署指南](docs/EcomCraft%20局域网部署指南.zh-CN.md)（多人登录、排队上限、防火墙、共享存储与 HTTPS 限制） · [升级与数据说明](docs/EcomCraft%20Upgrade.zh-CN.md)（数据保留、备份与恢复）
- [提示词优化与部署](docs/EcomCraft%20Prompt%20Enhancement.md)
- [EcomCraft 架构与路线图](docs/EcomCraft%20Architecture.zh-CN.md) · [局域网多服务器平台需求](docs/EcomCraft%20局域网多服务器平台需求.zh-CN.md) · [简易模式改版需求](docs/EcomCraft%20简易模式改版需求.zh-CN.md)（开发规划，含尚未实现的内容）
- [API 使用说明](docs/API.zh-CN.md) · [命令行参数](docs/Command%20Line%20Arguments.zh-CN.md)

## 数据与权限

选择云端提示词模型时，输入文本会发送给所配置的服务商；这与限定在已登记 ComfyUI 执行服务器的生成请求是两条不同路径。局域网执行服务器的 ComfyUI 没有登录验证，应通过防火墙只允许平台服务器访问其端口。安装向导的“仅自己使用，但允许局域网访问”模式仍将访问者视为管理员；用于多人访问前，必须另行配置账号认证与权限，并检查监听地址、后端访问和素材目录权限。默认的 `Data/`、`Models/`、`Output/` 目录保存本地设置、模型和生成数据；若修改了目录配置，也请保留实际使用的目录。

## 联系与部署服务

- 问题反馈与功能建议：[GitHub Issues](https://github.com/Accroid/EcomCraft/issues)。
- 部署服务：提供单机或多机局域网的远程部署支持（安装、登记执行服务器、共享存储配置与团队培训），联系方式：（稍后补充）。

## 来源与许可

本项目是 SwarmUI 的独立二次开发项目，**不是 SwarmUI 官方发行版**。感谢原作者 Alex “mcmonkey” Goodwin 及所有贡献者。项目沿用 [MIT License](LICENSE.txt)，并保留[上游版权与第三方组件声明](THIRD_PARTY_NOTICES.md)。模型、工作流和其他依赖须遵守各自许可。
