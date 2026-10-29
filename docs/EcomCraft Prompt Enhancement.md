# EcomCraft 提示词优化（MagicPrompt）使用与部署指南

> **语言：** 简体中文 · [English](EcomCraft%20Prompt%20Enhancement.en.md)

EcomCraft 创作工作台的「✦ 优化提示词」按钮，会把你输入的简短描述，交给大语言模型（LLM）改写成一段细节更丰富、更适合生图的提示词，再回填到描述框，可直接用于生成。

该功能**内置 MagicPrompt**（源自开源项目 [HartsyAI/SwarmUI-MagicPromptExtension](https://github.com/HartsyAI/SwarmUI-MagicPromptExtension)，MIT 协议，随 EcomCraft 安装即默认启用，无需另行安装扩展）。

它支持 **6 类后端**，你可以按是否有 GPU、是否愿意付费自由选择：

| 后端 | 类型 | 是否需要 API Key | 说明 |
|---|---|---|---|
| **Ollama** | 本地 | 否 | 最简单的本地免费方案，一条命令装好模型 |
| **OpenAI 兼容服务（本地 / 远程）** | 本地或远程 | 取决于服务 | LM Studio、LocalAI、vLLM、自建服务及远程兼容接口 |
| **OpenRouter** | 云端 | 是 | 一个 Key 接入数百种模型，**含免费模型** |
| **OpenAI** | 云端 | 是 | ChatGPT（GPT-4o / GPT-4.1 等） |
| **Anthropic** | 云端 | 是 | Claude（Sonnet / Opus 等） |
| **Grok** | 云端 | 是 | xAI 的 Grok 模型 |

---

## 方案一：Ollama 本地免费（最推荐，数据不出本机）

适合：有一台普通电脑，希望零成本、离线使用。

### 1. 安装 Ollama

到 [https://ollama.com/download](https://ollama.com/download) 下载并安装（支持 Windows / macOS / Linux）。安装后 Ollama 会在后台运行，默认地址 `http://localhost:11434`。

### 2. 拉取一个模型

打开终端（Windows 用 PowerShell / CMD），执行：

```bash
# 轻量、足够做提示词扩写（约 2GB，3B 参数）
ollama pull llama3.2
```

其他可选模型：

| 命令 | 模型 | 大小 | 适用 |
|---|---|---|---|
| `ollama pull llama3.2` | Llama 3.2（3B） | ~2 GB | 速度快，够用 |
| `ollama pull qwen2.5` | Qwen2.5（7B） | ~4.7 GB | 效果更好，需要更多内存/显存 |
| `ollama pull phi3` | Phi-3（3.8B） | ~2.3 GB | 轻量备选 |

> 没有独显也能用：Ollama 会自动用 CPU + 内存运行，3B 模型在普通电脑上一条提示词通常几秒到十几秒。

### 3. 在 EcomCraft 中配置

1. 打开 EcomCraft 创作工作台，点击「创作描述」下方的 **设置**（管理员）。
2. 选择：
   - **模型后端**：`Ollama（本地，免费）`
   - **服务地址**：`http://localhost:11434`（同机默认，无需改）
   - **提示词模型**：点「拉取」从列表选择，或手动填 `llama3.2:latest`
3. 点击 **保存**。

---

## 方案二：OpenRouter 云端（含免费模型，无需好硬件）

适合：本机配置较低、不想本地跑模型，又希望有免费额度。

1. 到 [https://openrouter.ai](https://openrouter.ai) 注册。
2. 进入 [https://openrouter.ai/keys](https://openrouter.ai/keys) 创建一个 API Key（`sk-or-...`）。
3. 在 EcomCraft 设置中：
   - **模型后端**：`OpenRouter（含免费模型）`
   - **API Key**：粘贴 Key
   - **提示词模型**：可填免费模型，例如：
     - `meta-llama/llama-3.3-70b-instruct:free`
     - `google/gemini-2.0-flash-exp:free`
     - `qwen/qwen-2.5-7b-instruct:free`
4. 点击 **保存**。

> 免费模型可能有速率限制；具体可用模型以 OpenRouter 页面标注 `:free` 的为准。

---

## 方案三：OpenAI / Claude / Grok 等付费云端

适合：已经在用这些服务、追求稳定和高质量。

1. 在对应平台创建 API Key：
   - OpenAI：[https://platform.openai.com/api-keys](https://platform.openai.com/api-keys)
   - Anthropic Claude：[https://console.anthropic.com/settings/keys](https://console.anthropic.com/settings/keys)
   - Grok（xAI）：[https://console.x.ai](https://console.x.ai)
2. EcomCraft 设置中选择对应后端，粘贴 API Key。
3. 模型名可在该平台文档中查到（如 OpenAI 的 `gpt-4o-mini`、Anthropic 的 `claude-3-5-sonnet-latest`），也可点「拉取」。

---

## 方案四：其他本地 OpenAI 兼容服务（LM Studio / vLLM 等）

「OpenAI 兼容服务（本地 / 远程）」后端可对接实现了 OpenAI `/v1/chat/completions` 与 `/v1/models` 接口的服务，包括以下本地程序：

- **LM Studio**：图形界面，加载模型后开启「Local Server」，默认地址 `http://localhost:1234/v1`。
- **vLLM**：`vllm serve <模型> --port 8000`，地址 `http://localhost:8000/v1`。
- **text-generation-webui / LocalAI / 自建 FastAPI**：同理。

配置时：
- **模型后端**：`OpenAI 兼容服务（本地 / 远程）`
- **服务地址**：填该服务的 Base URL（根地址或包含 `/v1` 均可，以实际为准）
- **API Key**：本地服务一般无需 Key，留空即可；若服务设置了 Key 则填写。
- **提示词模型**：点「拉取」选择。

---

## 方案五：自托管专用提示词模型 BeautifulPrompt（pai-bloom）

EcomCraft 还随附了一个脚本，可部署阿里 PAI 开源的**提示词专用模型** BeautifulPrompt（`pai-bloom-1b1-text2prompt-sd`），并把它包装成 OpenAI 兼容接口，然后用「OpenAI 兼容服务（本地 / 远程）」后端接入。

### 硬件要求

模型为 BLOOM 1.1B（约 11 亿参数），体量很小：

| 项目 | 最低 | 推荐 | 说明 |
|---|---|---|---|
| GPU 显存（F16） | ~3 GB | ≥ 4 GB | 权重 F16 约 2.1 GB |
| GPU 显存（8/4-bit 量化） | ~2 GB / ~1.5 GB | ≥ 3 GB / ≥ 2 GB | 需自行用量化方式加载 |
| 纯 CPU | 8 GB 内存 | 16 GB 内存 | FP32 权重约 4.2 GB |
| 磁盘 | 5 GB | 10 GB | 模型约 2.1 GB + Python/torch 依赖 |
| 网络 | 首次需联网 | — | 首次下载模型后可离线 |

### 部署步骤

```bash
# 1. 准备 Python 3.10–3.12 环境并安装依赖
pip install -r src/Extensions/EcomCraft/deploy/requirements.txt

# 2. 启动（自动选择 CUDA / Apple MPS / CPU）
python src/Extensions/EcomCraft/deploy/serve_prompt_enhancer.py --host 0.0.0.0 --port 8000

# 3. 自检
curl http://localhost:8000/v1/models
```

然后在 EcomCraft 设置中：
- **模型后端**：`OpenAI 兼容服务（本地 / 远程）`
- **服务地址**：`http://localhost:8000`（脚本根路径即兼容入口；按实际拼接到 `/v1`）
- **提示词模型**：`alibaba-pai/pai-bloom-1b1-text2prompt-sd`
- 无需 Key。

> 注：pai-bloom 是较早的通用 SD 提示词模型，输出偏艺术化；若追求电商产品摄影的干净描述，优先使用方案一（Ollama 拉取较新的模型）或云端模型。

---

## 配置字段说明

| 字段 | 说明 |
|---|---|
| 模型后端 | 选择上面 6 类后端之一 |
| 服务地址（Base URL） | Ollama / OpenAI 兼容后端可编辑，支持本地或远程地址；其他后端地址固定 |
| 提示词模型 | 点「拉取」自动列出该后端的可用模型，也可手动填写模型 ID |
| 视觉模型服务 | 看图描述用的服务，仅 Ollama / OpenAI 兼容两类。视觉服务的地址（`visionbaseurl`）、Key（`openaiapi_vision`）和模型与提示词服务分开保存，即使两边选同一类服务、用不同地址也互不覆盖 |
| 视觉模型（Ollama） | 选 Ollama 时自动检测：连不上时提示没有安装或没有启动，显示“一键安装本地模型”；已运行时只列出能看图的模型（按 Ollama 报告的 vision 能力，旧版 Ollama 按模型名判断），装了多个可以切换。地址默认本机 `http://localhost:11434`，Ollama 在局域网另一台电脑上时在“高级”里修改 |
| 视觉模型（OpenAI 兼容） | 填写视觉服务地址、可选的 API Key（本机 LM Studio、vLLM 一般不需要）后点「拉取」选择，或手动填写。不勾选“允许看图描述”时无需填写视觉参数即可保存 |
| API Key | 云后端必填；Ollama 无需；本地兼容服务一般无需。Key 安全存储在 SwarmUI 用户数据中，不会写入配置文件 |

切换后端时，服务地址和 Key 状态会自动更新为该后端的已保存值或默认值。

### 生成等待时间

拉取模型列表仍限时 20 秒。提示词优化需要等待完整回答，独立默认超时为 120 秒；已有配置中的旧默认 `timeout: 20` 自动按此生成时限处理，不改写用户配置。其他自定义旧时限继续生效。管理员可通过现有设置 API 的 `backends.<后端>.generation_timeout` 显式设置 1–600 秒的生成时限，该值优先于旧 `timeout`。配置无效时使用 120 秒。

请求取消、会话中断或服务关闭会取消上游等待；超时提示包含实际等待上限，不自动重试。拉取成功不代表模型能在时限内生成；第三方服务自身的网关超时也不受本地等待上限控制。

### 输入 Key 后直接拉取模型

拉取到的提示词模型直接作为原“提示词模型”输入框的下拉候选，不再额外显示第二个选择框；可输入模型名称筛选或手动填写。拉取不覆盖当前输入，选定后点击“保存”才应用。

1. 选择模型后端，填写服务地址（可编辑的后端）和 API Key。
2. 直接点击“拉取”，无需先保存。拉取使用当前表单中的后端、地址和新 Key；不会更改全局配置或保存 Key。Key 留空时，仅在请求地址与此后端已保存的地址一致时沿用已保存的 Key，修改地址后需重新填写所需的 Key。
3. 选择模型后点击“保存”，才会保存 Key 和提示词后端配置；不会改动独立的视觉模型配置。拉取成功仅表示模型列表可读取，实际优化仍取决于模型权限和服务支持。

关闭或取消设置会丢弃未保存的 Key；切换后端、修改地址或 Key 会取消进行中的拉取并清除旧列表。拉取失败或超时后可修改表单重试。OpenAI 兼容地址可填写服务根地址或带 `/v1` 的地址，系统不会重复拼接 `/v1`。

工作台通过 `PreviewMagicPromptModels` 临时拉取所选后端的文本模型，需要配置保存和模型列表权限。此接口不调用配置或 Key 保存逻辑，不返回上游错误正文或凭据。

---

## 看图描述（视觉模型，默认开启）

参考图功能（F59）需要把参考图发给视觉模型，读出构图描述后逐张生成提示词。看图描述**默认开启**（决策 A5），只用于参考图；管理员可以关闭。把商品图发给视觉模型描述商品仍默认关闭。

1. 管理员可以在工作台的 **提示词优化设置** 中取消勾选 **允许看图描述** 来关闭它。开关下方会显示当前配置的视觉模型。
2. 视觉模型与提示词模型是两份独立配置，在同一弹窗里分别设置：视觉服务提供 Ollama 和 OpenAI 兼容两类，默认同为 Ollama，视觉模型默认 `llama3.2-vision:latest`。最省事的做法是在服务器的 EcomCraft 目录运行一键安装脚本（F69）：没有 Ollama 时自动安装（Windows 优先用 winget，否则下载官方安装包并校验数字签名后才运行；Linux 用官方安装脚本），启动服务并拉取视觉模型，失败时给出原因和手动步骤：

```powershell
powershell -ExecutionPolicy Bypass -File launchtools\install-vision-model-windows.ps1 -Model llama3.2-vision:latest
```

```bash
bash launchtools/install-vision-model-linux.sh llama3.2-vision:latest
```

   `-Model`（Linux 为第一个参数）填提示词优化设置中的视觉模型名称（如换成 Qwen3-VL 时填 `qwen3-vl:8b`），省略时为默认模型；macOS 请先从官网安装 Ollama 应用。也可以手动安装 Ollama 后拉取：

```bash
ollama pull llama3.2-vision
```

   不想碰命令行时，也可以在 **提示词优化设置 → 允许看图描述 → 一键安装本地模型**（管理员）里直接安装：选择模型档位（Qwen3-VL 2B / 4B / 8B / 30B 或 Llama 3.2 Vision，附下载体积与显存建议）后，服务器会运行同一个脚本自动安装；进行中显示已用时和脚本输出，可以关闭弹窗（安装会继续，重开时自动恢复进度），安装成功后服务器会把该模型自动保存为视觉模型（本机 Ollama），即使中途关闭了弹窗也一样，无需再点“保存设置”。同一时间只允许一个安装，也只允许安装上面列出的档位；Windows 和 Linux 可用，macOS 请按上文手动安装。

3. 参考图上传后会自动逐张描述。点“添加参考图”时会先检查视觉模型是否可用：未配置时不打开选图，改为在下方显示看图描述指引，点“打开提示词优化设置”步骤可直接进入配置。关闭看图描述、视觉服务连不上或没有拉取视觉模型时，“照参考图做”也会同样说明缺什么：管理员看到具体处理步骤，其他账号看到“请联系管理员”；普通出图不受影响。

4. 管理员也可以直接在“照参考图做”页面的 **看图模型** 下拉框里切换视觉模型（列出视觉服务中能看图的模型），选中即保存，和提示词优化设置里的视觉模型是同一项，之后描述的参考图都用新模型。想确认实际调用的是哪个模型，可以看服务日志中的`EcomCraft is describing an image with <服务类型> / <模型名>`，或在 Ollama 所在电脑运行 `ollama ps`。

### 推荐视觉模型：Qwen3-VL（中文图生文效果更好）

默认的 `llama3.2-vision` 对中文商品图的理解一般。做看图描述（图生文）建议改用阿里开源的视觉模型 **Qwen3-VL**（Apache-2.0 协议）：中英文描述质量高，能读出商品图上的文字、标签和吊牌（OCR 能力强），下载量和口碑都长期靠前。

Ollama 官方库已收录 Qwen3-VL。同一系列里的 2B / 4B / 8B / 30B 只是“脑容量”不同：数字越大，认得越准、描述越细、图上文字识别越稳，但也更吃显存、单张稍慢；用法完全一样，随时可以再拉一档换用（下表为默认 4-bit 量化版的下载体积）：

| 命令 | 模型 | 下载体积 | 建议显存 | 描述能力的差别 |
|---|---|---|---|---|
| `ollama pull qwen3-vl:2b` | Qwen3-VL 2B | ~1.9 GB | 4 GB，或纯 CPU | 能认出主体和大致构图，小字标签容易漏；速度最快 |
| `ollama pull qwen3-vl:4b` | Qwen3-VL 4B | ~3.3 GB | 6 GB | 日常商品图描述准确，复杂花纹、密集小字偶尔出错 |
| `ollama pull qwen3-vl:8b` | Qwen3-VL 8B | ~6.2 GB | 8 GB | 细节、中文标签和构图光线的描述明显更稳、更专业；**大多数团队的最佳选择** |
| `ollama pull qwen3-vl:30b-a3b` | Qwen3-VL 30B（MoE） | ~20 GB | 24 GB | 最强档：多商品同框、长图密集文字也能读全；MoE 结构速度不慢 |

拿不准时按显存选即可：无独显选 2B，6 GB 选 4B，8 GB 及以上直接 8B；有 24 GB 大卡（如 3090/4090）且要求最高质量再上 30B。拉取也可以用上面的一键安装脚本完成（把 `-Model` 参数换成所选档位）。

装好所选档位后，在设置里直接选择即可：

1. 打开工作台的 **提示词优化设置**（管理员），确认 **允许看图描述** 已勾选。
2. **视觉模型服务** 选 `Ollama（本地，免费）`，弹窗会自动检测本机 Ollama。
3. 在 **视觉模型** 下拉框里选择刚拉取的档位（如 `qwen3-vl:8b`）；没有出现时点“重新检测”。
4. 点击 **保存**。开关下方应显示 `视觉模型：ollama / qwen3-vl:8b`。

想用 LM Studio、vLLM 等本地 OpenAI 兼容服务跑 Qwen3-VL 时，把 **视觉模型服务** 选为 `OpenAI 兼容服务（本地 / 远程）`，填写该服务的 Base URL（需要时再填 API Key）后拉取选择。视觉服务的地址和 Key 与提示词服务分开保存，可以一边用 Ollama 或另一台兼容服务跑提示词扩写、一边用这台兼容服务跑看图描述；视觉服务地址与提示词服务不同时，不会把提示词服务的 Key 发给视觉服务。

两点提醒：

- 看图描述每张图最多等待 180 秒（本地模型第一次用时要先载入内存，显卡没有响应时还要改用 CPU 重试），实际上限不超过上文的生成时限（默认 120 秒）。Qwen3-VL 8B 在 8 GB 以上显存的 GPU 上通常几秒完成；纯 CPU 请选 2B 档，超时的参考图标为描述失败，可以单独重试。
- 看图时已关闭 Qwen3 系列的“思考”过程，输出更短、更快。
- 国内网络直接拉取 Ollama 模型较慢时，可为 Ollama 进程设置 HTTPS_PROXY 代理后重启再拉取。

### 看图模型的运行设备（Ollama）

客户电脑的显卡各不相同。有些显卡跑看图模型会卡住：例如 Windows 上 RX 5600 XT 这类较老的 AMD 显卡，Ollama 只能通过 Vulkan 使用它，看图请求会长时间没有结果，之后 Ollama 显示模型“已加载”却不占显存，连文字请求也一起卡住。同一台电脑改用 CPU，一张图大约 8–18 秒就能描述完。

为此，提示词优化设置的 Ollama 看图部分有一个 **运行设备** 选项：

| 选项 | 行为 |
|---|---|
| 自动（默认） | 每个“Ollama 地址 + 视觉模型”第一次看图时，先在显卡上最多等 60 秒；没有结果就让 Ollama 卸载该模型，改用 CPU 重试，并在本次服务运行期间一直用 CPU，重启服务后再试显卡。显卡正常的电脑（多数 NVIDIA 显卡、Apple Silicon Mac）不受影响 |
| 只用显卡 | 一直交给 Ollama 自动选择设备，不改用 CPU |
| 只用 CPU | 一直用 CPU 看图（Ollama 的 `num_gpu 0`），不占用显存，适合显卡跑不动或要把显存留给出图的电脑 |

自动改用 CPU 后，设置里的说明会改为“显卡没有响应，本次运行已自动改用 CPU”；每次重启都这样时，直接改选“只用 CPU”可以省掉第一次的等待。任何一次看图超时后，EcomCraft 都会让 Ollama 卸载该模型，避免卡住的状态影响后面的请求。这个选项只对 Ollama 生效；OpenAI 兼容服务的运行设备由该服务自己决定。

注意：看图描述会把用户上传的图片发送到所配置的视觉模型服务。使用本地 Ollama 时图片不出本机；若把视觉后端改为云端服务，图片会离开本机，请管理员确认团队可接受后再改。参考图本身只在内存中短暂存在，不会保存到服务器。

## 验证与排错

| 现象 | 原因 / 处理 |
|---|---|
| 「Ollama Connection Error / Cannot connect to the Ollama server」 | Ollama 未安装或未运行；确认已启动 `ollama serve`，地址端口正确，并已 `ollama pull` 模型 |
| 「Authentication / API Key not found」 | 云后端未设置 Key 或 Key 错误；在设置中重新粘贴 |
| 「401 / 账号会话已失效」 | 登录会话过期，刷新页面重新连接 |
| 「Request timeout」 | 本地模型较慢或网络不通，可重试；CPU 跑大模型可能较慢 |
| 看图描述无结果 / 状态显示服务不可用 | 视觉模型未拉取、Ollama 未运行、设置里未选对视觉模型，或模型太慢（纯 CPU 请用 2B 档）；参考上方 Qwen3-VL 小节 |
| 看图很慢、第一张约 1 分钟后才出结果 | 显卡跑不动看图模型，“自动”模式已改用 CPU；在设置里把运行设备改为“只用 CPU”可省掉这段等待，见上方“看图模型的运行设备” |
| 拉取模型列表为空 / 失败 | 检查服务是否运行、地址是否含正确路径、Key 是否有效 |
| 其他电脑无法访问本地服务 | 本地服务需监听 `0.0.0.0`，并在防火墙放行对应端口 |
| 想恢复默认设置 | 在原生引擎/配置中重置 MagicPrompt 配置，或重新在设置中选择后端 |

后端配置（后端类型、模型名、服务地址）为全局共享，保存在 SwarmUI 的用户数据中；API Key 按用户隔离保存。升级 EcomCraft 时这些配置会保留，不会被覆盖。
