# SwarmUI 基础使用指南

> **语言：** [English 原文](Basic%20Usage.md) · 简体中文

欢迎使用 SwarmUI！本指南将带你完成安装后的初始配置，并生成第一张图片。

## 目录

- [安装](#安装)
- [配置](#配置)
- [生成第一张图片](#生成第一张图片)
- [细化与放大](#细化与放大)
- [调整参数与提示词](#调整参数与提示词)
- [添加更多后端](#添加更多后端)
- [使用网格生成器](#使用网格生成器)
- [组合功能与进阶使用](#组合功能与进阶使用)

## 安装

第一步是按照 [SwarmUI 上游安装指南](https://github.com/mcmonkeyprojects/SwarmUI#installing-on-windows) 安装程序。

基础安装完成后，通常会自动打开一个网页，让你选择初始安装设置。

- 安装器提供跳过设置、直接使用默认值的选项，新用户可以先采用默认配置。
- 也可以按需选择：
  - **主题：** 选择喜欢的外观。
  - **使用范围：** 通常选择“仅自己使用”或“自己及局域网”。
  - **后端：** 如果已配置 ComfyUI 或其他后端，可以跳过；否则可选择安装一个后端。本地使用通常推荐 ComfyUI。
  - **已有 ComfyUI：** 如果希望复用已有安装，可在安装器中选择不安装后端，然后进入 `Server → Backends`，添加 `ComfyUI Self-Starting`，填写已有的 `main.py` 路径并保存。还需要在 `Server Configuration` 中把 `Model Root` 指向现有模型目录，并把 `SDModelFolder` 设置为 ComfyUI 使用的 `checkpoints`。
  - **初始模型：** 选择要下载的模型；如果已经有可用模型，可以跳过。
- 确认配置并开始安装。

安装结束后，界面应自动跳转到主工作台。关闭 SwarmUI 时，可以关闭启动后打开的控制台窗口，或使用 `Server → Shutdown`。之后可以通过桌面快捷方式或安装目录中的启动脚本重新运行。

## 配置

如果已有 Stable Diffusion 或 ComfyUI 模型目录，需要确认模型路径；全新安装且采用默认设置的用户通常不必更改。

1. 打开 `Server` 页面，再进入 `Server Configuration`。
2. 将 `ModelRoot` 设置为模型所在的 `models` 目录。
3. 将 `SDModelFolder` 设置为对应 UI 的子目录：Auto WebUI 通常是 `Stable-diffusion`，ComfyUI 通常是 `checkpoints`。
4. 完成修改后点击页面底部的 **Save**。该按钮通常只在修改设置后显示。

![服务器模型路径设置](/docs/images/servermodelpath.png)

还可以在服务器配置靠上方找到 `Launch Mode`：

- `app`：以桌面应用方式启动；此模式目前仅确认在 Windows 上可用。
- `web`：启动时为你打开浏览器标签页。
- `none`：不自动打开浏览器，由你自行访问界面。

## 生成第一张图片

1. 打开顶部的 `Generate` 标签页。
2. 点击界面底部的 `Models` 标签页。
3. 在左侧目录树中选择模型目录。全新安装并下载官方模型时，可能会看到 `OfficialStableDiffusion` 目录。
4. 在模型列表中点击目标模型的图标。模型列表也可切换为卡片、缩略图或列表视图。
5. 也可以点击底部标签上方信息栏中的模型名称，从快速下拉列表中切换模型。
6. 如果底部面板遮挡了工作区，可以拖动面板分隔条来调整位置。

![拖动底部面板](/docs/images/draggable.png)

在界面底部中央找到提示词输入框 `Type your prompt here...`，输入例如 `a photo of a cat`。然后按 Enter，或点击紫色的 **Generate** 按钮。

![输入第一条提示词](/docs/images/yourfirstprompt.png)

生成成功后，图片会显示在屏幕中央。

如果失败，界面通常会显示错误消息；也可以打开 `Server → Logs` 查看日志。仍无法解决时，可到 [GitHub Issues](https://github.com/mcmonkeyprojects/SwarmUI/issues) 搜索或提交问题，或在 [Discord](https://discord.gg/q2y38cqjNw) 中求助。

## 细化与放大

若初次生成结果还需要改善，可以使用 **Refine / Upscale** 参数组。

1. 找到 `Refine / Upscale` 参数组（一般位于 `Init Image` 下方；屏幕较小时可能需要向下滚动），勾选组标题旁的开关。
2. 如果有专用细化模型，启用 `Refiner Model` 并选择模型。
3. 如果需要放大，将 `Refiner Upscale` 设为 `1.5` 或 `2`。使用 DiT 模型（如 SD3、Flux）时，建议启用 `Refiner Do Tiling`。
4. 按需设置其余参数。如不清楚含义，可点击参数旁的 `?` 查看说明。
5. 点击 Generate 开始生成。再次关闭参数组开关即可停用。

![细化与放大设置](/docs/images/refiners.png)

## 调整参数与提示词

### 参数

SwarmUI 将参数分组展示，目标是在提供控制能力的同时保持界面易用：

- 可以展开或收起参数组；部分组还可通过滑块切换启用状态。
- 大多数参数旁都有紫色的 `?` 图标，点击后可查看说明和示例。
- 调试参数时，建议先固定 `Seed`，避免其他随机因素干扰对比。

### 特殊提示词语法

提示词主要是文本，但 Prompt 和 Negative Prompt 也支持特殊语法：

- 使用 SDXL 或 UnClip 时，可以把图片拖入提示词框，使用 ReVision/ClipVision 将图像内容作为条件输入。
- 可使用 `<random:cat, dog>` 等语法随机选择内容。
- 在提示词框输入 `<` 可以查看可用提示。
- 详见[高级提示词语法（中文）](Features/Prompt%20Syntax.zh-CN.md)。

## 添加更多后端

如果希望并行生成更多图像，可以配置多个后端。可用算力来源包括本机其他 GPU、其他电脑、Google Colab、租用的 GPU 服务器或经网络配置允许访问的其他 SwarmUI 实例。

1. 进入 `Server → Backends`。
2. 选择后端类型并点击添加按钮。
3. 对于远程主机，需要先在远端部署 SwarmUI 并建立连接，再添加 `Swarm-API-Backend` 并填写远程 URL。
4. 如果一台电脑有多张 GPU，可以添加多个 `ComfyUI Self-Starting` 后端，并为每个后端配置不同的 GPU ID。
5. 保存后若未出现错误，通常就可以开始使用。一次生成请求会按后端队列分配；当批量任务足够多时，SwarmUI 会自动利用多个后端。
6. 后端可以随时编辑或删除，配置会在重启后保留。

注意：自定义 ComfyUI 工作流标签页本身只使用第一个 ComfyUI 实例。点击 **Use This Workflow** 并返回主 Generate 页面后，才会按正常方式利用可用后端。

## 使用网格生成器

需要比较不同参数组合时，可以使用 Grid Generator：

1. 打开底部的 `Tools` 标签页。
2. 在下拉框中选择 `Grid Generator`。
3. 可以为网格命名，然后选择需要比较的参数轴。
4. 手动填入参数值；支持示例值时，也可以点击右侧按钮自动填入。
5. 新增一个参数轴后，界面会自动出现新的轴设置，可以继续添加。
6. 点击顶部的 **Generate Grid** 开始生成。若需恢复普通的 **Generate Image** 按钮，将工具选择切回工具列表顶部的空白项。
7. 点击输出链接可查看网格生成进度和结果。

![网格生成器示例](/docs/images/grids.png)

## 组合功能与进阶使用

多项功能通常可以同时启用。例如，可组合 [IP-Adapter 图像提示](Features/ImagePrompting.md) 与图生视频功能；如果某一组合无法正常工作，可以向 [GitHub Issues](https://github.com/mcmonkeyprojects/SwarmUI/issues) 提交案例。

更多内容请阅读[高级使用指南（中文）](Advanced%20Usage.zh-CN.md)。