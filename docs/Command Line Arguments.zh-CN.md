# 命令行参数

> **语言：** [English 原文](Command%20Line%20Arguments.md) · 简体中文

大多数设置都可以通过 UI 或 `Settings.fds` 文件进行配置，普通用户通常不需要使用命令行参数。不过，自动化和开发场景仍有一些重要选项（例如指定设置文件路径）；部分 UI 设置也可以被命令行参数覆盖，例如监听地址和端口。

## 用法示例

在家用 Windows 电脑上启动开发环境的示例：

```powershell
.\launch-windows-dev.ps1 --host * --port 7850 --environment development --launch_mode web
```

如果参数无效，程序会拒绝启动，并提示哪个值有问题。

## 参数列表

| 参数 | 默认值 | 说明 |
|---|---|---|
| `--data_dir` | `Data` | 覆盖默认数据目录。 |
| `--settings_file` | `Data/Settings.fds` | 指定设置文件路径。若文件不在默认位置，必须显式传入；默认路径相对于 `--data_dir`。 |
| `--backends_file` | `Data/Backends.fds` | 指定后端配置文件路径。若文件不在默认位置，必须显式传入；默认路径相对于 `--data_dir`。 |
| `--environment` | `Production` | 设置 ASP.NET Web 环境，可用 `development` 或 `production`。Development 会显示更详细的调试日志与错误，Production 面向正常使用。 |
| `--host` | `localhost` | 覆盖服务器设置 `Network.Host`。 |
| `--port` | `7801` | 覆盖服务器设置 `Network.Port`。 |
| `--asp_loglevel` | `warning` | ASP.NET Web 日志的最低级别，可选 `Trace`、`Debug`、`Information`、`Warning`、`Error`、`Critical`、`None`。注意：`Information` 可能产生大量调试输出。 |
| `--loglevel` | `Info` | SwarmUI 自身的最低日志级别，可选 `Debug`、`Info`、`Init`、`Warning`、`Error`、`None`。通常 `Info` 代表正常运行日志。 |
| `--user_id` | `local` | 设置本地用户默认 UserID；主要用于单用户模式，多用户模式通常用不到。 |
| `--lock_settings` | `false` | 启用后，管理员也不能在 UI 中编辑服务器设置。修改设置需要编辑设置文件并重启服务。 |
| `--ngrok_path` | 无 | 指定 ngrok 可执行文件路径。启动时会自动加载并配置 ngrok，以公网 URL 共享 UI。 |
| `--cloudflared_path` | 无 | 指定 cloudflared 可执行文件路径，启动时自动加载并配置 TryCloudflare。 |
| `--proxy_region` | 无 | 设置 ngrok/cloudflared 代理区域；未指定时使用距离较近的区域。 |
| `--proxy_added_args` | 无 | 为代理启动增加额外参数。由于解析器限制，字符串第一个字符需为句点。例如 `--proxy_added_args ".--my-arg --arg -argy arg"`。 |
| `--ngrok_basic_auth` | 无 | 为 ngrok 入口配置基本身份验证要求。 |
| `--launch_mode` | `none` | 覆盖服务器设置 `LaunchMode`。 |
| `--require_control_within` | 无 | 指定远程 API 服务器必须在多少分钟内发送 `AdminTakeControl` API 请求，否则视为启动异常。适用于 AutoScalingBackend 等自动管理实例。 |
| `--no_persist` | `false` | 要求 SwarmUI 的大多数子系统避免持久化数据，例如会话处理器不保存会话。适用于多个实例共享存储等场景，但可能产生重复数据；建议至少正常运行一次后再启用。 |
| `--ci_test` | `false` | 特殊模式：执行基本验证测试，然后快速退出。 |
| `--ci_test_extensions` | `false` | 在 CI Test 模式中下载并测试编译常见扩展。 |
| `--help` | `false` | 显示简要参数列表和使用提示，然后在启动 SwarmUI 前退出。 |

## 环境变量

SwarmUI 核心本身很少直接使用环境变量。

如需为 ComfyUI 后端设置环境变量，可像平常一样设置。启动后端进程时，SwarmUI 会将相关环境变量传递给后端。

若希望环境变量同时应用于 SwarmUI 和所有子进程，可在仓库根目录创建 `.env` 文件。文件使用 `KEY=VALUE` 格式，并支持以 `#` 开始的注释，例如：

```env
SWARM_EXAMPLE_VAR=42
# 需要该值的某个程序
SOME_THING=Some value
```

已知用途示例：

- Linux 上安装 ComfyUI 后端时，可设置 `SWARM_NO_VENV=true`，让 SwarmUI 不创建虚拟环境。默认会使用 venv；这通常只用于 Google Colab 等特定容器化安装方式。
- 部分 AMD Radeon 显卡用户可能需要设置 `HSA_OVERRIDE_GFX_VERSION=10.3.0`，用于绕过某些 ROCm 兼容问题。