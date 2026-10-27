# SwarmUI 扩展开发

> **语言：** [English 原文](Making%20Extensions.md) · 简体中文

SwarmUI 扩展通常位于 `src/Extensions/`，内置扩展位于 `src/BuiltinExtensions/`。开始开发前应阅读英文原文中的项目结构和生命周期说明，并参考已有扩展的 API、页面资源和参数注册方式。

扩展应使用公开扩展点，限制自身文件范围；启动时注册的 API、回调和参数应在关闭时移除，避免重复初始化。所有 API 都应检查会话、权限和输入，避免返回原始异常、凭据或服务器路径。

EcomCraft 扩展位于 `src/Extensions/EcomCraft`。本仓库的 `AGENTS.md` 规定由维护者执行完整构建与运行验证；扩展开发者应在提交前按项目要求验证构建和启动流程。
