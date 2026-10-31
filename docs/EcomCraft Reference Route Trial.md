# EcomCraft Reference Route Trial Handbook

> **Language:** English · [简体中文](EcomCraft%20参考图路线实测.zh-CN.md)
>
> **Status:** handbook written; the trial (N12) has not started and the conclusions in section 8 are blank · **Version:** v1.0 · **Date:** 2026-10-31
>
> **Related:** [EcomCraft Implementation Plan](EcomCraft%20Implementation%20Plan.md) (T82, N12, G1), [EcomCraft Reference Images](EcomCraft%20Reference%20Images.md) (section 3.3, route E), [record sheet](EcomCraft%20参考图路线实测记录表.csv)

## 1. What this trial answers

The current "describe the reference → inpaint → paste back" route gives poor results. This trial decides:

1. Which route to switch to, with which model and quantization.
2. Whether the reference must be cleaned first.
3. Whether Windows and Mac can share one recipe.

The conclusion shapes phase 2. **If G1 does not pass, T83 and later tasks do not start.**

> **Note:** the scheme IDs R0–R5 in this handbook have nothing to do with the refactoring task IDs R00–R24.

**This handbook has not been run.** It was written without a GPU, so it provides no node graphs; the prompts and parameters below are starting points only, and the trial decides.

## 2. What to prepare

| Item | Requirement |
|---|---|
| Test machines (N11) | One Windows + NVIDIA 8 GB or more; one Apple silicon Mac with 24 GB or more unified memory. Stay as close to the minimum as you can, or the results will not represent the minimum tier |
| Priority categories (N09) | Suggested: hard-packaged personal care, home appliance and household goods |
| Products (N03) | About 15 transparent PNGs with clear logos and text, in varied shapes |
| References | Two per product: one simple (clean background, few props), one complex (many props or a real setting). About 30 groups in total. Try to match the subject's angle to the product |
| Rater | At least one operator who cannot retouch, to judge "ready to list" |
| Software | Run directly in ComfyUI, not through the EcomCraft platform (except R0). Update ComfyUI to the latest version and note the version |

Keep product images, references and results outside the repository. **Do not commit them.** They are for internal evaluation only and must not be published.

## 3. Schemes

| ID | Scheme | Model | How to run |
|---|---|---|---|
| R0 | Current route (baseline) | Current default | Use "Follow a reference" in EcomCraft |
| R1 | Direct edit with reference + product | Qwen-Image 2.1 | Start from the official ComfyUI template, with the reference as image 1, the product as image 2 and the prompt from section 4 |
| R2 | Clean the reference, then add the product | Qwen-Image 2.1 | First remove the old product and text with the cleaning prompt, then use the result as image 1 and run as R1 |
| R3 | Same as R1 | Qwen-Image-Edit-2511 | As R1 with this model |
| R4 | Same as R1 | FLUX.2 klein 4B | As R1 with this model |
| R5 | Route B: paste on the platform + low-strength blend | Best of R1–R4 | Place the product by hand on the cleaned reference, then redraw the whole image at low strength (the platform's current blend uses 0.35, a reasonable start) |

**Model resources:** [Qwen-Image 2.1](https://github.com/QwenLM/Qwen-Image-2.1) · [ComfyUI on 2.1](https://blog.comfy.org/p/qwen-image-21-in-comfyui-open-weight) · [Qwen-Image-Edit-2511](https://huggingface.co/Qwen/Qwen-Image-Edit-2511) · [ComfyUI 2511 tutorial](https://docs.comfy.org/tutorials/image/qwen/qwen-image-edit-2511.md) · [FLUX.2 klein](https://bfl.ai/blog/flux2-klein-towards-interactive-visual-intelligence)

**License:** the official Qwen-Image 2.1 license covers research and evaluation only, which this internal evaluation fits. The user has decided commercial use is handled before release, so it does not affect this selection.

## 4. Prompts (starting points)

You may adjust them, but record the final version in the sheet and number changed versions (P1, P2, …).

**Fusion (R1, R2 second step, R3, R4):**

```
Replace the main product in image 1 with the product from image 2.
Keep the composition, camera angle, background, props, lighting and color mood of image 1.
Keep the shape, colors, logo and printed text of the product from image 2 exactly unchanged.
Match the product's lighting, shadow and perspective to the scene. Remove all other text, logos and watermarks.
```

**Cleaning (R2 first step, R5):**

```
Remove the main product and all text, logos and watermarks from this image.
Keep the background, surface, props, lighting and camera angle unchanged. Fill the removed area naturally.
```

## 5. How to run

**Pilot first, then the full run.** The full run is 15 products × 2 references × 6 schemes × 2 platforms, about 360 images.

1. **Pilot (Windows):** 3 products × 2 references with R0, R1, R3 and R4. Get the models working and settle steps and prompts. Eliminate clearly unusable schemes here (for example, the product is completely wrong or it keeps crashing) and note why.
2. **Full run (Windows):** the remaining schemes plus R2 and R5, for all 30 or so groups.
3. **Mac:** only the 2 best schemes from Windows. Record models that fail too: a model that runs on only one platform cannot be the default. If R0 cannot run on Mac, compare with the Windows R0 results and say so in the notes.

**Keep results comparable:**

- Use the same product, reference and seed across all schemes in a group (a fixed seed of 1 is suggested).
- Output about 1 megapixel, matching the platform.
- Record the time of the second run, excluding the first model load. The ComfyUI console prints `Prompt executed in`.
- For peak VRAM use `nvidia-smi` or Task Manager; on Mac use Activity Monitor's memory figure.
- File names: `productID_referenceID_scheme_platform.png`, for example `P03_R2c_R1_win.png`.

## 6. How to score

Fill in the [record sheet](EcomCraft%20参考图路线实测记录表.csv), one row per image.

| Metric | How to record |
|---|---|
| Composition similarity | 1–5. 5: layout, camera and light nearly identical; 3: broadly the same, details differ; 1: looks like a different image |
| Product shape and color | 1–5. 5: matches the original; 3: differences, but the same product; 1: not this product |
| Logo and text legible | Yes / no. Zoom in; one wrong character means "no" |
| Natural blending | 1–5. 5: no visible compositing; 3: visible on close inspection; 1: obviously pasted on |
| Ready to list | Yes / no, judged by the operator who cannot retouch |
| Time per image, peak VRAM or memory | Measured values |
| Crash or black image | Yes / no |

**Do not tell the operator which scheme produced an image.** Show the original product and the reference first, then ask of each image: "Could this go straight into the store?"

## 7. How to decide (G1)

Calculate once per scheme and per platform: **ready-to-list rate = images marked "yes" ÷ total images**.

Suggested pass lines (adjustable):

| Metric | Windows minimum tier | Mac minimum tier |
|---|---|---|
| Ready-to-list rate | ≥ 60% | ≥ 60% |
| Clearly better than R0 | Yes | Yes |
| Time per image | ≤ 3 minutes | ≤ 10 minutes |
| Crashes or black images | None | None |

Suggested test for "clearly better than R0": a higher ready-to-list rate than R0, and the operator picks the new scheme in most groups when comparing side by side.

| Result | Next step |
|---|---|
| Passes | Write the conclusions in section 8 and enter phase 2 |
| Only Windows passes | The user decides whether to adjust the Mac targets |
| Nothing passes | Stop and report the data, then discuss route B, route D or relaxed targets |

## 8. Conclusions (fill in after the trial)

| Item | Conclusion |
|---|---|
| Trial date and machines | |
| ComfyUI version | |
| Chosen route | |
| Model and quantization (Windows) | |
| Model and quantization (Mac) | |
| Cleaning step needed? | |
| Final prompt version and full text | |
| Key parameters (steps, strength) | |
| Ready-to-list rate per scheme | |
| G1 passed? | |
| Open issues (for example, product types that do poorly) | |

## 9. Change log

| Date | Version | Change |
|---|---|---|
| 2026-10-31 | v1.0 | First version (T82): samples, schemes, prompts, scoring and the G1 decision method |
