# SwarmUI 常见问题排查

> **语言：** [English 原文](Troubleshooting.md) · 简体中文

排查问题时，可以先尝试重启 SwarmUI。某些临时状态或后端问题会在重启后恢复。

如果问题仍然存在，可以到 [GitHub Issues](https://github.com/mcmonkeyprojects/SwarmUI/issues) 搜索/提交，或在 [SwarmUI Discord](https://discord.gg/q2y38cqjNw) 的 `#help-forum` 频道求助。发帖前先搜索错误原文，常见问题可能已有解决办法。

## 安装问题

### NuGet 连接错误

如果日志中出现连接 `https://api.nuget.org/v3/index.json` 失败，例如 `Unable to load the service index for source https://api.nuget.org/v3/index.json`，通常表示首次启动或更新时无法访问构建依赖服务。

可以按顺序检查：

1. 确认网络可用，且防火墙、VPN 或代理没有阻断相关连接；可暂时排除网络拦截后重试。
2. 考虑临时网络故障，稍后再尝试。
3. 重启电脑后重试。
4. 如下文所述清理 NuGet 缓存。

如果报错提示找不到 `Hardware` 类型（该类型来自 NuGet 包），或提示 `Image<>` 不存在于 `SixLabors.ImageSharp` 命名空间，可能是 NuGet 缓存损坏。此情况不常见，但可以尝试重置缓存。

#### 重置 NuGet 缓存

- **Windows：** 在文件资源管理器中分别打开 `%appdata%/NuGet` 和 `%localappdata%/NuGet` 并删除其中缓存；再打开 `%userprofile%/.nuget`，删除 `packages` 目录。
- **Linux/macOS：** 删除当前用户的 `~/.nuget` 目录（例如执行 `rm -rf ~/.nuget`）。

删除前请确认路径正确，避免误删其他文件。

## Windows 上的 AMD 显卡

AMD 的 ROCm 对 Windows 的支持仍受到平台和显卡型号限制，某些配置下的性能和兼容性可能不如 Linux。部分较旧的 AMD GPU 用户报告，设置环境变量 `HSA_OVERRIDE_GFX_VERSION=10.3.0` 可以绕过部分驱动兼容问题，但不保证适用于所有设备。

## 常见错误信息

### `AssertionError: Torch not compiled with CUDA enabled`

这通常表示 ComfyUI 后端的 Python 依赖环境不正确。自定义节点包或 Python 包可能修改了依赖，尤其是在频繁通过 Comfy Manager 安装节点包后。

一种常见修复方式是按当前环境要求重新安装 PyTorch。在原文编写时，可尝试在后端 Python 环境中执行：

```powershell
python -s -m pip install torch torchvision -U --index-url https://download.pytorch.org/whl/cu128
```

这里的 `cu128` 代表 CUDA 版本，可能随时间变化；执行前请到 [PyTorch 官方安装页面](https://pytorch.org/get-started/locally/) 确认当前适用的命令。有关正确的 pip 环境和命令，见[通过 pip 安装依赖](#通过-pip-安装依赖)。

### `fatal: detected dubious ownership in repository at ...`

这是 Git 的目录所有权安全检查报错，常见于将 SwarmUI 安装在不合适的磁盘上，例如某些外置磁盘。

建议安装到标准系统磁盘。Windows 上通常使用 NTFS 格式的本地磁盘，例如 C 盘或其他内部硬盘分区。

## 遇到错误但不知道如何处理

1. **先阅读错误消息。** SwarmUI 很多错误都会用直白文本说明问题和可能的解决方法。
2. **搜索完整错误文本。** 在 [GitHub Issues](https://github.com/mcmonkeyprojects/SwarmUI/issues) 与 [Discord](https://discord.gg/q2y38cqjNw) 搜索。
3. **查看详细日志。** 进入 `Server → Logs`，必要时把 `ViewType` 切换为 `Debug`，查看完整上下文。
4. **提交问题报告。** 在 Logs 页面使用 **Pastebin** 按钮生成完整日志链接，并说明你当时执行了什么操作、使用了哪些参数、错误发生在生成前还是生成中；附上必要的 UI 截图通常也有帮助。

## 通过 pip 安装依赖

只有在明确知道需要哪个 Python 包时才手动安装依赖。随意更改后端 Python 环境可能导致后端损坏。

### Windows

1. 在 `(Swarm 安装目录)\dlbackend\comfy` 中打开命令行。
2. 使用嵌入式 Python 安装目标包，例如：

```powershell
python_embeded\python.exe -s -m pip install transformers -U
```

### Linux/macOS

1. 在 `(Swarm 安装目录)/dlbackend/ComfyUI/` 中打开命令行。
2. 激活虚拟环境并安装包：

```bash
source venv/bin/activate
python -s -m pip install transformers -U
```

Docker 环境应明确使用容器内的 Python，例如 `./venv/bin/python -s -m pip install ...`。

这里的 `-s` 很重要：它让 Python 将包安装到当前环境。直接使用系统的 `pip install` 可能意外引用全局 Python 包，从而导致依赖冲突。

## 重新安装 SwarmUI

### 推荐：重新安装到新目录

1. 关闭 SwarmUI。
2. 将旧的 Swarm 文件夹重命名为 `Old_SwarmUI`。
3. 在新目录重新运行安装程序，直到 Generate 页面可以正常使用。
4. 关闭新实例后，从旧目录中复制需要的数据，通常包括 `Data`、`Models` 和 `Output`。
5. 再次启动新实例并检查功能。

### 原目录内重新安装（高级操作）

1. 关闭 SwarmUI。
2. 将 `SwarmUI/dlbackend` 移到其他位置备份。它可能包含 ComfyUI、工作流和历史输出；建议先移动而不是直接删除。
3. 移走或清理 `SwarmUI/src/BuiltinExtensions/ComfyUIBackend/DLNodes` 中的自动下载内容。
4. 在 `SwarmUI/Data` 中删除 `Backends.fds`。
5. 使用文本编辑器打开同一目录中的 `Settings.fds`，把 `IsInstalled: true` 改成 `IsInstalled: false` 并保存。
6. 再次启动 SwarmUI，安装界面应重新出现。

重装后不要重复导致环境损坏的操作。频繁使用 Comfy Manager 安装节点或改动 Python 依赖，可能造成后端依赖冲突；必要时请谨慎管理节点包。

## 模型问题

### 新支持的模型无法正常工作

- 先确认 SwarmUI 已更新到包含该模型支持的版本。
- 如果模型是在更新前下载的，可尝试进入 Utilities 并执行 **Reset Model Metadata**，让 SwarmUI 重新扫描模型元数据。
- 在 Generate 页面的 Models 标签中检查模型的 `Type`。如识别错误，点击模型的 `☰` 菜单，进入 **Edit Metadata** 并修正 Architecture。

## 其他问题

如果遇到尚未覆盖的问题，可以在 [SwarmUI Discord](https://discord.gg/q2y38cqjNw) 反馈，并尽可能提供错误原文、日志和复现步骤。