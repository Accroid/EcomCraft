# SwarmUI 音频模型支持

> **语言：** [English 原文](Audio%20Model%20Support.md) · 简体中文

| 模型 | 年份 | 作者 | 规模 | 类型 | 质量/状态 |
|---|---:|---|---|---|---|
| [Ace Step 1.5](#ace-step-15) | 2026 | StepFun | 2B DiT | 音乐 | 新一代，速度快，质量尚可 |
| [MiniMax Music 3](#minimax-music-3) | 2026 | Hailuo / MiniMax | 2B DiT | 音乐 | 较新，速度较快，质量尚可 |
| [YuE2](#yue2) | 2026 | Multimodal Art Projection | 3B DiT | 音乐 | 新一代，速度很快，效果较好 |

图像模型及技术格式支持见[模型支持文档](Model%20Support.md)。

音频模型用途不同：

- **音效模型：** 生成通用音效，可用于视频等内容，例如 MMAudio。
- **音乐模型：** 生成完整歌曲，通常可控制乐器、风格和歌词，例如 ACE-Step。
- **语音模型：** 生成语音，通常会接收语音参考和需要朗读的文本，例如 VibeVoice。

## Ace Step 1.5

- [Ace Step 1.5 官方项目](https://huggingface.co/ACE-Step/Ace-Step1.5) 是 SwarmUI 原生支持的首批音频模型之一。
- 可从 [Comfy-Org/ace_step_1.5](https://huggingface.co/Comfy-Org/ace_step_1.5_ComfyUI_files/blob/main/split_files/diffusion_models/acestep_v1.5_turbo.safetensors) 下载模型，并放入 `diffusion_models` 目录。
- 所需的两个轻量 Qwen 文本编码器和音频 VAE 会自动下载。
- 选择模型后会出现相应的 `Text2Audio` 参数组。
  - **Prompt：** 输入歌曲歌词，可使用 `[Intro]` 或 `[Chorus - Pop Explosion]` 等段落提示标签。歌词和风格格式示例见[官方示例页](https://ace-step.github.io/ace-step-v1.5.github.io/)。
  - **Audio Duration：** 默认 120 秒，设计上可支持几秒到约 10 分钟的音频。
  - **Audio Style：** 简短描述音乐风格。
  - **CFG Scale：** Turbo 版本建议设为 `1`。
  - **Steps：** Turbo 版本建议设为 `8`。
  - **Sigma Shift：** 默认 `3`。

## MiniMax Music 3

- [MiniMax Music 3](https://huggingface.co/MiniMaxAI/MiniMax-Music3) 已得到 SwarmUI 支持。
- 可从 [Comfy-Org/MiniMax-Music-3](https://huggingface.co/Comfy-Org/MiniMax-Music-3/blob/main/diffusion_models/minimax_music3_dit_int8_convrot.safetensors) 下载模型，放入 `diffusion_models` 目录。
- 它使用专用的 8B LLM 构建歌曲，并使用 0.6B 小型 LLM 编码音频属性；相关模型会自动下载，音频 VAE 也会自动下载。
- 参数建议：
  - **Prompt：** 输入歌曲歌词。
  - **Audio Duration：** 默认 120 秒，最大时长可设至约 5 分钟。LLM 歌曲构建器可能生成短于该上限的歌曲。
  - **Audio Style：** 简短描述音乐风格。官方提供了[风格描述重写技能和模板](https://github.com/MiniMax-AI/MiniMax-Music3/tree/main/skills/music-caption-rewriter)。
  - **CFG Scale：** 官方参考值为 `1.7`。
  - **Steps：** 常见可用值例如 `20`；官方参考值为 `30`。

## YuE2

- [YuE2 项目主页](https://map-yue2.github.io/) 已得到 SwarmUI 支持。
- 可从 [Comfy-Org/YuE2 checkpoints](https://huggingface.co/Comfy-Org/YuE2/tree/main/checkpoints) 下载模型，放到 `Models/Stable-Diffusion` 目录。
- 模型文件自带音频 VAE 和文本编码器。
- 内部工作流程是先根据输入生成包含旋律、和弦等内容的完整规划，再由另一个模型根据规划生成音乐。
- 项目另有用于将歌曲转写为自有格式的 SheetSage2 工具，但该功能目前尚未集成到 SwarmUI。可在 Comfy 标签页使用简短工作流尝试：`Load Audio Encoder → Load Audio → SheetSage2 Audio to ABC → Preview Any)。

参数建议：

- **Prompt：** 输入歌词。该模型对生僻词发音的理解有限，必要时可用近似读音进行提示。例如与其直接输入 `SwarmUI`，可以输入 `Swarm-you-eye`。
- 歌词可使用段落标签，例如：

  [verse]

  some song lyrics here!

  I am singing a song!

  [chorus]

  Wow what a song!

  Such a singable song!

  模型可能会自行补充少量歌词，例如加入 “yeah” 或重复部分词句。
- **Text2Audio Style：** 输入简短音乐风格描述。这是自由形式的 LLM 输入，常见曲风通常可以理解，但复杂指令的解释不一定稳定。
- **Audio Duration：** 默认 300 秒（5 分钟），最高可设为 900 秒（15 分钟）；这只是最长时长，模型可能输出更短歌曲。
- **CFG Scale：** 建议设为 `1`。
- **Steps：** 常用值例如 `20`，具体请根据当前模型和效果调整。