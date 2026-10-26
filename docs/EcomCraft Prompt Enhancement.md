# EcomCraft 提示词优化（MagicPrompt）使用与部署指南

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

开发回归检查：运行 `node --test src/Extensions/EcomCraft/Tests/prompt-settings.test.cjs`。服务启动后，可设置环境变量 `ECOMCRAFT_TEST_URL=http://127.0.0.1:7801`，再运行 `node --test src/Extensions/EcomCraft/Tests/model-preview.integration.cjs`；接口测试只使用本地模拟服务和测试密钥，不保存用户配置。

---

## 验证与排错

| 现象 | 原因 / 处理 |
|---|---|
| 「Ollama Connection Error / Cannot connect to the Ollama server」 | Ollama 未安装或未运行；确认已启动 `ollama serve`，地址端口正确，并已 `ollama pull` 模型 |
| 「Authentication / API Key not found」 | 云后端未设置 Key 或 Key 错误；在设置中重新粘贴 |
| 「401 / 账号会话已失效」 | 登录会话过期，刷新页面重新连接 |
| 「Request timeout」 | 本地模型较慢或网络不通，可重试；CPU 跑大模型可能较慢 |
| 拉取模型列表为空 / 失败 | 检查服务是否运行、地址是否含正确路径、Key 是否有效 |
| 其他电脑无法访问本地服务 | 本地服务需监听 `0.0.0.0`，并在防火墙放行对应端口 |
| 想恢复默认设置 | 在原生引擎/配置中重置 MagicPrompt 配置，或重新在设置中选择后端 |

后端配置（后端类型、模型名、服务地址）为全局共享，保存在 SwarmUI 的用户数据中；API Key 按用户隔离保存。升级 EcomCraft 时这些配置会保留，不会被覆盖。
