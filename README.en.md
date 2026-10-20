# EcomCraft Studio

> **Language:** [简体中文](README.md) · English

**An intranet AI creation workspace for e-commerce design teams, developed on top of [SwarmUI](https://github.com/mcmonkeyprojects/SwarmUI).**

EcomCraft reuses SwarmUI's image and video generation capabilities and is gradually adding the unified entry point, node management, task tracking, asset sharing, and data-handling policies needed by teams.

> **Project status:** Active development. `✓` means code for the capability exists; it does not imply a successful full build, end-to-end validation, or production acceptance. Planned features are not presented as available functionality.

## Contents

- [Project Scope](#project-scope)
- [Current Capabilities](#current-capabilities)
- [Roadmap](#roadmap)
- [Technical Structure](#technical-structure)
- [Getting Started](#getting-started)
- [Documentation](#documentation)
- [Privacy and Security](#privacy-and-security)
- [Origin and License](#origin-and-license)

## Project Scope

EcomCraft is a SwarmUI derivative, not an official SwarmUI release. It is currently integrated as a SwarmUI extension and reuses the native generation engine and backend management where possible, while adding collaboration-oriented business features.

Design goals:

- **One entry point:** Employees access the creation workspace through a shared intranet address.
- **Visible compute capacity:** Inspect registered backend status, enabled state, and capacity signals in one place.
- **Traceable tasks:** Gradually link submitter, processing policy, execution node, errors, and generated results.
- **Explicit processing boundaries:** Local and cloud services must be clearly separated. Materials must not be uploaded or silently rerouted to the cloud without explicit authorization.
- **Separate sharing from privacy:** Private inputs and results must not become public just because collaboration features exist.
- **Controlled upgrades:** Upstream integration, testing, release approval, and rollback need a defined process.

## Current Capabilities

### Implemented

- ✓ Integrated into SwarmUI as an extension under `src/Extensions/EcomCraft`.
- ✓ Provides an image and video creation workspace that reuses SwarmUI generation flows.
- ✓ Restricts workspace requests to local self-starting ComfyUI and validates backend, model, permission, and parameter constraints server-side; there is no automatic remote or cloud fallback.
- ✓ Lets administrators publish ComfyUI workflows for selection in the browser workspace; dynamic inputs are checked against a server-side allowlist. Includes `EcomCraftStudioCatalog` and `EcomCraftStudioGenerate` APIs.
- ✓ Bundles MagicPrompt prompt enhancement with configurable local or cloud language-model providers.
- ✓ The Windows installer can create an EcomCraft Studio desktop launcher that supports application-window mode.
- ✓ Provides Chinese and English project READMEs plus Chinese user and developer documents linked from their English originals.

### Planned

- [ ] Persistent team node registry, health checks, capability approval, and controlled routing.
- [ ] Persistent task queue, state transitions, cancellation, retries, and execution-node records.
- [ ] End-to-end traceability between inputs, models, workflow versions, and results.
- [ ] Per-task cloud-service authorization, upload auditing, and centralized outbound-network policy.
- [ ] Team asset library, private asset access controls, and publication/unpublication workflow.
- [ ] Upstream integration, regression testing, release approval, backup, restore, and business-data rollback.

## Technical Structure

| Component | Location | Purpose |
|---|---|---|
| EcomCraft extension | `src/Extensions/EcomCraft/` | Workspace and workflow logic |
| Workspace UI | `src/Extensions/EcomCraft/Studio/` | Browser-based creation interface |
| SwarmUI core | `src/` | Sessions, permissions, APIs, models, and generation |
| Backend and workflows | SwarmUI / ComfyUI | Actual image and video generation |

EcomCraft is not a standalone generation engine. Prefer extension points and public APIs; change SwarmUI core only when necessary.

## Getting Started

1. Install and launch a compatible SwarmUI environment using the [upstream installation guide](https://github.com/mcmonkeyprojects/SwarmUI#installing-on-windows).
2. Configure a working ComfyUI backend. The current EcomCraft workspace only permits the local self-starting ComfyUI backend and does not silently fall back to remote or cloud backends.
3. Open the EcomCraft creation workspace in SwarmUI. For prompt-enhancement setup, see the [EcomCraft Prompt Enhancement Guide](docs/EcomCraft%20Prompt%20Enhancement.md).

For general operation, start with the [Chinese Basic Usage guide](docs/Basic%20Usage.zh-CN.md).

## Documentation

- [Chinese documentation index](docs/README.zh-CN.md) · [English documentation index](docs/README.md)
- [EcomCraft Prompt Enhancement and Deployment](docs/EcomCraft%20Prompt%20Enhancement.md)
- [EcomCraft ComfyUI workflow calls (Chinese)](docs/EcomCraft%20ComfyUI%20工作流.zh-CN.md)
- [Basic Usage in Chinese](docs/Basic%20Usage.zh-CN.md)
- [Advanced Usage in Chinese](docs/Advanced%20Usage.zh-CN.md)
- [Troubleshooting in Chinese](docs/Troubleshooting.zh-CN.md)
- [API Guide in Chinese](docs/API.zh-CN.md)
- [Command-Line Arguments in Chinese](docs/Command%20Line%20Arguments.zh-CN.md)

## Privacy and Security

- Local image generation and prompt enhancement are different data paths. When a cloud prompt model is selected, input text is sent to the configured provider; review its data policy first.
- Generation requests from the current workspace are restricted to the local self-starting ComfyUI backend. Cloud generation and per-task authorization are not implemented yet.
- Before exposing the workspace to multiple users on a LAN, review account permissions, network exposure, backend access, and asset-directory permissions.

## Origin and License

This project is an independent SwarmUI derivative and is **not an official SwarmUI release**. Thanks to original author Alex “mcmonkey” Goodwin and all contributors.

The project retains the [MIT License](LICENSE.txt) and [upstream copyright and third-party notices](THIRD_PARTY_NOTICES.md). Models, workflows, extensions, and other dependencies remain subject to their respective licenses.
