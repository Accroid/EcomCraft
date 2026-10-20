# 共享 SwarmUI 实例

> **语言：** [English 原文](Sharing%20Your%20Swarm.md) · 简体中文

SwarmUI 支持共享 AI 图像生成能力，允许其他人访问你运行的实例。常见共享对象分为两类：

- **朋友与家人：** 你信任、愿意让他们使用生成能力的人。
- **公众：** 其他任何用户，例如公开服务器、Discord 社区或信任程度较低的人群。

**安全提示：** 当前 SwarmUI 仅针对朋友与家人这类受信任环境经过安全验证。不建议直接向公众开放实例，除非你自行完成安全审计。另请查看[面向安全研究人员的公开说明](https://github.com/mcmonkeyprojects/SwarmUI/discussions/679)。

## 一般说明

- 可以通过模型目录白名单/黑名单限制用户能看到哪些模型文件夹，也可按角色设置不同限制。
- 每个用户有独立的图像输出目录、预设列表和用户设置。
- SwarmUI 提供一些预设角色，覆盖常见场景；也可以创建自定义角色。
- Owner 或 Admin 可以随时添加、删除用户、修改设置，甚至以其他用户身份访问其数据。请只授予可信人员管理员权限。
- 默认的 User 角色可访问的标签页较少，尤其不能使用自定义 Comfy 工作流。这是因为 Comfy 后端相关操作较难进行安全验证。
  - 如果是信任且确实需要原始 Comfy 工作流权限的朋友，可以考虑使用 `PowerUser` 角色，但必须理解其安全风险。
- 默认用户账号名为 `local)，它用于表示直接在实例所在本机访问的用户。

## 尚未实现但已规划的功能

- 双因素身份验证。
- 管理员快速切换账号。
- 强制用户首次登录后修改密码。
- 面向普通用户的简化稳定界面。
- 全服务器共享预设。
- 普通用户自己的模型整理和备注。
- 用户个人通配符。
- 面向远程用户的便捷功能，例如批量下载历史记录 ZIP。
- 更方便的用户会话管理与机器人令牌界面。

## 基础共享设置

### 1. 为默认本地账号设置密码并配置角色

1. 进入 `Server → Users)。
2. 选择名为 `local` 的用户（本机登录时默认使用此用户）。
3. 点击 **Change User Password**，设置一个你能记住且足够安全的密码并保存。
4. 在 Roles 中选择 `User)，按实际需求调整普通用户的权限和设置。
5. 可以通过模型白名单/黑名单排除不希望共享的模型目录。
6. 谨慎授予权限。某些默认关闭的权限确实存在安全原因，例如允许直接操作 Comfy 可能带来风险。
7. 保存角色，并按需要配置其他角色。

### 2. 启用账号认证

进入 `Server → Server Configuration`，找到 **User Authorization**：

- 启用 `AuthorizationRequired`。
- 如果希望本机访问也必须登录账号，可以选择关闭 `AllowLocalhostBypass`。
- 设置简短易识别的 `InstanceTitle`，它会用于标识实例。
- 设置 `LoginNotice`，在登录页展示访问说明或联系办法。
- 检查其他服务器选项，完成后保存。

关闭 `AllowLocalhostBypass` 后，就可以登录 `local` 账号。如果因设置失误而无法登录，可关闭 SwarmUI，用文本编辑器打开 `Data/Settings.fds`，把 `AuthorizationRequired` 恢复为 `false`，保存后重新启动。此方法适合本机故障恢复，不应作为绕过认证的常规方式。

### 3. 确认网络地址

参阅[高级使用：从其他设备访问 SwarmUI](Advanced%20Usage.zh-CN.md#从其他设备访问-swarmui)。

- 如果只在同一个家庭或办公室局域网内共享，使用局域网地址即可。
- 公网托管方式见高级使用文档，但当前不建议未经安全审计直接公开托管。
- 记录其他用户能够访问的完整 URL，后续需要提供给他们。

### 4. 创建共享用户

再次进入 `Server → Users`，点击 **Add User**：

1. 选择简单的用户名。原文建议只使用小写字母，不要包含空格或符号。
2. 设置初始密码。可用[随机字符串生成工具](https://www.random.org/strings/?num=10&len=20&digits=on&upperalpha=on&loweralpha=on&unique=on&format=html&rnd=new)生成强密码；应安全保存，供首次登录时使用。
3. 选择合适角色：
   - `Owner`：属于实例所有者的账号。
   - `Admin`：需要管理服务器控制项的用户。
   - `PowerUser`：信任且需要 Comfy 标签页等高级能力的用户。
   - `User`：仅需要常规生成能力的用户。
4. 点击 **Add**。可以先注销并以新账号登录，检查权限与界面是否符合预期。
5. 如有需要，可提前为用户配置设置或预设。
6. 将用户名、初始密码和服务器 URL 安全地交给用户，并提醒他们首次登录后立即修改密码。

## 用户注册

可在 Server Configuration 的用户注册选项中启用注册：

- 启用 `AllowRegistration`，并配置允许的注册方式。
- 支持基本的用户名/密码方式，也支持 Google OAuth。OAuth 配置可参考 [Google 官方文档](https://developers.google.com/identity/protocols/oauth2)。
- 可通过 `NewUserDefaultRole` 设置新用户默认角色（默认是 `user`）。
- 建议设置 `RegisterNotice` 注册提示。

启用后，用户在登录页面可以通过链接进入 `/Register` 注册页面。

## 模拟其他用户（User Impersonation）

管理员可以查看某个用户实际看到的界面和权限：

1. 在 `Server → Users` 中选择目标用户。
2. 点击 **Impersonate User**。页面会重新加载，并以该用户身份操作。
3. 完成后，在主 **User** 标签页点击 **Stop Impersonating**。
4. 如果模拟后无法访问 User 标签页，可打开新的 SwarmUI 浏览器标签页，通常会恢复自己的账号视图。

用户模拟通过 URL 参数记录，例如 `http://localhost:7801/Text2Image?impersonate=bob`。只有实际账号拥有 **Manage Users** 权限时，此功能才可用。

个人实例也可借助此功能快速切换到为不同用户配置的独立工作环境。

## 将共享的 SwarmUI 用作后端

可以将他人共享的 SwarmUI 实例添加为本机 SwarmUI 的后端。上游原文仍将该流程标记为待补充，因为账号认证信息的便捷生成与应用方式尚未说明完整。使用之前应先与实例所有者确认访问方式、授权范围和安全边界。