# EcomCraft Simple Mode Redesign

> **Language:** English · [简体中文](EcomCraft%20简易模式改版需求.zh-CN.md)
>
> **Status:** In development (F52–F56 implemented, pending build and manual acceptance; F57 code and blueprint file `Templates/blueprints/text-to-image.json` complete, pending build and acceptance) · **Version:** v3.7 · **Date:** 2026-10-29
>
> This document holds the finalized requirements for the simple-mode (three-step) redesign. Development follows this document; when requirements change, update this document first (Chinese and English together), record the change in the change log, and only then change the code. For the overall architecture and MVP roadmap see [EcomCraft Architecture and Roadmap](EcomCraft%20Architecture.md); for the interactive prototype see [EcomCraft Simple Mode Redesign Prototype](EcomCraft%20简易模式改版原型.html) (open directly in a browser; Chinese UI).

## 1. Background and Scope

Trial feedback after the MVP focused on simple-mode usability and out-of-the-box readiness: seven improvements, finalized as six features (F52–F57, registered in the architecture document and the development sheet):

| Requirement | Feature | Summary |
|---|---|---|
| R1 Product image upload | F52 | Four parallel sources: local file, camera, product library, gallery; generated works can be reused as product images |
| R2 Platform and style | F53 | "Style" becomes a two-level "product type" (category → subtype); visual style becomes optional; new "needs a model" checkbox |
| R3 One-line description | F54 | AI expansion; a bilingual (Chinese/English) prompt panel, both languages editable |
| R4 Candidate count | F55 | Manual positive-integer input; one shared cap of 20 images for single generation and one-click sets alike |
| R5 Start generating | F56 | Submission queues immediately; prompt composition moves to the background so ComfyUI starts ASAP |
| R6 Automatic workflow creation | F57 | The server generates standard ComfyUI workflows per image type and registers them |
| R7 Distinguishing the two generate buttons | folded into F55 | Hint texts only; no behavior change |

Out of scope: the three-step flow itself, professional mode, the cutout-only white main image branch (F22), backend security boundaries (section 9), and cloud generation.

## 2. R1 Product Image Upload: Four Sources (F52)

Step 1 offers four parallel, equal sources:

1. **Local upload**: click or drag, PNG / JPEG / WebP, max 5 MB (existing behavior).
2. **Camera upload**: opens the camera on phones; equals file picking on desktop (F42, existing behavior).
3. **From the product library**: the existing collapsed entry becomes a parallel button; a dialog lists the account's saved products (with confirmed cutouts and placements) as selectable thumbnails.
4. **From the gallery (new)**: a dialog browses the account's works gallery; pick an already generated work as the product image for iterative creation.

Gallery-picked images:

- Large gallery images are compressed to 5 MB client-side before use (existing limit kept).
- Upload quality checks (F38: resolution, blur, product coverage) apply.
- The picked work itself is never modified; library save, rename and delete behave as for normal uploads.

**Acceptance**: all four sources add a product image to the thumbnail strip; the source (local / camera / library / gallery) is visible on the thumbnail; gallery images over 5 MB are compressed first.

## 3. R2 Product Type, Optional Style and Model (F53)

### 3.1 Product type (two levels, primary)

The former "style" cards become a two-level "product type": pick a category, then a subtype card. Subtypes carry English prompt fragments merged into the prompt at generation time; a fragment describes a scene that suits the product, never the product itself (the product is pasted back after generation, so a described one would be drawn a second time).

| Category | Subtypes |
|---|---|
| Personal care & beauty ⭐focus | Shampoo, hand cream, body lotion, lipstick, perfume |
| Home appliances | Air conditioner, refrigerator, washing machine, rice cooker |
| Home & daily use | Drinkware & tableware, storage, table lamp, bedding |
| General product | Unspecified (fallback) |

- Data lives in `src/Extensions/EcomCraft/Templates/product-types.json`, same format as `styles.json` (Chinese/English names + English prompt fragment); administrators can edit it; validation rules match styles.
- Categories and subtypes stay at 3–5 entries each, with personal care as the focus; later additions only touch the data file.
- The choice is remembered in browser local storage (existing mechanism, key updated).

### 3.2 Visual style (optional, second row)

The six visual styles remain as an **optional** second row: the first card is "Auto (unspecified, up to the model)"; when unselected, no style fragment enters the prompt.

### 3.3 Needs a model (checkbox)

- When checked, a fixed model fragment (one display text, one English prompt fragment) joins the prompt; gender, hand-model and similar attributes come later.
- Mutually exclusive with the white main image: selecting that type disables and unchecks the box.
- Available for every image type except the cutout-only white main image; model scenes reuse the scene workflow blueprint, distinguished by the prompt.

**Acceptance**: the two-level choice, optional style and model checkbox interlock correctly and show in the prompt preview; invalid `product-types.json` entries are skipped and logged; the UI is complete in Chinese and English.

## 4. R3 One-line Description, AI Expansion and Bilingual Prompts (F54)

### 4.1 AI expansion

- An "AI expand" button next to the description box: the one-line description (or the chosen product type when empty) goes to MagicPrompt and is expanded into a full prompt, generated in the panel's current language and filled into the prompt panel for further editing.
- Failure fallback: a notice that the prompt service is unavailable and the user can generate directly with the one-line description; generation is never blocked (the server still composes at submit time, see F56).

### 4.2 Prompt panel (bilingual)

- The panel offers **Chinese / English tabs**; AI expansion fills both; both languages are editable.
- **Sending rules (final)**:
  - The user edited the **English** prompt → sent to the engine as-is, no translation.
  - The user edited the **Chinese** prompt → at submit time the **user's edited Chinese** is authoritative, automatically translated to English and sent (never the pre-edit version); the translation is written back to the English tab.
  - Unedited → the generated English is used.
  - **The prompt sent to the engine is always English** (diffusion models perform best with English; Chinese-capable models may extend this later).
- A status chip shows live state: "Synced", "Chinese edited · auto-translated on submit", "English edited · sent as-is".
- After generation, the task card shows the **actually used prompt** (Chinese display + English sent) in a collapsible box.
- Simple mode is still not a prompt workbench: one text area, no samplers, LoRAs or other professional parameters.

**Acceptance**: expansion fills both languages; after editing Chinese and submitting, the engine receives English translated from the edited Chinese; after editing English, it receives the user's text verbatim; generation still works when the model is unavailable; status chips are correct in both UI languages.

## 5. R4 Candidate Count and R7 Button Texts (F55)

### 5.1 Candidate count

- The dropdown becomes a number input accepting positive integers (1–20) only; invalid input shows "Enter a positive integer" / "Candidate count must be 1–20" immediately and disables the generate buttons.
- Server-side validation widens from 1–4 to 1–20 (video stays 1).

### 5.2 Shared cap of 20 (single and set)

- **Start generating**: candidate count ≤ 20.
- **One-click set**: estimated count = products × set types × candidates, **total ≤ 20**; above that the button disables with "Estimated N images, above the 20 cap; reduce products, types or candidates".
- The server enforces the same total cap for single and set requests.

### 5.3 Distinguishing the two buttons (R7, texts only)

- "Start generating" hint: N candidates for the current product × the chosen image type (with/without model).
- "One-click set" hint: N products × M set types (names listed) × K each, estimated X images (cap 20).
- Generation logic unchanged.

**Acceptance**: invalid input is blocked with disabled buttons; the set estimate updates live and disables above 20 with the reason; the server rejects over-cap requests; the cutout-only "one image" hint stays correct.

## 6. R5 Faster Generation Path (F56)

Today the server waits for MagicPrompt to compose the prompt (up to 45 s) before queueing, leaving a visible gap between the click and ComfyUI starting.

After the redesign:

- **Submission queues immediately** and returns the task id (single and set alike); prompt composition/translation moves into the background task and dispatches to ComfyUI as soon as it is ready.
- The model call is skipped when the user already expanded and did not edit, or edited the English prompt — the task reaches execution in seconds.
- Status shows three phases: **composing prompt → queued / running (with the execution server name) → done**.
- Fallback unchanged: when the prompt service is unavailable or times out (45 s cap kept in the background), the template + product-type defaults apply; generation is never blocked.
- One-click sets get the same treatment (today they compose once before building the batch).

**Acceptance**: the click yields task feedback immediately, with no synchronous wait of up to 45 s; the composing phase is visible in task status; the fallback path still generates.

## 7. R6 Automatic Standard Workflow Creation (F57)

Today the built-in templates (white main / scene / detail / model) reference placeholder workflow names that must be created by hand in each execution server's ComfyUI and published through review — the largest blocker to out-of-the-box use.

After the redesign:

- The extension ships a **workflow blueprint generator**: the server builds standard ComfyUI workflow JSON per image type (default checkpoint + sampler + decode + save; product-preserving templates carry a product-image input node, and the existing product-preserve post-processing pastes the original pixels back).
- **Registration (security boundary unchanged)**: generated workflows enter the WorkflowReview registry marked "system-generated", auto-publish after structural validation, remain visible in the review list and can be unpublished. **Ordinary users still submit workflow IDs only, never JSON.**
- **Triggers**:
  1. after startup the server checks automatically: a built-in template's workflow unpublished and an execution server online → generate and register;
  2. an "Initialize standard workflows" button plus a status list (generated / missing model / failed) in the administrator settings as a fallback.
- **Model dependency**: blueprints use the administrator-configured **default checkpoint**; before generating, the target execution server is checked for the model (F08 dependency check reused); without it, nothing is registered and a clear notice is shown.
  - Default models (N01, decided 2026-10-28): UNet `qwen-image-2.1-UC-Q4_K_M.gguf` (GGUF, needs the ComfyUI-GGUF nodes), text encoder `qwen3vl_8b_int8_convrot.safetensors`, VAE `qwen_image_2.1_vae_bf16.safetensors`. The first blueprints support only this model set and only **text-to-image**; the commercial license of this exact version still needs checking.
  - Image-to-image (product reference, inpainting, product blending) has no plan yet and is listed as non-development item **N10** (to do); blueprints are extended once it is decided.
- **Initial scope**: scene, detail and model scenes (one blueprint, prompt-differentiated); the white main image needs no workflow (cutout-only branch); the dedicated "model image" template keeps the manual configuration path.

**Implementation (T59)**:

- The blueprint is the ComfyUI workflow file shipped with the extension, `Templates/blueprints/text-to-image.json` (ComfyUI "Save" format, subgraphs allowed), converted and checked by the same `NativeWorkflowConverter` as discovered workflows; loader model names are replaced by the server settings `EcomCraft.StandardWorkflowUnet`, `StandardWorkflowTextEncoder` and `StandardWorkflowVae` (defaulting to the N01 set).
- Every generated workflow also gets a visible image parameter `ecproductimage` for the templates' `@image` and product preservation; the first version does not read it in the graph, and product preservation pastes the original product back after generation.
- They are published as `系统生成/ecomcraft/scene` and `系统生成/ecomcraft/detail` (matching the built-in templates' `*/ecomcraft/…` names), with "系统生成" (system generated) as source and reviewer, and only when at least one ready execution server has every node and model; otherwise the state is "missing nodes or models" with the names.
- About 30 seconds after startup a check runs and retries every minute until everything is published, taken down or the blueprint is missing; one taken down in the review is never restored by the automatic check. Administrators see the states under "Manage → Standard workflows" and "Initialize standard workflows" generates them at once (republishing ones taken down). API: `EcomCraftStandardWorkflows` (server-settings permission).

**Acceptance**: in a fresh environment with an execution server registered and the default model installed, scene and detail images generate without hand-built workflows; the review list shows the "system-generated" source and supports unpublishing; missing models produce a clear notice and no registration.

## 8. Relationship to Existing Features

| Existing | Impact |
|---|---|
| F07 three-step simple mode | Layout unchanged (topbar + three step cards); step 1 and 2 controls updated per this document |
| F37 prompt service | ComposeAsync gains expand / translate / compose call kinds; output remains prompt text only |
| F36 product library | Entry becomes a parallel button with a dialog; data unchanged |
| F03 one-click sets | Count math and cap change; batch orchestration unchanged |
| F22 white main image | Unchanged; the mutual exclusion with the model checkbox is surfaced in the UI |
| F08 model inventory | Reused by F57 before auto-generating |

## 9. Security Boundaries (Unchanged by This Redesign)

- Clients submit template IDs, server-published parameter whitelists and product-type/style IDs only; never arbitrary ComfyUI JSON, node graphs or backend-selection parameters.
- Generation goes only to registered execution servers (local self-starting or registered LAN ComfyUI); no fallback to unregistered addresses or the cloud.
- The prompt sent to the engine is always English; user-edited Chinese prompts are translated server-side, and the result is used only as prompt text.
- System-generated workflows come from server blueprints, publish through the review registry and can be unpublished; ordinary users have no JSON submission channel.
- Progress observation stores no prompts or results long-term; candidate cleanup (F39) and minimal disclosure rules are unchanged.

## 10. Feature IDs and Development Order

| Feature | Task | Scope |
|---|---|---|
| F52 four upload sources | T54 | Local / camera / library dialog / gallery pick (with compression) |
| F53 product type, style, model | T55 | Two-level choice, optional style, model checkbox, product-types.json |
| F54 bilingual prompts and AI expansion | T56 | Expand button, bilingual panel, editing and sending rules |
| F55 candidate count and button texts | T57 | Positive-integer input, 20-image cap, estimate hints |
| F56 faster generation path | T58 | Queue on submit, background prompt composition, three-phase status |
| F57 automatic workflow creation | T59 | Blueprint generator, auto-registration, default-model check |

Development order: **T55 → T57 → T56 → T58 → T59 → T54** (controls first, then the generation path, then workflows and upload sources). Each step runs the i18n tests and a build check before the next.

## 11. Progress and Handover (for the agent continuing development)

### 11.1 Current state (2026-10-28)

| Feature | Task | Status | Commit | Notes |
|---|---|---|---|---|
| F53 product type, optional style, model | T55 | **Implemented**, deployed, pending manual acceptance | 467c036d | Two-level choice + `product-types.json` + `EcomCraftStudioProductTypes` API; UI design doc v1.13 |
| F55 candidate count and button texts | T57 | **Implemented**, deployed, pending manual acceptance | 0e7c6b3e | Number input 1–20; single generations and sets share the 20-image cap (enforced server-side too); UI design doc v1.14 |
| F54 bilingual prompts and AI expansion | T56 | **Implemented**, not yet built, pending deployment and manual acceptance | 8124a752 | New `EcomCraftStudioExpandPrompt` API; the generate and set APIs accept `prompt_en` / `prompt_zh` and answer with `prompt_used` / `prompt_translated`; translation fallback code `prompt_untranslated`; the server still adds the product-type, model and style fragments; `simple-mode.test.cjs` now allows only the two panel text boxes; UI design doc v1.15. Translation currently runs inside the submit request; T58 moves it to the background |
| F56 faster generation path | T58 | **Implemented**, not yet built, pending deployment and manual acceptance | aee5a0bd | When the model is needed the submission returns `prepare_id` at once and the new `EcomCraftStudioPrepareStatus` API reports the stage; at most 5 submissions per account wait for their prompt; set tasks start in state `prompt` (cancellable); step 3 gained a task list for single generations; UI design doc v1.16 |
| F57 automatic workflow creation | T59 | Code and blueprint complete, pending build and acceptance | — | Generator, automatic publishing, status list and initialize button are implemented; the blueprint `Templates/blueprints/text-to-image.json` comes from the user's verified Qwen-Image 2.1 text-to-image workflow (GGUF UNet, `TextEncodeQwenImage21`), with the custom resolution node removed so the page sets width and height by the platform sizes |
| F52 four upload sources | T54 | **Implemented** (front end only), pending manual acceptance | 01c120e7 | Four equal buttons; the library and the gallery share a picker dialog; the gallery lists the latest 60 generated images and compresses picks over 5 MB to JPEG in the browser; thumbnails show their source; UI design doc v1.17 |

Service state: T56 (8124a752), T58 (aee5a0bd) and T54 (01c120e7, front end only) were written on a development machine without the .NET SDK and have not been built or restarted yet; rebuild the extension as in step 2 before deploying.

### 11.2 How to continue

1. Only **T59** remains in this redesign: N01 is done (default models in section 7); propose a plan and get the user's confirmation before developing; the image-to-image part waits for N10. One task at a time. Each task's files, dependencies, acceptance criteria, test method and a ready-to-paste Claude Code prompt are in the "开发任务" sheet (column K) of the [development sheet](EcomCraft%20开发总表.xlsx).
2. Required verification after every task:
   - `node --check src/Extensions/EcomCraft/Studio/simple.js` (and any other changed js)
   - `node --test src/Extensions/EcomCraft/Tests/studio-i18n.test.cjs src/Extensions/EcomCraft/Tests/error-codes.test.cjs src/Extensions/EcomCraft/Tests/simple-mode.test.cjs`
   - `dotnet build src/Extensions/EcomCraft/EcomCraft.csproj -c Debug -o <temp dir>` (0 errors)
   - `dotnet test src/Extensions/EcomCraft/Tests/EcomCraft.Tests.csproj -c Debug`
   - C# changes need a service restart to take effect (see `.agents/skills/ecomcraft-build-run`); frontend-only changes just need the `?v=` bump in `index.html` plus a forced browser refresh.
3. Hard constraints (see section 9 and `AGENTS.md`): clients submit template / parameter IDs only; only registered execution servers; the engine prompt is always English; every new UI text must be added to `Studio/lang/en.js`; keep Chinese and English docs in sync.
4. One commit per task (dev branch), message format in `.agents/skills/dev-branch-commit`; never commit `src/bin`, `src/obj`, `Data/` or `Output/`.
5. After user acceptance: set the task to "已完成" in the development sheet and refresh 11.1 above.

Interactive prototype (confirmed; this document governs): [EcomCraft Simple Mode Redesign Prototype](EcomCraft%20简易模式改版原型.html).

## Change Log

| Date | Version | Notes |
|---|---|---|
| 2026-10-28 | v3 | Initial final version: R1–R7 confirmed as features F52–F57; category system, bilingual prompt rules, the 20-image cap, the faster generation path and automatic workflow creation; interactive prototype attached |
| 2026-10-28 | v3.1 | Handover release: F53 (T55, 467c036d) and F55 (T57, 0e7c6b3e) implemented and deployed; section 11 rewritten as progress and handover notes (verification steps and the T56 test-assertion caveat included) |
| 2026-10-28 | v3.2 | F54 (T56, 8124a752) implemented: AI expand, the bilingual prompt panel and its sending rules, and "Prompt sent to the engine"; pending build and manual acceptance |
| 2026-10-28 | v3.3 | F56 (T58, aee5a0bd) implemented: submissions return at once, the prompt is written in the background, three-stage task status, sets reworked the same way; pending build and manual acceptance |
| 2026-10-28 | v3.4 | F52 (T54, 01c120e7) implemented: four upload entries, picker dialog for the library and the gallery, gallery compression and source labels; pending manual acceptance |
| 2026-10-28 | v3.5 | F57 (T59) deferred until N01 picks the default model; the other five features are code complete, pending build and manual acceptance |
| 2026-10-28 | v3.6 | N01 done: the default text-to-image models are the Qwen-Image 2.1 GGUF set; T59 no longer deferred, first version text-to-image only; image-to-image listed as to-do N10 |
| 2026-10-29 | v3.7 | F57 (T59) code complete: blueprint conversion with default models, product image parameter, system-generated publishing that can be taken down, startup check and administrators' status list; the blueprint file is still to be provided |
