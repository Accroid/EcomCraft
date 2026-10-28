---
name: ecomcraft-studio-ui
description: 修改 EcomCraft 工作台页面（src/Extensions/EcomCraft/Studio 下的 index.html、studio.css、studio.js）时使用，包括新增按钮、视图、弹窗、样式和文案。
---

# EcomCraft 工作台界面

工作台是独立页面（`/EcomCraft`），不使用 SwarmUI 原生前端的 `util.js` / `site.js`。设计方案、配色、字体和每个控件的预期行为以 `docs/EcomCraft 工作台界面设计.zh-CN.md` 为准。

## 适用场景

- 修改工作台的布局、样式、文案或交互
- 新增或删除工作台中的按钮、表单项、弹窗、视图
- 排查工作台前端缺陷

## 操作说明

- 先读设计文档第 2 节（颜色、字体、原则）和第 3 节（控件功能清单）。新增控件时同步在第 3 节补一行。
- 颜色只用 `studio.css` 顶部的变量：绿幕色 `--key` 只表示“可操作 / 已选中”，指示灯色 `--tally` 只表示“需要注意”（生成中、繁忙、删除）。界面主体保持中性灰，不新增装饰色、渐变或通用阴影。
- 只有预览区背景纸可以有造型和自动动画；其他地方不加入场动画和卡片悬停上浮。
- 页面的内容安全策略只允许本站资源：不能引用在线字体、CDN 脚本或外部图片；图标用 `svgIcon()` 生成的内联 SVG，不用 emoji。
- 可点击元素必须是 `<button type="button">` 或 `<a>`；动态创建按钮时务必设置 `type = 'button'`，否则在表单里会触发提交。图标按钮要有 `aria-label`。
- 浏览器本地存储一律用 `readStorage` / `writeStorage`，不要直接访问 `localStorage`（隐私模式下可能抛异常，导致页面初始化中断）；需要可靠保存或多人共享的数据放在服务端。
- 按钮点击区域不小于 32px；文字按钮 `.link-btn` 已用内边距加负外边距扩大点击区，新增样式时不要覆盖。
- 弹窗打开时调用 `DialogFocus.enter`，关闭时调用 `DialogFocus.leave`，并在全局 `keydown` 中处理 Esc 和 `DialogFocus.trap`。
- 保留现有元素 ID：`studio.js` 依赖它们。
- **新文字必须走翻译表**：界面原文写中文，中文原文就是翻译 key，英文写进 `Studio/lang/en.js`。固定文字（HTML 中的文字和 `title`、`placeholder`、`aria-label`、`alt`，以及 JS 里直接写入页面的中文字符串）由 `i18n.js` 自动查表；带变量的文字必须写成 `tr('已删除 {0} 个作品。', count)`，不要用带中文的模板字符串或 `+` 拼接中文。`window.confirm`、`document.title` 等不经过页面元素的文字也要用 `tr()`。用户内容（提示词、作品名、工作流名）不翻译，需要排除的元素加 `data-i18n-skip`。服务端错误在英文模式下按错误码显示 `error:<code>` 条目，新增错误码时同步补英文。改完运行 `node --test src/Extensions/EcomCraft/Tests/studio-i18n.test.cjs`：缺译、多余条目、英文里混入中文或占位符不一致都会失败。
- 简易模式先选任务（F61）：顶部任务卡片决定 `#view-simple` 的 `data-simple-task`，第 2 步字段用 `data-tasks="..."`（空格分隔的任务名）标记，CSS 隐藏不属于当前任务的字段；新增第 2 步字段时必须标明它属于哪些任务，不属于所有任务的字段才加 `data-tasks`。
- 默认视图是 `simple.js` 的三步式简易模式，原创作视图是只给管理员的“专业模式”。简易模式里唯一的提示词输入是 F54 的中英双语提示词面板（`simple-prompt-zh` / `simple-prompt-en`），不要再加其他提示词框或采样器、LoRA、步数等专业参数（`Tests/simple-mode.test.cjs` 会检查）；发送给引擎的提示词始终是英文，修改过的中文由服务端翻译。`simple.js` 先于 `studio.js` 加载，通过 `SimpleMode.attach()` 拿到 `studio.js` 的工具函数。
- 切换语言不刷新页面：静态文字由 `i18n.js` 重新翻译；用 `tr()` 生成的动态文字需要在 `studio.js` 的 `onLanguageChange` 中重新渲染。
- 文案：写用户能看懂的结果（“已删除 3 个作品”），加载状态以“…”结尾，错误说明原因和下一步；不用中点“·”拼接信息，不用全大写标签。
- 修改后运行 `node --check src/Extensions/EcomCraft/Studio/studio.js` 和上面的翻译测试，再启动服务在浏览器中实际查看效果（中英文各看一遍）。
- `studio.js`、`studio.css`、`i18n.js`、`lang/en.js` 由 `StudioRoutes.cs` 以 `/EcomCraft/...` 路径提供（新增前端文件时要在 `StudioRoutes.Assets` 中登记），每次请求都从硬盘读取，和页面本身保持一致。不要改回 `/ExtensionFile/...`：SwarmUI 在 Release 模式下会把该路径的文件缓存在内存里直到重启，更新代码后会出现“新页面 + 旧脚本”，旧脚本一启动就报错，所有按钮失效。修改后仍要更新 `index.html` 链接里的 `?v=` 版本号，避免浏览器缓存。
- `index.html` 中的 `#boot-check` 是启动失败兜底提示，`studio.js` 初始化的最后一步把它隐藏；新增初始化步骤时放在隐藏它之前。
- 原生工作流 ID 必须在 `Utilities.StrictFilenameClean` 处理后保持不变（SwarmUI 生成时会再次清理名称，ID 一变就找不到工作流）。前端按第一个 `/` 拆出来源服务器分组。
