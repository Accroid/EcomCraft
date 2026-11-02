# EcomCraft Studio

> **Language:** [简体中文](README.md) · English

EcomCraft Studio is a **LAN product-image production platform** for e-commerce teams, built on [SwarmUI](https://github.com/mcmonkeyprojects/SwarmUI). The repository includes the SwarmUI server source, so **you do not need a separate upstream SwarmUI installation**. The project is under active development and has not completed production acceptance; please report problems via [Issues](https://github.com/Accroid/EcomCraft/issues).

- **Operators and small shop owners** create images in three steps in simple mode, without prompts, models or parameters.
- **Graphic designers** use professional mode to tune models, samplers and LoRAs, and upload their own ComfyUI workflows.
- **Sales model**: to be decided (formerly an open-source core + team edition priced per account; commercial use is not claimed until model licensing is settled).

## Before and After

<!-- Before/after showcase: the uploaded product photo on the left, a generated result on the right. Images to be added later. -->

| Uploaded photo | Generated result |
|---|---|
| (image pending) | (image pending) |

Demo video: (to be added)

## Advantages: Compared with Local ComfyUI and Cloud Generation

There are three common ways to produce product images: set up local ComfyUI yourself, use a cloud generation service, or outsource to a studio. EcomCraft's generation engine is still ComfyUI, but the platform runs it on registered local or LAN execution servers — **operators and shop owners never set it up themselves, and generation never touches any cloud**. A comparison:

| | EcomCraft | Self-managed ComfyUI | Cloud generation services |
|---|---|---|---|
| **Product privacy** | Generation runs only on registered local or LAN execution servers, never falling back to the cloud; image description defaults to local Ollama (images leave the machine only if an administrator switches to a cloud vision service) | Images stay on the machine | Product photos are uploaded to the provider |
| **Getting started** | Three steps: upload the product → pick platform and reference → choose and export; no prompts, no parameters; a first-run wizard and hardware check get you started | Install environments, download models, learn nodes and workflows | Easy to start, but you write prompts and reroll repeatedly |
| **Phone photos work directly** | Snap and upload: automatic compression, straightening and cut-out, with EXIF and other capture metadata stripped | Find your own tools to compress, straighten and cut out | Most accept uploads, but capture metadata goes to the provider along with the photo |
| **Product fidelity** | The cut-out goes onto the canvas and the product's logo, text and shape are **pasted back unchanged** after generation; selling-point text is laid out by the program in fixed layouts, never written by AI | You must build the cut-out, paste-back and edge-fix chain yourself; products often drift | The product is repainted from scratch; logos and text often distort into gibberish |
| **Full image sets** | One-click sets for major platforms at home and abroad (Amazon, Taobao/Tmall, JD, TikTok Shop and more), exported to each platform's specs; Amazon white main images get a pure-white check and AI metadata | Every image generated and resized manually | Usually single images; sizes, formats and counts limited by the service |
| **Teamwork** | Account login, per-person limits, tasks auto-dispatched to idle GPUs, queue with progress and estimated time left | One user per machine; no accounts, queueing or permissions; sharing one machine is painful | Everyone opens their own account; images scatter across accounts |
| **Workflows** | Built-in templates' workflows are **generated automatically** per model; a designer's upload works for them at once and is shared with everyone after review | Everyone builds and stores their own; nobody manages node dependencies and versions | No custom workflows |
| **Finding your work** | Product library, starred candidates, project archive, "redo with the same settings" | You organize output folders yourself | Images live in the provider's account; migration means manual downloads |
| **Cost** | One-time GPU investment; unlimited generation, no per-image fees | The same hardware, plus your own setup and maintenance time | No hardware; pay per image or by subscription, and costs keep growing with volume |
| **Hardware** | A GPU is required (NVIDIA with 8 GB VRAM minimum on Windows; Apple silicon with 24 GB unified memory on Mac) | A GPU too, plus solving environments and dependencies yourself | None |

**How to choose**: teams that already have GPUs and care about confidentiality and full sets should pick EcomCraft; a single technical user who enjoys tinkering will find plain ComfyUI enough; with no GPU and photos that may leave the company, a cloud service can be a starting point.

"Follow a reference" is the single entry of simple mode: upload a product photo and a reference image you like, and the platform borrows the reference's composition and light for generation — no prompts; the product's pixels always come from your original photo and are pasted back by the platform, never repainted by the model.

APIs and security rules are in the [workflow calls guide](docs/EcomCraft%20ComfyUI%20Workflows.md).

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

## Contact and Deployment Service

- Bug reports and feature requests: [GitHub Issues](https://github.com/Accroid/EcomCraft/issues).
- Deployment service: remote setup for single-machine or multi-machine LAN installations (installation, server registration, shared storage and team training); contact details: (to be added).

## Origin and License

This project is an independent SwarmUI derivative, **not an official SwarmUI release**. Thanks to original author Alex “mcmonkey” Goodwin and all contributors. The project retains the [MIT License](LICENSE.txt) and [upstream copyright and third-party notices](THIRD_PARTY_NOTICES.md). Models, workflows, and other dependencies remain subject to their respective licenses.
