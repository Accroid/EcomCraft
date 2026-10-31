# EcomCraft Studio

> **Language:** [简体中文](README.md) · English

EcomCraft Studio is a **LAN product-image production platform** for e-commerce teams, built on [SwarmUI](https://github.com/mcmonkeyprojects/SwarmUI). The repository includes the SwarmUI server source, so **you do not need a separate upstream SwarmUI installation**. The project is under active development and has not completed production acceptance; please report problems via [Issues](https://github.com/Accroid/EcomCraft/issues).

- **Operators and small shop owners** create images in three steps in simple mode, without prompts, models or parameters.
- **Graphic designers** use professional mode to tune models, samplers and LoRAs, and upload their own ComfyUI workflows.
- **Sales model**: sold as software, priced per account.

## Before and After

<!-- Before/after showcase: the uploaded product photo on the left, a generated result on the right. Images to be added later. -->

| Uploaded photo | Generated result |
|---|---|
| (image pending) | (image pending) |

Demo video: (to be added)

## What It Solves

| Pain point | How EcomCraft handles it |
|---|---|
| No prompt or design skills | Choose a task first: **Follow a reference** (upload a reference image you like and get its composition and light), make one image, or make a full set; a one-line description can be expanded by AI into Chinese and English prompts |
| AI distorts the product, its logo and text | **Product preservation**: the cut-out goes onto the canvas and the product's logo, text and shape are pasted back unchanged after generation, with light matched to the scene; selling-point text is laid out by the program in fixed layouts, never written by AI |
| Every platform has its own sizes, white backgrounds and file caps | One-click sets and exports by the platform's **slots**; Amazon white main images use the cut-out only, with a pure-white check and AI metadata |
| Product photos must not leave the company | Generation runs **only on registered local or LAN** ComfyUI servers, never falling back to the cloud; the vision model defaults to local Ollama |
| Several GPU computers and nobody to schedule them | Tasks go to idle execution servers automatically; the queue shows progress and the estimated time left, and lets you stop your own tasks |
| Building workflows is hard | The built-in templates' workflows are **generated automatically** with the default models; a designer's uploaded workflow works for them at once and is published to everyone after review |
| Starting from scratch every time | Product library, brand kits (colors, logo, references), starring candidates, and "Refine in professional mode" |

The full feature list and progress are in the [Architecture and Roadmap](docs/EcomCraft%20Architecture.md); APIs and security rules are in the [workflow calls guide](docs/EcomCraft%20ComfyUI%20Workflows.md).

## Quick Start

**Prerequisites**

- [Git](https://git-scm.com/downloads) and the [.NET SDK](https://dotnet.microsoft.com/download) (.NET 10 SDK recommended; the project targets .NET 8). The first build needs NuGet access.
- Generating images needs a GPU, models and the matching ComfyUI nodes; running only the web server does not. Installing ComfyUI automatically on Linux needs Python 3.10–3.12 with `venv`; the Windows installer brings its own Python.

**Hardware requirements (a GPU is required; no GPU-less or cloud generation)**

| System | Minimum | Recommended |
|---|---|---|
| Windows | NVIDIA with 8 GB VRAM; AMD only within the ROCm support list | NVIDIA 16 GB or more |
| macOS | Apple silicon with 24 GB unified memory; Intel-chip Macs unsupported | 32 GB or more |

Windows and Mac are the priority platforms; the Linux launch scripts stay but are not verified for now. Machines below the threshold get a clear "not supported" message. Numbers are current defaults, to be tuned after measurement.

**Get the source and launch**

```bash
git clone https://github.com/Accroid/EcomCraft.git
cd EcomCraft
```

| System | Normal launch | Dev launch (forced rebuild) |
|---|---|---|
| Windows (PowerShell) | `.\launch-windows.bat` | `.\launch-windows-dev.ps1` |
| Linux | `./launch-linux.sh` | `./launch-linux-dev.sh` |
| macOS | `./launch-macos.sh` | — |

The first launch builds the server and takes a few minutes. macOS only gets the server launcher; local generation needs your own ComfyUI.

**First-time setup**

1. Open `http://127.0.0.1:7801` and follow the setup to install the **ComfyUI Self-Starting** backend and download models (or skip and configure later).
2. Open `http://127.0.0.1:7801/EcomCraft` for the workspace; administrators get a setup wizard on first visit (language, storage, execution servers, models, common platforms).
3. Other GPU computers on the LAN start ComfyUI with `--listen` and are registered under **Server → Backends** in the native engine as **ComfyUI API By URL**; see the [LAN Deployment Guide](docs/EcomCraft%20LAN%20Deployment.md).
4. "Follow a reference" needs a vision model: administrators can click "One-click local model install" in the workspace's prompt enhancement settings to pick a tier and install it (or run `launchtools/install-vision-model-windows.ps1`, Linux: `launchtools/install-vision-model-linux.sh`); see the [Prompt Enhancement guide](docs/EcomCraft%20Prompt%20Enhancement.md).

## Data and Security

- Clients send only template or workflow IDs and the server-published parameter whitelist; ComfyUI workflow JSON can only be uploaded by an account in professional mode and goes through structure checks and denied-node checks.
- Selecting a cloud prompt model sends input text to that provider; this is a separate path from generation, which only goes to registered execution servers.
- ComfyUI on LAN servers has no login, so firewall its port to the platform server only; turn on account login and set permissions before several people use it.
- `Data/`, `Models/` and `Output/` hold settings, models and works; upgrades and backups are covered in the [Upgrade and Data Guide](docs/EcomCraft%20Upgrade.md).

## Documentation

- [Documentation index](docs/README.md) · [Chinese documentation index](docs/README.zh-CN.md) · [Basic Usage (Chinese)](docs/Basic%20Usage.zh-CN.md) · [Troubleshooting (Chinese)](docs/Troubleshooting.zh-CN.md)
- [Workflow calls and APIs](docs/EcomCraft%20ComfyUI%20Workflows.md) · [LAN Deployment Guide](docs/EcomCraft%20LAN%20Deployment.md) · [Prompt enhancement and vision model](docs/EcomCraft%20Prompt%20Enhancement.md) · [Upgrade and Data Guide](docs/EcomCraft%20Upgrade.md)
- Development planning: [Architecture and Roadmap](docs/EcomCraft%20Architecture.md) · [Implementation Plan](docs/EcomCraft%20Implementation%20Plan.md) · [Refactoring Plan](docs/EcomCraft%20Refactoring%20Plan.md) · [UI and Workflow Redesign](docs/EcomCraft%20UI%20Redesign.md) · [Simple Mode Redesign](docs/EcomCraft%20Simple%20Mode%20Redesign.md) · [LAN Multi-Server Platform Requirements](docs/EcomCraft%20LAN%20Multi-Server%20Platform%20Requirements.md)

## Contact and Deployment Service

- Bug reports and feature requests: [GitHub Issues](https://github.com/Accroid/EcomCraft/issues).
- Deployment service: remote setup for single-machine or multi-machine LAN installations (installation, server registration, shared storage and team training); contact details: (to be added).

## Origin and License

This project is an independent SwarmUI derivative, **not an official SwarmUI release**. Thanks to original author Alex “mcmonkey” Goodwin and all contributors. The project retains the [MIT License](LICENSE.txt) and [upstream copyright and third-party notices](THIRD_PARTY_NOTICES.md). Models, workflows, and other dependencies remain subject to their respective licenses.
