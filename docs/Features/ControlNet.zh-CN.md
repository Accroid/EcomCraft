# SwarmUI ControlNet

> **语言：** [English 原文](ControlNet.md) · 简体中文

ControlNet 使用参考图中的边缘、深度、姿态等结构信息引导生成。它与 IP-Adapter 不同：ControlNet 更强调结构约束，IP-Adapter 更偏向图像整体概念或人物特征。

## 使用步骤

1. 将与模型架构匹配的 ControlNet 模型放入 `Models/controlnet`，刷新模型列表。
2. 在参数区展开 ControlNet，选择模型并上传引导图；未单独上传时，可能使用初始图。
3. 按模型要求选择预处理器。缺少预处理器时，可使用 SwarmUI 提供的安装按钮，或使用已预处理的图像并将预处理器设为 None。
4. 可先点 Preview 检查预处理结果，再生成图像。

ControlNet 模型必须与底模架构匹配，例如 SD1 与 SDXL ControlNet 通常不能混用。更多模型兼容性、安装依赖和排错细节见英文原文。
