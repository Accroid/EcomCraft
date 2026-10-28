# EcomCraft Prompt Enhancement (MagicPrompt) Setup and Deployment Guide

> **Language:** English · [简体中文](EcomCraft%20Prompt%20Enhancement.md)

The **✦ Enhance prompt** button in the EcomCraft workspace sends your short description to a large language model (LLM), which rewrites it into a richer, generation-ready prompt and fills it back into the description box.

MagicPrompt is **built in** (from the open-source [HartsyAI/SwarmUI-MagicPromptExtension](https://github.com/HartsyAI/SwarmUI-MagicPromptExtension), MIT licence) and enabled by default with EcomCraft — no separate installation.

It supports **6 kinds of backends**, so you can choose freely based on whether you have a GPU and whether you are willing to pay:

| Backend | Type | API key needed | Notes |
|---|---|---|---|
| **Ollama** | Local | No | The simplest local, free option — one command per model |
| **OpenAI-compatible service (local / remote)** | Local or remote | Depends | LM Studio, LocalAI, vLLM, self-hosted services and remote compatible APIs |
| **OpenRouter** | Cloud | Yes | One key, hundreds of models, **including free ones** |
| **OpenAI** | Cloud | Yes | ChatGPT (GPT-4o / GPT-4.1 etc.) |
| **Anthropic** | Cloud | Yes | Claude (Sonnet / Opus etc.) |
| **Grok** | Cloud | Yes | xAI's Grok models |

---

## Option 1: local and free with Ollama (most recommended)

For: any ordinary computer, zero cost, fully offline.

### 1. Install Ollama

Download from [https://ollama.com/download](https://ollama.com/download) (Windows / macOS / Linux). Ollama runs in the background at `http://localhost:11434` by default.

### 2. Pull a model

In a terminal (PowerShell / CMD on Windows) run:

```bash
# Light and enough for prompt expansion (about 2 GB, 3B parameters)
ollama pull llama3.2
```

Other options:

| Command | Model | Size | Best for |
|---|---|---|---|
| `ollama pull llama3.2` | Llama 3.2 (3B) | ~2 GB | Fast, sufficient |
| `ollama pull qwen2.5` | Qwen2.5 (7B) | ~4.7 GB | Better quality, needs more memory/VRAM |
| `ollama pull phi3` | Phi-3 (3.8B) | ~2.3 GB | Light alternative |

> Works without a discrete GPU: Ollama falls back to CPU + RAM, and a 3B model usually answers a prompt in seconds on an ordinary machine.

### 3. Configure EcomCraft

1. Open the EcomCraft workspace and click **Settings** under the creation description (administrators).
2. Choose:
   - **Model backend**: `Ollama (local, free)`
   - **Base URL**: `http://localhost:11434` (same machine, no change needed)
   - **Prompt model**: press **Fetch** and pick from the list, or type `llama3.2:latest`
3. Click **Save**.

---

## Option 2: OpenRouter cloud (free models, no good hardware needed)

For: low-spec machines, no local models, free quota wanted.

1. Register at [https://openrouter.ai](https://openrouter.ai).
2. Create an API key at [https://openrouter.ai/keys](https://openrouter.ai/keys) (`sk-or-...`).
3. In the EcomCraft settings:
   - **Model backend**: `OpenRouter (free models included)`
   - **API key**: paste the key
   - **Prompt model**: free models work, for example:
     - `meta-llama/llama-3.3-70b-instruct:free`
     - `google/gemini-2.0-flash-exp:free`
     - `qwen/qwen-2.5-7b-instruct:free`
4. Click **Save**.

> Free models may be rate-limited; treat the `:free` badge on OpenRouter as the source of truth.

---

## Option 3: paid clouds — OpenAI / Claude / Grok

For: already using these services, wanting stability and quality.

1. Create an API key on the platform:
   - OpenAI: [https://platform.openai.com/api-keys](https://platform.openai.com/api-keys)
   - Anthropic Claude: [https://console.anthropic.com/settings/keys](https://console.anthropic.com/settings/keys)
   - Grok (xAI): [https://console.x.ai](https://console.x.ai)
2. Pick the backend in the EcomCraft settings and paste the key.
3. Model names come from each platform's docs (e.g. OpenAI's `gpt-4o-mini`, Anthropic's `claude-3-5-sonnet-latest`); **Fetch** also lists them.

---

## Option 4: other local OpenAI-compatible services (LM Studio / vLLM etc.)

The `OpenAI-compatible service (local / remote)` backend works with anything implementing the OpenAI `/v1/chat/completions` and `/v1/models` endpoints, including:

- **LM Studio**: graphical app; load a model and enable "Local Server", default `http://localhost:1234/v1`.
- **vLLM**: `vllm serve <model> --port 8000`, URL `http://localhost:8000/v1`.
- **text-generation-webui / LocalAI / self-built FastAPI**: same idea.

Configuration:
- **Model backend**: `OpenAI-compatible service (local / remote)`
- **Base URL**: the service's base URL (with or without `/v1`, whatever the service uses)
- **API key**: usually empty for local services; fill it in if the service requires one.
- **Prompt model**: pick via **Fetch**.

---

## Option 5: self-hosting the dedicated prompt model BeautifulPrompt (pai-bloom)

EcomCraft also ships a script that deploys Alibaba PAI's open-source prompt model BeautifulPrompt (`pai-bloom-1b1-text2prompt-sd`) behind an OpenAI-compatible API, to be used with the `OpenAI-compatible service` backend.

### Hardware requirements

The model is a BLOOM 1.1B:

| Item | Minimum | Recommended | Notes |
|---|---|---|---|
| GPU VRAM (F16) | ~3 GB | ≥ 4 GB | F16 weights about 2.1 GB |
| GPU VRAM (8/4-bit quantized) | ~2 GB / ~1.5 GB | ≥ 3 GB / ≥ 2 GB | Quantize yourself |
| CPU only | 8 GB RAM | 16 GB RAM | FP32 weights about 4.2 GB |
| Disk | 5 GB | 10 GB | Model ~2.1 GB + Python/torch |

### Steps

```bash
# 1. Python 3.10–3.12 and dependencies
pip install -r src/Extensions/EcomCraft/deploy/requirements.txt

# 2. Start (auto-selects CUDA / Apple MPS / CPU)
python src/Extensions/EcomCraft/deploy/serve_prompt_enhancer.py --host 0.0.0.0 --port 8000

# 3. Self-check
curl http://localhost:8000/v1/models
```

Then in the EcomCraft settings:
- **Model backend**: `OpenAI-compatible service (local / remote)`
- **Base URL**: `http://localhost:8000` (the script's root is the compatible entry; join `/v1` as needed)
- **Prompt model**: `alibaba-pai/pai-bloom-1b1-text2prompt-sd`
- No key needed.

> pai-bloom is an older general SD prompt model with an artistic lean; for clean e-commerce product descriptions prefer Option 1 (a newer model via Ollama) or a cloud model.

---

## Configuration fields

| Field | Notes |
|---|---|
| Model backend | One of the 6 backends above |
| Base URL | Editable for Ollama / the compatible backend, local or remote; fixed for the others |
| Prompt model | **Fetch** lists the backend's models, or type the ID |
| Vision model service | The service used for image description — Ollama or an OpenAI-compatible one; saved separately from the prompt service |
| Vision model | The vision service's model name, picked via **Fetch** or typed in; Qwen3-VL recommended |
| API key | Required for cloud backends; not for Ollama; usually not for local services. Keys are stored safely in SwarmUI user data, never in config files |

Switching backends auto-fills the URL and key state from that backend's saved values or its defaults.

### Generation wait time

Fetching the model list stays limited to 20 seconds. Prompt enhancement waits for a full answer with its own default timeout of 120 seconds; an old `timeout: 20` in existing configs is treated as this generation limit without rewriting user settings. Other custom limits keep applying. Administrators can set an explicit 1–600 second generation limit via the existing settings API's `backends.<backend>.generation_timeout`, which wins over the old `timeout`. Invalid values fall back to 120 seconds.

Cancelled requests, broken sessions or a shutting-down service cancel the upstream wait; timeout notices name the actual wait limit and never retry silently. A successful fetch does not guarantee the model answers in time; gateway timeouts of third-party services are outside the local limit.

### Fetching models right after entering a key

Fetched models become the dropdown candidates of the prompt-model input; there is no second selector. Fetching never overwrites the current input, and only **Save** applies it.

1. Pick a backend and fill in the URL (for editable backends) and the key.
2. Click **Fetch** — no need to save first. Fetching uses the form's backend, URL and new key; it changes no global config and saves no key. With an empty key it reuses the saved key only when the request URL matches that backend's saved address; after changing the address, enter the key again.
3. Click **Save** to persist the key and the prompt backend; the separate vision-model configuration is never touched. A successful fetch only means the model list is readable — actual enhancement still depends on model permissions and service support.

Closing or cancelling the settings discards the unsaved key; switching backend, URL or key cancels a running fetch and clears the old list. A failed or timed-out fetch can be retried after editing the form. OpenAI-compatible URLs may be the service root or include `/v1`; the path is never doubled.

The workspace fetches text models of the chosen backend temporarily through `PreviewMagicPromptModels`, which needs configuration-save and model-list permissions. It calls no config or key saving and returns no upstream error bodies or credentials.

## Image description (the vision model, on by default)

The reference-image feature (F59) sends reference images to a vision model that reads out composition descriptions, one prompt per reference. Image description is **on by default** (decision A5) and used for references only; administrators can turn it off. Sending product photos to the vision model to describe products stays off by default.

1. Administrators turn it off by unticking **Allow image description** in the workspace's **prompt enhancement settings**. The line under the toggle shows the configured vision model.
2. The vision model is a separate configuration from the prompt model, set in the same dialog: the vision service is either Ollama or an OpenAI-compatible one, both default to Ollama, and the vision model defaults to `llama3.2-vision:latest`. The easiest way is the one-click install script (F69), run in the EcomCraft folder on the server: it installs Ollama when missing (on Windows through winget, or else the official installer, run only after its code signature checks out; on Linux the official install script), starts the service and pulls the vision model, and on failure says why and gives the manual steps:

```powershell
powershell -ExecutionPolicy Bypass -File launchtools\install-vision-model-windows.ps1 -Model llama3.2-vision:latest
```

```bash
bash launchtools/install-vision-model-linux.sh llama3.2-vision:latest
```

   `-Model` (the first argument on Linux) is the vision model name from the prompt enhancement settings (`qwen3-vl:8b` when switching to Qwen3-VL) and defaults to the default model; on macOS install the Ollama app from the website first. You can also install Ollama by hand and pull the model:

```bash
ollama pull llama3.2-vision
```

3. Uploaded references are described automatically, one at a time. When image description is off, the vision service cannot be reached, or the vision model has not been pulled, "Follow a reference" says what is missing: administrators see the steps to fix it and other accounts see "ask your administrator"; plain generation is unaffected.

### Recommended vision model: Qwen3-VL (much better Chinese image description)

The default `llama3.2-vision` is mediocre with Chinese product photos. For image description we recommend Alibaba's open-source **Qwen3-VL** (Apache-2.0): high-quality Chinese and English descriptions, strong OCR for labels, tags and text on product photos, and consistently top download rankings.

The official Ollama library hosts Qwen3-VL. The 2B / 4B / 8B / 30B variants of the family differ only in "brain size": the bigger the number, the more accurate the recognition, the richer the description and the more reliable the text reading — at the cost of more VRAM and slightly slower per image. Usage is identical, and you can pull another size and switch at any time (sizes below are the default 4-bit quantized downloads):

| Command | Model | Download | VRAM | Difference in description quality |
|---|---|---|---|---|
| `ollama pull qwen3-vl:2b` | Qwen3-VL 2B | ~1.9 GB | 4 GB, or CPU only | Recognizes the subject and rough composition; small labels get missed; fastest |
| `ollama pull qwen3-vl:4b` | Qwen3-VL 4B | ~3.3 GB | 6 GB | Accurate for everyday product photos; occasional errors on intricate patterns or dense small text |
| `ollama pull qwen3-vl:8b` | Qwen3-VL 8B | ~6.2 GB | 8 GB | Clearly steadier and more professional on details, Chinese labels, composition and lighting; **the best pick for most teams** |
| `ollama pull qwen3-vl:30b-a3b` | Qwen3-VL 30B (MoE) | ~20 GB | 24 GB | Top tier: reads multi-product scenes and dense long-image text fully; MoE keeps it fast |

When unsure, choose by VRAM: no discrete GPU → 2B, 6 GB → 4B, 8 GB or more → 8B; pick 30B only with a 24 GB card (e.g. 3090/4090) and the highest quality demands. Pulling also works through the one-click install script above (pass the chosen size as the `-Model` argument).

After pulling the size you picked, select it directly in the settings:

1. Open the workspace's **prompt enhancement settings** (administrators) and make sure **Allow image description** is ticked.
2. Set **Vision model service** to `Ollama (local, free)`; when it matches the "Model service" above, the Base URL is shared and needs no re-entry.
3. In **Vision model**, press **Fetch** and pick the size you just pulled (e.g. `qwen3-vl:8b`), or type it in.
4. Click **Save**. The line under the toggle should read `Vision model: ollama / qwen3-vl:8b`.

To run Qwen3-VL on a local OpenAI-compatible service such as LM Studio or vLLM, set **Vision model service** to `OpenAI-compatible service (local / remote)`, fill in that service's Base URL and fetch. The vision service is saved separately from the prompt service, so Ollama can handle prompt expansion while the compatible service handles image description.

Two notes:

- Image description waits at most 45 seconds per image. Qwen3-VL 8B usually answers in seconds on a GPU with 8 GB or more; on CPU pick the 2B size — past the limit, description falls back to the template defaults with a service-unavailable notice.
- If pulling from Ollama is slow on a restricted network, set an HTTPS_PROXY for the Ollama process, restart it and pull again.

Note: image description sends the user's uploaded images to the configured vision service. With local Ollama the images never leave the machine; pointing the vision backend at a cloud service sends images off-site — change it only if the team accepts that. The reference images themselves exist briefly in memory only and are never stored on the server.

## Verifying and troubleshooting

| Symptom | Cause / fix |
|---|---|
| "Ollama Connection Error / Cannot connect to the Ollama server" | Ollama not installed or not running; start `ollama serve`, check the address and port, and `ollama pull` a model |
| "Authentication / API Key not found" | Cloud backend without a key, or a wrong key; paste it again in the settings |
| "401 / session expired" | Login session expired; refresh the page |
| "Request timeout" | Local models can be slow or the network down; retry — large models on CPU may take a while |
| Image description empty / status shows the service unavailable | Vision model not pulled, Ollama not running, the wrong vision model selected in the settings, or the model too slow (45 seconds per image at most; pick the 2B size on CPU); see the Qwen3-VL section above |
| Model list empty / fetch fails | Check the service is running, the URL path is right and the key is valid |
| Other computers cannot reach a local service | The local service must listen on `0.0.0.0` and the port must be open in the firewall |
| Restoring defaults | Reset the MagicPrompt configuration in the native engine's settings, or choose the backend again in the settings |

Backend configuration (backend type, model name, base URL) is global and stored in SwarmUI's user data; API keys are stored per user. Upgrading EcomCraft preserves them.
