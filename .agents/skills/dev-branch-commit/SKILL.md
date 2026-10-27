---
name: dev-branch-commit
description: 在本仓库提交或推送代码时使用；说明分支用途（只往 dev 提交）、提交前检查清单和中文提交信息格式。
---

# 提交到 dev 分支

## 适用场景

- 完成一项修改后需要提交或推送
- 不确定该在哪个分支上工作
- 需要从上游 SwarmUI 合并代码

## 操作说明

### 分支用途

- `dev`：所有日常修改和提交都在这里。提交前用 `git branch --show-current` 确认当前分支是 `dev`。
- `main`：正式发布分支。由用户确认功能后手动把 `dev` 合并进来；代理不得提交、合并或推送到 `main`。
- `master`：从上游 SwarmUI 仓库 fork 的分支，只用于跟踪上游。只有用户明确要求同步上游时才操作它。
- 不要自行新建功能分支或发起 PR，除非用户要求。
- 日常开发在桌面的 `EcomCraft` 克隆中进行，检出 `dev` 并跟踪 `origin/dev`。旧的 `EcomCraft-gallery-delete` 工作区已失效（其主仓库已不存在），不要在其中工作。如果另有检出 `main` 的工作区，不要切换它的分支。

### 提交前检查

1. `git status --short`：确认只包含本次任务的文件；不要覆盖或顺带提交用户已有的修改。
2. `git diff --stat` 和 `git diff`：逐项确认改动符合预期。
3. `git diff --check`：没有行尾空格或冲突标记。
4. 不提交 `src/bin`、`src/obj`、`Data/`、`Output/`、`Models/`、临时截图或测试输出。`.agents/skills/` 已在 `.gitignore` 中放行，可以提交；其他位置的 `SKILL.md` 和 `skills/` 会被忽略。
5. 能运行的检查都运行一遍：`node --check src/Extensions/EcomCraft/Studio/studio.js`；C# 能编译就编译（见 `ecomcraft-build-run` 技能）。
6. 文档已按 `ecomcraft-docs-sync` 技能同步。

### 提交信息格式

- 第一行：`类型: 中文简述`，类型用 `feat`（新功能）、`fix`（修复）、`docs`（文档）、`refactor`（重构）、`chore`（杂项）。
- 正文：用 `- ` 列出用户能感知的改动和原因；改动较多时可用“画廊删除：”“其他：”等小标题分组。
- 注明没能验证的部分，例如“C# 改动未经编译验证（开发环境无 .NET SDK）”“尚未在真实 SwarmUI + ComfyUI 上验证”。
- 结尾保留系统要求的 `Co-Authored-By` 行。
- 多行信息用 PowerShell 单引号 here-string（`@'...'@`，结束符顶格）或 Bash heredoc 传给 `git commit -m`。

### 推送

- 提交后用 `git push`（`dev` 已跟踪 `origin/dev`）。推送会公开到 GitHub，用户没要求推送时先问。
- 不使用 `--force`、`--no-verify`，不改写已推送的历史。
- 推送时出现 `Unable to persist credentials with the 'wincredman' credential store` 只是凭据没保存，不影响推送结果；以输出中的 `->` 行确认是否成功。
