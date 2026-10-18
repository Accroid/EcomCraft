# MagicPrompt 代码来源说明（NOTICE）

本目录 `src/Extensions/EcomCraft/MagicPrompt/` 中的代码，改编（vendored）自开源项目：

- **项目**：HartsyAI/SwarmUI-MagicPromptExtension
- **地址**：https://github.com/HartsyAI/SwarmUI-MagicPromptExtension
- **作者**：Hartsy
- **许可证**：MIT（完整许可证文本见本目录 `LICENSE`，Copyright (c) 2025 Hartsy）

EcomCraft 在其基础上做了以下调整：

1. 命名空间由 `Hartsy.Extensions.MagicPromptExtension` 改为 `EcomCraftStudio.MagicPrompt`；
2. 仅保留多后端（Ollama / OpenAI 兼容 / OpenAI / Anthropic / OpenRouter / Grok）的核心后端与 WebAPI 代码，裁剪了挂载于 SwarmUI 原始界面的标签页、资源文件与独立扩展入口（这些功能由 EcomCraft 自身的界面与 `EcomCraft.cs` 承载）；
3. 默认模型、提示词优化指令等按 EcomCraft 电商产品摄影场景做了定制，用户可见品牌文案改为 EcomCraft。

对上游的归属链接（GitHub URL、HTTP-Referer 等）按原作者要求予以保留。
