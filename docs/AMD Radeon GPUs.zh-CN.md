# SwarmUI 中的 AMD Radeon GPU 支持

> **语言：** [English 原文](AMD%20Radeon%20GPUs.md) · 简体中文

首次安装 SwarmUI 时，安装器会提示是否安装 AMD Radeon 显卡支持，并尝试配置基本环境。

该支持主要通过 AMD 的 ROCm 加速平台和针对 ROCm 编译的 PyTorch 版本实现。由于 PyTorch 的兼容层设计，日志中即使实际使用 ROCm，也可能仍然出现 `cuda` 字样。

需要注意：与 NVIDIA 相比，AMD 的 AI 软件和驱动生态通常更加挑剔，安装、兼容和性能调优可能更困难。一些功能可能不可用，或性能明显较低。如果可以自由选择硬件，原文建议优先考虑 NVIDIA。AMD 专项文档目前仍较有限，欢迎熟悉相关环境的用户贡献。

## 同一台电脑混用 NVIDIA 与 AMD

同一台电脑混用 NVIDIA 与 AMD 后端配置会比较复杂，需要为两种硬件分别安装和管理后端。另一种做法是分别安装一套面向 AMD 和 NVIDIA 的 SwarmUI，再启动两个实例并通过后端连接组合使用。

## 使用多张 AMD GPU

与 NVIDIA 使用较直观的数字 ID 不同，AMD GPU 的设备 ID 规则更复杂。总体上可以参考[多 GPU 配置指南](Using%20More%20GPUs.zh-CN.md)，但设置 `GPU_ID` 时必须更加谨慎，并逐一验证实际使用的是哪一张 GPU。

## 常见问题

### 通用启动错误

AMD Radeon GPU 出现错误时，第一步是确认 AMD/Radeon 驱动已更新到适合当前设备和环境的版本。ROCm 对驱动版本比较敏感。

### GPU 型号不受支持

ROCm 仅支持部分 AMD GPU，较新的常见型号支持通常更好，较旧型号可能支持不足。

部分环境可以尝试通过环境变量 `HSA_OVERRIDE_GFX_VERSION=10.3.0` 指定替代 HSA GFX 版本。设置方式见[命令行与环境变量指南](Command%20Line%20Arguments.zh-CN.md#环境变量)。该设置只对部分设备和软件版本有效，不保证能解决所有兼容问题。

### 后端启动时出现致命异常

典型日志包括 `Windows fatal exception: access violation`，调用栈可能指向 SwarmUI 的嵌入式 Python 环境中的 `torch\cuda\__init__.py`。

原文提出可以尝试前述 `HSA_OVERRIDE_GFX_VERSION`，但也明确指出该方案仍需要 AMD 专家进一步验证。请同时检查 GPU 型号、驱动、ROCm 与 PyTorch 版本，不要把环境变量当作通用修复。

### RDNA Nightly 版本

如果使用集成式 AMD GPU，可以尝试 ComfyUI 文档中提到的 RDNA nightly 构建。请先核对其当前适用范围，尤其是 Windows/Linux、RDNA 3、3.5 与 4 的限制：[ComfyUI AMD GPU 实验性支持](https://github.com/Comfy-Org/ComfyUI#amd-gpus-experimental-windows-and-linux-rdna-3-35-and-4-only)。

安装自定义 pip 包时，也可参考[依赖安装排查指南](Troubleshooting.zh-CN.md#通过-pip-安装依赖)。