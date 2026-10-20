# EcomCraft Studio

> **语言：** 简体中文 · [English](README.en.md)

**面向电商设计团队的内网 AI 创作工作台，基于 [SwarmUI](https://github.com/mcmonkeyprojects/SwarmUI) 进行二次开发。**

EcomCraft 复用 SwarmUI 的图像与视频生成能力，并逐步扩展团队需要的统一入口、节点管理、任务追踪、素材共享和数据处理策略。

> **项目状态：** 仍在积极开发中。下方 `✓` 表示功能已有代码实现，不代表已通过完整构建、端到端或生产环境验收；未完成的规划不会被描述为可用功能。

## 目录

- [项目定位](#项目定位)
- [当前能力](#当前能力)
- [路线图](#路线图)
- [技术结构](#技术结构)
- [开始使用](#开始使用)
- [文档](#文档)
- [隐私与安全原则](#隐私与安全原则)
- [来源与许可](#来源与许可)

## 项目定位

EcomCraft 是 SwarmUI 的二次开发项目，而不是 SwarmUI 官方发行版。当前以 SwarmUI 扩展的形式集成，尽可能复用原生生成引擎和后端管理能力，并在其上建设面向团队协作的业务功能。

设计目标：

- **统一入口：** 员工通过一个内网地址访问创作工作台。
- **算力可见：** 统一查看已登记后端的状态、启用情况与容量信号。
- **任务可追溯：** 逐步关联提交者、处理策略、执行节点、错误和生成结果。
- **处理范围明确：** 本地与云端服务应有清晰边界；未经明确授权不得自动上传素材或故障转云端。
- **共享与隐私分离：** 私有输入与结果不应因团队共享功能而自动公开。
- **升级可控：** 上游同步、测试、发布和回滚需要明确流程。

## 当前能力

### 已实现

- [✓] 以 `src/Extensions/EcomCraft` 扩展接入 SwarmUI。
- [✓] 提供图片与视频创作工作台，复用 SwarmUI 的生成流程。
- [✓] 工作台请求限定本机自启动 ComfyUI，并在服务端校验后端、模型、权限和参数；不会自动回退到远程或云端。
- [✓] 支持管理员发布 ComfyUI 工作流供客户机在网页工作台选择；动态参数经过服务端白名单校验，并提供 `EcomCraftStudioCatalog` 与 `EcomCraftStudioGenerate` API。
- [✓] 内置 MagicPrompt 提示词优化功能，可配置本地或云端提示词模型。
- [✓] Windows 安装流程可创建 EcomCraft Studio 桌面启动入口，并支持应用窗口模式。
- [✓] 提供中英文项目 README，以及可从英文原文切换的多份中文使用和开发文档。

### 尚在规划

- [ ] 可持久化的团队节点登记、健康检查、能力审批与智能路由。
- [ ] 持久化任务队列、状态流转、取消、重试及实际执行端记录。
- [ ] 输入、模型、工作流版本与结果之间的完整追溯关系。
- [ ] 按任务授权的云端服务接入、上传记录和统一出网策略。
- [ ] 团队公共素材库、私有素材的访问控制和发布/下架流程。
- [ ] 上游集成、回归测试、发布审批、备份恢复和业务数据回滚机制。

## 技术结构

| 组成 | 位置 | 用途 |
|---|---|---|
| EcomCraft 扩展 | `src/Extensions/EcomCraft/` | 工作台与工作流业务逻辑 |
| 工作台页面 | `src/Extensions/EcomCraft/Studio/` | 浏览器端创作界面 |
| SwarmUI 核心 | `src/` | 会话、权限、API、模型与生成能力 |
| 后端与工作流 | SwarmUI / ComfyUI | 实际执行图像与视频生成 |

EcomCraft 不是独立生成引擎。修改时应优先通过扩展和公开 API 接入，只有确有需要时才调整 SwarmUI 核心代码。

## 开始使用

1. 先按 [SwarmUI 上游安装说明](https://github.com/mcmonkeyprojects/SwarmUI#installing-on-windows) 安装并启动兼容的 SwarmUI 环境。
2. 确认已配置可用的 ComfyUI 后端；当前 EcomCraft 工作台只允许使用本机自启动 ComfyUI，不会自动回退到远程或云端后端。
3. 打开 SwarmUI，进入 EcomCraft 创作工作台。提示词优化配置请参考[《EcomCraft 提示词优化指南》](docs/EcomCraft%20Prompt%20Enhancement.md)。

如需理解 SwarmUI 的基础操作，可阅读[中文基础使用指南](docs/Basic%20Usage.zh-CN.md)。

## 文档

- [中文文档总览](docs/README.zh-CN.md) · [English documentation index](docs/README.md)
- [EcomCraft 提示词优化与部署](docs/EcomCraft%20Prompt%20Enhancement.md)
- [EcomCraft ComfyUI 工作流调用（中文）](docs/EcomCraft%20ComfyUI%20工作流.zh-CN.md)
- [SwarmUI 基础使用（中文）](docs/Basic%20Usage.zh-CN.md)
- [高级使用（中文）](docs/Advanced%20Usage.zh-CN.md)
- [常见问题排查（中文）](docs/Troubleshooting.zh-CN.md)
- [API 使用说明（中文）](docs/API.zh-CN.md)
- [命令行参数（中文）](docs/Command%20Line%20Arguments.zh-CN.md)

## 隐私与安全原则

- 本地生成和提示词优化是不同的数据处理路径。选择云端提示词模型时，输入文本会发送到所配置的服务商；请先确认数据使用政策。
- 当前工作台的生成请求限定在本机自启动 ComfyUI 后端；云端生成服务与逐任务授权机制尚未完成。
- 在将工作台开放给局域网多人使用前，应审查账号权限、网络暴露面、后端访问和素材目录权限。

## 来源与许可

本项目是 SwarmUI 的独立二次开发项目，**非 SwarmUI 官方版本**。感谢原作者 Alex “mcmonkey” Goodwin 及所有贡献者。

项目沿用 [MIT License](LICENSE.txt)，并保留[上游版权与第三方组件声明](THIRD_PARTY_NOTICES.md)。模型、工作流、扩展和其他依赖的使用仍须遵守各自许可。
