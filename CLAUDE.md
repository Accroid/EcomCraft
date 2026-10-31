@AGENTS.md

# Claude Code 补充说明

本文件只补充 Claude Code 会话需要的入口信息；规则以 `AGENTS.md` 为准，不在这里重复。

## 工作区

- 只在桌面的 `EcomCraft` 克隆（`dev` 分支）中工作；`EcomCraft-gallery-delete` 已失效，不要在其中修改或构建。
- 开始任务前查看 `.agents/skills/` 中的相关技能（构建启动、API 路由、工作台 UI、文档同步、提交）。

## 路线图与任务

- **开工必读：** 每个任务开始前，先读 `docs/EcomCraft Architecture.zh-CN.md` 第 1、2 节和对应计划，确认任务符合产品目标；不符合时先问用户，不得自行偏离。
- 产品定位、架构和 MVP 范围：`docs/EcomCraft Architecture.zh-CN.md`（英文版 `docs/EcomCraft Architecture.md`）。
- 新功能计划（F74–F82 / T81–T110）：`docs/EcomCraft 实施计划.zh-CN.md`（英文版 `docs/EcomCraft Implementation Plan.md`）；基础重构（R00–R24）：`docs/EcomCraft 重构计划.zh-CN.md`（英文版 `docs/EcomCraft Refactoring Plan.md`）。两份计划按重构计划第 7 节的顺序穿插执行：先 T81，再 R00–R03 与 CP0，之后 R04–R20 和 R24（R21–R23 收尾并行，R23 等 R14/R20），G1 通过后开始 T83 及后续。
- 局域网多服务器需求：`docs/EcomCraft 局域网多服务器平台需求.zh-CN.md`。
- 功能（F）与开发任务（T）进度：`docs/EcomCraft 开发总表.xlsx`。一个会话只做一个 T 或 R 任务，按“开发任务”表的验收标准和测试方式验证。

## 常用检查命令

以下是静态检查、编译和单元测试命令（开发机没有 .NET SDK 时，编译和单元测试需在能运行的电脑上执行）：

- 前端语法：`node --check src/Extensions/EcomCraft/Studio/studio.js`
- 扩展编译：`dotnet build src/Extensions/EcomCraft/EcomCraft.csproj -c Debug -o <临时目录>`
- 主程序编译：`dotnet build src/SwarmUI.csproj -c Debug -o <临时目录>`（不要输出到正在使用的 `src/bin/live_release`）
- 提交前：`git status --short`、`git diff --stat`、`git diff --check`
- 单元测试：`dotnet test src/Extensions/EcomCraft/Tests/EcomCraft.Tests.csproj`（只覆盖不依赖 SwarmUI 的逻辑，例如调度规则 `SchedulerRules`；测试项目不参与扩展编译）
- 前端测试（本机可运行）：`node --test src/Extensions/EcomCraft/Tests/studio-i18n.test.cjs src/Extensions/EcomCraft/Tests/simple-mode.test.cjs`（英文模式不残留中文、翻译表无缺漏；简易模式无提示词框、专业参数只在专业模式）

## 修改 C# 扩展后

修改 `src/Extensions/EcomCraft/` 的 C# 代码后，必须按 `AGENTS.md` 和 `ecomcraft-build-run` 技能重新构建扩展（Release 模式按提交哈希缓存 DLL），否则服务会继续加载旧程序集。改前端文件只需刷新页面并更新 `index.html` 中的 `?v=`。
