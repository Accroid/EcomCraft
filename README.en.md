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

- A three-step simple mode as the default view: upload a product photo → pick platform, image type (template), a two-level product type (category → subtype), an optional visual style and whether a model is needed, optionally with one line of description → choose candidates and download. There is no prompt box; the server builds the prompt from the product type, style and description. The candidate count is a manual input of positive integers 1–20, and single generations and one-click sets share a cap of 20 images in total. Professional parameters such as sampler, LoRA and steps appear only in the administrators' professional mode. Platform list API: `EcomCraftStudioPlatforms`; product types API: `EcomCraftStudioProductTypes`.
- An image and video creation workspace (professional mode, visible to administrators) that reuses SwarmUI generation. Available modes depend on installed models, nodes, and account permissions. The interface is available in Chinese and English: switch with the header button without reloading; the choice is remembered in the browser, and the first visit follows the browser language.
- Administrator-published ComfyUI workflows that users can select in the workspace. The server checks workflow IDs and published input allowlists; APIs include `EcomCraftStudioCatalog`, `EcomCraftStudioGenerate`, `EcomCraftStudioQueue`, and `EcomCraftStudioCancelTask`.
- Workflow review: auto-discovered native workflows must be reviewed before users can run them. Users can submit workflows they saved and see rejection reasons; reviewers configure user-visible parameters (display name, default, range, options), check which execution servers can run each workflow, then publish, reject, or unpublish. Approval freezes a snapshot; when the source file changes the workflow needs review again, and users keep the old snapshot until the new version is approved. Permissions: `ecomcraft_submit_workflows` and `ecomcraft_review_workflows`. APIs: `EcomCraftStudioReviewList`, `EcomCraftStudioReviewDetail`, `EcomCraftStudioReviewDecide`, `EcomCraftStudioWorkflowSubmissions`, and `EcomCraftStudioSubmitWorkflow`. Native workflow batches increment actual sampler seeds for each image; `-1` randomizes the starting seed for the batch.
- Automatic discovery of `workflows` and `blueprints` in local ComfyUI installations and of workflows saved in the ComfyUI of LAN execution servers, including embedded subgraphs. Discovery runs in the background and refreshes about every 30 seconds; entries are named “source server / workflow”. Individual file paths are not required. `Settings.EcomCraft.ComfyWorkflowSearchPaths` adds optional directories. Imported workflows must pass graph and prompt validation and execute on an execution server that has the required models and nodes. The catalog deduplicates native workflows by executable content and hides entries whose saved model/node dependencies are unavailable on every online execution server. The page reads saved defaults, forwards edits to the corresponding nodes, and checks dependencies again before submission. On Windows AMD systems, automatic backend arguments disable pinned memory for ROCm compatibility when reloading quantized text encoders. Backends in a Comfy Desktop installation also append its sibling `ComfyUI-Shared/models` library to their model search paths.
- Execution server scheduling: each submission uses “Rule 1” to pick, among online servers that have the required nodes and models, the one with the shortest queue (counting tasks others submitted directly to ComfyUI); a whole batch runs on that one server. Users can restrict a task to one server in the creation panel and get a hint when that server is congested while another is idle. When no server can run the task, the reason is shown and nothing is submitted. Results record the server that ran them; failed tasks are never retried automatically and can be reassigned to another server with one click. The gallery shows each work's workflow and execution server.
- E-commerce scene templates: a template references a published workflow snapshot and adds Chinese and English names, an example image, suitable platforms, image sets and prompt fragments; built-in and administrator templates are stored separately, and invalid templates are not offered to users, with the reasons reported. Management APIs: `EcomCraftTemplateAdminList`, `EcomCraftTemplateSave`, and `EcomCraftTemplateDelete`; users read available templates with `EcomCraftStudioTemplates` (names in their language) and send `template_id` to `EcomCraftStudioGenerate` to generate from a template, still checked against the snapshot allowlist.
- Model list and commercial licenses: templates declare the models and nodes they need and each model's commercial-use license (allowed / forbidden / unknown); the workflows page lists what each server lacks and highlights license problems.
- Compliance: Amazon white background main images take a cut-out-only branch that centers the cut-out on pure white without any generation model; exports check main image backgrounds for pure white and write IPTC digital source type metadata for generated or composite pictures.
- Platform export: pick a platform and works are scaled and padded to its preset, one folder per product, then downloaded as a zip or written to the administrator's shared export folder; presets cover Chinese and international platforms, are editable, and the built-in values are marked as needing verification. APIs: `EcomCraftStudioExportOptions` and `EcomCraftStudioExport`.
- Product library (simple version): uploaded products are saved with their original photo, cut-out and confirmed placement and can be picked next time; isolated per account, with rename and delete. APIs: `EcomCraftStudioProducts`, `EcomCraftStudioProductSave`, `EcomCraftStudioProductUpdate`, and `EcomCraftStudioProductDelete`.
- One-click sets and batches: several product photos × every image type of the platform's set become independent tasks that run in parallel on several execution servers; each person's running tasks are limited, failed items can be retried individually and remaining tasks can be cancelled. APIs: `EcomCraftStudioGenerateSet`, `EcomCraftStudioBatchStatus`, `EcomCraftStudioBatchRetry`, and `EcomCraftStudioBatchCancel`.
- 4 built-in e-commerce scene templates: white background main image, lifestyle scene, detail close-up, and model or hand-held; they reference expert-made workflows by conventional name (such as `ecomcraft/white-main`) and show a missing-workflow notice until the workflow is installed and approved.
- Product preservation: with `preserve_product` on, the platform places the cut-out product on the input canvas locally and pastes the original product pixels back after generation, so logos, text and shapes stay unchanged. In the simple mode users can remove the background with one click (SwarmUI's background removal node on an execution server, API `EcomCraftStudioCutout`) and drag or resize the product.
- Prompt service: users pick a product type and an optional style, write one line (Chinese or English), and the configured prompt enhancement model writes an English prompt; checking "needs a model" adds a fixed model fragment. The model can only fill in prompt text; when the model is unavailable the template and style defaults are used with a notice. Sending product photos to a vision model is off by default and only administrators can turn it on. APIs: `EcomCraftStudioStyles`, `EcomCraftStudioProductTypes`, `EcomCraftStudioComposePrompt`, and `EcomCraftPromptSettings`.
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
