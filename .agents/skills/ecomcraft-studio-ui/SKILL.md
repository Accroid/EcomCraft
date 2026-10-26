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
- 保留现有元素 ID：`studio.js` 和 `Tests/*.test.cjs` 依赖它们。测试会截取 `    initTheme();` 之前的代码，并用只有 `value`、`hidden`、`disabled`、`textContent`、`classList.toggle` 的假元素运行；`updateControls` 等会被测试调用的函数只能使用这些属性。
- 文案：写用户能看懂的结果（“已删除 3 个作品”），加载状态以“…”结尾，错误说明原因和下一步；不用中点“·”拼接信息，不用全大写标签。
- 修改后运行：
  - `node --check src/Extensions/EcomCraft/Studio/studio.js`
  - `node src/Extensions/EcomCraft/Tests/<name>.test.cjs`（逐个运行全部 `*.test.cjs`）
  - `node src/Extensions/EcomCraft/Tests/studio-preview.cjs 7899` 后在浏览器打开 `http://127.0.0.1:7899/EcomCraft` 查看效果；Windows 上可以用 Edge 无头模式截图：`msedge --headless=new --user-data-dir=<临时目录> --window-size=1440,1000 --virtual-time-budget=9000 --screenshot=<文件> "http://127.0.0.1:7899/EcomCraft?shot=gallery#gallery"`
- 修改 `index.html` 引用的 `studio.css` / `studio.js` 后，更新链接里的 `?v=` 版本号，避免浏览器使用旧缓存。
