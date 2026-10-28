# SwarmUI Documentation

> **Language / 语言:** English (original) · [简体中文](README.zh-CN.md)

- [Basic Usage](/docs/Basic%20Usage.md) to learn how to get started.
- [Why Use Swarm](/docs/Why%20Use%20Swarm.md) to see what advanced Swarm offers you
- [Advanced Usage](/docs/Advanced%20Usage.md) for various advanced usage topics (use from external device, comfy workflows, ...)
- [Features](/docs/Features/README.md) for a sub-listing of various specific Swarm features and how to use them.
- Environment related docs:
    - [Docker](/docs/Docker.md) for using SwarmUI inside a Docker container.
    - [Using More GPUs](/docs/Using%20More%20GPUs.md) for more info about splitting generations between multiple GPUs.
    - [Sharing Your Swarm](/docs/Sharing%20Your%20Swarm.md) for info about sharing your SwarmUI instance with others.
    - [AMD Radeon GPUs](/docs/AMD%20Radeon%20GPUs.md) for more info about using an AMD graphics card with SwarmUI.
- [Model Support](/docs/Model%20Support.md) for details about support of different image model types.
    - [Video Model Support](/docs/Video%20Model%20Support.md) for details about video model types.
    - [Audio Model Support](/docs/Audio%20Model%20Support.md) for details about audio model types.
- TODO: Settings guide (server & user)
- [Command line arguments](/docs/Command%20Line%20Arguments.md) describes the available command line arguments in detail.
- [Extensions](/docs/Extensions.md) for info about how to use extensions.
    - [Making Extensions](/docs/Making%20Extensions.md) explains how to make extensions.
    - TODO: Backends (what a 'backend' is, supported options, usage, etc)
        - [ComfyUI Backend Extension](/src/BuiltinExtensions/ComfyUIBackend/README.md)
        - Legacy [Auto WebUI Backend Extension](/src/BuiltinExtensions/AutoWebUIBackend/README.md)
    - TODO: Tools (what a tool is, how to use em, etc)
        - [GridGenerator](/src/BuiltinExtensions/GridGenerator/README.md)
        - [ImageBatchTool](/src/BuiltinExtensions/ImageBatchTool/README.md)
    - Other:
        - [Dynamic Thresholding](/src/BuiltinExtensions/DynamicThresholding/README.md)
- [API](/docs/API.md) for details about the HTTP&WebSocket network API, to use swarm externally.
- [Image Metadata Format](/docs/Image%20Metadata%20Format.md) for details about the format of metadata Swarm adds to generated images by default.
- [Privacy](/docs/Privacy.md) contains notes for privacy-concerned Swarm users.
- [Troubleshooting](/docs/Troubleshooting.md) for information about how to solve common problems.

## EcomCraft

- [EcomCraft Architecture and Roadmap](/docs/EcomCraft%20Architecture.md) for product positioning, deployment topology, layered architecture, MVP scope and later features (development planning, including features not yet implemented).
- [EcomCraft LAN Multi-Server Platform Requirements](/docs/EcomCraft%20LAN%20Multi-Server%20Platform%20Requirements.md) for execution servers, workflow review and snapshots, scheduling, multi-user use, work storage and the reserved cloud capability.
- [EcomCraft Simple Mode Redesign](/docs/EcomCraft%20Simple%20Mode%20Redesign.md) ([Chinese](/docs/EcomCraft%20简易模式改版需求.zh-CN.md)) for the simple-mode redesign (F53 and F55 implemented pending acceptance; F52, F54, F56 and F57 pending; includes a handover section for the agent continuing development): four upload sources, two-level product types, bilingual prompts with AI expansion, the candidate-count cap, the faster generation path and automatic workflow creation; with an [interactive prototype](/docs/EcomCraft%20简易模式改版原型.html).
- [EcomCraft Platform Preset Slots](/docs/EcomCraft%20Platform%20Preset%20Slots.md) ([Chinese](/docs/EcomCraft%20平台预设槽位设计.zh-CN.md)) for the slot-based platform presets design (F58, implemented pending acceptance): per-slot image specs (size and ratio, white-image position, subject fill, size caps, forbidden content, AI labels) and image sets composed by slot; includes the N06 verification checklist.
- [EcomCraft Reference Images](/docs/EcomCraft%20Reference%20Images.md) ([Chinese](/docs/EcomCraft%20参考图需求.zh-CN.md)) for the reference-image requirements (F59, P1 implemented pending acceptance): one image per uploaded reference (such as a competitor's finished image); two paths — "borrow the composition" (vision model joins the prompt) and "scene reference" (image-to-image, needing an extra model); copyright boundary and temporary storage; P2 depends on N10.
- [EcomCraft UI and Workflow Redesign](/docs/EcomCraft%20UI%20Redesign.md) ([Chinese](/docs/EcomCraft%20界面改版需求.zh-CN.md)) for the v4 redesign requirements (F60–F67): task-first simple mode, navigation and the administrators' menu, professional mode for every account, phones, stars and a draft of personal analysis.
- [Calling ComfyUI Workflows in EcomCraft](/docs/EcomCraft%20ComfyUI%20Workflows.md) for workflow discovery on local installations and LAN execution servers, review and publishing, execution server scheduling, and the client API.
- [EcomCraft LAN Deployment Guide](/docs/EcomCraft%20LAN%20Deployment.md) for multi-user login, per-role queue limits, execution server firewalls, visibility of works on shared storage, and browser restrictions under `http://` LAN addresses.
- [EcomCraft Upgrade and Data Guide](/docs/EcomCraft%20Upgrade.md) for what survives an upgrade and how to back up and restore system data.
- [Prompt Enhancement and Deployment](/docs/EcomCraft%20Prompt%20Enhancement.en.md) ([Chinese](/docs/EcomCraft%20Prompt%20Enhancement.md)) for MagicPrompt backend configuration, generation timeouts and the image-description (vision) toggle.
- [EcomCraft Workspace UI Design](/docs/EcomCraft%20Workspace%20UI%20Design.md) for the workspace's design language and the per-page control inventory.
