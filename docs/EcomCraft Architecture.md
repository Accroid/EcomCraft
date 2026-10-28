# EcomCraft Architecture and Roadmap

> **Language:** English · [简体中文](EcomCraft%20Architecture.zh-CN.md)
>
> **Status:** MVP code complete, deployed and accepted in single-server form (A10), pending manual acceptance and seed-team trials (N07); this document describes the target architecture and marks implemented parts · **Version:** v2.2 · **Date:** 2026-10-29
>
> Related files:
> - [EcomCraft LAN Multi-Server Platform Requirements](EcomCraft%20LAN%20Multi-Server%20Platform%20Requirements.md): detailed requirements for the platform foundation (execution servers, review, scheduling, storage); it takes precedence.
> - [EcomCraft development tracker](EcomCraft%20开发总表.xlsx) (Chinese spreadsheet): features (F IDs), development tasks (T IDs), non-development items (N IDs) and progress tracking. Feature IDs in this document match the tracker.
> - [EcomCraft UI and Workflow Redesign](EcomCraft%20UI%20Redesign.md): requirements for task-first simple mode, navigation and administration, opening professional mode, and stars with personal analysis (F60–F67); it is authoritative for them.
>
> When the architecture or roadmap changes, update this document (Chinese and English together) and the requirements document first, then the tracker, and only then the code.

## 1. Product Positioning

EcomCraft is a **LAN product-image production platform for e-commerce operators, graphic designers and small shop owners**:

- **Users**:
  - Operators and shop owners: create images quickly in simple mode. They only upload product images, choose a platform and a style, then pick and download images; they never deal with prompts, models or parameters.
  - Graphic designers: adjust models, samplers, LoRAs, steps and other parameters in professional mode to create more and richer results.
  - Administrators (the shop's technical staff, or our deployment service): handle installation and configuration.
- **Sales model**: sold as software, priced per account (A12).
- **Deployment**: one or more GPU computers run ComfyUI, and one of them also runs the EcomCraft platform. Team members open the same page in a browser on the LAN, and the platform automatically sends tasks to idle GPU computers.
- **Markets**: both Chinese platforms (Taobao/Tmall, JD, Pinduoduo, Douyin and others) and international platforms (Amazon, Shopify, TikTok Shop and others); the interface and documentation are available in Chinese and English.
- **Differentiators**: fully local generation (product images never leave the company), product preservation, team collaboration, and one-click export that follows each platform's rules.

## 2. Confirmed Decisions

| ID | Decision |
|---|---|
| A1 | Deployment is "one GPU computer + browser access", with support for several GPU computers on the LAN; users see a single front-end page and tasks are dispatched automatically. |
| A2 | Target users are operators, graphic designers and shop owners: the default interface is simple mode with only the essential steps; any account can switch to professional mode, with no role restriction (administrators still handle installation, configuration and review). |
| A3 | Both Chinese and international platforms are supported; the interface, documentation and platform presets are provided in Chinese and English. |
| A4 | Users do not write prompts: a one-sentence description (Chinese or English) plus style options is turned into a prompt by the integrated online LLM (MagicPrompt). |
| A5 | Image description (sending reference images to a vision model to read their composition) is on by default; the default vision model is local Ollama, so images never leave the machine. Administrators can turn it off or switch to a cloud vision service (images then leave the machine, which administrators must confirm). Sending product photos to the vision model to describe products stays off by default and administrators enable it. (Revised 2026-10-29: image description was off by default.) |
| A6 | Cloud generation capability is reserved (executor abstraction, three-stage generation flow), but **the MVP develops no cloud generation features**; see D11 and R10 in the requirements. |
| A7 | Works are stored on the platform server by default; the administrator can switch to a NAS or a shared folder on another computer; system data always stays local. See D12 and R11 in the requirements. |
| A8 | Within the platform, works are visible only to their owner; the storage layer is not isolated. See D9 in the requirements. |
| A9 | We will not take image orders ourselves to validate demand; instead, 3–5 seed teams will try the MVP after release. |
| A10 | Single server first: the MVP is deployed and accepted as "one platform server that is also the execution server". The architecture and code for multi-server access, scheduling and storage remain in place; multi-machine testing (F28 / T33, N08) is deferred until a customer asks for it or the test environment is ready. |
| A11 | Both reference-image paths are open (F59): "borrow the composition" (prompt reference) and "scene reference" (image-to-image) are both offered, the latter needing the administrator to install an image-to-image model (N10); the goal is restoring the feel of layout and light, never a 1:1 copy. Reference mode is standalone: once references are uploaded the other step-2 options stop applying, per-reference prompts are shown and editable, and each reference makes exactly one image (cap 8, mutually exclusive with storyboards); references are temporary and never stored. |
| A12 | Revenue model: sold as software, priced per account. No complex roles or permissions: accounts are either administrators or regular accounts, and licensing limits only the number of accounts, never tiers features. |
| A13 | UI and workflow redesign v4 (F60–F67): simple mode starts by choosing a task (follow a reference / make one image / make a full set, defaulting to follow a reference) with secondary options under "More options"; administration moves into an administrators' "Admin" menu and the native engine is for administrators only; phones get simple mode only; designers may upload ComfyUI workflows, usable by the uploader at once and by other accounts after review and publishing; image description is on by default with setup guidance when no vision model is available; candidates can be starred, personal analysis is deferred. See the EcomCraft UI and Workflow Redesign. |

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
   Reserved (not developed): cloud ComfyUI and cloud image generation APIs, added as new "executor" types
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
| | Scheduling | Rule 1: choose the runnable executor with the shortest queue; the executor abstraction reserves cloud types | Implemented (requirements step 4, `Studio/Generation/ExecutionScheduler`) |
| | Post-processing | Product-preserving compositing (paste the original product back, using the local cut-out as the mask); candidate scoring later | Product preservation implemented (`Studio/PostProcess/`) |
| | Product library | Caches original product images and background-removal results for reuse | Simple version implemented (`Studio/Products/`) |
| | Brand kits | Brand colors, preferred background, logo and style references, per account | Implemented, pending acceptance (F12, `Studio/Brand/`); the prompt fragment applies to every template, images only fill a template's `brand_kit` slot |
| | Project archive | Files works by customer or product batch with a recipe (template, seed, prompt, execution server) read from the work's metadata | Server side implemented, pending acceptance (F04/T09, `Studio/Projects/`); workspace interface T10 not started |
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
8. Post-processing: the original product pixels are pasted back at the same placement, using the local cut-out as the mask → saved locally
   first, then synced to the work library → gallery
9. The operator picks images → export: platform sizes, pure-white check, AI metadata → download
   zip or export to the shared folder
```

When cloud services are added later, only the executor in steps 6–7 changes; background removal, product-preserving compositing and export still run locally.

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

## 7. MVP Scope

MVP acceptance: **an operator without design experience can, on their own, use a browser on the LAN to go from "upload product image → full image set → export for a platform", and the results can be listed for sale directly.**

Current status: the code for every MVP feature is complete (including the usability and operations items below), deployed and accepted in single-server form (A10); multi-machine testing is deferred with F28 and seed-team trials (N07) are still pending; the four expert-made workflows templates depend on (N02) are still conventional-name placeholders.

### MVP-A: Platform foundation

| ID | Feature | Description |
|---|---|---|
| F29 | Calling and automatic scheduling | Rule 1 dispatch; the executor abstraction reserves cloud types (requirements step 4) |
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
| F02 | Product preservation | Composite the original product pixels back into the generated image, using the local cut-out as the mask (workflows need not output a mask) |
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
| F40 | First-run setup wizard | Language → storage location → register servers → check models → choose usual platforms |
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
| F25 | Selling-point analysis and image-set storyboards | Enhancement (implemented, pending acceptance, T30) |
| F19 | Category presets | Enhancement |
| F12 | Brand kits | Enhancement (implemented, pending acceptance, T17) |
| F04 | Project archive and history | Enhancement (server side implemented, pending acceptance, T09; interface T10 not started) |
| F47 | Selling-point images (fixed text layouts; text is not generated by AI) | Enhancement (implemented, pending acceptance, T50) |
| F48 | Product showcase videos | Enhancement |
| F49 | Visible AI labels | Enhancement |
| F21 | Automatic candidate filtering | Enhancement |
| F06 | Before/after comparison export | Enhancement |
| F26 | Layered PSD export | Enhancement |
| F09 | Template import and export | Enhancement |
| F27 | ComfyUI App Mode compatibility | Enhancement |
| F23 | One-click install on Apple silicon Macs | Enhancement |
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
| F57 | Automatic standard workflow creation | Simple mode redesign v3 (default model chosen in N01, plan pending; text-to-image first, image-to-image waits for N10) |
| F58 | Platform presets by slot (per-slot size and ratio, subject fill, size caps, forbidden content, AI labelling) | Enhancement (implemented, pending acceptance, T60; design in [EcomCraft Platform Preset Slots](EcomCraft%20Platform%20Preset%20Slots.md), values pending the N06 check) |
| F59 | Reference images (one image per reference: borrow the composition and scene reference) | Enhancement (P1 implemented, pending acceptance, T61; P2 scene reference depends on N10, T62; requirements in [EcomCraft Reference Images](EcomCraft%20Reference%20Images.md)) |
| F60 | Professional mode open to every account (A2), with ComfyUI workflow upload (usable by the uploader at once, by others after review) | UI redesign v4 (T63 opening and own uploads; T73 review and publishing, to do) |
| F61 | Task-first simple mode (follow a reference / make one image / make a full set) | UI redesign v4 (T64) |
| F62 | Navigation, "Admin" menu and consistent wording | UI redesign v4 (T65, to do) |
| F63 | Phones get simple mode only | UI redesign v4 (T66, to do) |
| F64 | One-way hand-off from simple to professional mode | UI redesign v4 (T67) |
| F65 | Star candidates (first filter) | UI redesign v4 (T68, to do) |
| F66 | Gallery and queue cleanup | UI redesign v4 (T69, to do) |
| F67 | Star preference analysis and prompt feedback | UI redesign v4 (T70, deferred, design pending) |
| F68 | Image description on by default, with setup guidance | UI redesign v4 (T71) |
| F69 | One-click vision model install script | UI redesign v4 (T72, `launchtools/install-vision-model-*`) |
| F16 | Per-account licensing (limits the number of accounts only, no feature tiers) | Team and commercial (A12) |
| F18 | Cloud generation | Reserved (change the rules first) |
| F17 | Brand-specific model training | Reserved (custom service) |

## 9. Explicitly Out of Scope

- A public SaaS for small sellers, credits and online payment.
- SaaS-style restrictions such as watermarked previews.
- A complex role and permission system, or feature-tiered pricing (A12).
- Building separate workflows for each customer (differences go into category presets and brand kits; anything beyond that is paid customization).
- Cloud generation and taking image orders ourselves during the MVP.

## 10. Development Order

1. **Platform foundation**: F28 testing (deferred, A10) → F29 scheduling → F30 gallery and reassignment → F31 multi-user deployment → F32 storage; finish the F24 internationalization framework early so every new feature is built bilingual.
2. **E-commerce images**: F01 templates → F37 prompt service → F07 simple mode → F02 preservation (depends on N02) → F35 cut-out confirmation → F20 test bench → F03 image sets and batches → F36 product library → F05 export → F22 compliance → F08 model checklist.
3. **Usability and operations**: F38–F46, then finally F10 README and demo.

For each task's files, dependencies, acceptance criteria and Claude Code prompt, see the "开发任务" (development tasks) sheet in the tracker.

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
