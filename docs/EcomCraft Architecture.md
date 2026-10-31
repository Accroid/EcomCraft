# EcomCraft Architecture and Roadmap

> **Language:** English · [简体中文](EcomCraft%20Architecture.zh-CN.md)
>
> **Status:** MVP code complete, deployed and accepted in single-server form (A10), pending manual acceptance and seed-team trials (N07); goal updated 2026-10-31 (v3.0): reference-driven single entry, a GPU is required, Windows and Mac first (see sections 1–2 and the EcomCraft Implementation Plan) · **Version:** v3.0 · **Date:** 2026-10-31
>
> Related files:
> - [EcomCraft Implementation Plan](EcomCraft%20Implementation%20Plan.md) (Chinese original [EcomCraft 实施计划](EcomCraft%20实施计划.zh-CN.md)): the new-goal feature plan (F74–F82, T81–T110, N11–N12); phase 0–6 task details live there.
> - [EcomCraft Refactoring Plan](EcomCraft%20Refactoring%20Plan.md) (Chinese original [EcomCraft 重构计划](EcomCraft%20重构计划.zh-CN.md)): the foundation refactoring (R00–R24) interleaved with the implementation plan; see its section 7 for the order.
> - [EcomCraft LAN Multi-Server Platform Requirements](EcomCraft%20LAN%20Multi-Server%20Platform%20Requirements.md): detailed requirements for the platform foundation (execution servers, review, scheduling, storage); it takes precedence.
> - [EcomCraft development tracker](EcomCraft%20开发总表.xlsx) (Chinese spreadsheet): features (F IDs), development tasks (T IDs), non-development items (N IDs) and progress tracking. Feature IDs in this document match the tracker.
> - [EcomCraft UI and Workflow Redesign](EcomCraft%20UI%20Redesign.md): requirements for task-first simple mode, navigation and administration, opening professional mode, and stars with personal analysis (F60–F67); it is authoritative for them (the F61 single-entry revision is in the Implementation Plan's T89).
>
> When the architecture or roadmap changes, update this document (Chinese and English together) and the requirements document first, then the tracker, and only then the code.

## 1. Product Positioning

EcomCraft lets e-commerce operators, designers and small shop owners, **with just one product photo and one reference image they like**, get listing-ready product images and whole image sets on their own GPU-equipped Windows or Mac computer (or LAN) — without learning models or workflows, with the product staying true, and with images never leaving the company.

- **Users**:
  - Operators and shop owners: three steps in simple mode (upload the product → pick platform and references → pick and export), never touching prompts, models or parameters.
  - Graphic designers: adjust models, samplers, LoRAs, steps and other parameters in professional mode (the refinement bench), or do local edits and instruction editing.
  - Administrators (the shop's technical staff, or our deployment service): handle installation and configuration.
- **Hardware threshold (A15, a GPU is required)**:
  - Windows: NVIDIA with at least 8 GB VRAM (16 GB or more recommended); AMD only within the ROCm support list.
  - Mac: Apple silicon only, at least 24 GB unified memory (32 GB or more recommended); Intel-chip Macs are unsupported.
  - Machines below the threshold get a clear "not supported" message; no GPU-less option exists (D5; numbers to be tuned after measurement).
- **Target platforms (A16)**: Windows and Mac first; the Linux launch scripts stay but get no investment or verification for now.
- **Sales model**: open-source core + team edition (closed-source extension), priced per account (A12, A18).
- **Deployment**: one or more GPU computers run ComfyUI, and one of them also runs the EcomCraft platform. Team members open the same page in a browser on the LAN, and the platform automatically sends tasks to idle GPU computers.
- **Markets**: both Chinese platforms (Taobao/Tmall, JD, Pinduoduo, Douyin and others) and international platforms (Amazon, Shopify, TikTok Shop and others); the interface and documentation are available in Chinese and English.
- **Differentiators**: fully local generation (product images never leave the company), product preservation, listing-ready images and sets made by following a reference, and one-click export that follows each platform's rules.

## 2. Confirmed Decisions

| ID | Decision |
|---|---|
| A1 | Deployment is "one GPU computer + browser access", with support for several GPU computers on the LAN; users see a single front-end page and tasks are dispatched automatically. |
| A2 | Target users are operators, graphic designers and shop owners: the default interface is simple mode with only the essential steps; any account can switch to professional mode, with no role restriction (administrators still handle installation, configuration and review). |
| A3 | Both Chinese and international platforms are supported; the interface, documentation and platform presets are provided in Chinese and English. |
| A4 | Users do not write prompts: a one-sentence description (Chinese or English) plus style options is turned into a prompt by the integrated online LLM (MagicPrompt). (Note 2026-10-31: once A14's single entry lands, simple mode no longer shows the one-line description or style options — the controls' code stays, and the capability remains available in professional mode.) |
| A5 | Image description (sending reference images to a vision model to read their composition) is on by default; the default vision model is local Ollama, so images never leave the machine. Administrators can turn it off or switch to a cloud vision service (images then leave the machine, which administrators must confirm). Sending product photos to the vision model to describe products stays off by default and administrators enable it. (Revised 2026-10-29: image description was off by default.) |
| A6 | No cloud generation (revised 2026-10-31; formerly "cloud capability reserved, not developed in the MVP"): generation runs only on the local self-started or LAN-registered execution servers, and no request goes to any cloud generation service; cloud options for the vision and prompt models are unaffected (A5). The scheduling layer still uses the "executor" abstraction but produces only local and LAN executors. |
| A7 | Works are stored on the platform server by default; the administrator can switch to a NAS or a shared folder on another computer; system data always stays local. See D12 and R11 in the requirements. |
| A8 | Within the platform, works are visible only to their owner; the storage layer is not isolated. See D9 in the requirements. |
| A9 | We will not take image orders ourselves to validate demand; instead, 3–5 seed teams will try the product after release. |
| A10 | Single server first: the MVP is deployed and accepted as "one platform server that is also the execution server". The architecture and code for multi-server access, scheduling and storage remain in place; multi-machine testing (F28 / T33, N08) is deferred until a customer asks for it or the test environment is ready. |
| A11 | Reference-image route upgraded to "follow a reference" (F74, revised 2026-10-31): the goal is feeding the reference and the product photo together into an editing model (route E), then locating the product, pasting its original details back and verifying distortion — no longer relying on the old "vision prompt + inpaint" route; the concrete route and model await the G1 trial. "Highly similar" results are allowed (D2, boundary in A17); no competitor-link scraping, libraries or recommendations. References are temporary and never stored. Of the old two paths, scene reference (T62) is superseded by T84; borrow-the-composition stays as the `reference_mode: composition` fallback. |
| A12 | Revenue model (revised 2026-10-31): open-source core + team edition (A18), priced per account. Accounts remain just "administrators" and "regular accounts" with no complex roles or permissions; team-edition features (LAN multi-server, usage statistics, approval flows, industry template packs) live in a separate closed-source extension, licensing still limits only the account count, and the open-source edition is never protected by watermarks or crippled features. |
| A13 | UI and workflow redesign v4 (F60–F67): administration moves into an administrators' "Manage" menu and the native engine is for administrators only; phones get simple mode only; designers may upload ComfyUI workflows, usable by the uploader at once and by other accounts after review and publishing; image description is on by default with setup guidance when no vision model is available; candidates can be starred, personal analysis is deferred. See the EcomCraft UI and Workflow Redesign. (Revised 2026-10-31: the former "task-first simple mode (follow a reference / make one image / make a full set)" is superseded by A14's single entry.) |
| A14 | Single-entry generation page (F61 revised, P4): only "follow a reference" remains; the "make one image" and "make a full set" entries go away; full sets are also reference-driven (several references make a set by slot). Three steps: upload the product → pick platform, size and references (slots editable, similarity, optional white-background image) → pick and export. Controls tied to the old entries are hidden from simple mode (code kept until G2 decides); the tied features migrate per T90. |
| A15 | Hardware threshold (P1, D5): a GPU is required; no GPU-less or cloud-generation options; insufficient hardware gets a clear "not supported" message. Minimums: Windows NVIDIA 8 GB VRAM, Mac Apple silicon 24 GB unified memory; Intel Macs unsupported, AMD only within the ROCm list. Numbers to be tuned after measurement. |
| A16 | Platform focus (P2): Windows and Mac first; the Linux launch scripts stay but get no development or verification for now. |
| A17 | Similarity boundary (D2): users may upload their own references and generate compositions, scenes and lighting highly similar to them; text, logos and watermarks never enter the finished image (the system re-lays-out text); references with real models do not keep their faces (first batch: prompt "not supported"); the copyright notice and the stronger international-platform notice stay; no competitor-link scraping, competitor libraries or recommendations, no "replicate competitors" marketing — the UI consistently says "follow a reference". |
| A18 | Editions (P6): open-source core + team edition (closed-source extension assembly), priced per account; the split is designed in T109. |

## 3. Deployment Topology

```
  Browsers (operators / designers / owners / administrators; Chinese or English UI; any computer or phone on the LAN)
                         │  HTTP, account login
                         ▼
  ┌──────────────── Platform server A: SwarmUI + EcomCraft ────────────────┐
  │  Workspace page → EcomCraft API → orchestration / scheduling /           │
  │  post-processing / export                                                │
  │  System data: Data/EcomCraft/ (review snapshots, templates, platform      │
  │  presets, work index)                                                    │
  └──────┬──────────────────┬──────────────────┬──────────────┬─────────────┘
         │ (1)              │ (1)              │ (2)          │ (3)
         ▼                  ▼                  ▼              ▼
   A local ComfyUI    B / C LAN ComfyUI     NAS / shared folder   Online LLM (MagicPrompt)
   (execution server) (execution servers)   (work library,        prompt enhancement only,
                                             exports)             never generates images
  ┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄
   Not built (A6, revised 2026-10-31): cloud ComfyUI and cloud image generation APIs; cloud options for the vision and prompt models are unaffected
```

(1) Generation tasks, sent only to registered execution servers · (2) Work sync · (3) Text only

## 4. Layered Architecture

| Layer | Module | Responsibility | Status |
|---|---|---|---|
| Front end | Simple mode | Three-step image creation for operators and owners (any account can switch to professional mode, see A2): upload → choose platform and style → pick and download | Implemented (`Studio/simple.js`), the default view; includes four upload sources, product types, the bilingual prompt panel, selling points and storyboards, brand kit choice and selling-point images (simple mode redesign v3 and later enhancements, pending acceptance) |
| | Admin views | Workflow review, template configuration, server status, model checklist, storage settings, setup wizard | Implemented: review view, templates and model checklist, storage settings (with disk thresholds and backup/restore), first-setup wizard |
| | Internationalization | Chinese / English translation tables; every UI string is looked up by key | Implemented for the workspace (`Studio/i18n.js`, `Studio/lang/en.js`) |
| API | Catalog / templates | Returns template names, sample images and adjustable fields (minimal disclosure) | Extends the existing `EcomCraftStudioCatalog` |
| | Generation / image sets | Accepts "template ID + product + platform + description" | Extends the existing `EcomCraftStudioGenerate` |
| | Queue / gallery / export / review / admin | — | Implemented, plus operations APIs (diagnostics, backup/restore, candidate retention) |
| Domain services | Templates and image sets | Template = published review snapshot + e-commerce information (Chinese and English names, sample images, target platforms); image set = a group of templates | Template data structure and management APIs implemented (`Studio/Templates/`) |
| | Prompt service | One-sentence description + style → MagicPrompt → English prompt; its output may only fill prompt text fields | Implemented (`Studio/Prompting/`), wraps the existing MagicPrompt; also AI expansion and Chinese/English translation (F54) and selling-point storyboards (F25; the model may only answer in a fixed line format and every shot's template is checked) |
| | Generation orchestration | Expands image sets and batches into tasks; per-user limits; tracks the status of each image | Implemented (`Studio/Generation/BatchService`); submissions that need the model return at once and the prompt is written in the background (F56); sets can expand by storyboard (F25) |
| | Scheduling | Rule 1: choose the runnable executor with the shortest queue; the executor abstraction produces only local and LAN executors (cloud types de-reserved per A6) | Implemented (requirements step 4, `Studio/Generation/ExecutionScheduler`) |
| | Post-processing | Product-preserving compositing (paste the original product back, using the local cut-out as the mask); candidate scoring later | Product preservation implemented (`Studio/PostProcess/`) |
| | Product library | Caches original product images and background-removal results for reuse | Simple version implemented (`Studio/Products/`) |
| | Brand kits | Brand colors, preferred background, logo and style references, per account | Implemented, pending acceptance (F12, `Studio/Brand/`); the prompt fragment applies to every template, images only fill a template's `brand_kit` slot |
| | Project archive | Files works by customer or product batch with a recipe (template, seed, prompt, execution server) read from the work's metadata | Implemented, pending acceptance (F04, server T09 `Studio/Projects/`, gallery interface T10): switch the current project in the gallery, filter by project, add or remove works, and "Make again with same settings" in the preview fills in the simple mode |
| | Export | Chinese and international slot-based platform presets (F58), padding, zip or export to a shared folder, AI metadata, Amazon pure-white check | Implemented (`Studio/Export/`); also selling-point images (F47: the server draws the text with the bundled Noto Sans SC; no AI writes it) |
| | Storage | Work library and export locations, write locally then sync, migration, cleanup, disk monitoring | Implemented (requirement R11, `Studio/Storage/`) |
| Infrastructure | Reused from SwarmUI | Accounts and permissions, T2IEngine, `ComfyUIAPIBackend`, image processing | Existing |

## 5. Data Flow for a One-Click Image Set

```
1. The operator picks a product from the product library (or uploads a new image) → confirms the
   background-removal result and may drag or scale the product's position
2. Chooses a platform (such as Amazon) and a style (with sample images), optionally adding a
   sentence such as "on a wooden table, morning sunlight"
3. Prompt service: description + style → MagicPrompt → English prompt (used only as prompt text)
4. Orchestration: Amazon set = white-background main image (pure cut-out branch, no generative
   model) + scene + detail + model shot, 4 candidates each
5. Each task is validated item by item against the snapshot whitelist → the user's queue limit is checked
6. Scheduling: dispatched to the execution servers with the shortest queues that have the required
   nodes and models (several run in parallel)
7. ComfyUI outputs the generated image → returned to the platform server (the input canvas is built locally by the platform from the cut-out and product placement)
8. Post-processing: the original product pixels are pasted back at the same placement, using the local cut-out as the mask (standard workflows: details from the original, light from the generated picture, plus a contact shadow) → saved locally
   first, then synced to the work library → gallery
9. The operator picks images → export: platform sizes, pure-white check, AI metadata → download
   zip or export to the shared folder
```

When cloud services are added later, only the executor in steps 6–7 changes; background removal, product-preserving compositing and export still run locally. (Revised 2026-10-31: per decision A6 there is no cloud generation anymore; this sentence stays only as a historical design note — generation always runs on the local or LAN execution servers.)

## 6. Key Design Conventions

### 6.1 Security boundaries (following AGENTS.md)

- The client submits only identifiers such as template IDs, workflow IDs, server-published parameter whitelists and product library IDs; it never submits arbitrary ComfyUI JSON, node graphs, backend selectors or file paths.
- LLM output may only fill prompt text fields and never decides workflows, nodes, parameters or execution servers; the future storyboard feature must also validate every item against templates and whitelists.
- Generation tasks go only to registered execution servers; online LLMs process text only; no request is sent to any cloud generation service.
- APIs for ordinary users never return server addresses, storage paths, raw workflows, other users' tasks or keys.

### 6.2 Internationalization

- Interface: every workspace string lives in `zh-CN` / `en` translation tables, and switching language does not reload the page; tests check that no Chinese remains in English mode. Implemented: the Chinese source text is the translation key, the English table is `Studio/lang/en.js`, and the test is `Tests/studio-i18n.test.cjs`. In English mode, server errors show a generic English message per error code; localizing the server messages themselves is not done yet.
- Content: names and descriptions of templates, styles and platform presets are stored with both Chinese and English fields.
- Prompts: users may describe in either language; the prompt service always outputs English prompts.
- Documentation: EcomCraft documents are written in Chinese and English together.

### 6.3 Platform presets

An editable configuration file lists Chinese and international platforms. Each platform records its Chinese and English names, image sizes, padding colour, compliance rules (for example, Amazon main images use no generative model and require a pure white background) and whether AI metadata is written. Exact values are checked against each platform's official rules during development and confirmed by the administrator.

Today each platform is organized by "slots" (F58, implemented pending acceptance): each platform consists of several image slots (white main image, scene main image, 3:4 wireless image, video cover and so on), each with its own size and ratio, minimum subject fill, per-file size cap, forbidden-content and AI labelling rules, and image sets combine templates by slot. See [EcomCraft Platform Preset Slots](EcomCraft%20Platform%20Preset%20Slots.md); values await the N06 check.

### 6.4 Storage

| Data | Location |
|---|---|
| System data: account database, review snapshots, template and platform preset configuration, work index, thumbnails | `Data/` on the platform server; cannot be changed |
| Built-in templates and default platform presets | Extension directory, shipped with each release |
| Works: original product images, generated results | Work library location: local (default), NAS or shared folder |
| Export packages | Browser download, or the export location (shared folder) |

### 6.5 Code organization

Follow the "smallest change" principle: do not refactor existing files all at once; put new features in new files.

```
src/Extensions/EcomCraft/
├─ EcomCraft.cs                  Registration entry point (existing)
├─ MagicPrompt/                  Online LLMs (existing)
└─ Studio/
   ├─ StudioApi.cs, WorkflowReview*.cs, WorkflowDiscovery.cs, etc. (existing)
   ├─ Templates/                 Templates, image sets, platform presets
   ├─ Generation/                Orchestration, scheduling, executors, per-user limits
   ├─ Prompting/                 Prompt service (wraps MagicPrompt)
   ├─ PostProcess/               Product-preserving compositing
   ├─ Products/                  Product library
   ├─ Export/                    Sizes, packaging, metadata, compliance checks
   ├─ Storage/                   Work library location, sync, migration, cleanup
   ├─ index.html, studio.js, studio.css (existing)
   ├─ New front-end scripts such as i18n.js and simple.js
   └─ languages/zh-CN.json, en.json
```

## 7. MVP Scope and Acceptance

The new MVP acceptance criteria (2026-10-31, Implementation Plan 1.4): on minimum-spec Windows and Mac computers:

1. An operator unfamiliar with design goes from install to first image in under 30 minutes without a tutorial (excluding model downloads).
2. The target categories reach the "listing-ready" pass rate (see the G1 gate).
3. Main images and detail pages can be exported.

The old MVP's code (single-server form) is complete (including the usability and operations items below) and accepted per A10; multi-machine testing is deferred with F28, seed-team trials (N07) are pending, and the four expert-made workflows templates depend on (N02) remain conventional-name placeholders.

### MVP-A: Platform foundation

| ID | Feature | Description |
|---|---|---|
| F29 | Calling and automatic scheduling | Rule 1 dispatch; the executor abstraction produces only local and LAN executors (requirements step 4) |
| F30 | Queue, failure reassignment and gallery | Requirements step 5 |
| F31 | Multi-user login and LAN deployment | Accounts, queue limits, firewall, shared storage and LAN HTTPS guidance (requirements step 6) |
| F32 | Work storage location | Local / NAS / shared folder, write locally then sync (requirements step 7, R11) |
| F24 | Chinese and English interface | Translation table framework; existing strings migrated; new features built bilingual from the start |

### MVP-B: E-commerce images

| ID | Feature | Description |
|---|---|---|
| F01 | E-commerce scene templates | Built on published snapshots; 4 built in: white-background main image, scene, detail, model or hand-held |
| F37 | Prompt service | One-sentence description + style → MagicPrompt enhancement; image description on by default (references only), product photo description off by default |
| F07 | Three-step simple mode | The default interface; no prompt box, styles are options with sample images |
| F02 | Product preservation | Composite the original product pixels back into the generated image, using the local cut-out as the mask; the standard workflows also harmonize once, taking details from the original and light from the generated picture, with a contact shadow (workflows need not output a mask) |
| F35 | Background-removal confirmation and product positioning | Confirm the cut-out before generating; drag or scale the product |
| F03 | One-click image sets and batches | Full image set per platform; several GPUs in parallel; per-user limits |
| F36 | Product library (basic) | Upload once; cache the original and cut-out for reuse |
| F05 | Platform export | Chinese and international platform presets; download zip or export to a shared folder |
| F22 | Compliance: Amazon main image and AI metadata | Amazon white-background main images use the pure cut-out branch; exports carry AI-generated metadata |
| F08 | Model checklist and commercial licences | Models each server is missing and their licence status; downloads are done manually by the administrator |
| F20 | Template test bench | Batch-generate on a fixed test product set and measure the pass rate |

### MVP-C: Usability and operations

| ID | Feature | Description |
|---|---|---|
| F38 | Upload quality check | Warn early about low resolution, blur or a product that is too small |
| F39 | Automatic cleanup of unselected candidates | Unselected candidates are removed after N days; selected images are kept |
| F40 | First-run setup wizard | Usability and operations (implemented, pending acceptance, T44; revised 2026-10-31: T94 reworks it into "language → hardware check → recommended recipe → download → sample self-check → pick platforms", with a LAN exit for unsupported hardware) |
| F41 | Plain-language errors | Explain the cause and next step in words operators understand |
| F42 | Phone upload and mobile gallery | Take product photos on a phone and upload them; browse the gallery on a phone (without browser features that need HTTPS) |
| F43 | Completion notifications | In-page notice when a batch finishes |
| F44 | One-click diagnostic package | Bundle logs, server status and configuration, without keys, prompts or images |
| F45 | Backup and upgrade | One-click backup of system data; upgrades keep data |
| F46 | Disk space monitoring | Warn the administrator when local or shared storage runs low |
| F10 | Chinese and English README and demo | Before/after images, demo video, deployment service information |

### Non-development items the MVP depends on

- **N01** Choose base models that allow commercial use (commercial licensing is the first filter).
- **N02** Commission 4 template workflows; they must output a product mask.
- **N03** Collect test products, focusing first on the main product category.
- **N07** After release, find 3–5 seed teams to try it.
- **N08** Prepare a test environment: at least two GPU computers and one shared storage location.

## 8. Features for Later Development

| ID | Feature | Category |
|---|---|---|
| F28 | LAN multi-server real-world testing | Platform foundation (deferred: A10 single-server first, until a customer asks or N08 is ready; steps 1–7 are implemented and verified on one machine) |
| F23 | One-click install on Apple silicon Macs | Enhancement (priority raised 2026-10-31: T95 in phase 3 "out of the box", depends on T84 and T93) |
| F25 | Selling-point analysis and image-set storyboards | Enhancement (implemented, pending acceptance, T30) |
| F19 | Category presets | Enhancement |
| F12 | Brand kits | Enhancement (implemented, pending acceptance, T17) |
| F04 | Project archive and history | Enhancement (implemented, pending acceptance: server T09, interface T10) |
| F47 | Selling-point images (fixed text layouts; text is not generated by AI) | Enhancement (implemented, pending acceptance, T50) |
| F48 | Product showcase videos | Enhancement |
| F49 | Visible AI labels | Enhancement |
| F21 | Automatic candidate filtering | Enhancement |
| F06 | Before/after comparison export | Enhancement |
| F26 | Layered PSD export | Enhancement |
| F09 | Template import and export | Enhancement |
| F27 | ComfyUI App Mode compatibility | Enhancement (deferred) |
| F11 | Finer roles and permissions | Not planned (A12: accounts are only administrators or regular accounts) |
| F13 | Work approval flow | Team and commercial |
| F14 | Usage statistics | Team and commercial |
| F50 | User groups and work sharing | Team and commercial |
| F51 | Server operations (maintenance mode, workflow distribution, remote cleanup) | Team and commercial |
| F52 | Four upload sources (local, camera, product library, gallery pick) | Simple mode redesign v3 (implemented, pending acceptance) |
| F53 | Two-level product type, optional visual style and model checkbox | Simple mode redesign v3 (implemented, pending acceptance) |
| F54 | Bilingual prompts and AI expansion | Simple mode redesign v3 (implemented, pending acceptance) |
| F55 | Manual candidate count with a 20-image cap | Simple mode redesign v3 (implemented, pending acceptance) |
| F56 | Faster generation path (queue on submit) | Simple mode redesign v3 (implemented, pending acceptance) |
| F57 | Automatic standard workflow creation | Simple mode redesign v3 (T59 code and blueprint complete, pending build and acceptance; text-to-image first; since 2026-10-30 the scene and detail workflows generate only around the placed product (inpainting with the product canvas's alpha as the mask), full image-to-image still waits for N10) |
| F58 | Platform presets by slot (per-slot size and ratio, subject fill, size caps, forbidden content, AI labelling) | Enhancement (implemented, pending acceptance, T60; T97 extends the slot data with first_main, selling, detail_page and the second platform batch, depends on N06; design in [EcomCraft Platform Preset Slots](EcomCraft%20Platform%20Preset%20Slots.md), values pending the N06 check) |
| F59 | Reference images (one image per reference: borrow the composition and scene reference) | Enhancement (P1 implemented, pending acceptance, T61; route upgraded 2026-10-31: P2 scene reference (T62) is superseded by T84's fusion route (F74), the old path stays as the `reference_mode: composition` fallback; requirements in [EcomCraft Reference Images](EcomCraft%20Reference%20Images.md)) |
| F60 | Professional mode open to every account (A2), with ComfyUI workflow upload (usable by the uploader at once, by others after review) | UI redesign v4 (T63 opening and own uploads; T73 review and publishing) |
| F61 | Task-first simple mode (follow a reference / make one image / make a full set) | UI redesign v4 (T64 implemented; revised 2026-10-31 (A14): becomes the single entry "follow a reference", T89 and T90, sets driven by references) |
| F62 | Navigation, "Manage" menu and consistent wording | UI redesign v4 (T65) |
| F63 | Phones get simple mode only | UI redesign v4 (T66) |
| F64 | One-way hand-off from simple to professional mode | UI redesign v4 (T67) |
| F65 | Star candidates (first filter) | UI redesign v4 (T68) |
| F66 | Gallery and queue cleanup | UI redesign v4 (T69) |
| F67 | Star preference analysis and prompt feedback | UI redesign v4 (T70, deferred, design pending) |
| F68 | Image description on by default, with setup guidance | UI redesign v4 (T71) |
| F69 | One-click vision model install script | UI redesign v4 (T72, `launchtools/install-vision-model-*`) |
| F70 | Estimated time remaining (writing prompts, storyboards, image description, prompt enhancement and generation) | Enhancement (T74) |
| F71 | Adaptive device for the vision model: image description on local Ollama picks the GPU or CPU for the customer's graphics card and moves to the CPU when the card cannot run it; three phases: P1 automatic fallback and manual choice, P2 a measured self-test whose result is saved, P3 model recommendation from the hardware and sharing the card with generation | Enhancement (T75 P1, T76 P2, T77 P3) |
| F72 | Clean up leftover local ComfyUI at startup: after the platform is force-killed, crashes or loses power, self-started ComfyUI instances no longer pile up (requirements in the LAN platform requirements, "Reliability") | Enhancement (T78) |
| F73 | Output size chosen before generating: by platform slot by default, common or custom sizes instead; generated at the aspect ratio and scaled to the exact size, no size choice after generation (requirements in section 5.4 of the EcomCraft Simple Mode Redesign) | Enhancement (T80) |
| F74 | Reference fusion route (references v2): the reference and product photo go together into an editing model, then the product is located, its original details pasted back and distortion verified; reference analysis yields the subject position and slot automatically; aspect fitting and abnormal-reference handling | Phase 2: references v2 (T83, T84, T85, T87, T88; route and model await the G1 trial) |
| F75 | Product locating, paste-back and distortion checks: frequency-separated paste-back when contours align, keep the model's result when they do not; one retry on excessive difference, then flag "product may be distorted" | Phase 2: references v2 (T86) |
| F76 | Hardware detection and one-click model setup: GPU and memory tiers (recommended / minimum / conditional / unsupported); scan installed models against recipes (including user-placed ones), one-click download and auto-configuration (China-first mirrors, resume, checksums) | Phase 3: out of the box (T91, T92, T93) |
| F77 | Windows and Mac installers: no .NET, Python or Git for users; installer plus desktop shortcut; signing pending the user's decision | Phase 3: out of the box (T96) |
| F78 | Full sets by slot: several references assigned to platform slots (first image, selling point, scene, detail, white background, detail screens); duplicate slots allowed, unfilled slots shown; set-wide style consistency option | Phase 4: sets and details (T98, T101) |
| F79 | Selling-point info and wording checks: product name, selling points, specs and sizes filled when making a set and saved per product; the model only suggests and the user confirms, nothing invented; advertising-law superlative notices | Phase 4: sets and details (T99) |
| F80 | Selling-point images and detail-page layouts: several layouts, text always drawn by the server; layout picked from the reference analysis and product position to avoid the product; detail pages as per-screen slices and one long image | Phase 4: sets and details (T100) |
| F81 | Built-in reference library: sample images by category and slot (rights-cleared only); a base pack ships with releases, industry packs belong to the team edition | Phase 4: sets and details (T102) |
| F82 | Professional-mode refinement bench: version history, local edits with product lock, object removal and background swap, instruction editing, aspect change and upscale into platform sizes, P1 text layer, recolor, lighting, batch apply | Phase 5: professional mode (T103–T108) |
| F16 | Per-account licensing (limits the number of accounts only, no feature tiers) | Team and commercial (A12, A18; T109 splits the open-source core from the closed-source team extension) |
| F17 | Brand-specific model training | Reserved (custom service, deferred) |

## 9. Explicitly Out of Scope

- A public SaaS for small sellers, credits and online payment.
- SaaS-style restrictions such as watermarked previews.
- A complex role and permission system, or feature-tiered pricing (A12).
- Building separate workflows for each customer (differences go into category presets and brand kits; anything beyond that is paid customization).
- GPU-less options and cloud generation (2026-10-31, A6 and A15: a GPU is required; insufficient hardware gets a clear "not supported" message; no GPU-less or cloud-generation options. Cloud options for the vision and prompt models are unaffected).
- Taking image orders ourselves to validate demand.

## 10. Development Order

From 2026-10-31 the phases of the [EcomCraft Implementation Plan](EcomCraft%20Implementation%20Plan.md) apply, interleaved with the [EcomCraft Refactoring Plan](EcomCraft%20Refactoring%20Plan.md) (R00–R24; see its section 7 for the order):

1. **Phase 0, planning on paper (T81)**: the goals go into the repository documents and the tracker.
2. **Phase 1, route trial (N11, N09/N03, T82, N12) → G1 decision gate**: choose the reference route, model and quantization; phase 2 may not start unless G1 passes. T82 and N12 run in parallel with refactoring phases A–B; neither touches platform code.
3. **Phase 2, references v2 (T83–T90) → G2 quality gate**: reference analysis, fusion workflow, generation-flow integration, product locating/paste-back/verification, aspect fitting, abnormal handling, single entry.
4. **Phase 3, out of the box (T91–T96)**: hardware tiers, model recipes and one-click download, wizard and self-check, Mac adaptation, installers.
5. **Phase 4, sets and details (T97–T102)**: platform slot data, sets by slot, selling points, layouts, style consistency, built-in reference library.
6. **Phase 5, professional mode (T103–T108)**: the refinement bench.
7. **Phase 6, release (T109–T110)**: the open-source / team-edition split and release documentation.

Phases 3, 4 and 5 do not block each other after G2, but tasks touching the same files must not run at once. For each task's files, dependencies, acceptance criteria and Claude Code prompt, see the "开发任务" (development tasks) sheet in the tracker.

## 11. Change Log

| Date | Version | Description |
|---|---|---|
| 2026-10-27 | v1.0 | First version: positioning, deployment topology, layered architecture, data flow, design conventions, MVP scope and later features |
| 2026-10-28 | v1.2 | Product preservation now uses the local cut-out as the mask: the platform builds the input canvas at the same placement and pastes the original product back after generation, so workflows need not output a product mask |
| 2026-10-28 | v1.1 | The Chinese / English workspace interface (F24 / T22) is implemented; noted how it works and that server messages are not localized yet |
| 2026-10-28 | v1.3 | All MVP code complete (T06, T16, T41–T49): status table refreshed (generation orchestration, scheduling, storage etc. now implemented); operations APIs (diagnostics, backup/restore) and usability features (upload quality, candidate cleanup, setup wizard, mobile upload, completion notices, disk monitoring) added; multi-machine testing and seed-team trials pending |
| 2026-10-28 | v1.4 | Code complete for simple mode redesign v3 F52, F54 and F56 and for the enhancements F25 (storyboards), F12 (brand kits) and F47 (selling-point images); F04 project archive server side complete (T09, AGENTS.md gained the project archive rule); status table adds brand kits and the project archive; F57 deferred (waiting for N01); all pending build and manual acceptance |
| 2026-10-28 | v1.4 | Simple mode redesign v3 added (F52–F57 / T54–T59, confirmed, development pending): four upload sources, two-level product type, bilingual prompts with AI expansion, a 20-image candidate cap, a faster generation path, and automatic standard workflow creation; requirements in "EcomCraft Simple Mode Redesign" |
| 2026-10-28 | v1.5 | First two redesign tasks done and deployed: F53 (T55, 467c036d) and F55 (T57, 0e7c6b3e); handover notes and remaining tasks in "EcomCraft Simple Mode Redesign" section 11 |
| 2026-10-28 | v1.6 | Decision A10: single server first; multi-server support stays in the architecture and code, multi-machine testing (F28 / T33) and N08 deferred until a customer asks; F28 moved from MVP-A to later features; F58 platform presets by slot added (design draft, including the N06 checklist) |
| 2026-10-28 | v1.7 | F58 platform presets by slot implemented pending acceptance (T60): platforms.json upgraded to v3 slots, image sets composed by slot kind, exports follow slot specs with automatic size-cap compression, pure-white and subject-fill hints, forbidden-content and AI-declaration notices; values still await the N06 check |
| 2026-10-28 | v1.8 | Added the F59 reference-image requirements (decision A11): two paths (borrow the composition / scene reference via image-to-image), each reference generating its own image (cap 8, mutually exclusive with storyboards, temporary and never stored), copyright boundary (restore the feel, stronger notices on international platforms); T61 pending, T62 depends on N10; reserved the P3 direction of whole-set references to a whole set |
| 2026-10-28 | v1.9 | F59 P1 standalone reference mode implemented pending acceptance (T61): reference upload (cap 8), per-reference editable prompts (EcomCraftStudioReferenceDescribe), the other step-2 options disabled, one image per reference per product (fixed scene template, mutually exclusive with storyboards), reference thumbnails on task cards, reference mode unavailable with vision off; AGENTS.md gained the reference-image rule; P2 still waits for N10 |
| 2026-10-29 | v2.0 | Graphic designers added to the target users (simple mode for operators and owners, professional mode for designers); A2 now lets any account switch to professional mode; new A12 revenue model: sold as software, priced per account, no complex roles or permissions (F11 now not planned, F16 now per-account licensing); new F60 / T63 opens professional mode (to do) |
| 2026-10-29 | v2.1 | New decision A13, UI and workflow redesign v4 (F60–F67 / T63–T70), requirements in the EcomCraft UI and Workflow Redesign; F60 adds ComfyUI workflow upload in professional mode (published after review) |
| 2026-10-29 | v2.2 | A5 revised: image description on by default (references only; product photo description still off by default); A13 revised: uploaded ComfyUI workflows are usable by the uploader at once; new F68/T71 setup guidance and F69/T72 one-click install script; AGENTS.md gained the own-workflow rule |
| 2026-10-29 | v2.3 | UI redesign v4 code complete pending acceptance: F60 (T63, T73), F61–F66, F68, F69 (T64–T69, T71, T72); F67 (T70) deferred |
| 2026-10-29 | v2.5 | New F70 estimated time remaining (T74): the server keeps the actual durations of the last 20 runs of each kind of operation in memory (seconds only, no content, cleared on restart) and estimates by the median; generation keeps the per-image time by "workflow + size + execution server", and a queued task shows "starts in about …" from the tasks ahead of it; F57 blueprint provided |
| 2026-10-29 | v2.6 | New F71 adaptive device for the vision model (T75–T77): customer machines may have NVIDIA, AMD or Intel graphics or be a Mac, and some cards (such as older AMD cards running through Vulkan) hang on the vision model with no output and leave Ollama reporting a model as loaded when it is not; P1 turns off thinking, unloads the model after a timeout and moves to the CPU when a first, time-limited GPU attempt fails, and administrators can choose "Automatic / GPU only / CPU only"; P2 and P3 are pending |
| 2026-10-30 | v2.7 | New F72 cleanup of leftover local ComfyUI at startup (T78): force-killing the platform does not end the ComfyUI it started, and repeated restarts once left 17 instances on one server; the platform now records the process ID and start time of the ComfyUI it starts and ends leftovers on the next start |
| 2026-10-30 | v2.7 | F59 adds placing the product per reference (T79): in reference mode each reference has its own product position and size, adjusted against the reference shown behind the preview, with no automatic inference; requirements in EcomCraft Reference Images, section 4 item 9 |
| 2026-10-30 | v2.8 | New F73 output size chosen before generating (T80): the size is picked in step 2, the model generates at its aspect ratio near one megapixel and the platform scales locally to the exact size; zip downloads keep the generated size, the platform only decides format and size cap |
| 2026-10-30 | v2.9 | Better compositing in the standard workflows: after the product is put on the scene the whole picture is sampled again at low strength, the paste becomes a frequency-separated blend (details from the original, light from the generated picture, with a limit on tone changes) with a contact shadow; the canvas's transparent part is neutral gray and the repainted area reaches slightly into the product; the default placement is a little below the middle at size 0.55; the default seed is random |
| 2026-10-29 | v2.4 | F04 project archive interface complete pending acceptance (T10): gallery project picker and project manager, adding or removing works in bulk, "Make again with same settings" filling in the template, platform, product, prompt and seed; template generation accepts `seed`, which only sets the snapshot's seed parameter |
| 2026-10-31 | v3.0 | Goal update (see the EcomCraft Implementation Plan and Refactoring Plan, added to the repository with T81): section 1 becomes the reference-driven goal with the hardware threshold; new decisions A14 (single entry), A15 (GPU required), A16 (Windows and Mac first), A17 (highly-similar boundary), A18 (open-source core + team edition); revised A6 (no cloud generation), A11 (reference route upgraded to the fusion route, pending G1), A12 (team-edition feature split allowed), A13 (three task entries void); section 7 becomes the new MVP acceptance; section 8 adds F74–F82, removes F18 and pulls F23 forward; section 9 adds GPU-less options and cloud generation; section 10 becomes the new phases; both plan documents are linked |
