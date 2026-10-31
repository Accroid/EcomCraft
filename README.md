# EcomCraft Studio

> **语言：** 简体中文 · [English](README.en.md)

EcomCraft Studio 是给电商团队用的**局域网商品图生产平台**，基于 [SwarmUI](https://github.com/mcmonkeyprojects/SwarmUI) 二次开发，仓库已包含 SwarmUI 服务器源码，**无需另装上游 SwarmUI**。项目仍在开发中，尚未完成生产环境验收；遇到问题请提交 [Issues](https://github.com/Accroid/EcomCraft/issues)。

- **运营和小店老板**：用简易模式三步出图，不碰提示词、模型和参数。
- **平面设计人员**：用专业模式调模型、采样器、LoRA，上传自己的 ComfyUI 工作流。
- **销售方式**：按软件销售，按账号数收费。

## 效果对比

<!-- 效果对比图位置：左为上传的商品原图，右为平台生成的成品图。图片稍后提供，先保留占位。 -->

| 上传原图 | 生成成品 |
|---|---|
| （图片待补） | （图片待补） |

演示视频：（稍后提供）

## 解决什么问题

| 痛点 | EcomCraft 的做法 |
|---|---|
| 不会写提示词、不懂设计 | 先选要做的事：**照参考图做**（上传你想要的参考图，按它的构图和光线出图）、做一张图、出一整套；一句话描述可由 AI 扩写成中英文提示词 |
| AI 把商品画变形、Logo 和文字走样 | **商品保真**：抠图放到画布上生成，出图后把商品的 Logo、文字和形状原样贴回，光线融入场景；卖点文字由程序按固定版式排版，不让 AI 写字 |
| 各平台尺寸、白底、大小上限都不一样 | 按平台**槽位**一键出整套、按规格导出；亚马逊白底主图走纯抠图，检查纯白并写入 AI 元数据 |
| 商品图不能外传 | 生成**只在本机或局域网**登记的 ComfyUI 上执行，不回退云端；看图模型默认用本机 Ollama |
| 团队多台显卡没人调度 | 自动分派到空闲的执行服务器，队列可看进度和预计剩余时间、可结束自己的任务 |
| 搭工作流门槛高 | 内置模板的工作流由系统按默认模型**自动生成**；设计师上传的工作流自己马上能用，审核后发布给全员 |
| 每次出图都要从头挑 | 商品库、品牌资料包（品牌色、Logo、参考图）、候选图收藏、“在专业模式里精修” |

完整的功能清单与进度见[架构与路线图](docs/EcomCraft%20Architecture.zh-CN.md)，接口与安全规则见[工作流调用文档](docs/EcomCraft%20ComfyUI%20工作流.zh-CN.md)。

## 快速开始

**安装要求**

- [Git](https://git-scm.com/downloads) 和 [.NET SDK](https://dotnet.microsoft.com/download)（建议 .NET 10 SDK，项目目标为 .NET 8）。首次构建需要访问 NuGet。
- 生成图片需要显卡、模型和对应的 ComfyUI 节点；只启动网页服务不需要显卡。Linux 自动安装 ComfyUI 需先装 Python 3.10–3.12 及 `venv`；Windows 安装向导自带 Python。

**配置要求（必须有显卡，不做无显卡和云端出图）**

| 系统 | 最低配置 | 推荐 |
|---|---|---|
| Windows | NVIDIA 显卡 8GB 显存；AMD 仅限 ROCm 支持列表内 | NVIDIA 16GB 以上 |
| macOS | Apple 芯片，24GB 统一内存；Intel 芯片 Mac 不支持 | 32GB 以上 |

重点支持 Windows 和 Mac；Linux 启动脚本保留但暂不验证。达不到门槛时程序会明确告知不支持；数值为当前默认，实测后调整。

**获取源码并启动**

```bash
git clone https://github.com/Accroid/EcomCraft.git
cd EcomCraft
```

| 系统 | 常规启动 | 开发启动（强制重建） |
|---|---|---|
| Windows（PowerShell） | `.\launch-windows.bat` | `.\launch-windows-dev.ps1` |
| Linux | `./launch-linux.sh` | `./launch-linux-dev.sh` |
| macOS | `./launch-macos.sh` | — |

首次启动会构建服务器，需要几分钟。macOS 只提供服务器启动，本机生成需自行准备 ComfyUI。

**首次配置**

1. 浏览器打开 `http://127.0.0.1:7801`，按首次设置安装 **ComfyUI Self-Starting** 后端并下载模型（也可跳过，之后再配）。
2. 打开 `http://127.0.0.1:7801/EcomCraft` 进入工作台；管理员首次进入会看到设置向导（语言、存储、执行服务器、模型、常用平台）。
3. 局域网的其他显卡电脑以 `--listen` 启动 ComfyUI，再在原生引擎 **Server → Backends** 以 **ComfyUI API By URL** 登记，见[局域网部署指南](docs/EcomCraft%20局域网部署指南.zh-CN.md)。
4. “照参考图做”需要看图模型：管理员可在工作台“提示词优化设置”里点“一键安装本地模型”选择档位直接安装（或运行 `launchtools/install-vision-model-windows.ps1`，Linux 为 `launchtools/install-vision-model-linux.sh`），见[提示词优化指南](docs/EcomCraft%20Prompt%20Enhancement.md)。

## 数据与安全

- 客户端只提交模板 / 工作流 ID 和服务端发布的参数白名单；工作流 JSON 只能由账号在专业模式上传，并经结构校验和禁用节点检查。
- 选择云端提示词模型时，输入文字会发给所配置的服务商；这和只发往已登记执行服务器的生成请求是两条路径。
- 局域网 ComfyUI 没有登录验证，应用防火墙只允许平台服务器访问其端口；多人使用前须开启账号登录并配置权限。
- `Data/`、`Models/`、`Output/` 保存设置、模型和作品；升级与备份见[升级与数据说明](docs/EcomCraft%20Upgrade.zh-CN.md)。

## 文档

- [中文文档总览](docs/README.zh-CN.md) · [基础使用](docs/Basic%20Usage.zh-CN.md) · [常见问题排查](docs/Troubleshooting.zh-CN.md)
- [工作流调用与接口](docs/EcomCraft%20ComfyUI%20工作流.zh-CN.md) · [局域网部署指南](docs/EcomCraft%20局域网部署指南.zh-CN.md) · [提示词优化与看图模型](docs/EcomCraft%20Prompt%20Enhancement.md) · [升级与数据说明](docs/EcomCraft%20Upgrade.zh-CN.md)
- 开发规划：[架构与路线图](docs/EcomCraft%20Architecture.zh-CN.md) · [实施计划](docs/EcomCraft%20实施计划.zh-CN.md) · [重构计划](docs/EcomCraft%20重构计划.zh-CN.md) · [界面与操作改版需求](docs/EcomCraft%20界面改版需求.zh-CN.md) · [简易模式改版需求](docs/EcomCraft%20简易模式改版需求.zh-CN.md) · [局域网多服务器平台需求](docs/EcomCraft%20局域网多服务器平台需求.zh-CN.md)

## 联系与部署服务

- 问题反馈与功能建议：[GitHub Issues](https://github.com/Accroid/EcomCraft/issues)。
- 部署服务：单机或多机局域网远程部署（安装、登记执行服务器、共享存储与团队培训），联系方式：（稍后补充）。

## 来源与许可

本项目是 SwarmUI 的独立二次开发项目，**不是 SwarmUI 官方发行版**。感谢原作者 Alex “mcmonkey” Goodwin 及所有贡献者。项目沿用 [MIT License](LICENSE.txt)，并保留[上游版权与第三方组件声明](THIRD_PARTY_NOTICES.md)。模型、工作流和其他依赖须遵守各自许可。
