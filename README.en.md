# EcomCraft Studio

> **Language:** [简体中文](README.md) · English

EcomCraft Studio is an e-commerce creation workspace built on [SwarmUI](https://github.com/mcmonkeyprojects/SwarmUI). This repository includes the SwarmUI server source and the EcomCraft extension; **you do not need a separate upstream SwarmUI installation**. The project is under active development and has not completed production acceptance. If you hit a bug, please report it via Issues.

## Before and After

<!-- Before/after showcase: the uploaded product photo on the left, a generated result on the right. Images to be added later. -->

| Uploaded photo | Generated result |
|---|---|
| (image pending) | (image pending) |

## Demo

- Demo video: (to be added)
- The usual creation flow is described under “Quick Start” and in the [documentation overview](docs/README.md).

## Quick Start

### Prerequisites

- [Git](https://git-scm.com/downloads) and the [.NET SDK](https://dotnet.microsoft.com/download): .NET 10 SDK is recommended; the project targets .NET 8. Check the installations with `git --version` and `dotnet --list-sdks`.
- The first build needs access to NuGet. Installing ComfyUI and downloading models also require an internet connection.
- To install local ComfyUI during initial setup on **Linux**, install Python (3.10–3.12 recommended) and its `venv` module first. The **Windows** setup downloads a portable ComfyUI package that includes Python, so a separate Python installation is unnecessary. To reuse an existing ComfyUI installation, provide a working environment and startup script.
- Image and video generation require the chosen models, their ComfyUI nodes, and compatible compute hardware; video memory requirements depend on the model. The web server itself does not need a GPU or Node.js.

### Get the source and launch

```bash
git clone https://github.com/Accroid/EcomCraft.git
cd EcomCraft
```

Run the matching command from the repository root:

| System | Normal launch | Development launch (forced rebuild) |
|---|---|---|
| Windows (PowerShell) | `.\launch-windows.bat` | `.\launch-windows-dev.ps1` |
| Linux | `./launch-linux.sh` | `./launch-linux-dev.sh` |
| macOS | `./launch-macos.sh` | — |

The first launch builds the server and restores .NET dependencies, which may take several minutes. Development launch scripts regenerate build outputs; use the normal script for regular operation. macOS has a server launch script, but the repository's automatic ComfyUI installer only covers Windows and the Linux NVIDIA/AMD paths. Local generation on macOS requires a compatible ComfyUI environment that you configure and verify yourself.

### First-time setup

1. Open `http://127.0.0.1:7801` in a browser on the same machine. If you changed the server port or host, use your configured address.
2. On the initial setup page, choose who can access the server, install a **ComfyUI Self-Starting** backend, and download models, or skip backend and model installation and configure them later. The default “just yourself” mode treats local visitors as administrators; configure separate user accounts after installation if needed.
3. When the local ComfyUI backend is running, open `http://127.0.0.1:7801/EcomCraft`. Access to the workspace requires the relevant generation-page and model permissions.

If an existing ComfyUI installation was not detected, click **Scan ComfyUI** at the top of **Server → Backends** in the native engine. Choose the desired `main.py` if multiple installations are found, select an existing or new **ComfyUI Self-Starting** backend, then click Save on its card. Scanning only discovers paths; it does not change the backend automatically.

Workspace generation uses only registered ComfyUI execution servers: the local self-starting ComfyUI, plus LAN ComfyUI instances registered as **ComfyUI API By URL** backends under **Server → Backends** (start ComfyUI on the LAN machine with `--listen`; see the [workflow guide](docs/EcomCraft%20ComfyUI%20Workflows.md#lan-execution-servers), whose “Execution server scheduling” section explains how tasks are assigned across servers). When no execution server is available, it does not switch to unregistered addresses, remote SwarmUI instances, or cloud services. For prompt enhancement, see the [Prompt Enhancement Guide](docs/EcomCraft%20Prompt%20Enhancement.md). If NuGet connection errors occur during the build, check your network, proxy, and access to `https://api.nuget.org/v3/index.json`.

After opening the workspace, switch to the task queue to see, per execution server, running jobs, queue length (more than 2 is shown as busy), queue positions, and node progress. Users with generation permission can stop their own active tasks. Queue state is runtime information and is not retained after a server restart.

## Feature Status

Implemented:

- A three-step simple mode as the default view. The page starts by choosing what to make, "Follow a reference" (default), "Make one image" or "Make a full set", and step 2 shows only that task's options, with visual style, model, brand kit, description and prompt under "More options". Steps: upload a product photo (four equal entries: this computer, the camera, the product library, or a generated work from the gallery, compressed in the browser when over 5 MB; thumbnails show the source) → pick platform, image type (template), a two-level product type (category → subtype), an optional visual style and whether a model is needed, optionally with one line of description → choose candidates and download. "AI expand" next to the description turns that line (or, when empty, the product type) into a bilingual Chinese/English prompt in an optional prompt panel where both languages can be edited: edited Chinese is translated into English by the server on submit, edited English is sent as is, and with an empty panel the server builds the prompt from the product type, style and description. The engine always receives English, and the results area can unfold the prompt actually used. The candidate count is a manual input of positive integers 1–20, and single generations and one-click sets share a cap of 20 images in total. Professional parameters such as sampler, LoRA and steps appear only in the administrators' professional mode. Platform list API: `EcomCraftStudioPlatforms`; product types API: `EcomCraftStudioProductTypes`.
- An image and video creation workspace (professional mode, visible to administrators) that reuses SwarmUI generation. Available modes depend on installed models, nodes, and account permissions. The interface is available in Chinese and English: switch with the header button without reloading; the choice is remembered in the browser, and the first visit follows the browser language.
- Administrator-published ComfyUI workflows that users can select in the workspace. The server checks workflow IDs and published input allowlists; APIs include `EcomCraftStudioCatalog`, `EcomCraftStudioGenerate`, `EcomCraftStudioQueue`, and `EcomCraftStudioCancelTask`.
- Workflow review: auto-discovered native workflows must be reviewed before users can run them. Users can submit workflows they saved and see rejection reasons; reviewers configure user-visible parameters (display name, default, range, options), check which execution servers can run each workflow, then publish, reject, or unpublish. Approval freezes a snapshot; when the source file changes the workflow needs review again, and users keep the old snapshot until the new version is approved. Permissions: `ecomcraft_submit_workflows` and `ecomcraft_review_workflows`. APIs: `EcomCraftStudioReviewList`, `EcomCraftStudioReviewDetail`, `EcomCraftStudioReviewDecide`, `EcomCraftStudioWorkflowSubmissions`, and `EcomCraftStudioSubmitWorkflow`. Native workflow batches increment actual sampler seeds for each image; `-1` randomizes the starting seed for the batch.
- Automatic discovery of `workflows` and `blueprints` in local ComfyUI installations and of workflows saved in the ComfyUI of LAN execution servers, including embedded subgraphs. Discovery runs in the background and refreshes about every 30 seconds; entries are named “source server / workflow”. Individual file paths are not required. `Settings.EcomCraft.ComfyWorkflowSearchPaths` adds optional directories. Imported workflows must pass graph and prompt validation and execute on an execution server that has the required models and nodes. The catalog deduplicates native workflows by executable content and hides entries whose saved model/node dependencies are unavailable on every online execution server. The page reads saved defaults, forwards edits to the corresponding nodes, and checks dependencies again before submission. On Windows AMD systems, automatic backend arguments disable pinned memory for ROCm compatibility when reloading quantized text encoders. Backends in a Comfy Desktop installation also append its sibling `ComfyUI-Shared/models` library to their model search paths.
- Execution server scheduling: each submission uses “Rule 1” to pick, among online servers that have the required nodes and models, the one with the shortest queue (counting tasks others submitted directly to ComfyUI); a whole batch runs on that one server. Users can restrict a task to one server in the creation panel and get a hint when that server is congested while another is idle. When no server can run the task, the reason is shown and nothing is submitted. Results record the server that ran them; failed tasks are never retried automatically and can be reassigned to another server with one click. The gallery shows each work's workflow and execution server.
- E-commerce scene templates: a template references a published workflow snapshot and adds Chinese and English names, an example image, suitable platforms, image sets and prompt fragments; templates may declare a slot kind (`kind`, for example white main image, scene main image, detail or model), and one-click sets compose templates by the platform's image slots (F58); built-in and administrator templates are stored separately, and invalid templates are not offered to users, with the reasons reported. Management APIs: `EcomCraftTemplateAdminList`, `EcomCraftTemplateSave`, and `EcomCraftTemplateDelete`; users read available templates with `EcomCraftStudioTemplates` (names in their language) and send `template_id` to `EcomCraftStudioGenerate` to generate from a template, still checked against the snapshot allowlist.
- Model list and commercial licenses: templates declare the models and nodes they need and each model's commercial-use license (allowed / forbidden / unknown); the workflows page lists what each server lacks and highlights license problems.
- Compliance: Amazon white background main images take a cut-out-only branch that centers the cut-out on pure white without any generation model; exports check main image backgrounds for pure white and write IPTC digital source type metadata for generated or composite pictures.
- Platform export (slot-based, F58): each platform consists of image slots (white main image, scene main image, an optional 3:4 wireless image and so on) with their own size, format, per-file size cap, subject fill and forbidden-content rules; exporting picks the platform and slot, scales and pads to the slot's spec, re-encodes automatically when over the cap, checks white slots for pure white, hints at low subject fill, and notes platforms that ask you to declare AI generation. Works are downloaded as a zip or written to the shared export folder, one folder per product; presets cover Chinese and international platforms, are editable, and the built-in values are marked as needing verification (N06). APIs: `EcomCraftStudioExportOptions` and `EcomCraftStudioExport`.
- Selling-point images: the user's title (up to 20 characters) and up to 3 selling points (up to 16 characters each) are laid out on one candidate in a fixed layout (bottom banner, left text column, or title with badges). The server draws the text with the bundled Noto Sans SC fonts (SIL OFL 1.1, commercial use allowed), never with AI; the accent color defaults to the chosen brand kit's first color; the result is saved as a new work, the original stays unchanged and its AI metadata is kept. Emoji are not supported. API: `EcomCraftStudioSellingImage`.
- Product library (simple version): uploaded products are saved with their original photo, cut-out and confirmed placement and can be picked next time; isolated per account, with rename and delete. APIs: `EcomCraftStudioProducts`, `EcomCraftStudioProductSave`, `EcomCraftStudioProductUpdate`, and `EcomCraftStudioProductDelete`.
- Project archive (server side): each account can keep up to 200 projects (a customer or a batch of products) and file gallery works into them; when filing, the server reads a recipe from the work's own metadata (template, seed, the prompt subject actually used, execution server) for "make again with the same settings". Records are visible to their owner only and can be deleted at any time; no images, workflows or server addresses are stored, and the works stay in the gallery. APIs: `EcomCraftStudioProjects`, `EcomCraftStudioProjectSave`, `EcomCraftStudioProjectDelete`, `EcomCraftStudioProjectAddWorks`, `EcomCraftStudioProjectRemoveWorks`, and `EcomCraftStudioProjectWorks`; the workspace interface follows in a later version (T10).
- Brand kits: each account can keep up to 20 kits (name, up to 5 brand colors, a preferred background, a logo and up to 4 style reference images) that only it can see and use. When a kit is chosen for generation, its colors (turned into English color words) and preferred background (Chinese is translated once when saved) join the prompt; the logo and the first reference image only go to the image parameters a template declares in its `brand_kit` slot, and the logo is never drawn by AI. Clients only send the kit's ID. APIs: `EcomCraftStudioBrandKits`, `EcomCraftStudioBrandKitSave`, and `EcomCraftStudioBrandKitDelete`.
- One-click sets and batches: several product photos × the platform's set become independent tasks that run in parallel on several execution servers; when the platform defines image slots the set is composed by slot kind, with optional slots joining when ticked (F58); each person's running tasks are limited, failed items can be retried individually and remaining tasks can be cancelled.
- Reference images, the "Follow a reference" task (F59): upload up to 8 references (such as a competitor's finished images); each reference gets an English composition description from the vision model, shown per image with the English editable (edits win), and "generate by reference" makes exactly one image per reference per product with the product-preserving scene template, sending the prompt as shown. Image description is on by default (references only; product photo description stays off by default) and administrators can turn it off; when it is off, the vision service cannot be reached or no vision model is installed, the page says what is missing (administrators see the steps and can run the one-click install script `launchtools/install-vision-model-windows.ps1` or `launchtools/install-vision-model-linux.sh`; other accounts are asked to contact one), and plain generation is unaffected. The page carries a copyright notice, stronger on international platforms, and references are kept in memory only, never stored. API: `EcomCraftStudioReferenceDescribe` and `EcomCraftStudioVisionStatus`, with `reference_prompts` on `EcomCraftStudioGenerateSet`.
- Selling points and set storyboard: from the product description (or prompt panel), optional selling points, product type and style, the prompt service lists up to 5 selling points and plans up to 8 images (each = an image type available on the platform + a scene description + its purpose); users edit, add or remove shots and then generate the set from the storyboard. The server accepts only registered template IDs and cleaned text and checks every shot again on submit; when the model is unavailable the platform's set types are listed as an editable storyboard. Storyboards live only on the current page. API: `EcomCraftStudioStoryboard`; set APIs: `EcomCraftStudioGenerateSet`, `EcomCraftStudioBatchStatus`, `EcomCraftStudioBatchRetry`, and `EcomCraftStudioBatchCancel`.
- 4 built-in e-commerce scene templates: white background main image, lifestyle scene, detail close-up, and model or hand-held; they reference expert-made workflows by conventional name (such as `ecomcraft/white-main`) and show a missing-workflow notice until the workflow is installed and approved.
- Product preservation: with `preserve_product` on, the platform places the cut-out product on the input canvas locally and pastes the original product pixels back after generation, so logos, text and shapes stay unchanged. In the simple mode users can remove the background with one click (SwarmUI's background removal node on an execution server, API `EcomCraftStudioCutout`) and drag or resize the product.
- Prompt service: users pick a product type and an optional style, write one line (Chinese or English), and the configured prompt enhancement model writes an English prompt; checking "needs a model" adds a fixed model fragment. The model can only fill in prompt text; when the model is unavailable the template and style defaults are used with a notice. Sending product photos to a vision model is off by default and only administrators can turn it on. "AI expand" writes the prompt in both languages; an edited Chinese prompt is translated into English on submit, and when the translation fails the product-type and style defaults are used with a notice, without blocking generation. When the model has to write or translate the prompt, the submission returns at once and the prompt is written in the background (still within 45 seconds); tasks show "Writing the prompt → Queued / Running (with the execution server's name) → Done". An unchanged expansion or edited English skips the model and is queued directly; one-click sets likewise return first and compose the prompt in the background. APIs: `EcomCraftStudioStyles`, `EcomCraftStudioProductTypes`, `EcomCraftStudioComposePrompt`, `EcomCraftStudioExpandPrompt`, `EcomCraftStudioPrepareStatus`, and `EcomCraftPromptSettings`.
- Work storage: administrators can set the work library to a local folder, NAS or shared folder (saving checks writability, free space and speed). Works are saved on the platform server first and copied in the background with automatic retries; the gallery shows "Pending sync", and old works can be migrated after the location changes. APIs: `EcomCraftStorageSettings`, `EcomCraftStorageSave`, `EcomCraftStorageMigrate`, and `EcomCraftStoragePending`.
- A live task queue showing, per execution server, running jobs, pending jobs (including tasks the platform assigned but has not yet handed to ComfyUI), idle / working / congested state, current nodes, and node progress, with support for stopping the current user's active generation tasks.
- Generated results can be downloaded, reused as the initial image for a new creation, or opened in the gallery; the full-size preview supports previous / next buttons and arrow keys, plus zoom for both images and videos (mouse wheel, double click, pinch or buttons, `+` / `-` / `0` keys) with drag-to-pan once zoomed in.
- A gallery with starring, search, and single or batch deletion of the current user's outputs. Deletion requires the `user_delete_image` permission and a confirmation; files go to the recycle bin when the server enables `Paths.RecycleDeletedImages`, otherwise they are deleted permanently.
- MagicPrompt prompt enhancement with configurable local or cloud language model services. Enter an API key to preview models immediately; configuration is applied only when you save. Prompt generation defaults to a 120-second timeout; model listing keeps its 20-second limit.
- Upload quality checks: resolution, sharpness (Laplacian variance) and the product's share of the cut-out are checked on upload; weak photos get a plain warning with a suggestion and the user can continue. Thresholds live in `upload_quality` of `platforms.json`.
- Unpicked candidate cleanup: simple-mode candidates that were never picked, downloaded or exported are deleted after a retention time (14 days by default, administrator-configurable or off); paths are verified to stay inside the works folder and only pictures or videos are removed. API: `EcomCraftStudioMarkKept`.
- Plain-language errors: every API failure carries an error code and the front end shows a Chinese or English explanation with a next step, never a stack trace, address, prompt or node graph.
- Administrator first-setup wizard: language → storage location → register servers → model check → common platforms; every step is skippable and the wizard can be reopened from the setup guide button. Common platforms come first and are preselected in the simple mode.
- Mobile support: a camera-capture upload (the `capture` attribute works under `http://` LAN addresses), a single-column gallery on narrow screens, and no browser features that need HTTPS.
- Completion notices: batches announce their outcome in the page, and a background tab shows “(done/total)” progress in its title until everything ends.
- One-click diagnostics (administrators): recent logs, execution-server status, versions and sanitized configuration in one zip; secrets, passwords, prompts, images and session data are removed automatically. API: `EcomCraftDiagnostics`.
- System data backup and restore (administrators): `Data/EcomCraft` plus the account database packed into a zip with a SHA-256 manifest; restores validate every file and require confirmation. See the [Upgrade and Data Guide](docs/EcomCraft%20Upgrade.md). APIs: `EcomCraftBackupCreate` and `EcomCraftBackupRestore`.
- Disk space monitoring: the free space of the platform server and the work library is checked in the background; below the warning threshold (10 GB by default) administrators see a notice, below the critical threshold (2 GB by default) new generations are rejected with a plain reason. Thresholds are configurable or can be disabled.
- An optional EcomCraft Studio desktop shortcut during Windows initial setup.

Planned: team node management, persistent task history, a shared asset library, and per-task authorization for cloud generation. These planned features are not yet available.

## Repository Layout

| Path | Purpose |
|---|---|
| `src/` | SwarmUI server and browser frontend |
| `src/Extensions/EcomCraft/` | EcomCraft workspace, MagicPrompt, and workflow APIs |
| `Desktop/` | Desktop launch entry point |
| `docs/` | User and developer documentation |

## Documentation

- [Chinese documentation index](docs/README.zh-CN.md) · [English documentation index](docs/README.md)
- [Basic Usage (Chinese)](docs/Basic%20Usage.zh-CN.md) · [Troubleshooting (Chinese)](docs/Troubleshooting.zh-CN.md)
- [EcomCraft ComfyUI workflow calls](docs/EcomCraft%20ComfyUI%20Workflows.md) · [LAN Deployment Guide](docs/EcomCraft%20LAN%20Deployment.md) (multi-user login, queue limits, firewalls, shared storage and HTTPS restrictions) · [Upgrade and Data Guide](docs/EcomCraft%20Upgrade.md) (data retention, backup and restore)
- [Prompt Enhancement and Deployment](docs/EcomCraft%20Prompt%20Enhancement.md)
- [EcomCraft Architecture and Roadmap](docs/EcomCraft%20Architecture.md) · [LAN Multi-Server Platform Requirements](docs/EcomCraft%20LAN%20Multi-Server%20Platform%20Requirements.md) · [Simple Mode Redesign](docs/EcomCraft%20Simple%20Mode%20Redesign.md) (development planning, including features not yet implemented)
- [API Guide (Chinese)](docs/API.zh-CN.md) · [Command-Line Arguments (Chinese)](docs/Command%20Line%20Arguments.zh-CN.md)

## Data and Access

Selecting a cloud prompt model sends input text to the configured provider. That is a separate path from generation requests restricted to registered ComfyUI execution servers. ComfyUI on LAN execution servers has no login; use a firewall so that only the platform server can reach its port. The installer's “just yourself, with LAN access” mode still treats visitors as administrators. Before allowing multiple users, configure account authentication and permissions separately, and review the listening address, backend access, and asset-directory permissions. The default `Data/`, `Models/`, and `Output/` directories hold local settings, models, and generated data; if you change their paths, retain the configured directories instead.

## Contact and Deployment Service

- Bug reports and feature requests: [GitHub Issues](https://github.com/Accroid/EcomCraft/issues).
- Deployment service: remote setup for single-machine or multi-machine LAN installations (installation, server registration, shared storage and team training); contact details: (to be added).

## Origin and License

This project is an independent SwarmUI derivative, **not an official SwarmUI release**. Thanks to original author Alex “mcmonkey” Goodwin and all contributors. The project retains the [MIT License](LICENSE.txt) and [upstream copyright and third-party notices](THIRD_PARTY_NOTICES.md). Models, workflows, and other dependencies remain subject to their respective licenses.
