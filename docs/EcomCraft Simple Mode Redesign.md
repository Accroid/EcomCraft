# EcomCraft Simple Mode Redesign

> **Language:** English · [简体中文](EcomCraft%20简易模式改版需求.zh-CN.md)
>
> **Status:** Confirmed, development pending · **Version:** v3 · **Date:** 2026-10-28
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

The former "style" cards become a two-level "product type": pick a category, then a subtype card. Subtypes carry English prompt fragments merged into the prompt at generation time.

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
- **Initial scope**: scene, detail and model scenes (one blueprint, prompt-differentiated); the white main image needs no workflow (cutout-only branch); the dedicated "model image" template keeps the manual configuration path.

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

## 11. Development Progress

| Feature | Status | Notes |
|---|---|---|
| F52–F57 | Not started | Interactive prototype done and confirmed: [EcomCraft Simple Mode Redesign Prototype](EcomCraft%20简易模式改版原型.html) |

## Change Log

| Date | Version | Notes |
|---|---|---|
| 2026-10-28 | v3 | Initial final version: R1–R7 confirmed as features F52–F57; category system, bilingual prompt rules, the 20-image cap, the faster generation path and automatic workflow creation; interactive prototype attached |
