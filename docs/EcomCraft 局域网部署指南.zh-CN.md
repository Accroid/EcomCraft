# EcomCraft 局域网部署指南

> **语言：** 简体中文 · [English](EcomCraft%20LAN%20Deployment.md)

本文说明如何在一个团队的局域网内部署 EcomCraft，供多人同时使用。内容包括：开启账号登录、限制每人排队数量、为执行服务器配置防火墙、作品存放在共享存储时谁能看到，以及用 `http://` 局域网地址访问时浏览器会限制哪些功能。

SwarmUI 账号系统的通用说明见 [Sharing Your Swarm（分享你的 SwarmUI）](Sharing%20Your%20Swarm.zh-CN.md)，从其他设备访问的网络设置见 [高级用法：从其他设备访问 SwarmUI](Advanced%20Usage.zh-CN.md#从其他设备访问-swarmui)。本文只补充 EcomCraft 需要的部分，不重复其中的内容。

## 1. 部署结构

| 角色 | 作用 | 示例 |
|---|---|---|
| 平台服务器 | 运行 SwarmUI + EcomCraft，负责账号、审核、调度和保存作品；也可以同时是执行服务器 | A：`192.168.1.10` |
| 局域网执行服务器 | 只安装 ComfyUI，由平台服务器调用 | B：`192.168.1.21:8188` |
| 团队成员的电脑或手机 | 用浏览器打开平台服务器上的工作台 | `http://192.168.1.10:7801/EcomCraft` |

团队成员只访问平台服务器。执行服务器上的 ComfyUI 只给平台服务器访问，不给团队成员直接打开。

**不要把任何一台服务器暴露到公网**：不要在路由器上做端口映射，也不要把 ComfyUI 端口开放给整个网络。SwarmUI 目前只按“亲友之间共享”的安全级别验证过，ComfyUI 本身没有登录验证。

## 2. 平台服务器：让局域网能访问

1. 在原生引擎的 **Server → Server Configuration → Network** 中，把 `Host` 改为 `*`（Windows）或 `0.0.0.0`（Linux），`Port` 默认为 `7801`。保存后重启服务。
2. 在平台服务器的防火墙中，只允许局域网访问这个端口。Windows 示例（以管理员身份运行 PowerShell）：

   ```powershell
   New-NetFirewallRule -DisplayName "EcomCraft 7801 (LAN)" -Direction Inbound -Protocol TCP -LocalPort 7801 -RemoteAddress LocalSubnet -Action Allow
   ```

3. 在另一台电脑上用浏览器打开 `http://192.168.1.10:7801/EcomCraft`（换成你的平台服务器地址），确认能看到登录页或工作台。

## 3. 开启账号登录（R9.1）

多人使用时必须开启账号登录，每人一个账号。操作步骤见 [Sharing Your Swarm](Sharing%20Your%20Swarm.zh-CN.md#基础共享设置) 的“基础共享设置”，要点如下：

1. **先给自己设密码**：在 **Server → Users** 中给默认账号 `local` 设置密码；也可以新建一个 `Owner` 角色的账号给自己用。
2. **开启登录**：在 **Server → Server Configuration → User Authorization** 中开启 `AuthorizationRequired`。
   - `AllowLocalhostBypass` 默认开启：在平台服务器本机打开时不用登录。如果平台服务器也有人日常使用，建议关闭它。
   - **Network** 分组中的 `AuthBypassIPs` 列出了免登录的地址，默认只有本机。**不要把局域网地址加进去**，否则这些地址上的人不用登录就能直接使用管理员权限。
3. **为每位成员建账号**：普通成员用 `User` 角色，需要审核工作流或管理服务器的人用 `Admin` 角色。
4. **分配 EcomCraft 权限**：在 **Server → Users → Roles** 中按角色勾选。

| 权限 | 作用 | 默认 |
|---|---|---|
| `ecomcraft_use_published_workflows` | 在工作台调用已发布的工作流；配合出图权限可在专业模式上传自用工作流 | 普通用户 |
| `ecomcraft_submit_workflows` | 把自己在 ComfyUI 中保存或在专业模式上传的工作流提交审核 | 普通用户 |
| `ecomcraft_review_workflows` | 审核、发布、驳回、下架工作流，配置用户可见参数 | 管理员 |

生成还需要 SwarmUI 自带的基础生成权限；`User` 角色默认已经具备。不要给普通成员 ComfyUI 原生工作流相关的权限，这类权限可以绕过 EcomCraft 的参数白名单。

如果开启登录后自己进不去，先关闭服务，用文本编辑器打开 `Data/Settings.fds`，把 `AuthorizationRequired` 改回 `false`，再启动服务。

## 4. 每人排队上限（R9.2）

每人能同时排队的图片数，由所在角色的 `MaxQueued` 决定。在 **Server → Users → Roles** 中逐个角色设置：

- 默认值：`User` 和 `PowerUser` 为 100,000（相当于不限），`Owner` 和 `Admin` 更高，`Guest` 为 10。
- 建议给 `User` 设一个较小的值，例如 `8`–`20`，防止一个人一次提交太多任务，把所有执行服务器都占满。
- 计数单位是图片张数：一次提交 4 张算 4。一个账号有多个角色时，取其中最大的值。
- 超过上限时，工作台提示“排队任务已达到账号角色允许的上限”（错误码 `queue_limit`），本次不提交。
- 同一角色还有 `MaxT2ISimultaneous`，限制一个人同时在执行的图片数，默认值通常不用改。

## 5. 局域网执行服务器的防火墙

ComfyUI 没有登录验证：能连上它端口的人，就能直接提交任意工作流、读取它保存的工作流和生成结果。所以每台局域网执行服务器的 ComfyUI 端口**只对平台服务器开放**。

1. 在执行服务器上启动 ComfyUI 时加上 `--listen 0.0.0.0`（可用 `--port` 指定端口）。
2. 配置防火墙，只允许平台服务器访问 ComfyUI 端口。
   - Windows 示例（以管理员身份运行 PowerShell，把地址和端口换成你的）：

     ```powershell
     New-NetFirewallRule -DisplayName "ComfyUI only from EcomCraft platform" -Direction Inbound -Protocol TCP -LocalPort 8188 -RemoteAddress 192.168.1.10 -Action Allow
     ```

     ComfyUI 第一次监听网络时，Windows 可能弹窗询问是否允许 Python 通信。如果当时点了“允许”，系统会建一条对所有地址开放的程序规则，需要在“高级安全 Windows Defender 防火墙”的入站规则中找到它，删除或改为只允许平台服务器地址。
   - Linux（ufw）示例：

     ```bash
     sudo ufw allow from 192.168.1.10 to any port 8188 proto tcp
     sudo ufw deny 8188/tcp
     ```

3. 在平台服务器上用 **ComfyUI API By URL** 登记这台服务器，并在后端标题中填写显示名称。审核这台服务器上的工作流之前先设好名称，之后不要修改：工作流 ID 以服务器名称开头。详细步骤和可登记的地址范围见 [工作流文档：局域网执行服务器](EcomCraft%20ComfyUI%20工作流.zh-CN.md#局域网执行服务器)。
4. 验证：在另一台成员电脑上打开 `http://192.168.1.21:8188`，应该**打不开**；在平台服务器上打开，应该能打开。

## 6. 作品放在共享存储时谁能看到（D9）

工作台里的作品默认只有本人能看到。但这只是平台内的限制，**存储层不按用户隔离**：

- 目前作品保存在平台服务器的输出目录（默认 `Output/`，每个账号一个子目录）。能直接访问这个目录的人（例如平台服务器上的其他 Windows 用户，或者你把它设成了共享文件夹），可以看到所有人的作品。不要把它共享出去。
- 管理员可以在工作台画廊页的“存储设置”中把作品库设到 NAS 或其他电脑的共享文件夹（Windows 用 `\\服务器\共享\目录` 形式，不支持映射的盘符；Linux / macOS 先由系统挂载）。保存时会检测能否写入、剩余空间和写入速度，失败不保存。作品先保存在平台服务器本机，再在后台复制过去，共享存储离线时生成照常进行，画廊显示“待同步”，恢复后自动补上。
- 能访问作品库共享目录的人可以看到所有人的作品。如需限制，应在 NAS 上只给平台服务器运行服务所用的账号授权，不给团队成员的账号授权。平台不保存 NAS 密码，访问共享所需的认证由操作系统完成：Windows 上让运行服务的账号能访问该共享（例如以有权限的域账号或本机账号运行服务），Linux / macOS 上在系统里挂载并设置权限。
- 工作台的任何接口都不会把存储路径返回给普通用户。

## 7. 用 `http://` 局域网地址访问时的浏览器限制

浏览器把 `http://192.168.x.x` 这类地址视为“不安全上下文”，会禁用一部分功能（`http://localhost` 除外）。

| 功能 | 在 `http://` 局域网地址下 | 对 EcomCraft 的影响 |
|---|---|---|
| 剪贴板写入（复制图片、复制文本） | 不可用 | 工作台目前不使用；原生 SwarmUI 界面里的“复制图片”等按钮会失效，可以改用下载 |
| 网页直接打开摄像头（`getUserMedia`） | 不可用 | 工作台不使用。“拍照上传”按钮用带 `capture` 属性的文件选择方式调起相机，不受影响；手机上可以完成上传、出图和浏览画廊 |
| 浏览器系统通知（Notification） | 不可用 | 工作台目前不使用。计划中的“完成通知”在 `http://` 下只能在页面内提示 |
| 安装为应用（PWA / Service Worker） | 不可用 | 无影响 |
| 上传、下载、生成、画廊、队列 | 正常 | 无影响 |

需要上述功能时，可以给平台服务器配置 HTTPS：在平台服务器前面放一个反向代理（例如 Caddy、Nginx 或 IIS），使用内部证书颁发机构签发的证书，并让每台成员设备信任这个证书。局域网里的自签证书如果没有被设备信任，浏览器会持续报警告，这时仍然算不安全上下文。不要为了获得 HTTPS 而把服务暴露到公网。

## 8. 部署检查清单

- [ ] 平台服务器 `Host` 已改为局域网可访问，防火墙只对局域网开放 `7801`
- [ ] `local` 账号已设密码，`AuthorizationRequired` 已开启，`AuthBypassIPs` 只有本机地址
- [ ] 每位成员有自己的账号；审核权限只给需要的人
- [ ] `User` 角色的 `MaxQueued` 已按团队规模设置
- [ ] 每台局域网执行服务器以 `--listen` 启动，防火墙只允许平台服务器访问 ComfyUI 端口；从成员电脑访问 ComfyUI 端口失败
- [ ] 执行服务器已设好显示名称后再审核工作流
- [ ] 平台服务器的输出目录没有共享给团队成员
- [ ] 没有任何服务器做了公网端口映射
