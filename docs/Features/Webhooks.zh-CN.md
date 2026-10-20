# SwarmUI Webhook

> **语言：** [English 原文](Webhooks.md) · 简体中文

Webhook 会在服务端事件发生时向管理员配置的 URL 发送 HTTP POST 和 JSON。配置入口为 `Server → Server Configuration → Webhooks`。接收端必须由管理员控制并能从 SwarmUI 主机访问。

正文支持 `%prompt%` 等元数据占位符，规则大致遵循用户设置中的输出路径格式。`%image%` 会生成图像 URL；只有接收端可访问该 URL 时才有用。某些配置可能把提示词或图像发送到第三方，因此配置前应检查数据范围和目标服务。

Webhook 类型包括队列开始/结束、每次生成、手动生成、服务启动和关闭。部分 Hook 会等待远端响应并阻塞队列或关机；请设置可用的接收服务并考虑超时。Discord 图片上传的特殊格式和各事件的详细时序见英文原文。
