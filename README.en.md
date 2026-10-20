# EcomCraft Studio

> **Language:** [简体中文](README.md) · English

EcomCraft Studio is an e-commerce creation workspace built on [SwarmUI](https://github.com/mcmonkeyprojects/SwarmUI). This repository includes the SwarmUI server source and the EcomCraft extension; **you do not need a separate upstream SwarmUI installation**. The project is under active development and has not completed production acceptance.

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

Workspace generation uses only local self-starting ComfyUI; it does not switch to a remote or cloud backend when the local backend is unavailable. For prompt enhancement, see the [Prompt Enhancement Guide](docs/EcomCraft%20Prompt%20Enhancement.md). If NuGet connection errors occur during the build, check your network, proxy, and access to `https://api.nuget.org/v3/index.json`.

After opening the workspace, switch to the task queue to see running local ComfyUI jobs, queue positions, and node progress. Users with generation permission can stop their own active tasks. Queue state is runtime information and is not retained after a server restart.

## Feature Status

Implemented:

- An image and video creation workspace that reuses SwarmUI generation. Available modes depend on installed models, nodes, and account permissions.
- Administrator-published ComfyUI workflows that users can select in the workspace. The server checks workflow IDs and published input allowlists; APIs include `EcomCraftStudioCatalog`, `EcomCraftStudioGenerate`, `EcomCraftStudioQueue`, and `EcomCraftStudioCancelTask`.
- Automatic discovery of native workflows in local ComfyUI workflow directories, converted into safe workspace workflows. Additional directories can be configured with `Settings.EcomCraft.ComfyWorkflowSearchPaths`; discovered workflows must still contain recognizable positive and negative prompt inputs.
- A live task queue showing local ComfyUI running jobs, pending jobs, current nodes, and node progress, with support for stopping the current user's active generation tasks.
- MagicPrompt prompt enhancement with configurable local or cloud language-model services.
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
- [EcomCraft ComfyUI workflow calls (Chinese)](docs/EcomCraft%20ComfyUI%20工作流.zh-CN.md)
- [Prompt Enhancement and Deployment](docs/EcomCraft%20Prompt%20Enhancement.md)
- [API Guide (Chinese)](docs/API.zh-CN.md) · [Command-Line Arguments (Chinese)](docs/Command%20Line%20Arguments.zh-CN.md)

## Data and Access

Selecting a cloud prompt model sends input text to the configured provider. That is a separate path from generation requests restricted to local ComfyUI. The installer's “just yourself, with LAN access” mode still treats visitors as administrators. Before allowing multiple users, configure account authentication and permissions separately, and review the listening address, backend access, and asset-directory permissions. The default `Data/`, `Models/`, and `Output/` directories hold local settings, models, and generated data; if you change their paths, retain the configured directories instead.

## Origin and License

This project is an independent SwarmUI derivative, **not an official SwarmUI release**. Thanks to original author Alex “mcmonkey” Goodwin and all contributors. The project retains the [MIT License](LICENSE.txt) and [upstream copyright and third-party notices](THIRD_PARTY_NOTICES.md). Models, workflows, and other dependencies remain subject to their respective licenses.
