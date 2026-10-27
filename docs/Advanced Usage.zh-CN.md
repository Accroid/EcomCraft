# SwarmUI 高级使用指南

> **语言：** [English 原文](Advanced%20Usage.md) · 简体中文

## 目录

- [从其他设备访问 SwarmUI](#从其他设备访问-swarmui)
- [通过互联网访问](#通过互联网访问)
- [公开托管 SwarmUI](#公开托管-swarmui)
- [自定义 ComfyUI 工作流](#自定义-comfyui-工作流)
- [Triton、TorchCompile 与 SageAttention](#triton-torchcompile-与-sageattention)
- [旧版 Windows Triton 安装说明](#旧版-windows-triton-安装说明)
- [使用多个模型目录](#使用多个模型目录)

## 从其他设备访问 SwarmUI

### 局域网访问

1. 打开 `Server → Server Configuration`。
2. 找到 `Host` 设置（默认值为 `localhost`），将其改为 `0.0.0.0` 并保存，然后重启 SwarmUI。
3. 必要时，在操作系统防火墙中允许 SwarmUI 监听端口的访问。
4. 如果可检测到局域网地址，可在 `Server Info` 标签页查看。

将服务监听在所有网卡上会扩大可访问范围，请同时检查防火墙规则和账号认证。

### 无端口转发的互联网访问

可使用 Cloudflared 或 ngrok 建立临时/代理访问入口。

- **Cloudflared：** 按 [cloudflared 安装说明](https://github.com/cloudflare/cloudflared?tab=readme-ov-file#installing-cloudflared) 安装软件。这里只需要安装程序本身，无需照搬文档中的账号或域名配置。启动 SwarmUI 时可传入 `--cloudflared-path [...]`，或在服务器配置的 `CloudflaredPath` 中设置路径，然后重启。
- **ngrok：** 按 [ngrok 官方文档](https://ngrok.com/) 安装并登录账号，然后通过启动参数 `--ngrok-path [...]` 指定程序路径。

## 通过互联网公开托管 SwarmUI

此部分面向有经验的系统管理员，不建议直接照做后就把服务暴露到公网。

- **不要在没有认证保护的情况下公开服务。**
- 部分 Swarm API（例如转发原始 ComfyUI 请求的接口）可能带来安全风险。
- SwarmUI 提供了初步的实验性多用户认证功能，但在充分验证前，仍建议在服务前面增加独立的认证边界。账号相关说明见[共享 SwarmUI 实例](Sharing%20Your%20Swarm.md)。
- 了解反向代理的用户可以使用 Apache2 或 Nginx 配合认证。
- 应配置 HTTPS（例如使用 Let's Encrypt 的 Certbot）。直接使用 HTTP 可能引起问题。

## 自定义 ComfyUI 工作流

自定义工作流需要使用 ComfyUI 后端。

1. 点击顶部的 `Comfy Workflow Editor` 标签页。
2. 使用 [ComfyUI 工作流示例](https://comfyanonymous.github.io/ComfyUI_examples/) 或自己的节点图构建工作流。
3. 可以在编辑过程中直接在 ComfyUI 中测试生成效果。
4. 对不直观的节点参数，建议添加 `Primitive` 节点并给它们设置清楚的标题，再将它们连接到输入端。
5. 确保工作流末端只有一个 `Save Image` 节点，然后点击 **Use This Workflow**。

![应用自定义工作流](/docs/images/usecomfy.png)

应用后，参数列表会根据工作流更新。被 SwarmUI 识别的参数会继续使用原生参数控件；其他参数会按节点 ID 或标题显示。之后可像平常一样生成图片，Grid Generator 的可选参数轴也会根据工作流参数自动更新。

修改了工作流后，必须再次点击 **Use This Workflow** 才会加载新内容。要恢复常规工作流，请在参数列表顶部附近找到并点击 **Disable Custom Comfy Workflow**。

更详细的说明见[自定义 ComfyUI 工作流](Features/Comfy-Workflows.md)。

## Triton、TorchCompile 与 SageAttention

以下属于高级性能优化选项。安装前请确认当前 PyTorch、GPU、驱动和依赖版本兼容，并保留能恢复的环境备份。

### Linux

通常可以尝试：

1. 更新 PyTorch。
2. 参考[依赖安装排查指南](Troubleshooting.zh-CN.md#通过-pip-安装依赖)，安装 `triton` 和 `sageattention`。
3. 编辑后端配置，在 `ExtraArgs` 中加入 `--use-sage-attention`。

实际兼容性因环境而异。

### Windows

在 SwarmUI 安装目录下打开 `dlbackend\comfy` 对应的命令行，执行：

```powershell
.\python_embeded\python.exe -s -m pip install triton-windows
.\python_embeded\python.exe -s -m pip install sageattention
```

也可以查看 [SageAttention 发布页](https://github.com/woct0rdho/SageAttention/releases) 中适合环境的 wheel 包。

安装完成后：

1. 启动 SwarmUI。
2. 进入 `Server → Backends`，编辑 ComfyUI Self-Start 后端。
3. 在 `ExtraArgs` 中添加 `--use-sage-attention`，保存并等待后端加载。
4. 返回 Generate 页面，连续执行两次生成以确认第二次生成也正常。

原文记录的测试环境中，SageAttention 通常带来几个百分点的速度提升；显存受限时，个别用户报告的提升可能更明显。实际效果依模型与硬件而异，不能保证。

可选地，进入 `Advanced → Advanced Model Addons`，将 `Torch Compile` 设置为 `inductor`。首次运行某个模型会明显变慢，因为需要编译；后续运行可能更快。是否划算取决于后续生成数量、模型与硬件。

## 旧版 Windows Triton 安装说明

以下内容是旧版安装指引，可能只适用于特定环境。仅建议熟悉 Python 环境和 Windows 开发工具的用户尝试；不应把它当作当前所有安装的通用步骤。

Triton 是主要面向 Linux 的 AI 加速库，可以通过特定方案在 Windows 上使用；SageAttention 依赖 Triton。

1. 按照 [Triton-Windows 指南中有关 VCRedist/MSVC 的步骤](https://github.com/woct0rdho/triton-windows?tab=readme-ov-file#5-msvc-and-windows-sdk) 安装相关组件，并核对该指南顶部针对 GPU 的特殊要求。
2. 在 `(Swarm 安装目录)\dlbackend\comfy` 中运行：

   ```powershell
   python_embeded\python.exe --version
   ```

   记录嵌入式 Python 版本，并安装**完全相同版本**的全局 Python。
3. 打开一个新终端，运行 `python --version` 确认版本匹配；若不匹配，需要检查 PATH。使用 `where python` 查找全局 Python 的安装目录。
4. 打开全局 Python 目录和 `dlbackend\comfy\python_embeded` 目录，将全局 Python 中的 `Libs` 文件夹复制到嵌入式 Python 目录，并复制 `Include` 文件夹中的内容。
5. 返回 `dlbackend\comfy` 目录，安装依赖：

   ```powershell
   .\python_embeded\python.exe -s -m pip install triton-windows
   .\python_embeded\python.exe -s -m pip install sageattention
   ```

若该流程与现有 Python 环境不匹配，请停止操作并查阅上游最新文档，以免破坏后端环境。

## 使用多个模型目录

如果模型分散在多个目录中（例如原先的 Auto1111 和 ComfyUI 安装，或者快速 SSD 与较慢磁盘），SwarmUI 支持将它们配置为多个模型根目录。

1. 打开 `Server → Server Configuration`，找到顶部的 `Paths` 配置。
2. 将 `ModelRoot` 设置为以分号 `;` 分隔的目录列表。例如：

   ```text
   C:/AI/Auto1111/Models;C:/AI/ComfyUI/models
   /home/username/SwarmUI/Models;/media/fatdrive/SwarmModels
   ```

   可以按需添加任意数量的目录。若有快慢磁盘组合，建议把快速目录放在前面。
3. 若希望模型下载工具将模型下载到指定根目录，可将 `DownloadToRootID` 设为对应索引：第一个目录为 `0`，第二个为 `1`，依此类推。
4. 其他模型路径通常填写子目录名称。例如，`SDModelFolder` 可以设为 `checkpoints`，或设为 `Stable-Diffusion;checkpoints` 以兼容两种目录布局。

列表中第一个目录在加载和下载时优先使用。多数情况下，这些设置项需要的是子目录名，而不是完整系统路径。