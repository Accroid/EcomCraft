---
name: ecomcraft-docs-sync
description: 修改 EcomCraft 功能、API、配置项或安全边界后同步文档时使用，也用于新增或修改 docs/ 下的文档；说明每类改动要更新哪些文件、中英文文档如何对应。
---

# 同步 EcomCraft 文档

功能改了但文档没改，是本项目文档过时的主要原因。每次改功能，都按下面的对照表检查一遍。

## 适用场景

- 新增或修改 EcomCraft 功能、接口、配置项、权限或安全边界
- 新增、删除或改名 `docs/` 下的文档
- 修改需求或设计（要先改文档，再改代码）

## 操作说明

### 改动类型与要更新的文件

| 改了什么 | 要检查的文件 |
|---|---|
| 用户可见的功能或行为 | 功能细节写进对应功能文档和架构文档的状态表；`README.md`（中文）和 `README.en.md`（英文）只保留定位、痛点与核心能力、快速开始、安全边界和文档链接，只有改变这些内容时才两边同时改，不要把功能细节和接口名堆回 README |
| API 名称、请求或返回字段、错误码 | `docs/EcomCraft ComfyUI 工作流.zh-CN.md` 与英文版 `docs/EcomCraft ComfyUI Workflows.md` 的客户端调用部分（接口名集中在这里，README 不再逐个列出） |
| 工作流发现、导入、执行服务器、调度、局域网接入、作品存储 | `docs/EcomCraft ComfyUI 工作流.zh-CN.md` 与 `docs/EcomCraft ComfyUI Workflows.md`；涉及需求范围时先改 `docs/EcomCraft 局域网多服务器平台需求.zh-CN.md` 及其英文版 `docs/EcomCraft LAN Multi-Server Platform Requirements.md` |
| 产品定位、架构、MVP 范围、功能或任务的增删 | `docs/EcomCraft Architecture.zh-CN.md` 与 `docs/EcomCraft Architecture.md`，再更新 `docs/EcomCraft 开发总表.xlsx`（F / T 编号保持一致，编号不复用） |
| 完成开发总表中的某个任务 | 开发总表“开发任务”的状态由用户确认后填写；需求文档的“开发进度”节同步更新（中英文） |
| 工作台页面、按钮、配色、交互 | `docs/EcomCraft 工作台界面设计.zh-CN.md` 第 3 节控件清单（详见 `ecomcraft-studio-ui` 技能） |
| 部署方式、账号登录、角色权限、排队上限、防火墙、HTTPS、共享存储可见性 | `docs/EcomCraft 局域网部署指南.zh-CN.md` 与 `docs/EcomCraft LAN Deployment.md` |
| 提示词优化（MagicPrompt）、生成超时 | `docs/EcomCraft 提示词优化指南.zh-CN.md`、`docs/EcomCraft Prompt Enhancement.md` |
| 配置键（如 `Settings.EcomCraft.ComfyWorkflowSearchPaths`） | 所有提到该键的文档，用 Grep 全文搜索，确保名称一致 |
| 新增、删除或改名 EcomCraft 文档 | `docs/README.zh-CN.md` 与 `docs/README.md` 中的 EcomCraft 文档列表，以及两个根目录 README 的“文档”节 |
| 开发规则、构建方式或项目约定 | `AGENTS.md` 和对应的 `.agents/skills/*/SKILL.md` |

### 一致性要求

- API 名称、配置键、权限名、错误码在代码和所有文档中必须完全一致。改名后用 Grep 搜索旧名称，确认没有残留。
- 安全边界（只接受工作流 ID 和参数白名单、只用登记的执行服务器、不回退云端、只返回必要信息）的描述在 README 和功能文档中保持一致，不要在某一处写得更宽松。
- 局域网多服务器相关功能以需求文档为准：需求变化时先改需求文档，再改代码和其他文档。

### 中英文文档

- 根目录 `README.md` 是中文，`README.en.md` 是英文，内容要对应，文首的语言切换链接保持可用。
- `docs/` 下 SwarmUI 原有文档采用“英文 `X.md` + 中文 `X.zh-CN.md`”成对维护；修改其中一份时检查另一份是否需要同步。中文只是导读、不是完整翻译时，必须标明“导读”并链接英文原文。
- EcomCraft 同时面向国内外用户：新增或修改 EcomCraft 文档时，中文和英文两个版本必须同时编写或同步。新文档采用“英文 `X.md` + 中文 `X.zh-CN.md`”命名；已有中文文件名的文档（如需求文档）保留原名，英文版另起英文文件名，两份文首互相链接。
- 尚无英文版的旧 EcomCraft 文档（`EcomCraft 工作台界面设计.zh-CN.md`），在下次修改时补齐英文版，并更新 README 和 docs 索引中的链接。`EcomCraft ComfyUI 工作流.zh-CN.md` 已有英文版 `EcomCraft ComfyUI Workflows.md`，两份需同步修改。
- `.gitignore` 默认忽略 `docs/` 下的新文件，`!/docs/EcomCraft *` 放行了 EcomCraft 文档；新文档不以 `EcomCraft ` 开头时，需要在 `.gitignore` 中另行放行。
- `docs/EcomCraft 开发总表.xlsx` 是中文规划表，不需要英文版；其中的路线图在架构文档中有中英文描述。
- 不要编辑 `docs/APIRoutes/`，它由源码自动生成。

### 写法

- 写给使用者和管理员看：说明能做什么、怎么配置、出错时怎么办；不要贴大段代码或内部实现细节。
- 不写尚未实现的功能；规划中的内容放在需求文档或架构文档里并标明状态。
- 文档链接中的空格写成 `%20`，与现有文档保持一致。
