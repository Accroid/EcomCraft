# EcomCraft 维护、上游同步与预发布

本项目保留 SwarmUI 的 fork 关系和 Git 历史，以 EcomCraft 的名称、功能范围和版本独立维护。本文负责仓库与发布约定，README 仅展示产品内容和功能清单。

## 1. 远端与基线

| 项目 | 约定 |
| --- | --- |
| 自有仓库 / origin | https://github.com/Accroid/EcomCraft.git |
| 官方上游 / upstream | https://github.com/mcmonkeyprojects/SwarmUI.git |
| upstream 推送地址 | `disabled://upstream`，本地禁止通过该远端推送 |
| Git 默认推送远端 | `origin` |
| GitHub CLI 默认仓库 | `Accroid/EcomCraft` |
| 初始化上游基线 | `40375eb0bd7a4ff25cda731bfa2b70966356a272` |
| 自有主分支 | `main` |
| 原上游基线分支 | `master`，保留，不作为 EcomCraft 的生产发布标记 |

本地只读推送地址是防误操作措施，不是 GitHub 服务端权限。其他开发者、CI 和重新克隆的副本必须分别配置；不要将它理解为任何凭据都绝不可能访问上游。

新副本的配置命令：

```bash
git remote add upstream https://github.com/mcmonkeyprojects/SwarmUI.git
git remote set-url --push upstream disabled://upstream
git config --local remote.pushDefault origin
git config --local remote.upstream.tagOpt --no-tags
gh repo set-default Accroid/EcomCraft

git remote -v
gh repo set-default --view
```

已经存在 upstream 时先检查地址，不重复添加、不盲目覆盖。获取上游时使用 `git fetch --no-tags upstream`，不要把上游版本标签误当作 EcomCraft 的发布版本。

## 2. 分支与主分支保护

| 分支 | 用途 |
| --- | --- |
| `main` | 自有项目基线；后续修改经 PR 合并。初始化不代表 M0 已通过，也不代表已有生产版本。 |
| `feature/*` | 自有功能开发。 |
| `fix/*` | 自有问题修复。 |
| `integration/upstream-*` | 选择性集成上游版本或修复，完成兼容验证。 |

main 的初始保护要求：

- 必须通过 PR 合并；管理员同样受到分支保护限制。
- 禁止强制推送、禁止删除，合并前解决讨论。
- 新提交使已有审核失效。
- 单维护者阶段批准数为 0：维护者通过 PR 人工决定是否合并，不假称完成了第二人的独立审核。加入其他维护者后应改为至少 1 人批准。
- 没有实际 CI 运行记录时不配置虚构的 required checks。运行并核对真实 job 名称、应用来源和成功记录后，再将它们设为强制检查；当前保护不能代替 CI 或 M0 验收。
- 不要求线性历史；自有仓库只允许 merge commit，禁止 squash/rebase 合并，保留与上游的祖先关系。

GitHub 仓库管理员仍能修改规则本身，所以这些设置属于治理约束，而不是不可改变的安全边界。实时配置可用以下命令核对：

```bash
gh repo view Accroid/EcomCraft --json nameWithOwner,isFork,parent,defaultBranchRef
gh api repos/Accroid/EcomCraft/branches/main/protection
gh api repos/Accroid/EcomCraft --jq '{allow_merge_commit,allow_squash_merge,allow_rebase_merge}'
```

初始化时先发布项目基线、创建 main 并验证保护，最后切换默认分支；之后不直接向 main 推送，不重写已经共享的历史。

## 3. 定期受控同步上游

### 检查频率

- 至少每周由维护者检查一次上游 Release、提交和安全通告；明确的安全问题及时评估。
- 检查与合并是两件事；没有待引入修复时不必升级。
- 本轮只建立人工操作约定，不建立定时自动合并、自动发布或生产更新任务。

### 常规版本集成

在没有未提交改动的开发副本中操作：

```bash
git switch main
git pull --ff-only origin main
git fetch --no-tags upstream
git log --oneline main..upstream/master
git diff --stat main...upstream/master
```

1. 审核差异，选择明确的上游版本或完整提交 SHA，记录要修复的具体问题、依赖和兼容风险；不要只写“同步最新版”。
2. 从当前 main 创建独立的 `integration/upstream-*` 分支。
3. 在集成分支合并已审核的上游提交，保留 merge commit。不要在 main 上直接执行上游合并，也不要把整个集成压成一个提交。
4. 处理冲突，特别检查 EcomCraft README、扩展 API、权限、项目文件和运行配置默认值。
5. 在独立测试副本构建并验证；复跑前端/契约/C# 测试及实际生成、策略、素材权限和归档回归。由具备环境的开发者执行宿主构建与启动。
6. 提交到 origin，创建目标明确为自有仓库的 PR：

   ```bash
   gh pr create --repo Accroid/EcomCraft --base main --head "$(git branch --show-current)"
   ```

7. PR 中逐项记录引入的修复、完整上游 SHA、测试结果、未解决风险及回滚方案；人工审核后采用 merge commit 合并。
8. 合并不等于发布。只有完成相应验证和批准后才制作新版本。

### 紧急修复

无法整体升级时，可在独立修复分支使用 `git cherry-pick -x` 引入已审核的修复，保留原始提交来源。先核对依赖和适用版本，并记录选择性引入的提交，避免后续完整同步时遗漏或重复处理。

不要为使仓库看起来像新项目而清空历史，也不要 rebase、reset 或强推已经发布的 main。

## 4. M0 验证是预发布门槛

完整操作和证据要求见 [M0 验证清单](../src/Extensions/EcomCraft/docs/M0-verification.md)。

当前交付只包含管理员只读诊断代码；不是完整 MVP。创建 main、设置保护或拉取上游都不改变以下结论：

- 本地尚无 .NET SDK，C# 判定器测试、扩展编译和宿主加载未验证。
- 真实账号直接 API 鉴权、浏览器和双机/GPU 行为未验证。
- 六项 Spike 尚未完成；任务级策略、持久化任务、素材发布等业务能力缺口不得标记通过。
- 尚无本仓库 CI 成功运行证据；工作流文件存在不等于检查已经执行。

下一步由具备环境的开发者在测试副本中：

1. 运行 C# 判定器测试，构建并加载扩展。
2. 验证 Owner/Admin/User/PowerUser 及缺失/无效会话的直接 API 权限。
3. 验证页面刷新、报告下载、故障恢复、浏览器和主题。
4. 按 SPIKE-01～06 留存可复核证据；无法验证或尚未实现的项目保持阻断，先完成必要设计或实现。
5. 由维护者记录通过项、失败项和批准结论。真实账号、素材及会话信息不进入公开 Git 仓库。

在门槛完成前，不创建预发布标签、GitHub Release、发布包或生产部署。不用“alpha”标签掩盖没有完成的验证。

## 5. 独立版本与预发布

M0 完成并获人工批准后，再发布明确标为预发布的 EcomCraft 版本，例如 `ecomcraft-v0.1.0-alpha.1`。

- EcomCraft 版本独立于 SwarmUI 和扩展内部版本；不重用、移动或覆盖上游标签。
- 固定批准的完整提交 SHA；标签、发布说明和工件必须指向同一个版本。
- GitHub Release 必须设置 prerelease，不宣称为稳定版，不自动标记为最新稳定版本。
- 发布说明列出自有新增功能、引入的上游修复、基线 SHA、测试证据、已知限制、安装和回滚步骤。
- 安装与更新来源必须是自有仓库；审核原有安装器、启动脚本及自动更新逻辑，不能用官方安装包冒充包含 EcomCraft。
- 发布包不得包含密钥、会话、真实客户素材、运行数据或不必要的模型文件；保留上游与第三方许可。

本阶段尚未制作 EcomCraft 发布工件或预发布记录。

## 6. 回滚与数据边界

分别管理应用工件、配置、任务/素材索引、原始素材和结果文件、模型与工作流。每次升级记录这些组件的版本和兼容关系，先验证备份可读取、可恢复。

失败发布时恢复上一份已批准的软件与兼容配置，不以强推 main 或覆盖整个数据目录代替回滚。涉及数据库变化时先设计兼容迁移与恢复步骤，不能直接回退数据库后丢弃任务和素材。

至少完成一次测试环境中的失败升级/发布回滚演练，核对任务与素材完整性后，才能将回滚能力标记为通过。
