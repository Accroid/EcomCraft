# EcomCraft 中调用 ComfyUI 工作流

EcomCraft 不会把任意 ComfyUI JSON 直接暴露给普通客户机。推荐由管理员在 SwarmUI 的 ComfyUI 工作流编辑器中打开已有工作流，确认依赖和参数后，启用 **Enable in Simple**（在简单工作台中启用）并保存。

## 网页端

1. 确认已配置 `ComfyUI Self-Starting` 后端，并且状态为 `RUNNING`。
2. 进入 EcomCraft Studio，刷新页面。
3. 在“创作工作流”下拉框中选择管理员发布的工作流。
4. 填写工作流显示的输入参数和标准提示词，点击生成。

工作流下拉框只显示同时满足以下条件的工作流：当前账号有使用权限、工作流已启用简单工作台、工作流 JSON 和参数定义有效。图片和视频工作流会按类型分别显示。

## 客户机 API

先建立 SwarmUI 会话，然后请求 `POST /API/EcomCraftStudioGenerate`。请求只提交工作流 ID 和已发布参数，不要提交原始 ComfyUI JSON：

```json
{
  "session_id": "当前会话 ID",
  "source": "local",
  "kind": "image",
  "mode": "text2img",
  "workflow_id": "Examples/my_product_workflow",
  "workflow_params": {
    "product_style": "clean studio",
    "reference_image": "data:image/png;base64,..."
  },
  "model": "官方模型名称",
  "prompt": "一瓶护肤品，商业产品摄影",
  "negativeprompt": "模糊，低清晰度",
  "width": 1024,
  "height": 1024,
  "images": 1
}
```

可选字段包括 `seed`、`steps`、`cfgscale`、`sampler` 和 `scheduler`。视频工作流使用 `kind: "video"`，并按工作台要求提交 `text2videoframes`，每批只能生成一个视频。

调用前可读取 `POST /API/EcomCraftStudioCatalog`，工作流列表位于返回值的 `workflows` 字段，其中 `inputs` 描述每个可填写参数的 ID、类型、默认值和允许选项。客户机应按这个列表构造 `workflow_params`，不要猜测参数名。

## 安全边界

- 工作台只选择本机自启动 ComfyUI，不会因为本机不可用而自动切换远程或云端节点。
- 服务端会重新检查工作流是否已发布、参数是否在白名单中、模型和图片是否属于当前账号可用范围。
- 发布工作流中的默认图片不会自动复用；每个客户机必须重新上传自己的图片参数。
- 需要编辑原始节点图、使用未发布工作流或调试节点时，请使用原生 SwarmUI 的 ComfyUI 工作流页面，并授予相应的管理员权限。
