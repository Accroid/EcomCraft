# 高级提示词语法

> **语言：** [English 原文](Prompt%20Syntax.md) · 简体中文

SwarmUI 除了普通的文本提示词外，还支持多种内嵌语法，用于调整权重、随机选择内容、复用变量、指定局部提示词以及控制视频生成阶段。下文示例中的语法标记请保持原样输入。

## 权重（Weighting）

![提示词权重示例](/docs/images/prompt-weight.jpg)

- 可使用提示词权重，例如 `an <weight[1.1]:orange> cat` 或 `an <weight[1.5]:orange> cat`，让模型对某一段文字给予更高或更低的关注度。数值大于 `1` 表示增强，小于 `1`（例如 `0.5`）表示减弱。
  - 也可以选中提示词文字，然后按住 Ctrl 并按向上/向下方向键来调整权重。
  - 此功能默认假设使用标准 ComfyUI 后端。
  - 不同模型对权重的响应差别很大：基于 CLIP 的模型（例如 Stable Diffusion）通常效果较好；基于 T5 的模型可能效果较差；较新的 LLM 文本编码器会把权重作为 conditioning 的直接倍数，结果因模型而异；有些模型可能完全忽略权重。部分模型（例如 Krea 2）另有专用处理逻辑。
  - 对 SD1、SDXL 等传统模型，以及 Anima 等适用模型，如果用户设置中默认启用的 **Parse Alternative Prompt Syntaxes** 已打开，也会解析 Comfy/Auto1111 风格的 `(orange:1.5)` 语法。

## 交替内容（Alternating）

![交替提示词示例](/docs/images/alternate-cat-dog.jpg)

- 使用 `<alternate:cat, dog>` 可以让生成过程在每个采样步骤中交替使用 `cat` 和 `dog`，使两个概念融合或混合。
  - 与 `random` 类似，条目也可以用 `|` 或 `||` 分隔，例如 `<alternate:cat || dog>`。条目数量不限，例如 `<alternate:cat, dog, horse, wolf, taco>` 会每 5 步循环一次。
  - 简写形式为 `<alt:cat,dog>`。
  - 若用户设置中的 **Parse Alternative Prompt Syntaxes** 已启用，也会解析 Auto1111 风格的 `[cat|dog]`。
  - 可以在交替条目中使用 `<lora:...>`，让 LoRA 只应用于相应步骤；这样做可能会降低性能。

## 从某一步切换内容（From-To）

![From-To 示例](/docs/images/fromto-cat-dog.jpg)

- 使用 `<fromto[#]:before, after>` 可以在指定采样步之后，将提示词从前一段切换为后一段。
  - 时间点可使用步数（例如 `10`，表示第 10 步）或比例（例如 `0.5`，表示进行到一半）。
  - 条目可使用 `|` 或 `||` 分隔，但必须恰好有两个条目。
  - 例如 `<fromto[0.5]:cat, dog>` 会在生成进行到一半时从 `cat` 切换为 `dog`。
  - 若 **Parse Alternative Prompt Syntaxes** 已启用，也会解析 Auto1111 的 `[cat:dog:0.5]`。
  - 可以在其中使用 `<lora:...>`，让 LoRA 仅应用于前一段或后一段对应的步骤；这可能会降低性能。

## 随机选择（Random）

![随机提示词示例](/docs/images/random-cats.jpg)

- 使用 `<random:red, blue, purple>` 可在每次生成时从列表中随机选择一个条目。
  - 随机结果由主 Seed 决定；如果 Seed 固定，随机选择也会固定。
    - 可通过 `Wildcard Seed` 参数单独控制随机选择。
    - 如果更改 Seed 后随机内容仍不变化，请检查是否意外启用了 `Wildcard Seed`。
  - 条目可用逗号 `,`、竖线 `|` 或双竖线 `||` 分隔。系统会选择最适合作为分隔符的字符。例如条目本身含逗号时，可用 `|`：`<random:red|blue|purple>`。
  - 条目可使用数字范围，例如 `<random:1-3, blue>` 会产生 `1`、`2`、`3` 或 `blue`。
    - 也支持小数范围，例如 `<random:0.8-1.2>` 会选择 `0.8`、`0.9`、`1.0`、`1.1` 或 `1.2`。小数位数与输入范围一致。
  - 使用 `<random[1-3]:red, blue, purple>` 可随机选择 1 到 3 个条目，例如得到 `red blue`、`red blue purple` 或 `blue`。
    - 可以在数量范围后添加逗号，例如 `<random[1-3,]:red, blue, purple>`，表示输出条目之间使用逗号。
    - 在可能的情况下，重复选择会尽量避免同一条目；当要求数量大于条目总数时则无法避免重复。

## 通配符（Wildcards）

![通配符示例](/docs/images/wildcards-cats.jpg)

- 使用 `<wildcard:my/wildcard/name>` 可从通配符文件中随机选择内容。通配符文件本质上是预先保存的文本文件，每行一个候选条目。
  - 可以在界面底部的 **Wildcards** 标签页中编辑。
  - 也可以将其他 UI 导出的文本集合放入 `Data/Wildcards` 目录进行导入。
  - 通配符支持与 `random` 相同的多选语法，例如 `<wildcard[1-3]:animals>` 可能得到 `cat dog` 或 `elephant leopard dog`。
  - 简写形式为 `<wc:my/wildcard/name>`。
  - 随机结果同样由主 Seed 决定；若要单独控制，请使用 `Wildcard Seed` 参数。
  - 可排除特定条目，例如 `<wildcard:animals,not=cat,dog>`。
    - 可与[变量](#variables)组合，例如 `a photo of a <setvar[animal]:<wildcard:animals>> playing with a <wildcard:animals,not=<var:animal>>`，从而避免同一动物同时出现在两个位置。
  - 在提示词框中输入类似 `<wildcard:animals:` 的内容，可以触发该通配符条目的自动补全；例如输入 `<wildcard:animals:do` 时会出现 `dog`。
    - 这只是前端输入便利功能，用于从通配符中快速挑选特定行。不要把末尾多余的冒号当作正常语法提交。

<a id="variables"></a>
## 变量（Variables）

![变量示例](/docs/images/setvar-cat.jpg)

- 可以在一条提示词中保存并重用变量，尤其适合重复使用随机结果或通配符。
  - 使用 `<setvar[var_name,emit]:data>` 保存变量；`emit` 可为 `true` 或 `false`，默认是 `true`。
    - 例如：`<setvar[color]:<random:red, blue, purple>>`
  - 使用 `<var:var_name>` 读取变量，例如 `<var:color>`。
  - 完整示例：`a photo of a woman with <setvar[color]:<random:blonde, black, red, blue, green, rainbow>> hair standing in the middle of a wide open street. She is smiling and waving at the camera, with beautiful sunlight glinting through her <var:color> hair. <segment:face and hair> extremely detailed close up shot of a woman with shiny <var:color> hair`
    - 这个例子即使在 segment 语法中也会读取同一个变量，因此头发颜色在整次生成中保持一致。
  - 如果不希望 `setvar` 把变量值本身输出到提示词中，可将 `emit` 设为 `false`，例如 `<setvar[color,false]:red>`。
    - `a <setvar[color]:red> dog with <var[color]> eyes` 会变成 `a red dog with red eyes`。
    - `<setvar[color,false]:red> a dog with <var[color]> eyes` 则会变成 `a dog with red eyes`。

## 宏（Macros）

![宏示例](/docs/images/setmacro-cat.jpg)

- 宏也可以保存并复用一段提示词语法，适合动态重复复杂的随机表达式。
  - 使用 `<setmacro[macro_name,emit]:data>` 保存宏，`emit` 默认是 `true`。
    - 例如：`<setmacro[color]:<random:red, blue, purple>>`
  - 使用 `<macro:macro_name>` 调用，例如 `in a room with <macro:color> walls, <macro:color> floors, and <macro:color> carpet`。
  - 与变量不同，宏在定义时不会立即求值，而是在每次通过 `<macro:...>` 调用时才展开。
  - 完整示例：`Photo of a woman with <setmacro[color]:<random:red|white|green|blue|purple|orange|black|brown>> hair, <macro:color> shirt, <macro:color> pants`。头发、上衣和裤子会分别随机选择颜色。
  - 若不希望定义宏时输出其值，可设 `emit` 为 `false`，用法与变量类似。

## 触发词（Trigger Phrase）

![触发词示例](/docs/images/trigger-arcane-cat.jpg)

- 如果模型或当前 LoRA 的元数据中包含触发词，可以使用 `<trigger>` 自动将其插入提示词。
  - 若加载的多个模型都含有触发词，会用逗号组合，例如 `cat` 和 `dog` 会变成 `cat, dog`。
  - 触发词中的分号会自动替换为逗号，例如 `cat; dog` 会变成 `cat, dog`。
  - 这主要是一个简单的自动填充功能，适合网格或批量生成，并非通用的格式处理器。如果需要精确控制格式，建议直接复制触发词。
  - 如果没有可用触发词，则会替换为空内容。

## 重复文本（Repeat）

![重复示例](/docs/images/repeat-random.jpg)

- 使用 `<repeat[3]:cat>` 可以重复输出文本，例如得到 `cat cat cat`。
  - 也可以将其与随机语法结合，例如 `<repeat[1-3]: <random:cat, dog>>`，随机输出 1 到 3 个 `cat` 或 `dog`。

## Textual Inversion 嵌入

- 使用 `<embed:filename>` 将 Textual Inversion 嵌入添加到正向或负向提示词。
  - 嵌入文件应放在 `(SwarmUI)/Models/Embeddings` 目录。
  - 这种嵌入在 SDv1 时代较为常见，在新模型中使用较少。

## LoRA

![LoRA 示例](/docs/images/lora-arcane-cat.jpg)

- 可使用 `<lora:filename>` 启用 LoRA，或使用 `<lora:filename:weight>` 指定权重。
  - 通常更推荐在页面底部的 GUI 中选择 LoRA。
  - LoRA 标签在提示词中的位置通常没有影响；LoRA 本身并不是普通文本提示词功能，这个写法主要方便熟悉 Auto WebUI 的用户。
  - 若把 LoRA 标签放在 `<fromto[...]:...>` 或 `<alternate:...>` 内，LoRA 只应用于对应步骤。
  - 对 `<segment:...>` 或 `<object:...>` 而言，位置确实会影响行为：其中的 LoRA 仅作用于对应的分段或对象。
  - `weight` 是倍数：`1` 为默认值，`0.5` 表示减弱一半，`2` 表示增强一倍。通常大于 `2` 的值可能严重损害画质。
  - 还可以使用 `<lora:filename:backbone_weight:textenc_weight>`，分别指定模型主干（UNet/DiT）与文本编码器的权重。

## 预设（Presets）

![预设示例](/docs/images/style-preset-cats.jpg)

- 使用 `<preset:presetname>` 将预设内容插入提示词。
  - 对 LoRA 通常更建议使用界面选择器；此语法主要用于动态组合预设，例如 `<preset:<random:a, b>>`。
  - 简写形式为 `<p:presetname>`。

## 参数（Params）

- 可通过 `<param[paramName]:paramValue>` 直接设置生成参数。
  - 例如 `<param[CFG Scale]:1>` 或 `<param[cfgscale]:1>` 都可以将 CFG Scale 设为 1。
    - 参数名不区分大小写，并会忽略空格。可以直接从 UI、API 结构或元数据中复制参数名称，只要拼写正确即可。
  - 可以嵌套其他语法，例如 `<param[cfgscale]:<random:1,2,3>>` 会随机设置 CFG Scale。
  - 这适用于 SwarmUI Generate 页面左侧列出的各种生成参数。
  - 部分参数可以指定作用阶段，例如 `<refiner>`、`<base>`、`<video>`、`<segment:...>` 或 `<extend:...>`。
    - 支持分阶段控制的参数包括 `CFG Scale`、`Steps`、`Sampler`、`Scheduler`、`Seed`、`Variation Seed` 与 `Variation Seed Strength`。
    - 例如，`<video> <param[cfgscale]:5>` 只会把视频阶段的 CFG Scale 设为 `5`。
- 也可以读取参数值：使用 `<param:paramName>`，例如 `<param:CFG Scale>` 会展开为当前 CFG Scale 值。

## 自动分割与细化

![分割与细化示例](/docs/images/segment-ref.jpg)

- 使用 `<segment:texthere>`，可以通过 CLIP 分割自动细化图像中的某个区域。
  - 它类似“修复人脸”，但适用范围更广，可以指定要细化的对象以及细化方式。
  - 也可写为 `<segment:texthere,creativity,threshold>`。其中 `creativity` 是重绘强度，`threshold` 是分割最低阈值；例如 `<segment:face,0.6,0.5>`，默认 creativity 为 `0.6`、threshold 为 `0.5`。
  - 可查看[功能介绍](https://github.com/Stability-AI/StableSwarmUI/discussions/11#discussioncomment-7236821)了解更多。
  - 首次使用 CLIPSeg 时，SwarmUI 会自动下载 [clipseg-rd64-refined-fp16 模型](https://huggingface.co/mcmonkey/clipseg-rd64-refined-fp16)。
  - 可在 segment 的提示词区域中嵌入 `<lora:...>`，让 LoRA 作用于该分段。
  - 也可以把 `texthere` 替换成 `yolo-modelnamehere`，使用 YOLOv8 分割模型（类似 ADetailer 的处理方式）。
    - YOLO 模型放在 `(Swarm)/Models/yolov8` 目录；可参考[示例模型列表](https://github.com/hben35096/assets/releases/)。
    - 可在模型名后加 `-1`、`-2` 等，选择按从左到右排序的特定检测结果。若不指定索引，多个目标可能会被合并处理。
    - 对 YOLO 模型，可在末尾附加 `,<creativity>,<threshold>` 来设置参数，例如 `<segment:yolo-face_yolov8m-seg_60.pt-1,0.8,0.25>`。
    - YOLO 模型的默认 confidence threshold 为 `0.25`，与 CLIPSeg 常用阈值不同；YOLO 也没有 CLIPSeg 那样的最大阈值。
    - 若模型支持多个类别，可在模型名后用 `:<classes>:` 筛选类别。类别可以是 ID 或名称，以逗号分隔，例如 `<segment:yolo-modelnamehere:0,apple,2:,0.8,0.25>`。
  - 可以用 `|` 将多个区域合并为一个分段，例如 `<segment:face|hair>` 会把所有检测到的脸部和头发作为一组进行细化；YOLO 与 CLIPSeg 检测结果也可以组合。
  - 高级参数组 **Segment Refining** 可进一步控制：
    - `Segment Model`：设置分割处理使用的基础模型。
    - `Save Segment Mask`：保存生成遮罩的预览图。
    - `Segment Target Resolution`：指定分段处理的目标分辨率。有用户反馈，对 SDXL 等模型，人脸修复使用 `1248x1824` 效果不错。
    - 其他选项请点击 UI 参数旁的 `?`。

## 清除背景与透明化（Clear）

![透明背景示例](/docs/images/clear-cat.png)

- 使用 `<clear:texthere>` 可自动清除图像中的部分内容并转为透明。语法与上面的 `segment` 类似。由于需要透明通道，应使用 PNG 而不是 JPG。
  - 例如 `<clear:background>` 可尝试清除背景。
  - 专用的 **Remove Background** 参数通常比自动分割清除更合适。

## 分段标记（Break）

- 使用 `<break>` 手动指定 CLIP 文本分段位置（在 Auto WebUI 中对应 `BREAK`）。
  - CLIP 是将文本输入转换为模型 conditioning 的文本编码器。对许多 CLIP 模型而言，文本会按每段 75 个 token 分块编码。
  - 如果提示词长度超过 75 token，系统会自动切分；自动切分点可能打断一个完整概念。例如“a photo of a big fluffy dog”在简化示例中可能被切为“a photo of a big fluffy”和“dog”。
  - 用 `<break>` 可以手动控制切分点，例如 `a photo <break> big fluffy dog`，将风格信息和主体信息放在不同分段中。

<a id="regional-prompting"></a>
## 区域提示词（Regional Prompting）

![区域提示词示例](/docs/images/sdxl_catdog.jpg)

*上图在 SDXL 中使用类似 `a photo of a cat/dog mix <region:0,0,1,0.5,1> a photo of a cat <region:0,0.5,1,0.5,1> a photo of a dog` 的提示词生成。*

- 使用 `<region:x,y,width,height,strength> prompt here`，可以为图像某个区域单独指定提示词。
  - X、Y、Width、Height 都是图像尺寸比例，例如 `0.5` 表示图像宽度或高度的一半。
  - 例如 `<region:0,0,0.5,1> a cat` 表示让猫出现在图像左侧半幅、贯穿整个高度的区域内。
  - `strength` 表示区域提示词相对于全局提示词的影响强度。
  - 使用 `<region:background>` 可指定只作用于未被其他区域覆盖的背景部分。
  - 很小的区域可能会被模型忽略；区域提示功能的作用强度相对较弱。
  - 不同模型的表现差异很大。MM-DiT 模型（例如 SD3、Flux）可能只在早期步骤处理区域提示，后期逐渐忽略；SDXL 等模型通常会更强地响应区域提示。
  - 区域提示词中也可以使用 `<lora:` 语法，为该区域单独添加 LoRA。

## 区域对象提示（Regional Object Prompting）

![区域对象提示示例](/docs/images/sdxl_object_catdog.jpg)

*上图示例使用 `a photo of a cat/dog mix <object:0,0,1,0.5,0.1,0.5> a photo of a cat <object:0,0.5,1,0.5,0.1,0.5> a photo of a dog`。*

- 使用 `<object:x,y,width,height,strength,strength2> prompt here` 可为指定区域提供独立提示词，并在该区域上执行自动局部重绘。
  - `strength`（第一个强度值）是区域提示词强度，参见[区域提示词](#regional-prompting)。
  - `strength2` 控制自动重绘的 creativity。
  - 自动重绘可能改善对象的细节，尤其对较小区域有帮助，但也可能产生意外结果。
  - 可以在对象提示词中使用全局功能语法，例如 `<lora:`，让 LoRA 在对象重绘阶段应用。

## 基础模型提示词（Base）

- 使用 `<base>` 后的文本只会发送给基础模型，不会发送给 Refiner、图生视频等后续模型。
  - 其中也可以使用 `<lora:>` 为基础模型添加专属 LoRA。

## 细化模型提示词（Refiner）

- 使用 `<refiner>` 后的文本只会发送给 Refiner/放大阶段使用的模型。
  - 其中也可以使用 `<lora:>` 为 Refiner 添加专属 LoRA。

## 视频提示词（Video）

- 使用图生视频时，可通过 `<video>` 指定单独用于图生视频模型的提示词。
  - 例如：`a photo of a cat <video> the cat walks forward`。
  - 其中也可以使用 `<lora:>` 为视频模型添加专属 LoRA。
- 使用 Wan 2.2 等具有模型切换阶段的图生视频流程时，可使用 `<videoswap>` 为切换阶段提供单独提示词。
  - `<video>` 仅发送给主要图生视频模型，`<videoswap>` 仅发送给切换阶段模型。
  - 可使用 `<lora:>` 为视频切换模型单独添加 LoRA。

## 延长视频（Video Extend）

- 使用 `<extend:frames>`，可以通过图生视频模型按指定帧数延长视频。
  - 这仍是实验性功能，能力有限，仅部分模型表现较好，而且常会出现画质问题。
  - 例如 `<extend:33>` 表示延长 33 帧。
  - 使用前需要在 **Video Extend** 参数组中设置参数，至少要指定 `Video Extend Model`。
  - Overlap 必须小于延长帧数的三分之一。对许多图生视频模型，除非模型专门训练过重叠帧，使用 `1` 帧重叠可能更合适。
  - 同时检查 **Advanced Video** 参数。
  - 部分模型可以在 `Other Fixes → Trim Video End Frames` 中裁掉末尾帧；不要使用 `Trim Start`。

示例：

```text
a video of a cat standing in a forest
<extend:81> the cat starts running through the forest
<extend:81> the cat runs up to a river
<extend:81> the cat stops running at the river edge, and drinks from it
```

## 注释（Comment）

- 使用 `<comment:stuff here>` 可以在提示词框里添加个人备注；备注不会传入实际提示词。

## 仅用于细化/放大的提示词补充

- 使用 Refine/Upscale 参数组时，可在提示词中添加 `<refiner> some prompt here`，让这一段只应用到细化阶段。
  - 这部分也支持 `<lora:...>`，可将 LoRA 专门应用于 Refiner。