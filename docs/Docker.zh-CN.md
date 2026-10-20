# 使用 Docker 运行 SwarmUI

> **语言：** [English 原文](Docker.md) · 简体中文

本文介绍在 Docker 容器中运行 SwarmUI 的原因、安装方式及注意事项。主要面向进阶用户；如果你从未使用过 Linux 命令行，建议先熟悉基本操作。

## 目录

- [为什么使用 Docker](#为什么使用-docker)
- [安装方式](#安装方式)
  - [标准隔离式安装](#标准隔离式安装)
  - [开放透传式安装](#开放透传式安装)
  - [自定义配置](#自定义配置)
  - [如何选择](#如何选择)
- [各平台安装](#各平台安装)
  - [Linux](#linux)
  - [Windows](#windows)
  - [macOS](#macos)
- [Docker Compose](#docker-compose)
- [高级用法与排错](#高级用法与排错)

## 为什么使用 Docker

**Docker 是什么？** [Docker](https://www.docker.com/) 是一种容器化工具，可将软件放在受限制的运行环境中，并与主机上的其他软件和文件系统隔离。

**实际好处是什么？** 主要是缩小安全事件的影响范围。如果 SwarmUI 内部发生意外，容器可以限制其接触范围，让你更容易关闭实例，并降低其访问容器外内容的机会。

> Docker 不是绝对安全保证。容器能提高隔离性，但针对性攻击仍可能通过网络或其他漏洞影响主机。

**这是否说明 SwarmUI 本身不安全？** 不能这样理解。SwarmUI 依赖 ComfyUI 后端，后端又依赖 Python 与众多 Python 包；用户还可能安装第三方自定义节点和扩展。依赖链中的任一项目都可能发生供应链攻击——即你信任的程序依赖了另一个可能被入侵的组件。此类事件并不常见，但并非不可能。因此，即使在 Docker 中运行，也应优先安装来自可信开发者的扩展和自定义节点。

更多安全背景见 [Docker Engine 安全文档](https://docs.docker.com/engine/security/)。

Docker 还便于管理进程、隔离依赖、快速销毁并重建实例。在某些 Windows 环境中，使用 Linux 容器还可能获得更稳定的后端运行体验；在 Linux 上，容器也可以减少与系统全局 Python 包之间的冲突。

替代方案包括其他容器化工具，或使用一台独立机器运行 AI 工具、避免与重要的个人数据混用。在 Windows 上，也可以研究 [Sandboxie Plus](https://sandboxie-plus.com/) 等沙箱工具。

## 安装方式

### 标准隔离式安装

标准容器安装会在构建阶段预编译 SwarmUI，并为 `Data` 与 `dlbackend` 创建独立的持久化卷，同时直接映射 `Models` 和 `Output` 目录。

这种方式便于从一个 SwarmUI 源码目录运行多个独立实例，但直接访问容器内部文件会稍微麻烦一些。

### 开放透传式安装

开放透传容器会直接在原目录中运行 SwarmUI，只是将进程放进容器。这种方式限制相对少，可能更适合个人使用，但隔离性不如标准方式严格。

除非你清楚正在做什么，否则不建议把已有安装直接透传进容器。主机和容器不应共同管理同一个 Python 环境目录，否则很容易导致环境损坏。

文件权限也可能出现差异：容器可能把文件视为 root 所有。如果要从主机侧访问，可能需要在 SwarmUI 目录中修复所有权，例如使用 `sudo chown yournamehere:yournamehere -R ./`。这一般不会影响容器内使用，但应确认命令执行目录正确。

**Windows 上非常不推荐开放透传方式**，因为 Linux 容器与 Windows 主机对同一目录的交互可能导致异常；使用风险由操作者自行承担。

### 自定义配置

若需映射其他模型目录、转发额外端口等，可复制 `launchtools/launch-standard-docker.sh` 或 `launchtools/launch-open-docker.sh`，将副本命名为 `custom-launch-docker.sh` 并修改副本。不要直接编辑原始脚本，以免与 Git 更新冲突。

通常不必修改 Dockerfile。标准版和开放版的主要区别是：标准版会把源文件复制到容器中，开放版则直接透传现有文件。

如果将 Models 目录以只读方式挂载（例如末尾添加 `:ro`），请在服务器配置中关闭 **Model Metadata Per Folder**。只读挂载下无法编辑模型元数据。

### 如何选择

如果没有特殊需求，建议选择 **Standard（标准隔离式）**。如果熟悉 Docker、明确需要透传行为，可以选择 Open；如果对安全边界有更高要求，也可以基于项目提供的 Docker 文件自行维护容器。

## 各平台安装

不同操作系统的步骤相似，但前置依赖有所不同。你需要会使用基本命令行；Docker 还不是完全即装即用的软件。

### Linux

1. 按照 [Docker Engine Linux 安装指南](https://docs.docker.com/engine/install/) 安装 Docker Engine。一般不需要 Docker Desktop。
2. 按照 [Linux 安装后步骤](https://docs.docker.com/engine/install/linux-postinstall/) 配置普通用户使用 Docker。
3. 可选：阅读 [Rootless 模式说明](https://docs.docker.com/engine/security/rootless/)。上游文档提示，部分 rootless 问题尚未解决；使用前请确认当前 Docker 版本的状态，并理解相关安全权衡。
4. 安装 [NVIDIA Container Toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/latest/install-guide.html)。
5. 安装 Git，然后下载 SwarmUI：

   ```bash
   git clone https://github.com/mcmonkeyprojects/SwarmUI
   cd SwarmUI
   ```

6. 运行以下任意一个脚本，不要传入 CLI 参数：

   ```bash
   ./launchtools/launch-standard-docker.sh
   # 或
   ./launchtools/launch-open-docker.sh
   ```

7. 浏览器访问 [http://localhost:7801](http://localhost:7801)。

### Windows

1. 安装 [Docker Desktop for Windows](https://docs.docker.com/desktop/setup/install/windows-install/)。通常需要启用 BIOS 虚拟化和 Windows WSL2；若没有使用过 WSL2，安装配置可能需要额外时间。
2. 从 [Git 官网](https://git-scm.com/download/win) 安装 Git。
3. 打开终端并切换到准备放置 SwarmUI 的目录，执行：

   ```powershell
   git clone https://github.com/mcmonkeyprojects/SwarmUI
   ```

4. 打开 `launchtools` 文件夹，双击 `windows-standard-docker.bat` 或 `windows-open-docker.bat`。
5. 浏览器访问 [http://localhost:7801](http://localhost:7801)。

### macOS

macOS 方案在原文中尚未经过充分测试，预计与 Linux 类似；请以 Docker 官方说明为准。

1. 安装 [Docker Desktop for Mac](https://docs.docker.com/desktop/setup/install/mac-install/)。
2. 安装 Git。
3. 克隆并进入仓库：

   ```bash
   git clone https://github.com/mcmonkeyprojects/SwarmUI
   cd SwarmUI
   ```

4. 运行 `./launchtools/launch-standard-docker.sh` 或 `./launchtools/launch-open-docker.sh`，不要传入 CLI 参数。
5. 浏览器访问 [http://localhost:7801](http://localhost:7801)。

## Docker Compose

仓库包含一个等价于标准安装方式的 Compose 示例。

1. 将 `launchtools/example-docker-compose.yml` 复制到 SwarmUI 根目录，并命名为 `docker-compose.yml`；如需挂载其他磁盘，可编辑副本。
2. 在 Linux 环境执行：

   ```bash
   HOST_UID="$(id -u)" HOST_GID="$(id -g)" docker compose up
   ```

3. 用完后可运行 `docker compose rm` 清理容器。

如果没有明确的 Compose 使用需求，建议优先使用项目自带的启动脚本。

## 高级用法与排错

- 若需进入正在运行的容器，可执行 `docker exec -it swarmui bash -l`。
  - 安装 Python 包前，先进入 `/SwarmUI/dlbackend/ComfyUI`，然后使用 `./venv/bin/python -s -m pip install ...`。
- 容器内 SwarmUI 的主要目录为 `/SwarmUI`。
- AMD 或 Intel GPU 的容器配置需要对应的驱动和容器工具。请先查阅当前 GPU 厂商及容器运行时的说明。
- 标准容器会以当前用户身份运行。历史版本曾以 root 运行；旧脚本可能支持使用唯一参数 `fixch` 以 root 修复文件所有权。
- 对开放透传模式，必要时可在 SwarmUI 目录中执行 `sudo chown -R $UID:$UID ./` 修复文件权限；因为文件系统直接透传，务必确认当前目录。