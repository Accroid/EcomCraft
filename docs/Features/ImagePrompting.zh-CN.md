# 图像提示：编辑模型、IP-Adapter 与 ReVision

> **语言：** [English 原文](ImagePrompting.md) · 简体中文

可将图像拖入提示词框，或在提示词框中按 Ctrl+V。支持方式取决于模型架构和相应参数。

- **编辑模型**：Flux Kontext、Qwen Image Edit 等模型通常直接接受提示图，不要同时启用额外图像提示模块；请查看对应的[模型支持说明](../Model%20Support.zh-CN.md)。
- **ReVision**：面向 SDXL 的图像提示方式。降低 ReVision Strength 可减弱影响；提示词过强时可能压过图像引导。
- **IP-Adapter**：将参考图的视觉概念用于引导生成。依模型架构安装对应 IP-Adapter 和视觉编码器，并在参数中选择适合的类型。
- **Style Model**：例如 Flux Redux，通过相同的图像提示参数组使用。

IP-Adapter、ReVision、ControlNet 和编辑模型用途不同，不能互相替代。安装来源、兼容架构和额外参数见英文原文及模型作者说明。
