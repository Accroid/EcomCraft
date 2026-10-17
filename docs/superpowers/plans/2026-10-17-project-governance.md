# EcomCraft 独立项目与受控上游同步 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 保留 SwarmUI fork 历史，发布独立品牌首页，建立只读上游和受保护自有主分支；将 M0 验证作为预发布的硬门槛。

**Architecture:** 自有提交只推送到 Accroid/EcomCraft，官方上游仅供读取。初始化 main 后要求后续通过 PR 合并并保留 merge commit；main 的初始化不代表生产验收。README 仅介绍项目与功能，治理和验证说明放在独立文档。

**Tech Stack:** Git、GitHub CLI / REST API、Markdown、Node.js；不引入运行时依赖。

## Global Constraints

- 保留 fork 与原始提交历史；不删除或重写 master、已推送功能分支或上游引用。
- origin 必须为 https://github.com/Accroid/EcomCraft.git；不得向 mcmonkeyprojects/SwarmUI 写入。
- 不运行宿主构建或启动，不触碰 Data、Output、Models，不提交未跟踪的原始需求文件。
- 保留用户修改的 README 标题和 [✓] 标记；上游 Legal 内容原样迁移，不修改 LICENSE.txt。
- 不启用自动生产更新，不制造 CI 成功状态，不将主分支保护当作 M0 验收。
- 缺少 .NET SDK、真实账号和双机环境时停止在 M0 验证门槛之前；不创建 tag、Release 或部署工件。

---

## Task 1: 独立品牌与维护说明

**Files:** 修改 README.md；新增 docs/Third-Party-Notices.md、docs/EcomCraft-Maintenance.md 和本计划。

- [ ] 保留 README 的项目目标、已实现/待实现功能清单及 SwarmUI 二次开发说明；仅添加维护文档入口。
- [ ] 比较 `git show HEAD:README.md` 的 Legal 原文，确认迁移后的版权、许可和第三方声明逐字保留。
- [ ] 编写维护文档，明确远端、分支保护、上游集成、M0 阻断、发布与回滚边界。
- [ ] 检查本次文件的相对链接、空白和 `git diff --check`，复跑 Node 前端与契约测试。

## Task 2: 只读 upstream

**State:** 本地 Git 远端与配置；不修改项目运行配置。

- [ ] 验证 GitHub 登录账号为 Accroid，自有仓库 id 为 1422008168，parent 为 mcmonkeyprojects/SwarmUI。
- [ ] 添加 upstream=https://github.com/mcmonkeyprojects/SwarmUI.git，将 push URL 设置为 disabled://upstream，设置 remote.pushDefault=origin。
- [ ] 禁止本次拉取上游 tag，获取 upstream 分支状态，但不合并任何上游提交。
- [ ] 核对 origin/upstream 的实际 fetch/push URL 和 GitHub CLI 默认仓库，验证只读推送地址没有指向官方服务器。

## Task 3: 提交文档与初始化 main

**Files:** 仅 Task 1 中列出的四个文件；不混入需求文件或运行产物。

- [ ] 确认当前功能分支 HEAD 和自有远端功能分支一致，暂存区没有其他内容。
- [ ] 逐文件暂存并提交，标题为“完善 EcomCraft 项目首页、功能清单与上游同步维护说明”，正文列明功能标记、许可迁移、治理规则和未执行验证。
- [ ] 使用显式 refspec 推送到 origin/feature/ecomcraft-m0-diagnostics，不强制推送。
- [ ] 确认自有仓库没有 main 后，以当前提交创建 main；保留原始 master。

## Task 4: 主分支保护与默认分支

**State:** 仅 Accroid/EcomCraft 的 main 保护与仓库设置。

- [ ] 设置 main 保护：必须经过 PR、过期审核失效、讨论须解决、管理员同样受限、禁止强推和删除。
- [ ] 单维护者初始 required_approving_review_count=0；不冒充独立复审。没有 CI 运行记录时 required_status_checks=null，不伪造检查名。
- [ ] 不要求线性历史；仓库仅允许 merge commit，避免上游集成历史被 squash/rebase 丢失。
- [ ] 保护创建成功并读取验证后，将默认分支设为 main；若 API 拒绝保护，不切换默认分支，也不降级保护绕过错误。
- [ ] 本地建立跟踪 origin/main 的 main，设置其 pushRemote=origin，核对 GitHub CLI 默认仓库仍为 Accroid/EcomCraft。

## Task 5: M0 验证与发布门槛交接

**Files:** 复用 src/Extensions/EcomCraft/docs/M0-verification.md；保留其首次实现的历史记录，不冒改原有证据。

- [ ] 复核本机 .NET SDK、真实账号、浏览器和双机/GPU 环境；区分代码检查、宿主编译和实际业务验收。
- [ ] 无匹配环境则明确报告：C# 判定器、宿主集成与 SPIKE-01～06 未通过；不得创建预发布。
- [ ] 将定期检查与上游集成操作写入维护文档，但本批不建立定时自动合并、自动发版或生产部署任务。
- [ ] 最后通过 GitHub API 核验 fork 关系、远端 main 提交、默认分支、保护设置和原 master 不变，并向用户报告剩余门槛。

## 验证命令

```bash
node --check src/Extensions/EcomCraft/Assets/ecomcraft.js
node --test src/Extensions/EcomCraft/Tests/*.test.js
git diff --check
git remote -v
gh repo view Accroid/EcomCraft --json nameWithOwner,isFork,parent,defaultBranchRef
gh api repos/Accroid/EcomCraft/branches/main/protection
```

本计划描述配置顺序；实际执行结果以终端验证与维护者留存证据为准，不以未运行的命令替代结果。
