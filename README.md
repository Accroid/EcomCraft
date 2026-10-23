# EcomCraft Studio

> **语言：** 简体中文 · [English](README.en.md)

EcomCraft Studio 是基于 [SwarmUI](https://github.com/mcmonkeyprojects/SwarmUI) 的电商创作工作台。本仓库包含 SwarmUI 服务器源码及 EcomCraft 扩展，**无需另行安装上游 SwarmUI**。项目仍在开发中，尚未完成生产环境验收。如果你在使用过程中出现bug，请通过Issues提交给我。

## 快速开始

### 安装要求

- [Git](https://git-scm.com/downloads) 和 [.NET SDK](https://dotnet.microsoft.com/download)：建议安装 .NET 10 SDK；项目以 .NET 8 为目标框架。运行 `git --version` 和 `dotnet --list-sdks` 可检查安装情况。
- 首次构建需要访问 NuGet；安装 ComfyUI 和下载模型也需要网络连接。
- 若在 **Linux** 上通过首次设置安装本机 ComfyUI，需预先安装 Python（建议 3.10–3.12）及其 `venv` 模块。**Windows** 安装向导使用带 Python 的 ComfyUI 便携包，无需先单独安装 Python。复用已有 ComfyUI 时，需提供可运行的环境与启动脚本。
- 生成图像或视频需要所选模型、相应的 ComfyUI 节点及兼容的计算设备；所需显存取决于模型。仅启动网页服务无需 GPU，也无需 Node.js。

### 获取源码并启动

```bash
git clone https://github.com/Accroid/EcomCraft.git
cd EcomCraft
```

在仓库根目录运行对应命令：

| 系统 | 常规启动 | 开发启动（强制重建） |
|---|---|---|
| Windows（PowerShell） | `.\launch-windows.bat` | `.\launch-windows-dev.ps1` |
| Linux | `./launch-linux.sh` | `./launch-linux-dev.sh` |
| macOS | `./launch-macos.sh` | — |

首次启动会构建服务器并还原 .NET 依赖，可能需要几分钟。开发启动脚本会重新生成构建文件，日常运行使用常规启动脚本。macOS 提供服务器启动脚本，但仓库的 ComfyUI 自动安装流程只覆盖 Windows 及 Linux 的 NVIDIA/AMD 路径；在 macOS 上使用本机生成功能需自行准备兼容的 ComfyUI 环境并验证。

### 首次配置

1. 在本机浏览器打开 `http://127.0.0.1:7801`。如果已修改服务器端口或监听地址，以实际配置为准。
2. 按首次设置页面选择使用范围、安装 **ComfyUI Self-Starting** 后端并下载模型，或跳过后端和模型安装后再配置。默认的“仅自己使用”模式会将本机访问者视为管理员；多人账号需要安装后另行配置。
3. 确认本机 ComfyUI 后端运行后，打开 `http://127.0.0.1:7801/EcomCraft` 进入工作台。工作台需要相应的生成页面和模型访问权限。

安装后若未识别已有 ComfyUI，可在原生引擎的 **Server → Backends** 页面顶部点击 **Scan ComfyUI**；找到多个安装位置时选择要使用的 `main.py`，再选择现有或新建的 **ComfyUI Self-Starting** 后端，最后点击后端卡片上的 Save。扫描只查找路径，不会自动修改后端。

工作台的生成请求只允许本机自启动 ComfyUI，不会在本机不可用时自动改用远程或云端后端。配置提示词优化可阅读[提示词优化指南](docs/EcomCraft%20Prompt%20Enhancement.md)。若构建时出现 NuGet 连接错误，检查网络、代理和 `https://api.nuget.org/v3/index.json` 的访问情况。

进入工作台后可以切换到“任务队列”查看本机 ComfyUI 后端的运行任务、等待位置和节点进度。拥有生成权限的账号可以结束自己发起的活动任务；队列状态是运行时信息，服务重启后不会保留历史记录。

## 功能状态

当前已实现：

- 图片与视频创作工作台，复用 SwarmUI 的生成能力；可用模式取决于已安装的模型、节点和账号权限。
- 管理员发布 ComfyUI 工作流供工作台选用；服务端校验工作流 ID 和已发布参数白名单，提供 `EcomCraftStudioCatalog`、`EcomCraftStudioGenerate`、`EcomCraftStudioQueue` 和 `EcomCraftStudioCancelTask` API。
- 自动发现本机 ComfyUI 工作流目录中的原生工作流，并转换为工作台可选的安全工作流。也可以在 `Settings` 的 `EcomCraft.ComfyWorkflowSearchPaths` 中配置额外目录；发现的工作流仍须包含可识别的正向和负向提示词输入。
- 实时任务队列展示本机 ComfyUI 的运行任务、排队任务、当前节点和节点进度，并支持结束当前账号自己的活动生成任务。
- MagicPrompt 提示词优化，可配置本地或云端语言模型服务。
- Windows 首次安装时可创建 EcomCraft Studio 桌面快捷方式。

规划中：团队节点管理、持久化任务历史、公共素材库，以及按任务授权的云端生成。上述规划功能当前不可作为可用功能使用。

## 项目结构

| 位置 | 用途 |
|---|---|
| `src/` | SwarmUI 服务器与浏览器前端 |
| `src/Extensions/EcomCraft/` | EcomCraft 工作台、MagicPrompt 与工作流接口 |
| `Desktop/` | 桌面启动入口 |
| `docs/` | 使用与开发文档 |

## 文档

- [中文文档总览](docs/README.zh-CN.md) · [英文文档总览](docs/README.md)
- [基础使用](docs/Basic%20Usage.zh-CN.md) · [常见问题排查](docs/Troubleshooting.zh-CN.md)
- [EcomCraft ComfyUI 工作流调用](docs/EcomCraft%20ComfyUI%20工作流.zh-CN.md)
- [提示词优化与部署](docs/EcomCraft%20Prompt%20Enhancement.md)
- [API 使用说明](docs/API.zh-CN.md) · [命令行参数](docs/Command%20Line%20Arguments.zh-CN.md)

## 数据与权限

选择云端提示词模型时，输入文本会发送给所配置的服务商；这与限定在本机 ComfyUI 的生成请求是两条不同路径。安装向导的“仅自己使用，但允许局域网访问”模式仍将访问者视为管理员；用于多人访问前，必须另行配置账号认证与权限，并检查监听地址、后端访问和素材目录权限。默认的 `Data/`、`Models/`、`Output/` 目录保存本地设置、模型和生成数据；若修改了目录配置，也请保留实际使用的目录。

## 来源与许可

本项目是 SwarmUI 的独立二次开发项目，**不是 SwarmUI 官方发行版**。感谢原作者 Alex “mcmonkey” Goodwin 及所有贡献者。项目沿用 [MIT License](LICENSE.txt)，并保留[上游版权与第三方组件声明](THIRD_PARTY_NOTICES.md)。模型、工作流和其他依赖须遵守各自许可。
