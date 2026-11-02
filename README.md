# EcomCraft Studio

> **语言：** 简体中文 · [English](README.en.md)

EcomCraft Studio 是给电商团队用的**局域网商品图生产平台**，基于 [SwarmUI](https://github.com/mcmonkeyprojects/SwarmUI) 二次开发，仓库已包含 SwarmUI 服务器源码，**无需另装上游 SwarmUI**。项目仍在开发中，尚未完成生产环境验收；遇到问题请提交 [Issues](https://github.com/Accroid/EcomCraft/issues)。

- **运营和小店老板**：用简易模式三步出图，不碰提示词、模型和参数。
- **平面设计人员**：用专业模式调模型、采样器、LoRA，上传自己的 ComfyUI 工作流。

## 效果对比

<!-- 效果对比图位置：左为上传的商品原图，右为平台生成的成品图。图片稍后提供，先保留占位。 -->

| 上传原图 | 生成成品 |
|---|---|
| （图片待补） | （图片待补） |


## 优势：与本地 ComfyUI、云端生图的对比

出商品图常见三条路：自己搭本地 ComfyUI、用云端生图服务、找外包拍图。EcomCraft 的生成引擎仍然是 ComfyUI，但由平台在本机或局域网登记的执行服务器上运行——**运营和老板不需要自己搭，生成也不经过任何云端**。三者对比：

| 对比项 | EcomCraft | 自己搭 ComfyUI | 云端生图服务 |
|---|---|---|---|
| **商品图隐私** | 生成只在本机和局域网登记的执行服务器上进行，不回退云端；看图描述默认用本机 Ollama（管理员改用云端视觉服务时图片才会离开本机） | 图片不出机器 | 商品图要上传到服务商 |
| **上手段槛** | 三步出图：传商品 → 选平台和参考图 → 挑图导出；不写提示词、不碰参数，首次使用有设置向导和硬件检测 | 要装环境、下模型、懂节点和工作流 | 上手快，但要写提示词、反复抽卡 |
| **手机照片直接用** | 手机拍照上传即可：自动压缩、转正、抠图，并清除 EXIF 等拍摄元数据 | 要自己找工具压缩、转正、抠图 | 多数可直接上传，但拍摄元数据随原图一起交给服务商 |
| **商品保真** | 抠图放上画布，出图后把商品的 Logo、文字和形状**原样贴回**；卖点文字由程序按固定版式排版，不让 AI 写字 | 要自己搭抠图、贴回、修边的完整流程，商品容易走样 | 直接重画商品，Logo 和文字经常变形、乱码 |
| **成套出图** | 按国内外主流平台（亚马逊、淘宝/天猫、京东、TikTok Shop 等）的槽位一键出整套，按各平台规格导出；亚马逊白底主图自动检查纯白并写入 AI 元数据 | 每张手动生成、手动改尺寸和格式 | 一般单张生成，尺寸、格式和数量受服务限制 |
| **团队协作** | 账号登录、按人限流，任务自动分派到空闲显卡，队列可看进度和预计剩余时间 | 单机单用户，没有账号、排队和权限，多人共用一台很麻烦 | 每人各开各的账号，图片散落在各账号里 |
| **工作流** | 内置模板的工作流由系统按模型**自动生成**；设计师上传的工作流自己马上能用，审核后全员共享 | 每人自己搭、自己存文件，节点依赖和版本没人统一管 | 不支持自定义工作流 |
| **找得回作品** | 商品库、候选图收藏、项目归档、“用相同参数重做” | 输出文件夹自己整理 | 图片留在服务商账号里，迁移靠手动下载 |
| **成本** | 一次显卡硬件投入，出图不限量、不按张付费 | 同样要硬件，还要投入搭建和维护的时间 | 无需硬件；按张或订阅付费，批量出图时成本持续增长 |
| **硬件要求** | 需要显卡（Windows 最低 NVIDIA 8GB 显存；Mac 需 Apple 芯片 24GB 统一内存） | 同样需要显卡，还要自己解决环境和依赖 | 无需显卡 |

**怎么选**：已有显卡、在意商品保密和成套出图的团队，选 EcomCraft；只有一个人的技术用户、愿意自己折腾，ComfyUI 本身就够用；没有显卡、且商品图可以出公司的，云端服务也能起步。

“照参考图做”是简易模式的唯一入口：上传商品图和一张你想要的参考图，平台借鉴参考图的构图和光线出图，不用写提示词；商品像素始终来自你的原图，出图后由平台贴回，不被模型重画。

接口与安全规则见[工作流调用文档](docs/EcomCraft%20ComfyUI%20工作流.zh-CN.md)。

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
4. “照参考图做”需要看图模型：管理员可在工作台“提示词优化设置”里点“一键安装本地模型”选择档位直接安装（或运行 `launchtools/install-vision-model-windows.ps1`，Linux 为 `launchtools/install-vision-model-linux.sh`），见[提示词优化指南](docs/EcomCraft%20提示词优化指南.zh-CN.md)。

## 数据与安全

- 客户端只提交模板 / 工作流 ID 和服务端发布的参数白名单；工作流 JSON 只能由账号在专业模式上传，并经结构校验和禁用节点检查。
- 选择云端提示词模型时，输入文字会发给所配置的服务商；这和只发往已登记执行服务器的生成请求是两条路径。
- 局域网 ComfyUI 没有登录验证，应用防火墙只允许平台服务器访问其端口；多人使用前须开启账号登录并配置权限。
- `Data/`、`Models/`、`Output/` 保存设置、模型和作品；升级与备份见[升级与数据说明](docs/EcomCraft%20Upgrade.zh-CN.md)。

## 文档

- [中文文档总览](docs/README.zh-CN.md) · [基础使用](docs/Basic%20Usage.zh-CN.md) · [常见问题排查](docs/Troubleshooting.zh-CN.md)
- [工作流调用与接口](docs/EcomCraft%20ComfyUI%20工作流.zh-CN.md) · [局域网部署指南](docs/EcomCraft%20局域网部署指南.zh-CN.md) · [提示词优化与看图模型](docs/EcomCraft%20提示词优化指南.zh-CN.md) · [升级与数据说明](docs/EcomCraft%20Upgrade.zh-CN.md)

## 联系与部署服务

- 问题反馈与功能建议：[GitHub Issues](https://github.com/Accroid/EcomCraft/issues)。
- 部署服务：单机或多机局域网远程部署（安装、登记执行服务器、共享存储与团队培训），联系方式：（稍后补充）。

## 来源与许可

本项目是 SwarmUI 的独立二次开发项目，**不是 SwarmUI 官方发行版**。感谢原作者 Alex “mcmonkey” Goodwin 及所有贡献者。项目沿用 [MIT License](LICENSE.txt)，并保留[上游版权与第三方组件声明](THIRD_PARTY_NOTICES.md)。模型、工作流和其他依赖须遵守各自许可。
