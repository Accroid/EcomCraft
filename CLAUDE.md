@AGENTS.md

# Claude Code 补充说明

本文件只补充 Claude Code 会话需要的入口信息；规则以 `AGENTS.md` 为准，不在这里重复。

## 工作区

- 只在桌面的 `EcomCraft` 克隆（`dev` 分支）中工作；`EcomCraft-gallery-delete` 已失效，不要在其中修改或构建。
- 开始任务前查看 `.agents/skills/` 中的相关技能（构建启动、API 路由、工作台 UI、文档同步、提交）。

## 路线图与任务

- 产品定位、架构和 MVP 范围：`docs/EcomCraft Architecture.zh-CN.md`（英文版 `docs/EcomCraft Architecture.md`）。
- 局域网多服务器需求：`docs/EcomCraft 局域网多服务器平台需求.zh-CN.md`。
- 功能（F）与开发任务（T）进度：`docs/EcomCraft 开发总表.xlsx`。一个会话只做一个 T 任务，按“开发任务”表的验收标准和测试方式验证。

## 常用检查命令

仓库目前没有自动化测试项目，以下是可在本机运行的静态与编译检查：

- 前端语法：`node --check src/Extensions/EcomCraft/Studio/studio.js`
- 扩展编译：`dotnet build src/Extensions/EcomCraft/EcomCraft.csproj -c Debug -o <临时目录>`
- 主程序编译：`dotnet build src/SwarmUI.csproj -c Debug -o <临时目录>`（不要输出到正在使用的 `src/bin/live_release`）
- 提交前：`git status --short`、`git diff --stat`、`git diff --check`

后续任务新增测试项目（例如 `src/Extensions/EcomCraft/Tests/`）后，把运行命令补充到这里。

## 修改 C# 扩展后

修改 `src/Extensions/EcomCraft/` 的 C# 代码后，必须按 `AGENTS.md` 和 `ecomcraft-build-run` 技能重新构建扩展（Release 模式按提交哈希缓存 DLL），否则服务会继续加载旧程序集。改前端文件只需刷新页面并更新 `index.html` 中的 `?v=`。
