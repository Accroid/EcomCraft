---
name: ecomcraft-build-run
description: 构建、启动、重启 EcomCraft / SwarmUI 服务时使用，包括修改 C# 后让新代码真正生效、排查“改了代码但服务还是旧行为”、启动后验证服务状态。
---

# 构建与重启 EcomCraft

主程序和 EcomCraft 扩展是两个分开编译的程序集。只重启服务或只构建主项目，常常会继续加载旧的扩展程序集，这是本项目最常见的“改了没生效”原因。

## 适用场景

- 修改了 `src/**/*.cs` 或 `src/Extensions/EcomCraft/**/*.cs`，需要编译验证或让服务加载新代码
- 用户反馈“改动没生效”“页面和接口行为对不上”
- 需要启动、重启或停止本地服务并确认它正常运行

## 操作说明

### 先确认环境

- 运行 `dotnet --list-sdks`。如果没有 .NET 8 SDK，就不能编译：只做静态检查，并在回复和提交信息里写明“C# 改动未经编译验证”。不要擅自安装 SDK，先问用户。
- 当前有两个工作区：`EcomCraft-gallery-delete`（`dev` 分支，日常开发）和 `EcomCraft`（`main` 分支）。只在当前工作区构建和启动；同一个数据目录只能运行一个服务实例。

### 停止旧进程

- 用 `Get-Process SwarmUI | Select-Object Id, Path` 查看。只停止 `Path` 位于当前工作区 `src\bin\live_release\` 下的进程；其他工作区或与本项目无关的进程不要动。
- 不停止旧进程，`dotnet build` 会因为输出文件被占用而失败。

### 两种启动方式和扩展缓存

扩展由 `src/Core/ExtensionsManager.cs` 的 `BuildExtension` 在服务启动时编译，输出到 `src/bin/extensions/SwarmExtensionEcomCraft/SwarmExtensionEcomCraft-<提交哈希前 8 位>.dll`。

- **开发模式** `launch-windows-dev.ps1`：删除 `src/bin`、`src/obj` 后以 Debug 重建主程序，扩展每次启动都会重新编译。改了 C# 想看效果时优先用它。
- **常规模式** `launch-windows.bat`：Release。只有 Git 提交变化（或存在 `src/bin/must_rebuild`）时才重建，扩展 DLL 按提交哈希缓存。**未提交的扩展 C# 改动不会被重新编译**，服务继续加载旧 DLL。
  - 处理办法（先停服务）：删除 `src/bin/extensions/SwarmExtensionEcomCraft/` 目录，让下次启动重新编译；或者按 `BuildExtension` 的同样参数手动构建：
    `dotnet build src/Extensions/EcomCraft/EcomCraft.csproj -c Release -o src/bin/extensions/SwarmExtensionEcomCraft/ -p:BaseIntermediateOutputPath=<仓库绝对路径>/src/obj/extensions/SwarmExtensionEcomCraft/;TargetName=SwarmExtensionEcomCraft-<哈希前8位>`
  - 改了主程序（`src/Core` 等）而没提交时，同样需要重建：删除 `src/bin/live_release` 或创建 `src/bin/must_rebuild` 后再启动。
- `studio.js`、`studio.css`、`index.html` 每次请求从硬盘读取，改了前端不需要重启服务，刷新页面即可（记得更新 `index.html` 里的 `?v=`）。其他通过 `/ExtensionFile/` 提供的文件在 Release 模式下会缓存到重启。

### 只想验证能否编译

- 主程序：`dotnet build src/SwarmUI.csproj -c Debug -o <临时目录>`，不要输出到正在使用的 `src/bin/live_release`。

### 启动后检查

- 后台启动服务，等待后检查：`Invoke-WebRequest http://127.0.0.1:7801/EcomCraft -UseBasicParsing -MaximumRedirection 0` 返回 200（开启账号登录时会返回 302 跳转到 `/Login`，同样说明服务在线；404 说明扩展没加载）；进程过一段时间仍在运行；日志里没有 `Failed to build extension`、`CriticalLoadError` 或未处理异常。
- 确认加载的是新程序集：`src/bin/extensions/SwarmExtensionEcomCraft/` 下 DLL 的修改时间晚于源码改动时间。
- 沙箱中出现 DPAPI、WMI 或文件访问被拒绝时，不要绕过，请用户批准后在正常用户环境启动。

### 禁止事项

- 不修改 `Data/`、`Output/`、`Models/`；不提交 `src/bin`、`src/obj` 或其他构建产物。
- 不手工复制发布文件代替构建；不修改启动脚本来“解决”缓存问题。
