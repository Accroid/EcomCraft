# 在 SwarmUI 中使用更多 GPU

> **语言：** [English 原文](Using%20More%20GPUs.md) · 简体中文

扩展生成算力主要有两种方式：在一台电脑中使用多张 GPU，或连接多台带有 GPU 的电脑。

## 在同一台电脑中使用多张 GPU

1. 打开 SwarmUI 的 `Server → Backends` 页面。
2. 为每张 GPU 添加一个自启动后端，并为各后端设置不同的 `GPU_ID`。
3. 如果电脑里混有不同型号的 GPU，设备 ID 不一定按硬件排列顺序递增，但通常是稳定的。NVIDIA 社区也记录过硬件顺序与 `nvidia-smi` 显示顺序不一致的情况，参见[相关讨论](https://forums.developer.nvidia.com/t/cuda-visible-devices-being-ignored/41808)。
4. 建议逐个后端触发生成任务并观察 GPU 使用率，确认每个 `GPU_ID` 对应的实际设备。只要 PCIe 硬件配置不变，映射一般会保持一致。

## 使用多台 GPU 电脑

### 基本做法

- 选择一台作为**主机**，在其上安装 SwarmUI；如果这台电脑也有 GPU，可以同时使用本地后端。
- 在其他电脑上启动后端。通常最简单的方式是在每台电脑上安装 SwarmUI，将 Host 设置为 `0.0.0.0)，然后在主机添加对应的 `Swarm-API-Backend`。
- 也可以直接运行带有 `--listen` 的 ComfyUI 或 Auto WebUI，但这种方式不作为首选。
- 确认操作系统防火墙允许相应服务的入站连接。
- 在主机浏览器中先尝试打开其他电脑的局域网地址，确认网络连通后再配置 SwarmUI 后端。Windows 上可在远程电脑执行 `ipconfig` 查找局域网 IP，常见格式如 `192.168.0.10`。

在主机的 `Server → Backends` 页面，添加 **API By URL** 类型的后端，如 `Swarm-API-Backend` 或 `ComfyUI API By URL)，并填写浏览器中已经验证可访问的地址。

通常建议使用 `Swarm-API-Backend`。若使用 `ComfyUI API By URL`，请确认自己了解其工作方式，并正确安装和配置 [Swarm 自定义节点](https://github.com/mcmonkeyprojects/SwarmUI/tree/master/src/BuiltinExtensions/ComfyUIBackend/ExtraNodes)。

- **云主机/租用 GPU：** 配置方式类似，但应使用服务器的公网地址或受控分享地址，例如 Cloudflare Tunnel 或 ngrok 的链接。
- **与朋友或家人共享 GPU：** 通常需要可访问的共享地址。除非已进行严格的安全审计，否则不建议直接做路由器端口转发；VPN 等私有网络方式也可使用。

## 选择主后端的顺序

后端会按列表顺序分配任务：第一个后端处理第一个排队的生成任务；只有同时排队至少两个任务时，第二个后端才会被使用，后续后端依此类推。因此建议把速度最快的后端放在列表前面，较慢的后端放在后面。

## 多台电脑的详细配置

### 第一步：规划网络与机器

- 确定哪台机器作为主机。最好选择在你需要生成时通常会开机、而且容易访问模型和输出文件的机器。
- 其他所有参与工作的机器都称为远程工作节点。
- 如果熟悉路由器设置，可以为相关机器配置静态局域网 IP，避免 IP 变化造成连接中断。
- 如果使用异地机器或云服务器，提前规划连接方式。临时云环境可使用 Cloudflare 等临时链接；购买的服务器最好配置防火墙，只允许主机访问对应端口。

### 第二步：安装主机

1. 按照 [SwarmUI 上游安装指南](https://github.com/mcmonkeyprojects/SwarmUI#installing-on-windows) 安装主机。
2. 按需安装模型并测试本地生成。
3. 如果主机有多张 GPU，按照本页前面的说明配置多个后端。

![SwarmUI 界面示例](/.github/images/swarmui.jpg)

### 第三步：安装远程工作节点

在每台远程工作节点上重复以下操作：

1. 安装 SwarmUI。首次启动安装器询问 `Who is this SwarmUI installation going to be used by?` 时，选择 **Just Yourself, with LAN access**，以允许主机连接。
2. 为远程节点配置所需模型。所有机器必须具有相同的模型文件名和相同的相对目录结构。例如主机有 `OfficialStableDiffusion/sd_xl_base_1.0.safetensors)，远程节点也必须使用同样的路径。
3. 可以通过 USB 数据盘或文件传输工具同步模型。若远程机器有多张 GPU，同样需要分别配置后端。
4. 在远程机器上本地测试需要使用的功能。若会用到 GGUF 模型或 IP-Adapter，需分别测试，确认相应后端能力已安装。
5. 进入 `Server → Server Info`，找到 **Local Network** 并记下地址，通常类似 `http://192.168.50.17:7801`。

![局域网访问选项](/docs/images/lan-access.png)

如果显示 `This server is only accessible from this computer.`，说明尚未开放局域网访问。进入 `Server Configuration → Network → Host`，将值设为 `0.0.0.0)，保存并重启。若无法显示正确地址，需要检查路由器和本机网络配置。

同时在防火墙中允许 SwarmUI 程序或默认端口 `7801` 的 TCP 入站连接。

### 第四步：把远程节点添加到主机

1. 在主机浏览器中打开每台远程节点的局域网地址，例如 `http://192.168.50.17:7801`。
2. 如果网页无法打开，先确认远程节点上的 SwarmUI 正在运行、IP 地址正确且未被防火墙拦截。
3. 确认连接正常后，返回主机本地 UI（通常是 `http://localhost:7801`），进入 `Server → Backends`。
4. 添加 `Swarm-API-Backend)，并把 **Address** 设置为前面已验证的远程节点地址。可以把后端名称改成便于识别的名称，例如 `Living Room PC`。
5. 如果远程机器并非始终开机，可按需启用 `AllowIdle`；如果预期它始终在线，调试时建议先关闭该选项。
6. 点击 Save 并观察状态。绿色的 `Running backend:` 表示正常；红色的 `errored backend:` 表示连接或配置有问题；启用了 AllowIdle 后显示灰色/淡绿色的 idle 状态表示当前未连接；橙色的 `Disabled backend:` 通常说明配置格式不合法。

![添加 Swarm 后端](/docs/images/add-swarm-backend.png)

![配置远程 Swarm 后端](/docs/images/config-swarm-backend.png)

### 第五步：验证所有 GPU 都参与工作

1. 在主机打开 Generate 页面，将参数大致恢复默认，并选择所有机器上都存在的模型。
2. 在 `Core Parameters → Images` 中，将生成数量调大，例如设为 `10)。通常建议至少为 GPU 总数的两倍；例如 3 台电脑、每台 2 张 GPU，共 6 张 GPU，可将数量设为 12 或更多。
3. 输入简单提示词，例如 `a cat)，点击 Generate，等待首批任务完成。这会让请求分配到各后端并加载模型。
4. 如果网络较大，可以重复一次，直到不再出现 `Waiting on model load...`。
5. 再次生成并观察底部状态信息，例如 `10 current generations, 4 running, 6 queued`。

![队列状态示例](/docs/images/queue-running.png)

其中 `current generations` 是总任务数，`running` 是正在分配给后端 GPU 的任务数，`queued` 是等待分配的任务数，通常 running 与 queued 相加等于总数。预计耗时只是粗略估计。

注意观察 running 数量。原文的示例假设每张 GPU 默认可同时分配两个图像任务，因此 running 数约为预期 GPU 总数的两倍时，往往表示后端都在工作；数值偏低时请检查远程节点及后端过滤原因。

## 常见问题排查

### Swarm-API-Backend 无法连接，或原本正常后来断开

- 确认远程机器已开机。
- 确认远程机器上的 SwarmUI 正在运行。
- 检查远程机器的 **Local Network** 地址是否变化；若变化，需要在主机 Backends 页面更新地址。
- 先直接在远程机器本机打开其 UI；如果本机都打不开，问题通常在远程 SwarmUI 启动。
- 如果远程本机可用，但主机浏览器无法访问，检查 IP 和防火墙。

### 后端变红，但网络正常

1. 尝试关闭并重启 SwarmUI。
2. 进入 `Server → Logs)，把 `View Type` 设为 `Debug)，查看后端启动或连接的具体错误。

### 后端显示绿色运行中，但没有用于生成

1. 进入 `Server → Logs)，将 `View Type` 设为 `Verbose)，并在 Filter 中输入 `filter`。
2. 如果日志区域为空，先触发一次生成再返回日志页面。
3. 查找类似 `Filter out backend 1 as the request requires lora mylora.safetensors, but the backend does not have that lora` 的信息。它表示该任务需要远程后端缺少的模型或 LoRA，因此后端被过滤掉。

## 多张 GPU 上使用 Comfy 工作流

Comfy 标签页与主 Generate 页面工作方式不同。

- 多 GPU 使用时，更推荐点击 **Use This Workflow in Generate Tab**，然后回到主 Generate 页面排队生成。
- 如果必须直接使用 Comfy 标签页，点击左上角 **MultiGPU → Use All**，可以将多个排队请求分配到多个后端。输出区域可能很快被后续结果替换，需要使用队列历史查看此前输出。
- 如果希望同一个请求提交到多个后端：同样点击 **MultiGPU → Use All**，创建多个输出节点（例如 Preview Image），并在各输出节点的右键菜单中进入 Colors，为每个节点分配唯一颜色。每种颜色会对应不同 GPU，连接到这些输出节点前面的整段工作流也会被分配到对应 GPU。
- 注意：例如 `LoadCheckpoint → KSampler` 后接 5 个输出节点，会重复生成 5 份相同结果，并非自动分摊成不同图像。