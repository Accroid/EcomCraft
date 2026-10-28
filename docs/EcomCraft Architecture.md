# EcomCraft Architecture and Roadmap

> **Language:** English · [简体中文](EcomCraft%20Architecture.zh-CN.md)
>
> **Status:** Planning (this document describes the target architecture; most modules are not implemented yet, and implemented parts are marked) · **Version:** v1.1 · **Date:** 2026-10-28
>
> Related files:
> - [EcomCraft LAN Multi-Server Platform Requirements](EcomCraft%20LAN%20Multi-Server%20Platform%20Requirements.md): detailed requirements for the platform foundation (execution servers, review, scheduling, storage); it takes precedence.
> - [EcomCraft development tracker](EcomCraft%20开发总表.xlsx) (Chinese spreadsheet): features (F IDs), development tasks (T IDs), non-development items (N IDs) and progress tracking. Feature IDs in this document match the tracker.
>
> When the architecture or roadmap changes, update this document (Chinese and English together) and the requirements document first, then the tracker, and only then the code.

## 1. Product Positioning

EcomCraft is a **LAN product-image production platform for e-commerce operators and small shop owners**:

- **Users**: operators and shop owners without design experience. They only upload product images, choose a platform and a style, then pick and download images; they never deal with prompts, models or parameters. An administrator (the shop's technical staff, or our deployment service) handles installation and configuration.
- **Deployment**: one or more GPU computers run ComfyUI, and one of them also runs the EcomCraft platform. Team members open the same page in a browser on the LAN, and the platform automatically sends tasks to idle GPU computers.
- **Markets**: both Chinese platforms (Taobao/Tmall, JD, Pinduoduo, Douyin and others) and international platforms (Amazon, Shopify, TikTok Shop and others); the interface and documentation are available in Chinese and English.
- **Differentiators**: fully local generation (product images never leave the company), product preservation, team collaboration, and one-click export that follows each platform's rules.

## 2. Confirmed Decisions

| ID | Decision |
|---|---|
| A1 | Deployment is "one GPU computer + browser access", with support for several GPU computers on the LAN; users see a single front-end page and tasks are dispatched automatically. |
| A2 | Target users are operators and shop owners without design experience: the default interface keeps only the essential steps, and advanced parameters are visible only to administrators. |
| A3 | Both Chinese and international platforms are supported; the interface, documentation and platform presets are provided in Chinese and English. |
| A4 | Users do not write prompts: a one-sentence description (Chinese or English) plus style options is turned into a prompt by the integrated online LLM (MagicPrompt). |
| A5 | Online LLMs process text only. Sending product images to an online vision model (to describe products automatically) is off by default; the administrator can enable it or use a local vision model instead. |
| A6 | Cloud generation capability is reserved (executor abstraction, three-stage generation flow), but **the MVP develops no cloud generation features**; see D11 and R10 in the requirements. |
| A7 | Works are stored on the platform server by default; the administrator can switch to a NAS or a shared folder on another computer; system data always stays local. See D12 and R11 in the requirements. |
| A8 | Within the platform, works are visible only to their owner; the storage layer is not isolated. See D9 in the requirements. |
| A9 | We will not take image orders ourselves to validate demand; instead, 3–5 seed teams will try the MVP after release. |

## 3. Deployment Topology

```
  Browsers (operators / owners / administrators; Chinese or English UI; any computer or phone on the LAN)
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
| Front end | Simple mode | Three-step image creation for operators and owners: upload → choose platform and style → pick and download | Planned |
| | Admin views | Workflow review, template configuration, server status, model checklist, storage settings, setup wizard | Review view implemented |
| | Internationalization | Chinese / English translation tables; every UI string is looked up by key | Implemented for the workspace (`Studio/i18n.js`, `Studio/lang/en.js`) |
| API | Catalog / templates | Returns template names, sample images and adjustable fields (minimal disclosure) | Extends the existing `EcomCraftStudioCatalog` |
| | Generation / image sets | Accepts "template ID + product + platform + description" | Extends the existing `EcomCraftStudioGenerate` |
| | Queue / gallery / export / review / admin | — | Queue and review implemented |
| Domain services | Templates and image sets | Template = published review snapshot + e-commerce information (Chinese and English names, sample images, target platforms); image set = a group of templates | Planned, built on the existing `WorkflowReview` |
| | Prompt service | One-sentence description + style → MagicPrompt → English prompt; its output may only fill prompt text fields | Planned, wraps the existing MagicPrompt |
| | Generation orchestration | Expands image sets and batches into tasks; per-user limits; tracks the status of each image | Planned |
| | Scheduling | Rule 1: choose the runnable executor with the shortest queue; the executor abstraction reserves cloud types | Requirements step 4; `ExecutionServers` provides the base |
| | Post-processing | Product-preserving compositing (paste the original product back using the product mask); candidate scoring later | Planned |
| | Product library | Caches original product images and background-removal results for reuse | Planned |
| | Export | Chinese and international platform size presets, padding, zip or export to a shared folder, AI metadata, Amazon pure-white check | Planned |
| | Storage | Work library and export locations, write locally then sync, migration, cleanup | Planned, see requirement R11 |
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
7. ComfyUI outputs "generated image + product mask" → returned to the platform server
8. Post-processing: the original product pixels are pasted back using the mask → saved locally
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

### MVP-A: Platform foundation

| ID | Feature | Description |
|---|---|---|
| F28 | LAN multi-server real-world testing | Build and verify the completed steps 1–3 against real ComfyUI (requirements V1–V3) |
| F29 | Calling and automatic scheduling | Rule 1 dispatch; the executor abstraction reserves cloud types (requirements step 4) |
| F30 | Queue, failure reassignment and gallery | Requirements step 5 |
| F31 | Multi-user login and LAN deployment | Accounts, queue limits, firewall, shared storage and LAN HTTPS guidance (requirements step 6) |
| F32 | Work storage location | Local / NAS / shared folder, write locally then sync (requirements step 7, R11) |
| F24 | Chinese and English interface | Translation table framework; existing strings migrated; new features built bilingual from the start |

### MVP-B: E-commerce images

| ID | Feature | Description |
|---|---|---|
| F01 | E-commerce scene templates | Built on published snapshots; 4 built in: white-background main image, scene, detail, model or hand-held |
| F37 | Prompt service | One-sentence description + style → MagicPrompt enhancement; image description off by default |
| F07 | Three-step simple mode | The default interface; no prompt box, styles are options with sample images |
| F02 | Product preservation | Composite the original product pixels back into the generated image using the product mask |
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
| F25 | Selling-point analysis and image-set storyboards | Enhancement (first priority after the MVP) |
| F19 | Category presets | Enhancement |
| F12 | Brand kits | Enhancement |
| F04 | Project archive and history | Enhancement |
| F47 | Selling-point images (fixed text layouts; text is not generated by AI) | Enhancement |
| F48 | Product showcase videos | Enhancement |
| F49 | Visible AI labels | Enhancement |
| F21 | Automatic candidate filtering | Enhancement |
| F06 | Before/after comparison export | Enhancement |
| F26 | Layered PSD export | Enhancement |
| F09 | Template import and export | Enhancement |
| F27 | ComfyUI App Mode compatibility | Enhancement |
| F23 | One-click install on Apple silicon Macs | Enhancement |
| F11 | Finer roles and permissions | Team and commercial |
| F13 | Work approval flow | Team and commercial |
| F14 | Usage statistics | Team and commercial |
| F50 | User groups and work sharing | Team and commercial |
| F51 | Server operations (maintenance mode, workflow distribution, remote cleanup) | Team and commercial |
| F16 | Paid feature licensing | Team and commercial |
| F18 | Cloud generation | Reserved (change the rules first) |
| F17 | Brand-specific model training | Reserved (custom service) |

## 9. Explicitly Out of Scope

- A public SaaS for small sellers, credits and online payment.
- SaaS-style restrictions such as watermarked previews.
- Building separate workflows for each customer (differences go into category presets and brand kits; anything beyond that is paid customization).
- Cloud generation and taking image orders ourselves during the MVP.

## 10. Development Order

1. **Platform foundation**: F28 testing → F29 scheduling → F30 gallery and reassignment → F31 multi-user deployment → F32 storage; finish the F24 internationalization framework early so every new feature is built bilingual.
2. **E-commerce images**: F01 templates → F37 prompt service → F07 simple mode → F02 preservation (depends on N02) → F35 cut-out confirmation → F20 test bench → F03 image sets and batches → F36 product library → F05 export → F22 compliance → F08 model checklist.
3. **Usability and operations**: F38–F46, then finally F10 README and demo.

For each task's files, dependencies, acceptance criteria and Claude Code prompt, see the "开发任务" (development tasks) sheet in the tracker.

## 11. Change Log

| Date | Version | Description |
|---|---|---|
| 2026-10-27 | v1.0 | First version: positioning, deployment topology, layered architecture, data flow, design conventions, MVP scope and later features |
| 2026-10-28 | v1.1 | The Chinese / English workspace interface (F24 / T22) is implemented; noted how it works and that server messages are not localized yet |
