# SwarmUI 文档总览

> **语言：** [English 原文](README.md) · 简体中文

本目录包含 SwarmUI 的使用、配置、API、扩展和功能说明。已翻译文档与英文原文并行维护；阅读配有中文译本的英文文档时，可通过文首的“简体中文”链接切换语言。模型支持、视频模型支持和少见模型支持目前提供中文导读，完整型号表、示例和细节以英文原文为准；自动生成的 API 路由参考也暂未翻译。

## 入门与日常使用

- [基础使用指南（中文）](Basic%20Usage.zh-CN.md) · [English](Basic%20Usage.md)：安装、配置、首次生成、参数与常用工具。
- [高级使用指南（中文）](Advanced%20Usage.zh-CN.md) · [English](Advanced%20Usage.md)：局域网访问、远程访问、自定义 ComfyUI 工作流、多模型目录及性能优化。
- [用户设置指南（中文）](User%20Settings.zh-CN.md) · [English](User%20Settings.md)：用户设置和输出路径命名模板。
- [常见问题排查（中文）](Troubleshooting.zh-CN.md) · [English](Troubleshooting.md)：安装失败、依赖错误、后端异常与重新安装。

## 部署、隐私与管理

- [Docker 部署（中文）](Docker.zh-CN.md) · [English](Docker.md)
- [隐私说明（中文）](Privacy.zh-CN.md) · [English](Privacy.md)
- [扩展使用（中文）](Extensions.zh-CN.md) · [English](Extensions.md)
- [命令行参数（中文）](Command%20Line%20Arguments.zh-CN.md) · [English](Command%20Line%20Arguments.md)
- [AMD Radeon GPU（中文）](AMD%20Radeon%20GPUs.zh-CN.md) · [English](AMD%20Radeon%20GPUs.md)
- [使用多张 GPU（中文）](Using%20More%20GPUs.zh-CN.md) · [English](Using%20More%20GPUs.md)
- [共享 SwarmUI 实例（中文）](Sharing%20Your%20Swarm.zh-CN.md) · [English](Sharing%20Your%20Swarm.md)

## 开发与集成

- [HTTP 与 WebSocket API（中文）](API.zh-CN.md) · [English](API.md)
- [扩展开发指南（中文）](Making%20Extensions.zh-CN.md) · [English](Making%20Extensions.md)
- [图像元数据格式（中文）](Image%20Metadata%20Format.zh-CN.md) · [English](Image%20Metadata%20Format.md)
- [EcomCraft 提示词优化与部署指南（中文）](EcomCraft%20提示词优化指南.zh-CN.md)（[English](EcomCraft%20Prompt%20Enhancement.md)）：后端配置、输入 Key 后直接拉取模型、保存流程、生成超时设置及看图描述（视觉模型）开关。
- [EcomCraft ComfyUI 工作流调用](EcomCraft%20ComfyUI%20工作流.zh-CN.md)（[English](EcomCraft%20ComfyUI%20Workflows.md)）：本机安装与局域网执行服务器的工作流自动发现、局域网 ComfyUI 接入、执行服务器调度、工作流与子图蓝图导入、重复项合并与依赖过滤、批量出图种子递增、Windows AMD 连续生成排查、共享模型目录接入、模型和参数默认值读取、修改回传及客户端调用。
- [EcomCraft 局域网部署指南](EcomCraft%20局域网部署指南.zh-CN.md)（[English](EcomCraft%20LAN%20Deployment.md)）：多人使用时开启账号登录、按角色设置排队上限、执行服务器防火墙、共享存储上作品的可见性，以及 `http://` 局域网地址下的浏览器限制。
- [EcomCraft 升级与数据说明](EcomCraft%20Upgrade.zh-CN.md)（[English](EcomCraft%20Upgrade.md)）：升级时哪些数据会保留，系统数据备份与恢复的操作步骤。
- [EcomCraft 工作台界面设计](EcomCraft%20工作台界面设计.zh-CN.md)（[English](EcomCraft%20Workspace%20UI%20Design.md)）：开发设计文档，涵盖配色与字体、各页面和按钮的功能清单、本机预览方法。
- [EcomCraft 架构与路线图（中文）](EcomCraft%20Architecture.zh-CN.md) · [English](EcomCraft%20Architecture.md)：开发规划文档，涵盖产品定位、部署拓扑、分层架构、一键套图数据流、设计约定、MVP 范围和后续功能；功能和任务进度见 [EcomCraft 开发总表](EcomCraft%20开发总表.xlsx)。
- [EcomCraft 局域网多服务器平台需求（中文）](EcomCraft%20局域网多服务器平台需求.zh-CN.md) · [English](EcomCraft%20LAN%20Multi-Server%20Platform%20Requirements.md)：开发需求文档，涵盖多服务器接入、工作流审核与快照、负载调度、多人使用、画廊共享、作品存储；云端对接不做（2026-10-31 起，见 D11、R10）。
- [EcomCraft 实施计划（中文）](EcomCraft%20实施计划.zh-CN.md) · [English](EcomCraft%20Implementation%20Plan.md)：新目标实施计划（2026-10-31）：参考图驱动的单入口、必须有显卡、Windows 和 Mac 优先；阶段 0–6 的任务详情（F74–F82、T81–T110、N11–N12）和 G1/G2 验收关卡。
- [EcomCraft 重构计划（中文）](EcomCraft%20重构计划.zh-CN.md) · [English](EcomCraft%20Refactoring%20Plan.md)：基础重构计划（2026-10-31）：与实施计划穿插执行的 R00–R24，含安全网（测试、CI）、缺陷修复（索引文件覆盖、备份路径、批次内存、raw_prompt）、后端拆分（StudioApi、BatchService）、前端拆分（studio.js、simple.js）、移除休眠云端执行方类型和检查点。
- [EcomCraft 回归清单（中文）](EcomCraft%20回归清单.zh-CN.md) · [English](EcomCraft%20Regression%20Checklist.md)：重构检查点 CP0–CP3 使用的 14 项手工回归流程。
- [EcomCraft 简易模式改版需求（中文）](EcomCraft%20简易模式改版需求.zh-CN.md) · [English](EcomCraft%20Simple%20Mode%20Redesign.md)：开发需求文档（F52–F56 已实现待验收，F57 代码完成、蓝图文件待提供；含面向继续开发代理的交接章节），涵盖上传四入口、产品类型两级选择、双语提示词与 AI 扩写、候选张数总上限、出图链路提速与标准工作流自动生成；附[交互原型](EcomCraft%20简易模式改版原型.html)。
- [EcomCraft 平台预设槽位设计（中文）](EcomCraft%20平台预设槽位设计.zh-CN.md) · [English](EcomCraft%20Platform%20Preset%20Slots.md)：开发设计文档（F58，已实现待验收）：按槽位定义各平台图片规格（尺寸比例、白底位置、主体占比、大小上限、禁用内容、AI 标识），套图按槽位组合模板；含 N06 规范核对清单。
- [EcomCraft 参考图需求（中文）](EcomCraft%20参考图需求.zh-CN.md) · [English](EcomCraft%20Reference%20Images.md)：开发需求文档（F59，P1 已实现待验收；2026-10-31 路线升级）：上传你想要的参考图，一张参考出一张图；路线升级为参考图 + 商品图直接输入编辑模型融合（路线 E，待 G1 实测，F74 / T83–T88，T62 被 T84 取代），旧“借鉴构图”保留为回退；高度相似边界与临时存储规则。
- [EcomCraft 界面与操作改版需求（中文）](EcomCraft%20界面改版需求.zh-CN.md) · [English](EcomCraft%20UI%20Redesign.md)：开发需求文档（F60–F69）：简易模式先选任务、导航与管理员菜单、专业模式向所有账号开放、手机端范围、收藏与个性化分析草案。
- [SwarmUI 设计动机（中文）](Motivations.zh-CN.md) · [English](Motivations.md)

## 模型与功能专题

- [模型支持列表（中文）](Model%20Support.zh-CN.md) · [English](Model%20Support.md)
- [视频模型支持（中文）](Video%20Model%20Support.zh-CN.md) · [English](Video%20Model%20Support.md)
- [音频模型支持（中文）](Audio%20Model%20Support.zh-CN.md) · [English](Audio%20Model%20Support.md)
- [较少见的模型支持（中文）](Obscure%20Model%20Support.zh-CN.md) · [English](Obscure%20Model%20Support.md)
- [为什么使用 SwarmUI（中文）](Why%20Use%20Swarm.zh-CN.md) · [English](Why%20Use%20Swarm.md)
- [功能专题索引（中文）](Features/README.zh-CN.md) · [English](Features/README.md)
- [高级提示词语法（中文）](Features/Prompt%20Syntax.zh-CN.md) · [English](Features/Prompt%20Syntax.md)

## 文档语言与维护说明

- 中文文档使用 `.zh-CN.md` 后缀，英文原文文件名保持不变。
- 命令、API 路由、配置键、参数名、模型 ID 和代码示例会保留原始拼写，避免复制后无法执行。
- 功能状态、硬件表现和第三方服务限制可能随上游变化；操作前应以当前版本和相关项目文档为准。
