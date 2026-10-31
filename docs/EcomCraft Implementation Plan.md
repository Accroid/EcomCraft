# EcomCraft New-Goal Implementation Plan

> **Version:** v1.0 · **Date:** 2026-10-31
>
> **Purpose:** handed to the development AI. Self-contained; no conversation history needed.
>
> **Basis:** repository `dev` branch commit `2a7e84ec`. Paths and function names verified, except those marked "read again before implementing"; after the refactoring some paths change — see section 7 of the [EcomCraft Refactoring Plan](EcomCraft%20Refactoring%20Plan.md) (Chinese original [EcomCraft 重构计划](EcomCraft%20重构计划.zh-CN.md)).

---

## 0. Required reading for the executor

### 0.1 Working rules

1. **Before starting each task, read the goal document first and confirm the task matches the goal:** before T81 is done, read section 1 of this document; after T81, read sections 1 and 2 of `docs/EcomCraft Architecture.zh-CN.md` and the task's own section in the plan. If a task conflicts with the goal, stop and ask the user. Then read `AGENTS.md`, `CLAUDE.md` and the relevant skills under `.agents/skills/`. On conflict: before T81, this document's goals and boundaries win; all other coding standards and security rules follow `AGENTS.md`.
2. Work only in the desktop `EcomCraft` clone, on the `dev` branch.
3. One T task per session, in the order of section 4; never start a task whose dependencies are unfinished.
4. Scope-change order: architecture and requirements documents (Chinese and English together) → the development tracker → the code.
5. Never modify `Data/`, `Output/`, `Models/`, `dlbackend/`; never commit build outputs.
6. Before committing run `git status --short`, `git diff --stat`, `git diff --check`; commit messages in Chinese. Commit and push only when the user asks.

### 0.2 Handle first (blocking)

1. **Execution order:** read the companion [EcomCraft Refactoring Plan](EcomCraft%20Refactoring%20Plan.md) and interleave per its section 7. This document's code integration points change after the refactoring; the mapping table in the refactoring plan's section 7 wins.
2. **Worktree:** the previously uncommitted 18 files were committed in `2a7e84ec`. Run `git status --short` before starting; if anything besides untracked `.claude/` shows up, ask the user first — never overwrite or revert it. Do not add `.claude/` to commits.

### 0.3 Environment limits

| Item | Situation |
|---|---|
| Current dev machine | No .NET SDK or runtime; can only write code and run Node tests |
| Compiling, unit tests, GPU runs | Done by the user on another computer. When something cannot be run, write "not compiled / not tested" honestly — never "passed" |
| Front-end tests (local) | `node --test src/Extensions/EcomCraft/Tests/studio-i18n.test.cjs src/Extensions/EcomCraft/Tests/simple-mode.test.cjs` |
| Unit tests | `dotnet test src/Extensions/EcomCraft/Tests/EcomCraft.Tests.csproj`, covering only rule classes that do not depend on SwarmUI |
| After C# changes | Rebuild the extension per the `ecomcraft-build-run` skill, or the old assembly keeps loading |
| After front-end changes | Update the `?v=` in `Studio/index.html` |

### 0.4 Code conventions (summary; full text in AGENTS.md)

- **C#:** no `var`; `///` on every field; no `{ get; set; }`; braces even on single lines; `ToLowerFast` for lowercasing.
- **JS:** `let` only; `/** */` at the top of functions; `else {` on its own line; standard `for`, no `forEach`; avoid `===` unless truly needed.
- **Rule classes:** put unit-testable pure logic into `*Rules.cs` (see `SetRules.cs`, `OutputSizeRules.cs`) with matching `Tests/*Tests.cs`.
- **UI text:** the Chinese source is the translation key; sync `Studio/lang/en.js`; keep the `studio-i18n` test passing.

## 1. Product goal

> EcomCraft lets e-commerce operators, designers and small shop owners, **with just one product photo and one reference image they like**, get listing-ready product images and whole image sets on their own GPU-equipped Windows or Mac computer (or LAN) — without learning models or workflows, with the product staying true, and with images never leaving the company.

### 1.1 Decided, no further discussion

| ID | Decision |
|---|---|
| G1 | **A GPU is required to use the product.** No GPU-less or cloud-generation options. Insufficient hardware gets a clear "not supported" message |
| G2 | **Windows and Mac are the priority platforms**; Linux is not considered for now: keep the scripts, invest and verify nothing |
| G3 | **Commercial licensing of models is not yet a selection criterion**; default models are chosen by quality and runnability only. Handle before release (see 8.1) |
| G4 | The generation page keeps only "follow a reference"; the "make one image" and "make a full set" entries go away; full sets are also reference-driven |
| G5 | Simple generation must produce main images, selling-point images and detail images |
| G6 | Editions: open-source core + team edition (closed-source extension), priced per account |

### 1.2 Default recommendations (user-approved, adjustable after trials)

| ID | Decision |
|---|---|
| D2 | Relax the "restore the feel only" red line to allow "highly similar". Boundary in 1.3 |
| D3 | First batch targets hard-packaged categories: personal care, appliances, daily goods. Clothing is out of the first batch |
| D5 | Minimum specs: Windows NVIDIA 8 GB; Mac Apple silicon with 24 GB unified memory. Intel Macs unsupported; AMD only within the ROCm support list. Numbers to be tuned after measurement |
| D6 | Get Windows working first, then adapt the Mac; but default-model selection must be validated on the Mac at the same time |

### 1.3 Reference-image boundary (into AGENTS.md)

**Allowed:** users upload their own reference images; generating compositions, scenes and lighting highly similar to the reference.

**Not built:**
- Competitor-link scraping, competitor image libraries or recommendations.
- Marketing that says "replicate competitors"; the UI consistently says "follow a reference".

**Must do:**
- Text, logos and watermarks never enter the finished image; the system re-lays-out text.
- References containing real models do not keep their faces (first batch: prompt "not supported", see T88).
- Keep the copyright notice and the stronger international-platform notice (current copy near `simple.js:2838`).
- References still never written under `Data/`, into the product library, archive, diagnostics or backups.

The user has not consulted a lawyer yet; that is a pre-release to-do (8.1), not a development blocker.

### 1.4 New MVP acceptance

On minimum-spec Windows and Mac computers:

1. An operator unfamiliar with design goes from install to first image in under 30 minutes without a tutorial (excluding model downloads).
2. The target categories reach the "listing-ready" pass rate (see the G1 gate).
3. Main images and detail pages can be exported.

## 2. Status and gaps

| Capability | Status | Gap |
|---|---|---|
| Reference images | Vision-written prompt (`PromptService.EcomCraftStudioReferenceDescribe`) → inpaint (`StandardWorkflows.AddProductInpaint`) → paste-back (`ProductComposite.PasteBlended`) | User feedback: results are poor; the route must change |
| Placement | Manual (T79, `reference_placements`) | Make automatic, manual as correction |
| Models | `Settings.cs:90-98` hard-codes three file names | Need per-hardware-and-platform recipes |
| Downloads | The model list only displays; SwarmUI has `DoModelDownloadWS` (`src/WebAPI/ModelsAPI.cs:626`) | No one-click download and auto-configuration |
| Hardware detection | Vision model only: `MagicPrompt/VisionDeviceCheck.cs:152-196` reads the GPU per OS | Nothing for generation models |
| Mac | `launch-macos.sh` exists; `VisionModelInstall.cs:77-78` has no Mac script; no test records | All to do |
| Platforms | `Templates/platforms.json` has 9, all `verified: false` | Missing platforms, selling points and detail slots |
| Detail images | `builtin/detail.json` is a **close-up**, not a detail page | Detail pages are new work |
| Selling-point images | `Export/SellingImageService.cs`, fixed layouts, server-drawn text | Layouts to extend |
| Professional mode | Text-to-image, image-to-image, inpaint, outpaint, upscale, workflow upload | Becomes a refinement bench |
| Caps | `SetRules.MaxReferences` is 8; `BatchService.cs:365` total cap 20 | A full set can exceed 8 images |

**To clean up:** `Templates/blueprints/text-to-image.json:155-162`'s `widgets_values_named` carries an adult-portrait prompt and unrelated model names unrelated to e-commerce (cleaned in T81).

## 3. Technical route

### 3.1 Why the current route is poor

1. A 60–120 word description cannot preserve exact layout, perspective and lighting; by generation time it is a different picture.
2. Product position and size rely on the user's manual estimation.
3. A single fixed-angle product photo rarely matches the scene's camera.
4. The current fusion does not re-light the product for the scene.

Prompt tweaks cannot fix this; the model must directly "see" the reference and the product photo.

### 3.2 Route comparison (judged on principle, not measured)

| Route | Approach | Composition | Product fidelity | Fusion | Hardware |
|---|---|---|---|---|---|
| A current | vision prompt, inpaint, paste back | Low | High | Low | Medium |
| B direct replace | remove the old product, repair background, insert the new product | Very high | Very high | Medium | Low |
| C multi-image editing | reference and product fed together to an editing model | High | Medium | High | Medium-high |
| D structure control | extract depth or line art, repaint by structure | Medium-high | High | Medium | Medium |
| **E hybrid (target)** | C fusion, then verify and restore product detail | High | High | High | Medium-high |

### 3.3 Route E flow

1. **Analyze the reference:** find the subject's position and size, judge the image slot, flag text and people.
2. **(Optional) clean up:** remove the old product, text, logos. Trials decide whether needed.
3. **Fuse:** reference (or clean scene) + product photo → editing model.
4. **Restore the product:** locate it in the result; when shape and angle align, paste the original details back.
5. **Verify:** on excessive difference retry once; if still failing, flag "product may be distorted" — never deliver silently.
6. **Text:** re-laid-out by `SellingImageService`.

**Known limit:** when the model rotates the product, pasting back misaligns — do not force it. When the reference's angle differs too much from the product, prompt for another photo or extra angles.

### 3.4 Candidate models

| Model | Capability | Footprint | Notes |
|---|---|---|---|
| **Qwen-Image 2.1** | Text-to-image and editing in one; up to 10 references; can output transparency | Community says Q4_K_M needs about 6–8 GB, unverified | **First trial target**: the user has it deployed; the current blueprint's `TextEncodeQwenImage21` node has an `images.image_1` input. Official default 40 steps, Turbo 8 |
| Qwen-Image-Edit-2511 | Instruction editing, multi-image input, 4-step加速 LoRA | Official gives no VRAM; GGUF exists | Official ComfyUI templates |
| FLUX.2 klein 4B | Text-to-image, editing, multi-image reference in one | Official ~13 GB; FP8 about 40% less, NVFP4 about 55% less | |

**None has an independent review for product replacement; results must be measured.** Do not treat this table as a conclusion.

## 4. Task overview

New IDs start at F74, T81, N11 (tracker maxima verified as F73, T80, N10).

Sizes: S half a day, M 1–2 days, L 3+ days.

| Phase | Task | Content | Depends | Size | Executor |
|---|---|---|---|---|---|
| 0 Planning on paper | T81 | Update documents, tracker, AGENTS.md; clean the blueprint residue | 0.2 | M | AI |
| 1 Route trial | N11 | Prepare test machines | — | — | User |
| | N09 / N03 | Confirm target categories, collect test products (existing items) | — | — | User |
| | T82 | Write the trial manual, prompts and record sheet | T81 | S | AI |
| | N12 | Run the trial, fill the sheet | T82, N11, N03 | — | User |
| | **G1** | **Route and model decision gate** | N12 | — | User + AI |
| 2 References v2 | T83 | Reference analysis | G1 | M | AI |
| | T84 | Fusion workflow and template | G1 | L | AI |
| | T85 | Generation-flow integration | T83, T84 | L | AI |
| | T86 | Product locating, paste-back and verification | T85 | L | AI |
| | T87 | Aspect fitting | T85 | M | AI |
| | T88 | Abnormal reference handling | T83, T85 | M | AI |
| | T89 | Single-entry generation page | T85 | L | AI |
| | T90 | Migrate features tied to the old entries | T89 | M | AI |
| | **G2** | **Quality acceptance gate** | T86–T90 | — | User |
| 3 Out of the box | T91 | Hardware detection and tiers | G2 | M | AI |
| | T92 | Model recipes and scanning installed models | G2, T91 | M | AI |
| | T93 | One-click download and auto-configuration | T92 | L | AI |
| | T94 | Wizard and sample self-check | T93 | M | AI |
| | T95 | Mac adaptation (F23) | T84, T93 | L | AI + user |
| | T96 | Installers and signing | T94, T95 | L | AI + user |
| 4 Sets and details | N06 | Verify platform rules (existing item) | — | — | User |
| | T97 | Platform and slot data | N06 | M | AI |
| | T98 | Full sets by slot | T89, T97 | L | AI |
| | T99 | Selling-point info and wording checks | T97 | M | AI |
| | T100 | Selling-point image and detail-page layouts | T98, T99 | L | AI |
| | T101 | Consistent style across a set | T98 | M | AI |
| | T102 | Built-in reference library | T98 | M | AI + user |
| 5 Professional mode | T103 | Version history | G2 | M | AI |
| | T104 | Local edits and product lock | T103 | L | AI |
| | T105 | Object removal, background swap | T104 | M | AI |
| | T106 | Instruction editing | T103 | M | AI |
| | T107 | Aspect change and upscale into platform sizes | T97 | S | AI |
| | T108 | P1: text layer, recolor, lighting, batch apply | T104 | L | AI (split first) |
| 6 Release | T109 | Open-source / team-edition split | G2 | L | AI + user |
| | T110 | Requirements and release documentation | T96 | S | AI |
| | N07 | Seed-team trial (existing item) | T96, T100 | — | User |

**Parallelizable:** phases 3, 4 and 5 do not block each other after G2, but do not edit the same files at once.

**Stop condition:** if G1 finds no route meeting the bar, **phase 2 must not start**; report and discuss.

## 5. Task details

### Phase 0

#### T81 Planning on paper

**Goal:** the repository documents reflect the new goal. No code-logic changes.

**Steps:**

1. **Architecture document** (`docs/EcomCraft Architecture.zh-CN.md` and `.md`):
   - Section 1: replace with this document's section 1 goal, adding the hardware threshold.
   - New decisions in section 2:
     - A14 single entry: only "follow a reference" remains; full sets are reference-driven.
     - A15 hardware threshold: a GPU is required.
     - A16 platforms: Windows and Mac first.
     - A17 similarity boundary: per 1.3.
     - A18 editions: open-source core + team edition.
   - Revise A6 (no more cloud-generation reservation), A11 (route and no 1:1 replication), A12 (team-feature split allowed), A13 (three task entries void).
   - Section 7: replace with 1.4's acceptance. Section 8: add the new F IDs, remove F18, pull F23 forward. Section 9: add "GPU-less options" and "cloud generation". Section 10: replace with this document's phases. Section 11: add a change-log entry, version v3.0.
2. **Reference-image requirements** (`docs/EcomCraft 参考图需求.zh-CN.md` and `EcomCraft Reference Images.md`): decision 2 and section 6 per 1.3; add the route to section 3; status "route pending G1".
3. **UI requirements** (`docs/EcomCraft 界面改版需求.zh-CN.md` and `EcomCraft UI Redesign.md`): U3, U4 and section 5 become the single entry of 6.1.
4. **LAN requirements** (`docs/EcomCraft 局域网多服务器平台需求.zh-CN.md` and English): D11 and R10's cloud reservation becomes "not built".
5. **AGENTS.md**:
   - Replace "no competitor scraping, image-library recommendations or one-click replication" with 1.3's boundary.
   - Replace "until cloud integration is confirmed, send nothing to cloud generation services" with "no cloud generation". **Cloud options for the vision and prompt models stay.**
   - Keep the Linux commands under "Build and launch", noting they are unverified for now.
6. **Tracker** (`docs/EcomCraft 开发总表.xlsx`): edit with openpyxl, backing up to a temp folder first.
   - The workbook has no charts; it has dropdowns and conditional formatting — confirm they survive the save.
   - Row 84 of "开发任务" is a footnote; insert new tasks before it.
   - Add this document's section 7 F rows to "功能总表"; columns J and K reuse the existing formulas (fix the row numbers).
   - Add rows per the new phase names in "阶段汇总" and widen the totals' ranges.
   - Add N11 and N12 to "非开发事项"; point N10 at T82; **leave N01's status alone**, appending the note: "Qwen-Image 2.1's official license is research and evaluation only; on 2026-10-31 the user decided not to consider it for now, handle before release".
   - Mark T29 and F18 deferred with reasons; mark T77 deferred; note T62 "superseded by T84".
   - Add "GPU-less and cloud generation" and "Linux verification" to "暂不做".
   - Update counts and document links in "使用说明".
7. **Blueprint:** delete node 459's `widgets_values_named` (the whole field) in `text-to-image.json`. First confirm `NativeWorkflowConverter` does not read the field; if it does, only replace the content with a neutral product prompt and the current model names.
8. **README** (`README.md`, `README.en.md`, `docs/README.zh-CN.md`): state the hardware requirements and target platforms.
9. **Put the plans into the repository:** copy this document and the refactoring plan in as `docs/EcomCraft 实施计划.zh-CN.md`, `docs/EcomCraft 重构计划.zh-CN.md`, and write the English `docs/EcomCraft Implementation Plan.md`, `docs/EcomCraft Refactoring Plan.md`. The in-repository versions are authoritative afterwards; the architecture document's related-files list links them.
10. **Must-read rule for starting work:** at the top of `AGENTS.md` and in `CLAUDE.md`'s roadmap section, write: "before each task, read sections 1–2 of the architecture document and the matching plan; confirm the task fits the product goal; if not, ask the user first — never drift on your own."

**Acceptance:** a fresh session, using only the repository, can answer the product goal, hardware threshold, target platforms and current task order; Chinese and English decision IDs match; searching "一键复刻" or "云端生成" leaves no stale rules; the tracker reopens cleanly in openpyxl with formulas and dropdowns intact; `git diff --check` passes.

### Phase 1

#### N11 Prepare test machines (user)

One **Windows + NVIDIA 8 GB or better**, one **Apple-silicon Mac (24 GB or better)**. The recorded RX 5600 XT (6 GB) test machine is not on the ROCm support list and cannot be the baseline.

#### T82 Trial manual

**Output:** `docs/EcomCraft 参考图路线实测.zh-CN.md` and English, with a record-sheet template (CSV or xlsx).

**No platform code.** Prefer official ComfyUI templates. The AI cannot run GPUs; never deliver an unexecuted node graph claiming it works.

**Manual contents:**

- **Samples:** about 15 transparent-background product photos in the target categories, each with 2 references (one simple, one complex scene), ~30 groups.
- **Schemes:**

| ID | Scheme | Model |
|---|---|---|
| R1 | Reference + product direct editing | Qwen-Image 2.1 |
| R2 | Clean the reference first, then insert the product | Qwen-Image 2.1 |
| R3 | Same as R1 | Qwen-Image-Edit-2511 |
| R4 | Same as R1 | FLUX.2 klein 4B |
| R5 | Route B: platform paste + low-strength fusion | The best of R1–R4 |
| R0 | Current route (baseline) | Existing |

- **Prompts (a starting point; adjust during the trial and record the final version):**

```
Replace the main product in image 1 with the product from image 2.
Keep the composition, camera angle, background, props, lighting and color mood of image 1.
Keep the shape, colors, logo and printed text of the product from image 2 exactly unchanged.
Match the product's lighting, shadow and perspective to the scene. Remove all other text, logos and watermarks.
```

- **Metrics (per group):**

| Metric | How recorded |
|---|---|
| Composition similarity | 1–5 |
| Product shape and color | 1–5 |
| Logo and text legible | Yes / no |
| Natural fusion | 1–5 |
| Listing-ready | Yes / no, judged by an operator who cannot retouch |
| Time per image, peak VRAM or memory | Measured |
| Platform | Windows / Mac |

#### G1 Decision gate

Suggested pass bars (user-adjustable):

| Metric | Windows minimum | Mac minimum |
|---|---|---|
| Listing-ready share | ≥ 60% | ≥ 60% |
| Clearly better than R0 | Yes | Yes |
| Time per image | ≤ 3 min | ≤ 10 min |
| Crashes or black images | None | None |

**Output:** the trial document states the chosen route, model, quantization, whether a cleanup step is needed, and whether Windows and Mac use different recipes.

**Branches:**
- Pass: enter phase 2.
- Windows-only pass: ask the user about adjusting the Mac target.
- All fail: stop and report the data. Route B or D, or relaxed targets, are options.

### Phase 2

The following assumes G1 picked route E. If not, revise this phase's design per the conclusion and confirm with the user first.

#### T83 Reference analysis (F74)

**Goal:** replace manual placement with automatic subject position and slot.

**Approach:**
- New `Studio/PostProcess/ReferenceAnalysis.cs`; pure logic in `ReferenceRules.cs`.
- Subject position: reuse `CutoutService.CutoutAsync(byte[])`'s fixed graph (`SwarmRemBg`), compute the bounding box from the mask, convert to a `Placement` (via `Placement.TryCreate`).
- Slot and flags: extend the vision prompt so the model answers in fixed lines only (like `StoryboardRules`); fall back to defaults when parsing fails.
- Extend `EcomCraftStudioReferenceDescribe`'s response:

```json
{ "index": 0, "prompt_en": "...", "prompt_zh": "...",
  "role": "main|scene|selling|detail|unknown",
  "placement": { "x": 0.5, "y": 0.55, "size": 0.5 },
  "has_text": true, "has_person": false, "subject_count": 1, "warnings": [] }
```

**Boundaries:** references stay in memory; no server addresses returned. Without a vision model, position still comes from `SwarmRemBg`, `role` is `unknown`, and the user chooses.

**Tests:** `ReferenceRulesTests`: bounding-box conversion, out-of-range, empty mask, model-output parsing (including garbage).

**Acceptance:** with 3 differently composed references, the previewed product lands on the old product's place with visually acceptable error; manual dragging still overrides.

#### T84 Fusion workflow and template (F74, replacing T62)

**Approach:**
- New blueprint `Templates/blueprints/reference-edit.json` using the G1-selected workflow.
- Edit `StandardWorkflows.cs`:
  - `Blueprint()` currently hard-codes `text-to-image.json` and always calls `AddProductInpaint`; select the blueprint and the inpaint step per kind.
  - `Kinds` gains `("reference", $"{Source}/ecomcraft/reference", "...")`.
  - Add the reference parameter `ecreferenceimage` (following `ProductImageParam`); the product keeps `ecproductimage`.
  - **Note:** today's `ecproductimage` is the transparent canvas with the product placed; confirm whether the editing model should receive the cropped product or the canvas, per G1's conclusion.
- New `Templates/builtin/reference.json`: like `scene.json`, declaring the `"reference": { "image": "parameter ID" }` slot designed in the Reference Images document 3.2; sync `TemplateStore` and validation (never the same parameter as the `brand_kit` slot).
- `ModelInputs` checks the new blueprint's loader nodes; if `Settings.EcomCraftData` needs new fields, write the `///` comments.

**Boundaries:** blueprint and model names never come from the client; publishing still goes through `WorkflowReview`; clients submit only the workflow ID and whitelisted parameters.

**Tests:** extend `BuiltinTemplateTests`, `TemplateValidatorTests`.

**Acceptance:** auto-published when an execution server has the models; `Unavailable()` names what is missing otherwise.

#### T85 Generation-flow integration (F74)

**Approach:**
- `Generation/BatchService.cs`:
  - `ReferencePrompts`, `ReferenceImages`, `ReferencePlacements` exist; the job payload assembles around lines 589–612 — fill the matching reference into `ecreferenceimage`.
  - `reference_mode` enables: `composition` (old route), `edit` (new route, default). The server validates against the whitelist.
  - New parameter `reference_strength`: `composition` (borrow composition only) or `close` (highly similar, default), mapped server-side to prompt or strength; clients submit no numeric value.
  - The new route no longer depends on per-reference editable vision prompts: the server template generates the prompt (into `PromptRules`); `reference_prompts` becomes optional.
- Professional users still write prompts in professional mode; simple mode hides them.
- The queue and time estimates (`TaskTiming`) recognize the new flow.

**Must verify:** the backend uploads images into ComfyUI's input folder on generation (see the `AddProductInpaint` comment "the backend uploads the image parameter"). This touches the "references never hit disk" rule: before implementing, read the upload flow and confirm whether those files persist; if they do, clean them after the task, or state it plainly in the requirements document and AGENTS.md.

**Tests:** `SetRulesTests` gains mode and strength validation.

**Acceptance:** 1 product × 3 references → 3 images, each matching its own reference; cancel, timeout and server disconnect all give plain errors; the queue and others' APIs never see the references.

#### T86 Product locating, paste-back and verification (F75)

**Approach:**
- New `Studio/PostProcess/ProductAlign.cs`; pure logic in `ProductAlignRules.cs`.
- Locating: run `SwarmRemBg`'s fixed graph on the result (same as T83), or per G1's choice; get the product region.
- Paste-back eligibility: compare the generated product's contour with the original's (overlap after scaled alignment). Thresholds in configuration, defaults from measured data.
- Eligible: compute the `Placement`, call the existing `ProductComposite.PasteBlended` (frequency-separated blend).
- Not eligible (angle changed): **do not paste**; keep the model's result.
- Verification: on excessive color/structure difference retry once (new seed); still failing, flag the result `product_warning` and show "product may be distorted, please check" on the candidate.
- Order with `ProductPreserve.ApplyAsync` and `OutputResize`: preserve first, scale after (the existing convention).

**Tests:** `ProductAlignRulesTests`: identical, translated, scaled, rotated, different products; `ProductCompositeTests` regression.

**Acceptance:** a product with a logo comes out legible; no double images when the model rotates it; warnings show correctly.

#### T87 Aspect fitting (F74)

**Problem:** references often differ in aspect from the target slot (1:1 reference, 3:4 slot).

**Approach:**
- Rules in `OutputSizeRules`: near-equal aspects (default difference ≤ 5%) crop directly; larger differences outpaint to the target aspect, reusing professional mode's outpaint or the editing model; the subject must stay whole.
- With F73: the model generates near 1 megapixel at the chosen aspect; `OutputResize` scales precisely.

**Tests:** extend `OutputSizeRulesTests`.

**Acceptance:** a 1:1 reference with a 3:4 slot keeps the subject whole, no stretching.

#### T88 Abnormal reference handling (F74)

| Case | Detection (T83) | Handling |
|---|---|---|
| Several products | `subject_count > 1` | Prompt; let the user click the subject to replace in the preview; without a choice, use the largest |
| Real model or hand-held | `has_person` | First batch: "references with people are not supported yet", allow replacing |
| Heavy text (posters, selling points) | `has_text` | "Text in the image will not be kept"; text goes through T100 |
| Subject too small or absent | bounding box too small | Prompt for another image |
| Product very different from reference (tall bottle vs flat box) | aspect ratio gap | Warn that results may suffer; do not block |
| Low resolution | existing F38 | Prompt |

All prompts in plain language through the existing error-code mechanism (`Tests/error-codes.test.cjs`).

#### T89 Single-entry generation page (F61 revised)

**Page structure:**

1. **Upload the product:** transparent PNG used directly (`ProductComposite.HasTransparency`), no cut-out; normal photos still auto-cut. Keep the product library and camera entries.
2. **Platform and references:**
   - **Pick platform and size first** (keeping F73's "size before generating", not at export).
   - Upload references: one → a single image; several → a set. Each shows its auto-detected slot, editable.
   - "Similarity": borrow composition only / highly similar (default).
   - "Also make a white-background image" checkbox: no reference needed, uses the existing `CutoutOnly`.
   - Sets show the selling-point form (T99).
3. **Pick and export:** candidates can be starred, downloaded, re-made, "edit this one" (into professional mode), or packaged per platform.

**Removed entries:** delete the `simple-task-picker` task cards; reference mode is the default. Image type, style, model, brand kit, one-line description, AI expand, bilingual prompts, storyboards are hidden from simple mode. **Keep the code, do not delete** until G2 decides.

**Touches:** `Studio/simple.js` (`setTask` ~line 486, `referenceMode` ~line 2746), `index.html`, `studio.css`, `lang/en.js`.

**Without a vision model:** the F68 blocking (near `simple.js:2669`) must change: the new route's position does not depend on the vision model, so no full-page block — only "pick the slot manually".

**Mobile:** the single entry must work on narrow screens (`mobile.test.cjs`); the professional-mode entry stays hidden (U9).

**Tests:** update `simple-mode.test.cjs`, `studio-i18n.test.cjs`, `mobile.test.cjs`.

**Acceptance:** a new user sees only three steps; no prompt box, model or parameters; no leftovers in either language.

#### T90 Migrate features tied to the old entries

The single entry affects features tied to the old tasks; handle each:

| Feature | Problem | Handling |
|---|---|---|
| Browser-remembered task (`chosenTask`) | Old values may be `single`, `set` | Fall back to the new flow when read |
| Make again with same settings (F04) | It currently refills simple mode's template and prompts, now hidden | Old works (no references kept, references are never stored) refill professional mode; new works prompt "references were not saved, please upload again" |
| Refine in professional mode (F64) | Rename to "edit this one" | Keep the one-way hand-off |
| Selling-point storyboard F25, style | Hidden | Confirm the APIs do not error without front-end calls |
| Workspace UI design document | Control list outdated | Sync `docs/EcomCraft 工作台界面设计.zh-CN.md` and English |

#### G2 Quality acceptance (user)

Run the full platform flow (not inside ComfyUI) with the N03 samples on Windows. The listing-ready share must be no lower than G1's result minus 5 points. On failure, return to T85/T86; do not proceed.

### Phase 3

#### T91 Hardware detection and tiers (F76)

**Approach:**
- New `Studio/Setup/HardwareProbe.cs`, reusing `VisionDeviceCheck.cs:152-196`'s per-OS reading (read again before implementing; extract a shared method). Read: OS and architecture, GPU model, VRAM, unified memory, free disk.
- Tiers in `Studio/Setup/HardwareRules.cs` (pure, unit-testable):

| Tier | Windows | Mac | Handling |
|---|---|---|---|
| recommended | NVIDIA ≥ 16 GB | Apple silicon ≥ 32 GB | Full quality |
| minimum | NVIDIA 8–15 GB | Apple silicon 24–31 GB | Quantized models, "slower" notice |
| conditional | AMD within the ROCm list with enough VRAM | — | Notice that quality and speed may suffer |
| unsupported | No discrete GPU, iGPU, insufficient VRAM, off-list AMD | Intel chips, low memory | Explain why |

- Numbers in configuration, set by G1 measurements; the ROCm list in configuration, never hard-coded.
- Admin-only API: `EcomCraftStudioHardwareStatus` (`Permissions.EditServerSettings`). Returns model and tier only, never paths or addresses.
- Unreadable hardware shows "unknown", not "unsupported"; administrators may continue.
- LAN execution servers' hardware is not read: probe locally only; judge the rest by node and model availability.

**Tests:** `HardwareRulesTests` covering every tier boundary.

#### T92 Model recipes and scanning installed models (F76)

**The user's own words: the program must read downloaded models and configure itself.** This must also cover models the user placed manually.

**Approach:**
- New `Templates/model-recipes.json` (shipped with releases; administrators can override via `Data/EcomCraft/`, same as `platforms.json`):

```json
{ "id": "win-nvidia-min", "os": "windows", "gpu": "nvidia", "tier": "minimum",
  "name": { "zh-CN": "...", "en": "..." },
  "files": [ { "role": "unet", "folder": "diffusion_models", "filename": "...", "bytes": 0, "sha256": "...",
    "sources": [ { "region": "cn", "url": "..." }, { "region": "global", "url": "..." } ] } ],
  "nodes": [ "UnetLoaderGGUF" ], "workflow_kinds": [ "reference", "scene", "detail" ] }
```

- File lists come from G1's conclusion; sizes and checksums from the real files — never invented.
- Scanning: list installed models via SwarmUI's model handlers (read `src/Text2Image/` first), match recipes by name and checksum; installed files are not re-downloaded.
- Models outside any recipe: listed as "unrecognized", usable in professional mode, never auto-defaulted.

**Boundaries:** download URLs come only from server-side recipes; clients never submit addresses or file names.

**Tests:** `ModelRecipeRulesTests`: recipe choice, partially installed, checksum mismatch, corrupt recipe file.

#### T93 One-click download and auto-configuration (F76)

**Approach:**
- Reuse `DoModelDownloadWS` (`src/WebAPI/ModelsAPI.cs:626`, read again first). If it lacks resume or verification, add it in the EcomCraft layer, avoiding core changes where possible.
- Flow: check disk space → download each file with progress → verify → write `Settings.EcomCraftData` (`StandardWorkflowUnet` etc.) → refresh model lists → call `StandardWorkflows.Ensure`.
- ModelScope or mirrors first in China; automatic failover; region from language and connectivity, administrator-editable.
- Required ComfyUI nodes (e.g. ComfyUI-GGUF) auto-installed from fixed sources declared by the recipe.
- Cancelable, resumable, plain-language failures. Writes through temporary-file replacement.

**Permissions:** `Permissions.DownloadModels` + `EditServerSettings`.

**Acceptance:** one click on a clean machine; resume after a dropped connection; a failed checksum never enables.

#### T94 Wizard and sample self-check (F40 revised)

**Approach:**
- Rework the first-run wizard: language → hardware result → recommended recipe (download size, expected speed) → download → sample self-check → pick usual platforms.
- Unsupported hardware: explain why, with an exit to "connect another computer over the LAN instead".
- Sample self-check: run one full flow with built-in test product and reference images (which we must have the right to use); only then show "setup complete"; on failure give next steps and the diagnostics entry.
- The self-check writes nothing into the user's works.
- Install the vision model alongside (`VisionModelInstall`).

**Acceptance:** a new computer goes from opening to first image without reading docs.

#### T95 Mac adaptation (F23)

**Approach:**
- Make `launch-macos.sh` + `src/Core/Installation.cs`'s local ComfyUI install work end-to-end with no manual steps outside the terminal.
- Native arm64 Python; keep `PYTORCH_ENABLE_MPS_FALLBACK=1` (already set); handle the known VAE-precision black-image issue (third-party reports; must be measured).
- Confirm G1's quantized formats and nodes run on MPS; a separate Mac recipe if needed.
- Add `launchtools/install-vision-model-macos.sh`, edit `VisionModelInstall.cs:77-78`, update `VisionInstallTests`.
- Check self-maintained paths, fonts and process cleanup (F72 `self_start_processes`) on macOS.
- Time estimates reflect measured speed.

**Acceptance (user, on a Mac):** a cleaned Apple-silicon Mac passes the full T94 flow; 20 images in a row with no crash or black image.

#### T96 Installers and signing (F77)

**Goal:** users do not install .NET, Python or Git themselves.

**Contents:**
- Windows: an installer with a desktop shortcut. Sync-check `Desktop/`, `src/Core/Installation.cs`, launch scripts and `src/wwwroot/ecomcraft.ico` (per AGENTS.md).
- Mac: `.app` or `.dmg`.
- **Signing:** unsigned builds hit Windows SmartScreen and macOS Gatekeeper, hurting out-of-box use. The user decides whether to buy certificates (an Apple developer account, Windows code signing); without them, document the bypass steps.
- Upgrade keeps `Data/`, `Models/`, `Output/` (F45). Uninstall asks whether to delete models.

### Phase 4

#### T97 Platform and slot data (F58 extended)

**Depends on N06:** third-party numbers contradict; official sources are mandatory. Unverifiable values stay `verified: false` — never invented.

**Platform priorities:**
- Batch 1 (existing, verify first): Taobao/Tmall, JD, Pinduoduo, Douyin, Xiaohongshu, Amazon, Shopify, TikTok Shop, eBay.
- Batch 2: Temu, AliExpress, Shopee, Lazada, Weixin Store, Kuaishou.
- Batch 3: SHEIN, Walmart, Etsy, 1688.

**New slots:** `first_main`, `selling`, `detail_page` (width, per-screen height, stitching rules). Sync `SetRules`, `ExportRules`, `ComplianceRules`, `docs/EcomCraft 平台预设槽位设计.zh-CN.md` and English.

**Tests:** `ExportTests`, `ComplianceTests`.

#### T98 Full sets by slot (F78)

**Rules:**
- Raise `SetRules.MaxReferences` from 8 to fit a set (suggest 15: 5 main + 10 detail screens); keep `BatchService.cs:365`'s total cap of 20.
- Multiple references → assigned to platform slots by `role`, user-adjustable.
- Duplicate slots allowed; unfilled slots show "not covered".
- One image per reference by default; candidates offer "make another"; individual remakes do not affect others.
- Batches group by slot.

**Single reference:** a single image only, with a prompt to add references or pick from the built-in library (T102); other slots are never invented.

**Set structure (from public notes of Yiketu, PicSet AI and Tencent Cloud; not an official platform standard):**

| Group | Slot | Purpose |
|---|---|---|
| Main images | First image | Attract clicks |
| | Selling point | Core selling points |
| | Scene | Usage context |
| | Detail | Materials and craft |
| | White background | Platform requirement, via `CutoutOnly` |
| Detail page | Hero | First impression |
| | Selling-point overview, core pages | Explain one by one |
| | Usage scenes | Immersion |
| | Detail close-ups | Craft |
| | Specs and sizes | User-entered data only |
| | Trust and after-sales | Close the sale |

#### T99 Selling-point info and wording checks (F79)

- When making a set, fill in: product name, 3–5 selling points, specs, sizes. The model may **suggest** selling points from the user's text; the user confirms.
- **Never invent** specs, certifications or claims.
- Saved per product in the product library (`ProductIndex`) to avoid re-entry.
- Wording checks in `Prompting/ClaimRules.cs`: advertising-law superlatives ("best", "number one", "national grade"), configurable word lists, advisory not blocking. Lists verified against official rules.
- International platforms: fill per language; no machine-translated auto-listing.

#### T100 Selling-point images and detail-page layouts (F80)

- Extend `Export/SellingImageService.cs` and `SellingImageRules.cs`: several layouts, text always drawn by the server (Noto Sans SC bundled) — **AI never writes text**.
- Text goes into the image's clear space: pick the layout from T83's analysis and the product's position, avoiding the product.
- Detail pages: laid out by slot, exported both as per-screen slices and as one long image; screen sizes per T97.
- Export: `ExportService` packages by group (main images, details) with ordered file names.
- International main-image text bans via `ComplianceRules` notices.

**Tests:** `SellingImageRulesTests`; text overflow, long copy, Chinese and English.

#### T101 Consistent style across a set (F78)

**Problem:** references with different styles make a set look messy.

**Approach:** an option "unify tone": match other images' tone locally to one user-chosen baseline (no regeneration, product area untouched). Off by default; defaults decided by measured results.

#### T102 Built-in reference library (F81)

- Sample images by category and slot, for users holding no references; also lowers copyright risk.
- **Sources must be rights-cleared:** self-generated and reviewed, with recorded provenance. Competitor images and unauthorized pictures never enter.
- A base pack ships with releases; industry packs belong to the team edition.
- Library images persist; that is separate from "user references never hit disk" — document both distinctly.

### Phase 5

#### Overall positioning

Professional mode changes from a "parameter panel" to a "refinement bench". Models and samplers move under "Advanced". Everything still submits whitelisted parameters only and runs only on registered execution servers.

#### T103 Version history (F82)

Every edit creates a new version; originals are never overwritten; compare and revert. Store only version links and file references; rules follow the project archive (owner-only, deletable, temporary-file writes); add the rule to AGENTS.md first.

#### T104 Local edits and product lock (F82)

- Reuse the existing inpaint mask controls (`index.html`'s `mask-canvas`).
- One sentence describes the change (Chinese allowed); the server turns it into a prompt.
- **Product locked by default:** the cut-out mask protects the product area; after the edit the original product is pasted back; the user may unlock.
- The model is G1's editing model.

#### T105 Object removal, background swap (F82)

- Removal: brush over clutter, text, watermarks to remove them.
- Background swap: the product stays; the scene changes by text or reference.

#### T106 Instruction editing (F82)

Whole-image instructions like "warmer light" go through the editing model with the product pasted back. Common-instruction buttons lower the entry bar.

#### T107 Aspect change and upscale

Wire the existing outpaint and upscale into platform sizes: picking a slot computes the outpaint direction and upscale factor.

#### T108 P1 features (split into separate tasks first)

- **Text layer:** editable, addable, changeable.
- **Recolor and rematerial:** must warn "must match the physical product" (Taobao forbids mismatch).
- **Lighting:** direction, shadows, relighting.
- **Batch apply:** one edit across SKUs or a whole set.
- **PSD export:** existing F26.

**P2:** model and pose swaps (needs the clothing route). Workflow upload and advanced parameters exist and stay.

### Phase 6

#### T109 Open-source / team-edition split (F16 revised)

| Module | Open source | Team |
|---|---|---|
| Follow a reference, platform export, white background | ✓ | ✓ |
| Basic sets, basic refinement | ✓ | ✓ |
| Hardware detection and one-click install | ✓ | ✓ |
| Accounts | 1–2 | Priced per account |
| LAN multi-server, NAS, shared brand kits | | ✓ |
| Usage statistics, approval flows | | ✓ |
| Industry templates and reference packs, support, deployment | | ✓ |

**Technique:**
- Team features live in a separate closed-source extension assembly registered through interfaces; the open-source edition lacks the code rather than hiding it behind a switch. **The limits live in open code; anyone can remove them.**
- Before splitting, map the dependencies: `ExecutionScheduler`, `WorkSync`, `WorkflowReview` couple deeply to the main flow; extract interfaces first. The open-source edition ships single-machine defaults.
- Licensing: offline license files, account-count only (A12). No watermarks or crippled features (tracker "not doing").

**Licenses (lawyer confirmation required before release):**
- SwarmUI upstream is MIT; keep the copyright notices (`LICENSE.txt`, `THIRD_PARTY_NOTICES.md`).
- Self-developed parts: AGPL-3.0 + a contributor agreement is suggested, keeping commercial options.
- ComfyUI is GPL-3.0; connecting as a separate process is usually fine, but bundling and bundled nodes need confirmation.
- Models are never distributed with the software; users download them.

#### T110 Requirements and release documentation

The website and README state the minimum specs and offer a "can it run" check (reusing T91), so users do not discover incompatibility after installing. Chinese and English in sync.

## 6. Key design

### 6.1 The new generation page

```
Step 1  Product      Transparent PNG / normal photo (auto cut-out) / product library / camera
Step 2  Platform and size → references (1 = single image, several = a set, slots editable) → similarity → [ ] also a white-background image
        For sets: fill in selling points and specs
Step 3  Pick         Star / make another / edit this one / package per platform and download
```

### 6.2 New APIs (summary)

| API | Permission | Notes |
|---|---|---|
| `EcomCraftStudioReferenceDescribe` (extended) | Generate | Adds `role`, `placement`, flags |
| `EcomCraftStudioGenerateSet` (extended) | Generate | Enables `reference_mode`, adds `reference_strength` |
| `EcomCraftStudioHardwareStatus` | Server settings | Hardware and tier |
| `EcomCraftStudioModelRecipes` | Server settings | Recipes and install state |
| `EcomCraftStudioModelInstall` | Server settings + download models | Install by recipe ID; no addresses accepted |

**Every new API:** handle timeouts, cancellation, concurrency; plain-language errors; never return addresses, paths, keys, prompts or node graphs. Follow the API-route skill for new routes. `docs/APIRoutes/` is generated; never hand-edit.

### 6.3 Unchanged security rules

- Clients submit only workflow IDs and whitelisted parameters, except own-workflow upload (F60).
- No paths, node graphs or backend selectors.
- Generation runs only on registered execution servers.
- No client-submitted model file names or download URLs.

## 7. New tracker IDs

| ID | Feature | Tasks |
|---|---|---|
| F74 | Reference fusion route (references v2) | T83, T84, T85, T87, T88 |
| F75 | Product locating, paste-back and distortion checks | T86 |
| F76 | Hardware detection and one-click model setup | T91, T92, T93 |
| F77 | Windows and Mac installers | T96 |
| F78 | Full sets by slot | T98, T101 |
| F79 | Selling-point info and wording checks | T99 |
| F80 | Selling-point images and detail-page layouts | T100 |
| F81 | Built-in reference library | T102 |
| F82 | Professional-mode refinement bench | T103–T108 |

**Revised IDs:**
- F61 → T89, T90.
- F40 → T94.
- F23 → T95, priority raised.
- F58 → T97.
- F16 → T109.
- F18 → deferred (not built).
- T62 → superseded by T84.
- T77 → deferred (route E leans less on vision).

**New non-development items:**
- N11 Prepare a Windows-NVIDIA and an Apple-silicon Mac test machine.
- N12 Run the reference-route trial.

## 8. Risks and to-dos

### 8.1 Must be handled before release (not a development blocker now)

| Item | Notes |
|---|---|
| **Model licensing** | Qwen-Image 2.1's official license says "FOR NON-COMMERCIAL PURPOSES ONLY" (research and evaluation); commercial use requires applying to `model-business@notice.qwencloud.com`. The user decided not to consider it now. **Do not raise it repeatedly during development, but never claim commercial usability in public documents.** |
| Default model files | The provenance and the "UC" in `qwen-image-2.1-UC-Q4_K_M.gguf` need verification |
| Dependency licenses | `qwen3-vl`, ComfyUI-GGUF etc. pending |
| Legal counsel | The highly-similar boundary; edition licensing |
| AI labeling | Domestic and foreign rules need a fresh check; visible labels (F49) are unbuilt |

### 8.2 Main risks

| Risk | Mitigation |
|---|---|
| No route reaches the bar | The G1 gate; no development below the bar |
| The editing model changes logos and text | T86 verification and paste-back; never deliver silently |
| The Mac is too slow or nodes break | G1 tests the Mac too; a separate recipe is possible; the user may adjust Mac targets |
| Product angle differs from the reference | Prompt for another image; multi-angle products later |
| Pasting back looks worse than not | Compare in trials; configurable thresholds |
| Taobao bans mismatched goods and "obviously pasted" AI images | Fidelity checks; verify the rule text before release |
| The dev machine cannot compile | State it honestly; every C# task counts as done only after the user verifies |
| Hidden old entries break dependents | T90 handles each; code stays for now |

### 8.3 Deferred or not built

- Not built: GPU-less and cloud generation, competitor scraping, Linux verification investment, complex roles, watermark limits.
- Deferred: clothing virtual try-on, favorite analysis (F67), App Mode compatibility (F27), brand-model training (F17), vision hardware recommendation (T77), multi-machine trials (F28), product video (F48).
- **Possible later (not registered):** multi-angle product photos, same-style multi-SKU, on-image text in multiple languages, automatic candidate filtering (F21; T86 is its basis).

## 9. Completion report per task

At the end of every task, report:

1. Which files changed and why.
2. Which checks ran and their results (paste failures verbatim).
3. **What was not verified** (not compiled, no GPU run, not on a Mac) and how the user should verify.
4. Whether documents and the tracker were synced (Chinese and English).
5. Out-of-scope problems found (report only; never fix in passing).

A task's status becomes "complete" only after the user verifies it; until then it is "pending acceptance".

## Appendix: competitor references (mostly from official marketing or third-party pages; not tested; prices may have changed)

| Product | Type | Worth borrowing | Price signal |
|---|---|---|---|
| [Meitu DesignKit](https://www.designkit.cn/ai-product-background) | Chinese SaaS | Reference replacement of the old product, manual removal, product sets and detail modules | Third parties say ~¥68/month for Pro |
| [PicSet AI](https://www.tbadc.com/hao/picset-ai.html) (third-party page) | Chinese SaaS | 4–15 image sets, ordering logic, selling-points-to-text, universal canvas | Third parties say from ~¥35/month |
| [WeShop](https://gongke.net/tools/weshop) (third-party page) | Chinese SaaS | Model images, set replication | Third parties say from ~¥29.8/month |
| [Yiketu (review)](https://m.tech.china.com/articles/20260515/202605151869521.html) | Chinese SaaS | Smart and manual set assembly, slot classification | Unknown |
| [Tencent Cloud AI product sets](https://cloud.tencent.com/document/product/862/133519) | API | 6 sub-image kinds, up to 12 images, multi-platform specs | Unknown |
| [Claid](https://claid.ai/ai-style-transfer) | International | Positioning references as read-only inspiration, not copied | Credit-based |
| EcomGen, ecommerce-image-suite, ComfyUI App Mode, Invoke, Photoroom etc. | See the tracker | See the tracker's "竞品分析" sheet | — |

**Model references:** [Qwen-Image-2.1 LICENSE](https://huggingface.co/Qwen/Qwen-Image-2.1/blob/main/LICENSE) · [Qwen-Image-2.1](https://github.com/QwenLM/Qwen-Image-2.1) · [ComfyUI on 2.1](https://blog.comfy.org/p/qwen-image-21-in-comfyui-open-weight) · [Qwen-Image-Edit-2511](https://huggingface.co/Qwen/Qwen-Image-Edit-2511) · [ComfyUI 2511 docs](https://docs.comfy.org/tutorials/image/qwen/qwen-image-edit-2511.md) · [FLUX.2 klein](https://bfl.ai/blog/flux2-klein-towards-interactive-visual-intelligence)

**Platform references:** [AMD and ComfyUI](https://www.amd.com/en/blogs/2026/amd-comfyui-advancing-professional-quality-generative-ai-ryzen-radeon.html) · [Local generation speed on Mac (third party)](https://www.heyuan110.com/posts/ai/2026-02-15-mac-mini-local-image-generation/) · [Flux on Apple silicon (third party)](https://apatero.com/blog/flux-apple-silicon-m1-m2-m3-m4-complete-performance-guide-2025) · [Taobao AI-image enforcement report](https://www.stdaily.com/web/gdxw/2025-03/28/content_316284.html)
