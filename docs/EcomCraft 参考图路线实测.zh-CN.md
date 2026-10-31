# EcomCraft 参考图路线实测手册

> **语言：** 简体中文 · [English](EcomCraft%20Reference%20Route%20Trial.md)
>
> **状态：** 手册已写好，实测（N12）未开始，第 8 节的结论待填 · **版本：** v1.0 · **日期：** 2026-10-31
>
> **相关文件：** [EcomCraft 实施计划](EcomCraft%20实施计划.zh-CN.md)（T82、N12、G1）、[EcomCraft 参考图需求](EcomCraft%20参考图需求.zh-CN.md)（第 3.3 节路线 E）、[记录表](EcomCraft%20参考图路线实测记录表.csv)

## 1. 这次实测要回答什么

当前的“看图写提示词 → 局部重绘 → 贴回”效果差。这次实测决定：

1. 换成哪条路线，用哪个模型和量化版本。
2. 是否需要先清理参考图。
3. Windows 和 Mac 能否用同一套配方。

结论决定阶段 2 怎么开发。**G1 没有通过，就不开始 T83 及后续任务。**

> **注意：** 本手册的方案编号 R0–R5 与重构任务编号 R00–R24 无关。

**这份手册没有经过实际运行。** 编写时没有显卡，所以这里不提供节点图；下面的提示词和参数只是起点，要以实测为准。

## 2. 开始前要准备什么

| 事项 | 要求 |
|---|---|
| 测试机（N11） | 一台 Windows + NVIDIA 8GB 以上；一台 Apple 芯片 Mac，24GB 以上统一内存。显存或内存尽量接近最低配置，否则结果不能代表最低档 |
| 主攻品类（N09） | 建议个护、家电、日用这类硬包装商品 |
| 商品（N03） | 约 15 个透明底 PNG，要带清晰的 Logo 和文字，形状不要全都一样 |
| 参考图 | 每个商品配 2 张：1 张简单（背景干净、道具少），1 张复杂（多道具或真实场景）。共约 30 组。尽量让参考图里的主体角度与商品接近 |
| 评分人 | 至少一位不会修图的运营，负责判断“可直接上架” |
| 软件 | 直接在 ComfyUI 里跑，不经过 EcomCraft 平台（R0 除外）。把 ComfyUI 更新到最新，记下版本号 |

商品图、参考图和生成结果都放在仓库之外，**不要提交到仓库**。这些图只用于内部评估，不要对外发布。

## 3. 方案

| 编号 | 方案 | 模型 | 怎么跑 |
|---|---|---|---|
| R0 | 当前路线（基线） | 现有默认模型 | 在 EcomCraft 里用“照参考图做”出图 |
| R1 | 参考图 + 商品图直接编辑 | Qwen-Image 2.1 | 从 ComfyUI 官方模板开始，图 1 放参考图，图 2 放商品图，用第 4 节的提示词 |
| R2 | 先清理参考图，再放入商品 | Qwen-Image 2.1 | 先用第 4 节的清理提示词去掉旧商品和文字，再把结果当图 1，按 R1 再跑一次 |
| R3 | 同 R1 | Qwen-Image-Edit-2511 | 同 R1，换模型 |
| R4 | 同 R1 | FLUX.2 klein 4B | 同 R1，换模型 |
| R5 | 路线 B：平台贴图 + 低强度融合 | R1–R4 中最好的 | 手动把商品摆到清理后的参考图上，再低强度重绘整张图（平台现有融合用 0.35，可作为起点） |

**模型资料：** [Qwen-Image 2.1](https://github.com/QwenLM/Qwen-Image-2.1) · [ComfyUI 关于 2.1 的说明](https://blog.comfy.org/p/qwen-image-21-in-comfyui-open-weight) · [Qwen-Image-Edit-2511](https://huggingface.co/Qwen/Qwen-Image-Edit-2511) · [ComfyUI 的 2511 教程](https://docs.comfy.org/tutorials/image/qwen/qwen-image-edit-2511.md) · [FLUX.2 klein](https://bfl.ai/blog/flux2-klein-towards-interactive-visual-intelligence)

**许可：** Qwen-Image 2.1 官方许可仅限研究和评估，这次内部评估符合许可。用户已决定商用问题发布前再处理，不影响本次选型。

## 4. 提示词（起点）

可以调整，但最终版要记进记录表，改过的版本要编号（P1、P2……）。

**融合（R1、R2 第二步、R3、R4）：**

```
Replace the main product in image 1 with the product from image 2.
Keep the composition, camera angle, background, props, lighting and color mood of image 1.
Keep the shape, colors, logo and printed text of the product from image 2 exactly unchanged.
Match the product's lighting, shadow and perspective to the scene. Remove all other text, logos and watermarks.
```

**清理（R2 第一步、R5）：**

```
Remove the main product and all text, logos and watermarks from this image.
Keep the background, surface, props, lighting and camera angle unchanged. Fill the removed area naturally.
```

## 5. 怎么跑

**先试跑，再跑全量。** 全量是 15 个商品 × 2 张参考 × 6 个方案 × 2 个平台，约 360 张。

1. **试跑（Windows）：** 取 3 个商品 × 2 张参考，跑 R0、R1、R3、R4。把模型跑通，定下步数和提示词。明显不可用的方案（比如商品完全走样、反复崩溃）在这里淘汰，记下原因。
2. **全量（Windows）：** 剩下的方案加上 R2、R5，跑完全部约 30 组。
3. **Mac：** 只跑 Windows 上最好的 2 个方案。跑不通的模型也要记下来：只能在一个平台运行的模型不能作为默认。Mac 上跑不了 R0 时，R0 的对比图用 Windows 的结果，并在备注里写明。

**保持可比：**

- 同一组的所有方案用同样的商品图、参考图和种子（建议固定为 1）。
- 输出约 100 万像素，和平台出图一致。
- 耗时记第二次运行的结果，排除首次加载模型的时间。ComfyUI 控制台会打印 `Prompt executed in`。
- 峰值显存看 `nvidia-smi` 或任务管理器；Mac 看活动监视器的内存占用。
- 文件命名：`商品ID_参考ID_方案_平台.png`，例如 `P03_R2c_R1_win.png`。

## 6. 怎么评分

把结果填进[记录表](EcomCraft%20参考图路线实测记录表.csv)，每张图一行。

| 指标 | 怎么记 |
|---|---|
| 构图相似 | 1–5 分。5：布局、机位和光线几乎一致；3：大体相同，细节不同；1：像另一张图 |
| 商品形状和颜色 | 1–5 分。5：和原图一致；3：有差异但是同一件商品；1：不是这件商品 |
| Logo 和文字可辨 | 是 / 否。放大看，文字错一个字也算“否” |
| 融合自然 | 1–5 分。5：看不出合成；3：仔细看能发现；1：明显是贴上去的 |
| 可直接上架 | 是 / 否。由不会修图的运营判断 |
| 单张耗时、峰值显存或内存 | 实测值 |
| 崩溃或黑图 | 是 / 否 |

**运营评分时不要告诉对方图来自哪个方案。** 先给运营看商品原图和参考图，再逐张问：“这张图能直接放到店铺上架吗？”

## 7. 怎么判断（G1）

每个方案、每个平台各算一次：**可直接上架比例 = “是”的张数 ÷ 总张数**。

建议的通过线（可以调整）：

| 指标 | Windows 最低档 | Mac 最低档 |
|---|---|---|
| 可直接上架比例 | ≥ 60% | ≥ 60% |
| 明显优于 R0 | 是 | 是 |
| 单张耗时 | ≤ 3 分钟 | ≤ 10 分钟 |
| 崩溃或黑图 | 无 | 无 |

“明显优于 R0”建议这样判断：可直接上架比例高于 R0，而且运营并排比较时，多数组选新方案。

| 结果 | 下一步 |
|---|---|
| 达标 | 在第 8 节写明结论，进入阶段 2 |
| 只有 Windows 达标 | 由用户决定是否调整 Mac 的目标 |
| 全部不达标 | 停止，汇报数据，再讨论路线 B、路线 D 或放宽目标 |

## 8. 结论（实测后填写）

| 项目 | 结论 |
|---|---|
| 实测日期和测试机 | |
| ComfyUI 版本 | |
| 选定路线 | |
| 模型和量化版本（Windows） | |
| 模型和量化版本（Mac） | |
| 是否需要清理步骤 | |
| 最终提示词版本和全文 | |
| 步数、强度等关键参数 | |
| 各方案可直接上架比例 | |
| G1 是否通过 | |
| 遗留问题（比如哪类商品效果差） | |

## 9. 变更记录

| 日期 | 版本 | 变更 |
|---|---|---|
| 2026-10-31 | v1.0 | 初版（T82）：样本、方案、提示词、评分和 G1 判断方法 |
