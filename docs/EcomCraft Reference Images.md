# EcomCraft Reference Images

> **Language:** English · [简体中文](EcomCraft%20参考图需求.zh-CN.md)
>
> **Status:** Confirmed (F59 / T61 pending development, T62 depends on N10) · **Version:** v1.1 · **Date:** 2026-10-28
>
> Related files: [EcomCraft Architecture and Roadmap](EcomCraft%20Architecture.md) (decision A11, F59) and the [EcomCraft development tracker](EcomCraft%20开发总表.xlsx) (T61, T62; N10; Chinese spreadsheet). When this document changes, update it (Chinese and English together) first, then the code.

## 1. Background and User Story

Besides their own product photo, operators often hold **finished images from competitors**: layouts, scenes, lighting and prop styling worth borrowing. They want "an image with the same feel, but with my product", not a text description of a composition — operators cannot phrase "45-degree overhead, clear space upper-left, warm morning light".

User story: *"I upload my product photo and several reference images (say a competitor's main image, scene image and detail image); the platform generates one image per reference — each matching the feel of its own reference, but with my product."* Later: use a competitor's whole image set as references and generate one's own set in one go.

## 2. Confirmed Decisions

Confirmed with the user on 2026-10-28 (A11, see the architecture document):

| # | Decision |
|---|---|
| 1 | **Both paths are open**: path 1 "borrow the composition" (prompt reference) and path 2 "scene reference" (image-to-image) are both offered; path 2 needs the administrator to **install an extra image-to-image model** on the execution server (depends on the N10 plan), and while it is missing the option is greyed out with a reason, without affecting path 1. |
| 2 | **Restore the feel, never a 1:1 copy**: Chinese platforms generally tolerate product-image borrowing, while international platforms review image similarity more strictly. The product goal is "restore the layout, light and mood"; the interface copy, prompt composition and (path 2's) repaint strength all serve that goal, and no "replicate / same-as / 1:1" feature is built. |
| 3 | **Each reference generates its own image, never merged** (clarified by the user, 2026-10-28): every reference image maps to one independent task that produces one image matching **that reference**; there is no primary/secondary split and no merged description. References are another expansion axis for image sets ("one per reference" alongside "one per image type") and are mutually exclusive with F25 storyboards. Cap of 8 (matching storyboards), still under the 20-image total cap. |
| 4 | **Reference images are temporary, never stored** (added by the agent): they exist only inside the request and the in-memory batch, and are discarded when the task ends; they never enter the product library, the F04 project archive, diagnostics or backups (section 7). |
| 5 | **Template scope** (added by the agent): path 1 is prompt text and works for every template; path 2 needs the template to declare a `reference` image slot, initially only scene-kind templates (`scene_main` / `detail` / `model` slot kinds); the white main image needs none. |

## 3. The Two Paths

### 3.1 Path 1: borrow the composition (prompt reference)

Each reference image goes to a vision model (MagicPrompt's vision capability; off by default per decision A5, enabled by the administrator or replaced by a local vision model), which produces **that reference's own** English composition description (camera, light, scene, color); the description joins that task's prompt, and the existing text-to-image plus product-preservation pipeline does the rest.

- One reference, one task, one description — **never merged, never mixed**, so multiple references cannot fight each other.
- No new generative model is needed; the existing text-to-image pipeline works as is.
- Effect: the broad direction of layout and mood carries over, details less so — consistent with "restore the feel".
- With vision off: the upload entry still works, but shows "borrowing compositions needs the administrator to enable image description", and the references then have no effect (generation is never blocked).

### 3.2 Path 2: scene reference (image-to-image)

The reference image becomes the input of an image-to-image / image-edit workflow: "put my product into a frame with a similar feel".

- **Each task's reference is its own input**: with one-to-one mapping the single-input nature of image-to-image is a perfect fit; multi-image blending (IPAdapter-style) is left for later.
- **Depends on N10 (the image-to-image plan)**: the default model chosen in N01 is text-to-image only; execution servers need an extra image-to-image model (such as a Qwen-Image-Edit style instruction model). Templates declare `requirements` as usual, the F08 model checklist and dependency check show what is missing, and templates whose models are missing stay hidden from users (existing behavior).
- Workflow sources: commissioned from experts (N02), or the F57 standard-workflow generator extended to an image-to-image branch (both after N10).
- **Feel, not copy**: the repaint strength / reference weight is fixed by the template in a sensible (lowish) range and checked at review; the interface consistently calls path 2 "scene reference (restores the feel)".
- The template declares a `reference` slot (like the brand kit's F12 `reference_image`): `"reference": { "image": "parameter ID" }` pointing at an image parameter of the workflow snapshot; it may not point at the same parameter as `brand_kit.reference_image` (a validation error). Clients submit only image data; the server fills the parameter per task; the allowlist boundary is unchanged.

## 4. Interface and Interaction (simple mode)

1. **Reference images (optional) entry**: a new upload entry in step 2, **at most 8**, wrapping rows of thumbnails; a 9th is refused with a hint about the cap. Uploads are compressed client-side (the same 5 MB cap as gallery picks, F52) and validated server-side like any image. Each can be removed; there is no primary/secondary marking.
2. **Reference mode**: once uploaded, two options appear, applying to all references uniformly —
   - **Borrow the composition** (default): each task's own reference is read by the vision model and its composition description joins that task's prompt; when image description is off, the reason is shown.
   - **Scene reference (restores the feel)**: each task fills its own reference into the image-to-image input; selectable only when the chosen image type's template declares a `reference` slot and can run (models present), otherwise greyed out with a reason ("this image type does not support scene reference yet" or "the administrator needs to install an image-to-image model").
   - Both may be ticked (composition description joins the prompt, and the reference goes to the image-to-image input).
3. **Count hint**: with references present, the button hint becomes "products × references × candidates", updated live; over the 20-image cap the buttons disable with a reason (F55).
4. **One-click sets**: with references present, the set expands as "one image per reference" (using the currently chosen image type's template) instead of by set types; **storyboards (F25) and references are mutually exclusive** — using both prompts the user to pick one.
5. **Task cards**: reference-driven tasks show the **thumbnail of their reference** on the card, with the task's actual prompt unfoldable (reusing F56's `prompt_used`).
6. **Copyright notice**: fixed under the entry — references are used only to borrow layout and light; use only images you have the right to use (self-shot, licensed or restriction-free), and keep results clearly distinct from the references.
7. **Stronger notice on international platforms**: when the chosen platform's `region` is `global`, an extra notice appears — international platforms review image similarity more strictly; borrow the layout only and make sure results do not resemble the reference before listing.

## 5. Data and APIs

| API | Change |
|---|---|
| `EcomCraftStudioGenerate` | New optional `reference_images` (array of image data URLs, 1–8) and `reference_mode` (array of `composition` / `scene`): with several references the server expands them into several tasks (one per reference), prompt composition happens per task in the background (the F56 flow), and the response keeps the batch/task structure |
| `EcomCraftStudioGenerateSet` | Same parameters; with references the batch expands as products × references (one task per reference, using the current template), mutually exclusive with `shots` (submitting both returns `invalid_parameters`); tasks carry `reference_index` so the page can show the reference thumbnail |
| `EcomCraftStudioExpandPrompt` | Unchanged: AI expansion still handles the description and product type; each reference's composition description is produced per task in the background after submission, and the task card shows the actual prompt |
| `EcomCraftStudioTemplates` | Template entries gain `reference_slot` (whether the scene-reference slot is declared), so the page can control path 2's availability |
| Validation | each `reference_images` entry is an image data URL validated like a product photo, count 1–8; `reference_mode` is a whitelist enum; template validation requires `reference.image` to be an image parameter of the snapshot and not the same parameter as the `brand_kit` slot |

## 6. Copyright and Compliance Boundary

- **Not built**: automatic scraping of competitor images, competitor image libraries or recommendations, and any "one-click replicate / same-as" feature — a red line.
- The fixed copyright notice plus the stronger international-platform notice (section 4).
- Similarity checks between results and references (warning when too close) belong under F21 automatic candidate filtering; not in this phase.
- Platform export (F58) does not know about references; AI metadata follows the existing F22 rules.

## 7. Storage and Privacy

- References **exist only inside the request and the in-memory batch**: kept in memory like product photos for the batch's lifetime (six hours), **never written under `Data/`**, and never entering the product library (F36), the project archive (F04), the candidate-cleanup registry (F39), diagnostics (F44) or backups (F45).
- Visible only to the submitter; shared queue and progress APIs never return references.
- Implementation adds the matching rule to AGENTS.md (as F04 did).

## 8. Phasing and Dependencies

| Phase | Content | Depends on | Task |
|---|---|---|---|
| P1 | Reference upload (up to 8) with copyright notices, one-per-reference task expansion (single shots and whole sets, mutually exclusive with storyboards, under the 20-image cap), borrow-the-composition (vision reads each task's reference in the F56 background flow), reference thumbnails on task cards, stronger international-platform notice | No hard dependency (A5's vision switch exists) | T61 (M) |
| P2 | Scene reference: the template `reference` slot and validation, each task filling its own reference into the image-to-image input, image-to-image templates (N02 / the F57 extension), dependency-check display | **N10** (the image-to-image plan and model installation), N02 | T62 (L, not started) |
| P3 (reserved) | Whole-set references → a whole set: the vision model classifies each reference's image type (white / scene / detail / model) and matches templates automatically, generating the set in one go | The vision classification accuracy after P1, N10, seed-team feedback | Not registered; see section 10 |

## 9. Acceptance Criteria (T61)

- With 3 references and 1 product: 3 images, each matching **its own** reference's composition (not a blend); a 9th upload is refused with a hint.
- 2 products × 3 references × 2 candidates = 12 images submits; adding more past 20 disables the buttons with a reason.
- Storyboards and references together prompt a choice; with references, one-click sets expand per reference with a correct count hint.
- Task cards show their reference thumbnail and actual prompt; with vision off the interface explains why and plain generation is unaffected.
- References never appear in the product library, gallery, archive or diagnostics; nothing remains after a service restart.
- International platforms show the stronger notice; Chinese platforms show the basic copyright notice only.
- The interface is complete in both languages; the `studio-i18n` and `simple-mode` tests pass.

## 10. Change Log

| Date | Version | Description |
|---|---|---|
| 2026-10-28 | v1.0 | First version: user story, the two-path decision (A11), interface and API design, copyright boundary, temporary-storage rules, P1/P2 phasing and acceptance criteria |
| 2026-10-28 | v1.1 | Revised the multi-reference semantics per the user's clarification: from "merge several references into one feel" to **one image per reference, never merged**; cap changed to 8 (matching storyboards), mutually exclusive with F25 storyboards, sets expand per reference; reserved the P3 direction "whole-set references → a whole set" |
