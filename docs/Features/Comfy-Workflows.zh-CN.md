# SwarmUI 自定义 ComfyUI 工作流

> **语言：** [English 原文](Comfy-Workflows.md) · 简体中文

## 在原生工作流编辑器中使用

1. 确认已配置并启动 `ComfyUI Self-Starting` 后端。
2. 打开顶部的 `Comfy Workflow Editor`。
3. 要加载已有的 ComfyUI 工作流，可在画布中使用 ComfyUI 的 **Load** 功能选择工作流 JSON，或拖入包含工作流数据的 ComfyUI 输出图片；也可点 **Browse Workflows** 打开已保存到 SwarmUI 的工作流。
4. 用 `Primitive` 节点为需要让网页修改的输入设置清晰标题，并检查模型、节点和依赖。
5. 确认输出节点符合工作流用途，然后点击 **Use This Workflow In Generate Tab** 应用到生成页。

应用后，SwarmUI 会把已识别的输入映射为原生参数；其他已定义输入会按工作流参数显示。修改工作流后需要再次点击 **Use This Workflow**，否则生成页仍使用旧版本。

## 在 EcomCraft 工作台发布

管理员确认工作流可以正常生成后，点击 **Save Workflow**，填写名称和说明，启用 **Enable In Simple Tab** 并保存。EcomCraft 只向有 `EcomCraft Published Workflow Use` 权限的客户机显示这类工作流，并通过 `EcomCraftStudioCatalog` 返回允许填写的 `inputs`。

客户机提交 `EcomCraftStudioGenerate` 时只传 `workflow_id` 和 `workflow_params`，不要提交原始工作流 JSON。完整的请求示例、参数校验和本机节点限制见 [EcomCraft ComfyUI 工作流调用](../EcomCraft%20ComfyUI%20工作流.zh-CN.md)。

## 恢复标准工作流

在生成页禁用自定义工作流即可恢复标准工作流。EcomCraft 工作台不会接受客户机临时上传的任意节点图；需要调试或编辑节点时，请使用原生工作流编辑器并遵守账号权限设置。
