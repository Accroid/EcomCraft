# EcomCraft 中调用 ComfyUI 工作流

> **语言：** 简体中文 · [English](EcomCraft%20ComfyUI%20Workflows.md)

EcomCraft 不会把任意 ComfyUI JSON 直接暴露给普通客户机。执行服务器上保存的 ComfyUI 工作流会被自动发现，经审核通过后才给用户调用（见下文“审核与发布”）。管理员也可以在 SwarmUI 的 ComfyUI 工作流编辑器中打开已有工作流，确认依赖和参数后，启用 **Enable in Simple**（在简单工作台中启用）并保存，这类工作流不经过 EcomCraft 审核。

## 网页端

1. 确认至少有一台执行服务器（本机 `ComfyUI Self-Starting` 后端，或已登记的局域网 ComfyUI，见下文“局域网执行服务器”）状态为 `RUNNING`。
2. 进入 EcomCraft Studio。页面可见且未生成时，每 30 秒自动更新工作流列表；也可点击“创作工作流”旁的“刷新”立即更新。执行服务器从不可用变为可用时也会刷新目录。
3. 在“创作工作流”下拉框中选择已发布的工作流（审核通过的原生工作流，或管理员在 SwarmUI 中启用简单工作台的工作流）。
4. 填写工作流显示的输入参数和标准提示词，点击生成。

工作流下拉框只显示同时满足以下条件的工作流：当前账号有使用权限、工作流已审核发布（或在 SwarmUI 中启用简单工作台）、工作流 JSON 和参数定义有效；被审核人隐藏的模型参数，当前账号也必须有权使用该模型。图片和视频工作流会按类型分别显示。

除管理员发布的工作流外，EcomCraft 会复用本机 ComfyUI 安装发现机制，扫描已配置后端及其他验证过的本机安装中的 `workflows`、`blueprints`、`user/default/workflows` 和 `user/default/blueprints`。已配置后端的 `--user-directory` 覆盖仍然有效。安装扫描结果缓存 60 秒，因此新增安装最多需要等到缓存过期后的下一次目录刷新；已发现安装中新保存的文件在下次刷新即可出现。无需逐个填写文件路径，`Settings.EcomCraft.ComfyWorkflowSearchPaths` 仅补充非标准目录，不会关闭自动发现。已登记的局域网执行服务器通过 ComfyUI HTTP 接口读取其 `user/default/workflows` 中保存的工作流。

工作流发现在后台进行，约每 30 秒刷新一次；目录和接口请求不会在页面请求中同步执行。服务启动后的第一次请求最多等待 5 秒获取首批结果。某个来源暂时读取失败时，保留该来源上一次的结果。

## 审核与发布

自动发现的原生工作流不会直接给用户调用，必须先审核：

1. 发现的工作流进入“待审核”。拥有 `ecomcraft_submit_workflows` 权限（默认普通用户）的账号可以在工作台“工作流”页面选择自己保存的工作流，填写说明后“提交审核”，并查看状态和驳回原因。
2. 拥有 `ecomcraft_review_workflows` 权限（默认管理员）的账号在“工作流审核”页面打开工作流，查看可运行服务器（例如“仅 A（B：缺少节点 xxx）”），逐个设置参数是否显示、显示名、默认值、取值范围和下拉可选项，然后“通过并发布”，或填写原因“驳回”。已发布的工作流可以“下架”。
3. 通过时保存快照，用户调用的是快照。之后在 ComfyUI 中修改来源文件，状态变为“待重新审核”，新版本通过前用户继续使用旧快照。来源服务器离线或文件被删除时，已发布的快照不受影响。
4. 无法通过结构校验的工作流显示“无法导入”及原因，不能提交或发布。

隐藏的参数不出现在用户表单中，固定使用快照里的默认值。创作描述、排除内容、宽度和高度始终显示。审核记录保存在服务器的 `Data/EcomCraft/workflow-reviews.json`。升级前自动发现即可使用的原生工作流，升级后需要审核通过才会重新出现在用户列表中。

支持旧版数组连接和新版对象连接，以及包含正负提示词编码节点的内嵌子图蓝图（例如 Qwen-Image 文生图蓝图及使用 `TextEncodeQwenImage21`、`UnetLoaderGGUF` 的 Qwen 2.1 工作流）。`TextEncodeQwenImage21` 的正负条件输出分别绑定 `prompt` 和 `negative_prompt`，不会把同一编码节点的两个文本框合并。`SaveImageAdvanced` 会改接工作台图片输出，避免复用原作者的输出路径。只有图片输出口而没有保存节点的蓝图会在服务端补充图片输出节点；子图实例分别展开，已连接的步数、CFG 等控制保持原连接。无效连接、循环、无法识别的提示词输入、暂不支持的旁路节点或管理员默认图片输入不会作为可执行工作流发布。自动发现不会把原始节点图或内部文件路径交给客户机。

选择自动发现的原生工作流后，页面会读取已保存的正负提示词、尺寸、步数、CFG、种子、采样器和调度器；模型加载器及降噪参数显示在工作流输入区。修改后按服务端绑定写回对应节点，多个节点保留各自的独立参数。已连接的已知整数/小数基础节点可编辑其值，连接本身保持不变。目录刷新保留未提交修改。正负提示词只在首次进入原生工作流且文本框为空时填入默认值；之后切换模型、工作流或返回标准工作台均保留当前提示词（包括主动清空的内容）。重新选择工作流会读取其他参数的默认值，返回标准工作台会恢复之前的其他标准参数。

页面连接状态中的“ComfyUI 执行服务器”是在线的执行服务器数（繁忙的服务器也计入，任务会排队等待），“标准工作台模型”是 SwarmUI 全局列表中当前账号可选的图片/视频模型数，不是云端模型数量，也不代表工作流依赖是否齐全。

原生工作流直接使用其模型文件名，不要求 SwarmUI 全局图片模型列表另有同名模型。模型下拉框只列出当前账号有权使用、且至少一台在线执行服务器上存在的模型。提交时调度器逐台检查节点和模型依赖，没有服务器能运行时返回 `no_execution_server` 并列出各服务器缺少的项目（见下文“执行服务器调度”）。标准工作台及手动发布的旧模板仍使用原有模型选择逻辑；旧模板没有当前类型的可选模型，或模型输入仅有 `(None)` 时，不进入可选列表。

种子读取保存的数值，支持到 JavaScript 安全整数上限 `9007199254740991`；填写 `-1` 可在提交时随机。一次生成多张图片时，以提交的种子为起点逐张加 1，并同步到实际采样节点；其他独立种子节点也按同一增量变化，超过上限后从 0 继续。填写 `-1` 时先随机确定起始值，再逐张递增。ComfyUI 画布的 randomize/increment/decrement 前端按钮行为不会直接执行。生成数量从保存的 batch_size 读取，仍限制 1–4 张，并交由 SwarmUI 分别调度，避免批量数量重复相乘。

生成接口正常返回 `images` 图片地址数组。固定起始种子并以相同参数再次提交时，可复现同一组图片，也可能命中 ComfyUI 缓存；需要新的一组图片时请更换种子或填写 `-1`。

本机后端位于 `ComfyUI-Installs/<安装名>/ComfyUI` 时，随 SwarmUI 加载的节点包会自动把同一根目录下的 `ComfyUI-Shared/models` 加入 ComfyUI 搜索路径。支持 checkpoints、VAE、LoRA、文本编码器、扩散模型等标准子目录，保留既有路径优先级，不复制模型或修改用户配置文件，也不会从共享目录加载自定义代码。仅使用关联安装的共享目录，不扫描其他盘符或切换后端。修改生效需要重启该本机后端；已禁用 SwarmUI 内置节点包的后端需要自行配置模型路径。

如果模型文件在共享目录中，但报 `ckpt_name 不可用`，请先检查后端是否已加载共享目录，不必重新下载同一模型。共享模型可供原生工作流使用，并不自动加入 SwarmUI 标准工作台的全局模型列表。

发现其他安装中的工作流仅导入模板，不会启动或切换到该安装。工作流在哪台服务器上被发现只表示来源；实际生成由调度器在具备相应节点、模型、文本编码器和 VAE 的执行服务器中分配（见下文“执行服务器调度”）。原生工作流只有在至少一台在线执行服务器上具备保存配置所需的全部节点和模型时才进入可选列表；不会把多台服务器的依赖拼在一起。服务器尚未就绪或缺少依赖时暂不显示，后续刷新会重新检查。此检查不代表模型组件搭配已经验证或完成 GPU 出图。提示词默认留空不会使工作流被过滤。

原生工作流的 ID 为 `来源/相对路径`，工作台下拉框按来源分组显示。来源是所属执行服务器的名称（后端 Title，未设置时为 `本机 #编号` 或 `服务器 #编号`）；不属于已登记服务器的本机安装使用安装文件夹名。ID 会按 SwarmUI 的工作流文件名规则清理：去掉 `.`、`#`、`&`、`%` 等字符和 `/` 两侧的空格，服务器名中的 `/` 换成 `-`。这样生成时 SwarmUI 再次清理名称也不会改变 ID，否则会找不到工作流。执行节点图、参数定义和默认值相同的原生项只显示一个，保留名称排序靠前的项；去重不会删除或改写原文件。名称冲突时附加由来源路径计算的 8 位短哈希，例如 `(1A2B3C4D)`。若刷新后仍未出现，请确认文件已保存到上述目录，当前选择的图片/视频类型正确，并检查工作流是否符合导入条件；仅打开编辑器画布或启动后端并不代表工作流已保存和通过校验。

工作台的“任务队列”页面按执行服务器显示运行任务、等待任务、当前节点和节点进度；等待任务包含平台已分配给该服务器、但尚未交给 ComfyUI 的任务。有任务在执行但未排队的服务器标记为“忙碌”（黄色指示灯），出现排队的服务器标记为“繁忙”（红色指示灯）。拥有生成权限的账号可以结束自己发起的活动任务；队列信息只在服务运行期间保留，不提供持久化历史。

## 执行服务器调度

每次提交时，平台按以下规则选择一台执行服务器，一次提交的多张图片都在同一台服务器上执行：

1. 只考虑在线、且具备该任务全部所需节点和模型的服务器。标准工作台模式由 SwarmUI 生成节点图，局域网服务器需要安装 SwarmUI 配套节点才能参与；已发布的原生工作流按其自身依赖判断。
2. 在这些服务器中选择排队数最少的一台；排队数相同时，选最近一次完成任务最早的一台。排队数 = 该服务器 ComfyUI 中运行和等待的任务数（包括其他人直接在该 ComfyUI 上提交的任务）+ 平台已分配给它、但还没有交给 ComfyUI 的任务数。后者是估算值，任务刚开始提交到 ComfyUI 的几秒内可能少算。
3. 有两台以上执行服务器时，创作区显示“执行服务器”下拉框，默认“自动分配（推荐）”。选择某台服务器后，任务只在这台服务器上排队，不会改派；这台服务器离线或缺少依赖时直接提示，不提交任务。所选服务器有排队而另一台可运行的服务器空闲时，提交后提示“建议取消限定”。
4. 没有可运行的服务器时不提交任务，并说明原因：所有服务器离线，或在线服务器分别缺少哪些节点、模型（每台最多列出 3 项）。
5. 提交成功后，状态栏显示实际执行的服务器名称；该名称也写入作品的元数据，作品画廊在每个作品的时间下方显示“工作流｜执行服务器”（标准工作台生成的作品显示“标准工作台”）。
6. 执行中的服务器掉线或任务失败时，平台不会自动重试。工作台显示失败原因和“改派到其他服务器”按钮，点击后用同一请求重新提交，并去掉服务器限定，由上述规则重新选择。请求只保存在页面内存中，刷新页面后只显示失败原因；结果不确定时不提供改派，请先在原生引擎核对，避免重复生成。

调度只在已登记的执行服务器之间进行，不会转到未登记地址、远程 SwarmUI 或云端服务。调度层按“执行方”抽象实现，预留了云端 ComfyUI 和云端模型 API 两种类型，本期不实现，也不会向它们发送任何请求。

## 电商场景模板

模板把一个已发布的工作流快照包装成运营能直接使用的电商场景，例如“白底主图”“场景图”。模板不包含自己的节点图，只引用已发布快照，并使用快照的参数白名单：

```json
{
  "id": "white-main",
  "name": { "zh-CN": "白底主图", "en": "White background main image" },
  "description": { "zh-CN": "纯白背景，适合平台主图", "en": "Pure white background for main images" },
  "example_image": "white-main.webp",
  "platforms": ["amazon", "taobao"],
  "sets": ["main-set"],
  "prompt": { "prefix": "product photo,", "suffix": "soft studio light", "negative": "text, watermark" },
  "categories": [],
  "brand_kit": null,
  "execution_plans": [
    { "kind": "local", "workflow": "B/白底主图", "presets": { "参数ID": 30 }, "user_fields": ["参数ID"] }
  ],
  "enabled": true
}
```

- `id`：小写字母、数字和连字符；`name` 必须同时有中文和英文，`description` 可选。
- `platforms`：适用平台 ID，任何平台都适用时填 `"*"`；`sets`：所属套图 ID，供一键套图使用。
- `prompt`：加在用户描述前后的提示词片段和排除内容；不能包含 `<…>` 指令或 `__通配符__`。
- `execution_plans`：本期每个模板必须有且只有一个本地方案（`kind` 为 `local`），`workflow` 是已发布快照的 ID。`presets` 固定快照参数的值，不能预设提示词或图片；`user_fields` 是允许用户修改的参数，必须是审核时未隐藏、且没有预设的参数。云端方案（`cloud_comfy`、`cloud_model_api`）是预留类型，本期会被拒绝。
- `preserve_product`（可选，默认 `false`）：商品保真。开启后，用户上传的商品图必须是去掉背景的透明 PNG 或 WebP；平台在本地把商品按位置放到输出尺寸的白色画布上，作为模板的商品图参数；生成完成后再按同一位置把原商品像素贴回每张结果（边缘向内羽化 2 像素），覆写已保存的作品并保留其中的元数据，因此商品的 Logo、文字和形状不会被生成改动。开启时模板必须把一个图片参数开放给用户。位置可由请求中的 `placement`（`x`、`y` 为商品中心占画面宽高的比例，0–1；`size` 为商品长边占画面短边的比例，0.1–1.5）指定，默认居中、0.7。工作流不需要输出商品蒙版；抠图结果只保存在内存中，最多两小时。
- `requirements`（可选）：模板所需的模型和节点。`models` 每项为 `{ name, folder, commercial, license_url, download_url }`：`name` 是模型文件名（可带子目录，不能含 `..` 或反斜杠），`folder` 是 ComfyUI `models` 下的目录（如 `checkpoints`、`loras`、`vae`），`commercial` 为商用许可状态 `allowed`（允许）、`forbidden`（禁止）或 `unknown`（未知，缺省值），两个链接只能是 http 或 https。`nodes` 是 ComfyUI 节点类名列表。模型由管理员在每台服务器上手动安装（假设 A2），平台不跨服务器下载。
- `cutout_only_platforms`（可选）：在这些平台上不调用生成模型，只把商品抠图居中放到纯白（RGB 255,255,255）背景上，商品长边约占画面短边的 85%，尺寸取该平台的导出规格（没有时 2000×2000），作为作品保存在画廊。内置白底主图对 `amazon` 开启。请求需带 `platform`。这样的作品标记为纯抠图，导出时不写 AI 生成标记。
- `categories`（品类预设）和 `brand_kit`（品牌资料包）是预留字段，`brand_kit` 本期须为 `null`。

内置 4 个模板：白底主图（`white-main`）、场景图（`scene`）、细节图（`detail`）、模特或手持图（`model`），适用所有平台，同属套图 `standard`；前三个开启商品保真，模特图因姿态和角度会变化而不开启。它们引用约定名称的工作流：`workflow` 写成 `*/ecomcraft/white-main` 这样的形式，表示任意执行服务器上路径为 `ecomcraft/white-main` 且已审核发布的工作流（有多个时取 ID 排序靠前的一个）；`user_fields` 中的 `@image` 表示该工作流第一个可见的图片参数，用来接收商品图。工作流由外部专家制作，按约定名称保存在执行服务器的 ComfyUI 中并通过审核后，模板自动可用；在此之前模板隐藏，管理接口显示“缺少工作流”。示例图放在同一目录，文件名为 `<模板 ID>.webp` 并写入 `example_image`。

内置模板随版本发布，放在扩展目录 `Templates/builtin/`（每个文件一个模板，示例图放在同一目录）；管理员自定义的模板保存在 `Data/EcomCraft/templates.json`，示例图在 `Data/EcomCraft/template-images/`，两者分开存放，升级不会覆盖自定义模板。自定义模板不能使用内置模板的 ID。

不合格的模板不会提供给用户，原因记录在服务器日志中，并通过管理接口返回。引用的快照被下架、参数被隐藏或预设值超出范围时，模板也会显示为不可用。管理接口需要审核权限（`ecomcraft_review_workflows`）：`EcomCraftTemplateAdminList` 列出所有模板及不可用原因，`EcomCraftTemplateSave` 新增或替换自定义模板（`template` 为模板 JSON，可选 `example_image_data` 为不超过 2 MB 的 PNG、JPEG 或 WebP 图片），`EcomCraftTemplateDelete` 删除自定义模板。`EcomCraftTemplateRequirements` 列出每个模板所需的模型和节点、每个模型的商用许可状态以及哪些执行服务器缺少它们（按服务器 ObjectInfo 中的模型文件和节点比对，服务器只给 ID 和名称）；工作流页面的“模板所需模型与商用许可”区域显示这些信息，“禁止商用”和“许可未知”会突出提示。

## 局域网执行服务器

局域网内只安装了 ComfyUI 的设备可以登记为执行服务器，由平台服务器统一调用。多人使用时的账号登录、防火墙和共享存储设置见[局域网部署指南](EcomCraft%20局域网部署指南.zh-CN.md)。开发计划和后续功能见 [局域网多服务器平台需求](EcomCraft%20局域网多服务器平台需求.zh-CN.md)。

1. 在局域网设备上启动 ComfyUI 时加上 `--listen 0.0.0.0`（可同时用 `--port` 指定端口），否则 ComfyUI 只接受本机访问。
2. 在该设备的防火墙中只允许平台服务器访问 ComfyUI 端口。ComfyUI 本身没有登录验证，不要把端口开放给整个局域网或公网。
3. 在平台服务器的原生引擎 **Server → Backends** 中添加 `ComfyUI API By URL` 后端，填写地址（如 `http://192.168.1.21:8188`），并在后端标题中填写显示名称（如“B”）。普通用户只看到显示名称，看不到地址。审核该服务器的工作流前先设好名称，之后不要修改：工作流 ID 以服务器名称开头。
4. 保存后等待后端状态变为 `RUNNING`。约 30 秒内，该设备 ComfyUI 中保存的工作流会出现在列表中。

只有以下地址会被视为执行服务器：回环地址、私有网段 IP（`10.x`、`172.16–31.x`、`192.168.x`、`169.254.x`、IPv6 唯一本地 / 链路本地地址），以及不含点的主机名或以 `.local`、`.lan`、`.home.arpa`、`.internal` 结尾的主机名。其他地址（包括公网域名）即使登记为后端，也不会被工作台使用。远程 SwarmUI 等其他后端类型同样不会被使用。

任务结果在执行完成后由平台服务器通过 ComfyUI 的 `/history` 和 `/view` 接口取回，保存到当前用户的输出目录并显示在作品画廊；参考图会先上传到执行服务器。这一回传路径尚未在真实局域网设备上完成验证。

## 客户机 API

先建立 SwarmUI 会话，然后请求 `POST /API/EcomCraftStudioGenerate`。请求只提交工作流 ID 和已发布参数，不要提交原始 ComfyUI JSON：

```json
{
  "session_id": "当前会话 ID",
  "source": "local",
  "kind": "image",
  "mode": "text2img",
  "workflow_id": "Examples/my_product_workflow",
  "workflow_params": {
    "product_style": "clean studio",
    "reference_image": "data:image/png;base64,..."
  },
  "model": "官方模型名称",
  "prompt": "一瓶护肤品，商业产品摄影",
  "negativeprompt": "模糊，低清晰度",
  "width": 1024,
  "height": 1024,
  "images": 1
}
```

可选字段包括 `seed`、`steps`、`cfgscale`、`sampler` 和 `scheduler`。视频工作流使用 `kind: "video"`，并按工作台要求提交 `text2videoframes`，每批只能生成一个视频。

`execution_server_id`（可选，整数）把任务限定在某台执行服务器上，取值为队列接口 `backends[].id`。省略时自动分配。服务端只接受已登记执行服务器的 ID；SwarmUI 原生的后端选择参数（如 `exactbackendid`）仍会被拒绝。传入 `async: true` 时提交后立即返回 `queued` 和 `task_id`。成功的响应都包含 `execution_server`（实际执行服务器的显示名称，不含地址），限定的服务器繁忙而另一台空闲时还包含 `server_hint`。调度相关的错误码：`invalid_execution_server`（ID 不属于已登记的执行服务器）、`execution_server_unavailable`（限定的服务器离线或缺少依赖，未改派）、`no_execution_server`（没有可运行的服务器，`error` 中说明原因）。超过账号角色的排队上限 `MaxQueued` 时返回 `queue_limit`，任务未提交（见[局域网部署指南](EcomCraft%20局域网部署指南.zh-CN.md#4-每人排队上限r92)）。

调用前可读取 `POST /API/EcomCraftStudioCatalog`，工作流列表位于返回值的 `workflows` 字段，其中 `inputs` 描述每个可填写参数的 ID、类型、默认值和允许选项。客户机应按这个列表构造 `workflow_params`，不要猜测参数名。目录还返回 `can_review_workflows` 和 `can_submit_workflows`，表示当前账号能否审核或提交工作流。审核接口为 `EcomCraftStudioReviewList`、`EcomCraftStudioReviewDetail` 和 `EcomCraftStudioReviewDecide`（`action` 为 `approve`、`reject` 或 `unpublish`），提交接口为 `EcomCraftStudioWorkflowSubmissions` 和 `EcomCraftStudioSubmitWorkflow`；它们只返回参数定义、状态和缺失的节点或模型名称，不返回原始节点图或服务器地址。审核错误码包括 `invalid_review`（配置无效）、`workflow_state`（当前状态不允许该操作或刚被他人修改）和 `review_save_failed`（记录无法保存）。可读取 `POST /API/EcomCraftStudioQueue` 获取各执行服务器的队列（`backends[]` 含 `id`、`name`、`status`、`running`、`pending`、`assigned`（平台已分配但尚未交给 ComfyUI 的任务数）、`queue`（调度使用的排队数）、`load`（`idle` 空闲、`working` 忙碌、`congested` 繁忙，队列无法读取时为 `null`）和 `busy`（等同于 `load` 为 `congested`），不含地址）和当前生成任务，以及 `finished_tasks`（当前账号最近 15 分钟内结束的后台任务：`id`、`state` 为 `done` 或 `failed`、`error`、`error_id`、`execution_server`，不含其他账号的任务）；结束当前账号的生成任务时调用 `POST /API/EcomCraftStudioCancelTask` 并传入队列返回的任务 `id`。

可读取 `POST /API/EcomCraftStudioTemplates`（可选 `language` 为 `zh-CN` 或 `en`）获取当前账号能运行的电商场景模板：`templates[]` 只含 `id`、`name`、`description`、`example_image`（模板示例图地址）、`kind`、`platforms`、`sets` 和 `fields`（允许修改的参数，格式同工作流的 `inputs`）；不能运行的模板列在 `hidden[]`，`reason` 为 `invalid`（模板配置无效）或 `unavailable`（没有在线服务器能运行，或账号无权使用其中的模型），审核人还会收到 `detail` 说明。按模板生成时，向 `EcomCraftStudioGenerate` 提交 `template_id` 和可选的 `template_params`（参数 ID 到值，只能是模板的 `fields`），以及 `prompt`、`negativeprompt`、`images` 等普通字段；服务端把模板解析成已发布快照、预设参数和提示词片段，再按快照白名单校验，客户端提交的 `workflow_id` 和 `workflow_params` 会被忽略。模板不可用时返回 `invalid_template` 或 `no_execution_server`，作品元数据记录 `ecomcraft_template`。

提示词服务：`EcomCraftStudioStyles`（可选 `language`）返回可选的视觉风格（`id`、`name`、`description`）；`EcomCraftStudioComposePrompt`（`style_id`、`description`，可选 `product_image`）把风格和一句话描述（中文或英文）通过已配置的提示词优化模型变成英文提示词，返回 `prompt`、`source`（`model` 或 `template`），模型不可用或 45 秒内没有回应时退回风格默认描述并返回 `notice` 和 `notice_code`（`description_used` 表示直接用了英文描述，`description_unused` 表示中文描述这次没有用上）。按模板生成时也可以不填 `prompt`，改为提交 `description` 和 `style_id`，由服务端组成提示词，响应中带 `prompt_notice` / `prompt_notice_code`。模型的回答只会作为提示词文字：服务端去掉原生指令、通配符、引号和多余段落，不能影响工作流、参数或执行服务器。把商品图发给视觉模型描述商品的功能默认关闭，只有拥有服务器设置权限的管理员能通过 `EcomCraftPromptSettings`（`vision_enabled` 为 `true` / `false`）开启，接口同时返回图片会发送到的服务商（`vision_provider`）和模型。

抠图：`EcomCraftStudioCutout`（`product_image` 为不超过 5 MB 的 PNG、JPEG 或 WebP 数据地址）在具备 SwarmUI 配套节点（`SwarmLoadImageB64`、`SwarmRemBg`、`MaskToImage`、`PreviewImage`）的已登记执行服务器上去掉背景，服务器按规则 1 选择；节点图由服务端固定生成，客户端不能提交。服务端只取回蒙版，再与用户上传的原图在本地合成透明背景的 PNG（`cutout`），商品像素就是原图本身。没有服务器具备这些节点时返回 `no_execution_server`，失败返回 `cutout_failed`；也可以直接上传透明背景的 PNG。简易模式在开启商品保真的模板下显示抠图确认：可自动抠图、拖动商品位置、调整大小，提交时带上 `placement`。

一键套图与批量：`EcomCraftStudioGenerateSet`（`products` 为 1–20 张商品图，每项 `{ image, placement? }`；`platform`；`set_id` 默认 `standard`；`style_id`、`description`；`candidates` 为每种图片的候选张数 1–4；可选 `execution_server_id`、`language`）把每张商品图 × 套图中适合该平台的每种图片类型展开成独立任务，最多 80 个。提示词整批只生成一次；保真模板需要的抠图由服务端自动完成（已是透明背景的图直接使用）。每个任务走普通的按模板生成，因此同样经过快照白名单校验，并各自按规则 1 调度到服务器、可多台并行；每人同时执行的任务数不超过角色 `MaxT2ISimultaneous` 与 4 中的较小值（需求 R9.4），其余在平台内排队。返回 `batch_id` 和 `jobs[]`（`id`、`product`、`template`、`template_name`、`state` 为 `queued` / `running` / `done` / `failed` / `cancelled`、`error`、`error_id`、`execution_server`、`images`）。`EcomCraftStudioBatchStatus` 读取进度，`EcomCraftStudioBatchRetry`（`batch_id`、`job_id`）重试失败或已取消的任务，`EcomCraftStudioBatchCancel` 取消还在排队的任务（正在执行的会完成）。批次只保存在内存中 6 小时，只有发起人能查看。

商品库：`EcomCraftStudioProducts` 列出当前账号保存的商品（`id`、`name`、`created`、`has_cutout`、`placement`、`image` / `cutout_image` 图片地址，不含文件位置）；`EcomCraftStudioProductSave`（`image`、可选 `name`、`cutout`）保存商品；`EcomCraftStudioProductUpdate`（`product_id`，可选 `name`、`cutout`、`placement`）重命名、保存确认过的抠图和位置；`EcomCraftStudioProductDelete` 删除商品及其文件。商品文件放在作品库位置的 `products/<账号>/<商品ID>/` 下，未设置作品库时放在平台服务器的 `Data/EcomCraft/product-files/`，不进入画廊；索引在 `Data/EcomCraft/products.json`。平台内按账号隔离，图片地址 `/EcomCraft/product-image/<ID>/original|cutout` 也只对本人开放。按模板生成和一键套图可以用 `product_id` 代替图片：服务端读取商品原图或已确认的抠图和位置，客户端不提交文件或路径。简易模式上传的商品自动入库，抠图和位置确认后同步更新，下次可从“从商品库选择”直接使用。

平台导出：平台预设在扩展目录的 `Templates/platforms.json`（管理员可在 `Data/EcomCraft/platforms.json` 放一份同格式的文件覆盖），包含国内外平台的中英文名称和导出规格（`width`、`height`、`background`、`format` 为 jpg 或 png、`quality`、`source` 来源说明、`verified` 是否已核对）。内置数值取自各平台公开的常见要求，均标为未核对，使用前须对照平台最新规范（N06）并改为 `verified: true`。`EcomCraftStudioExportOptions`（可选 `language`）列出有导出规格的平台和是否可以导出到共享文件夹；`EcomCraftStudioExport`（`platform`、`target` 为 `zip` 或 `folder`、`groups` 为 `[{ name, images }]`，`images` 是本人作品的画廊路径或生成结果地址，最多 200 张）把作品等比缩放到规格内、居中并用背景色补边（透明处也显示背景色），每组一个文件夹：`zip` 返回 `download` 下载地址（只有本人能下载，保留 1 小时），`folder` 写入管理员设置的导出位置 `<账号>/<时间-平台>/<商品>/`。错误码为 `invalid_export` 和 `export_failed`。导出时按作品来源写入 AI 标识元数据（XMP `Iptc4xmpExt:DigitalSourceType`、`xmp:CreatorTool`、`dc:description`，EXIF `Software`、`ImageDescription`）：普通生成图为 `trainedAlgorithmicMedia`，保真合成图（原商品像素 + 生成背景）为 `compositeWithTrainedAlgorithmicMedia`，纯抠图主图不写。规格带 `require_white_background`（亚马逊）时检查画面边缘一圈是否纯白，不是纯白的文件列在 `not_white` 中并在界面提示。

候选图保留与清理：简易模式生成的候选图会登记到平台；未被选中、下载或导出的候选图超过管理员设置的保留天数（默认 14 天，填 0 关闭）后由后台删除，删除前校验路径在作品目录内且为图片或视频。客户端在用户选中或下载候选时调用 `EcomCraftStudioMarkKept`（`images` 为本人作品的画廊路径或生成结果地址，最多 500 个）把作品移出清理登记；平台导出在服务端自动标记。专业模式的生成结果不登记、不会被自动清理。

上传质量阈值：`EcomCraftStudioPlatforms` 返回 `quality`（`min_side`、`blur_variance`、`min_cutout_ratio`），即 `platforms.json` 中 `upload_quality` 的阈值；客户端据此提示分辨率过低、模糊或商品占比过小，用户可以继续。

运维接口（管理员）：`EcomCraftDiagnostics` 生成诊断包 zip（最近日志、执行服务器状态、版本和脱敏后的配置；密钥、密码、令牌、提示词、图片和会话信息自动去除），返回 `download` 地址（`/EcomCraft/diagnostics/<id>`，仅创建者可下载，保留 1 小时）。`EcomCraftBackupCreate` 把 `Data/EcomCraft` 与账号数据库打包为带 SHA-256 清单的 zip（`/EcomCraft/backup/<id>`，保留 1 天）；`EcomCraftBackupRestore`（`data` 为 base64 的备份内容，必须 `confirm: true`）逐文件校验后恢复，被占用的账号数据库文件放入暂存目录供停机后替换，响应返回 `restored`、`staged` 和 `restart_required`。错误码为 `backup_failed`、`backup_invalid` 和 `backup_confirm_required`。

磁盘空间：后台定期检查平台服务器输出目录和作品库的剩余空间（阈值在存储设置中，警告默认 10 GB、严重不足默认 2 GB，填 0 关闭）。严重不足时 `EcomCraftStudioGenerate` 和 `EcomCraftStudioGenerateSet` 直接拒绝并返回 `disk_full`；`EcomCraftStudioQueue` 对管理员额外返回 `disk`（`level`、`output_free_gb`、`library_free_gb`）。

即时完成的任务：完全命中 ComfyUI 缓存的生成可能在提交请求返回前就结束，此时响应不带 `task_id`，而是直接携带 `images`（与纯抠图分支相同）；作品仍会保存到画廊。客户端应同时处理两种返回形式。

原生导入项额外返回 `native: true` 和 `defaults`。`inputs[].form` 标记绑定到标准页面字段的参数（如 `width`、`seed`）；其余输入通过 `workflow_params` 提交。省略标准字段时使用相应已发布参数或保存的默认值，同时提供时以标准字段为准。原生项无需提交根级 `model`，模型修改通过 `inputs` 中模型下拉框的 ID 提交，服务端仅接受公布的选项；参数 ID 是不透明标识，客户端不要自行构造。

## 安全边界

- 工作台只使用已登记的执行服务器（本机自启动 ComfyUI 和局域网地址的 `ComfyUI API By URL` 后端），不会因为执行服务器不可用而切换到未登记地址、远程 SwarmUI 或云端服务。
- 服务端会重新检查工作流是否已发布、参数是否在白名单中、模型和图片是否属于当前账号可用范围。
- 发布工作流中的默认图片不会自动复用；每个客户机必须重新上传自己的图片参数。
- 需要编辑原始节点图、使用未发布工作流或调试节点时，请使用原生 SwarmUI 的 ComfyUI 工作流页面，并授予相应的管理员权限。

## 更换本机后端后启动失败

若日志出现 `huggingface-hub>=1.5.0,<2.0 ... found huggingface-hub==2.3.0`，这是 Python 包版本冲突。启动器更新 diffusers 时会将已安装的 transformers、tokenizers 版本一并交给 pip 解析，避免依赖升级越过它们的版本上限。对已有冲突，应在该后端使用的虚拟环境中根据已安装包的共同依赖范围修复 huggingface-hub；不要直接升级全部环境或改动 PyTorch/ROCm。修复后先验证 `from transformers import CLIPTokenizer`，再重启后端。

## Windows AMD 连续生成时编码器报错

若首张图片完成，下一条任务在 `TextEncodeQwenImage21` 报 `hipErrorInvalidValue`，且后台调用栈位于量化权重的 `cast_to` / `copy_`，应检查 Windows ROCm 的固定内存（pinned memory）兼容性，而不是仅检查模型文件是否存在。针对 Qwen 量化编码器的两轮加载验证中，仅禁用异步卸载仍失败，禁用 pinned memory 后正向与空负向编码均通过。本项目在 Windows AMD 本机后端的自动启动参数中追加 `--disable-pinned-memory`，避免这一固定内存拷贝路径；异步卸载仍保留，传输性能可能随模型和设备变化。启用 `DisableInternalArgs` 的管理员需自行设置该参数。修改生效需要在队列空闲后重启后端；任务失败不会自动重试，前一条成功结果应从个人历史查看。
