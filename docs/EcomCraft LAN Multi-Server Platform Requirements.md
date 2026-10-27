# EcomCraft LAN Multi-Server Platform Requirements

> **Language:** English · [简体中文](EcomCraft%20局域网多服务器平台需求.zh-CN.md)
>
> **Status:** In development (phase 1 steps 1–3 code complete) · **Version:** v1.4 · **Date:** 2026-10-27
>
> This document is the reference for further development. When requirements change, update this document first (Chinese and English together), record the change in the change log at the end, and only then change the code. For the e-commerce image layer, the overall architecture and the MVP roadmap, see [EcomCraft Architecture and Roadmap](EcomCraft%20Architecture.md).

## 1. Background and Goals

The EcomCraft workspace originally could only call the self-starting ComfyUI on the machine running EcomCraft.

The goal is to turn EcomCraft into a ComfyUI scheduling platform for the local network:

- One or more servers on the LAN run ComfyUI; other devices open EcomCraft in a browser and call reviewed workflows directly.
- The platform lists each server's workflows automatically and sends each task to the least-loaded server that can run the workflow.
- Whichever server runs a task, the result returns to the user's own gallery.
- Works are stored on the platform server by default, and can also be kept on a LAN NAS or a shared folder on another computer.
- Cloud platforms (such as RunningHub) may be connected later; this phase only plans for them.

## 2. Terms

| Term | Meaning |
|---|---|
| Platform server | The machine running SwarmUI + EcomCraft. Called A in examples. |
| Execution server | A machine running ComfyUI that actually executes generation tasks. A can also be an execution server. |
| LAN server | A machine that only runs ComfyUI and is called over the network by the platform. Called B in examples. |
| Origin server | The server on which a workflow was discovered. It only records the source and does not decide where the workflow runs. |
| Snapshot | The workflow content, parameter configuration and content fingerprint frozen at approval time. |
| Busy | An execution server is running a task but has nothing waiting. |
| Congested | An execution server has a queue (its pending queue is not empty). |

## 3. Confirmed Key Decisions

| ID | Decision |
|---|---|
| D1 | Servers are connected with **Option A**: the administrator registers a ComfyUI address, and the platform reads workflows, nodes, models and queues through ComfyUI's own HTTP API. LAN servers need no extra software. A small server-side agent (Option B) may be added later if reading arbitrary directories or remote maintenance is needed. |
| D2 | Workflows must be reviewed before ordinary users can call them; workflows that users create themselves must be submitted for review. |
| D3 | The review capability is a permission that the administrator assigns to accounts or roles. |
| D4 | A snapshot is frozen at approval. When the workflow file on the server changes, it must be reviewed again before the new content is used. |
| D5 | When calling a workflow, users see only a parameter form, never the canvas; changing a workflow requires edit permission or the administrator. |
| D6 | Scheduling uses **Rule 1**: among all execution servers that can run the workflow, choose the one with the shortest queue, regardless of the origin server. |
| D7 | Any user may tick "run only on a specific server". If the chosen server is congested while another is idle, the page shows a hint. |
| D8 | If an execution server goes offline during a task, the task fails immediately with no automatic retry; the user is told they can reassign it manually. |
| D9 | Within the platform, results are visible only to their owner by default; users may share them, after which members of the same group can see them. **The storage layer is not isolated**: when works are stored on a NAS or another computer's shared folder, anyone who can access that storage can see everyone's works. |
| D10 | The platform supports multiple concurrent users and must have account login enabled. |
| D11 | Cloud generation capability is reserved: the scheduling layer is designed around an "executor" abstraction with room for cloud types. **This phase and the MVP develop no cloud generation features**, and the rules still forbid sending requests to cloud generation services. |
| D12 | The work storage location is configurable: by default works are stored on the platform server itself, and the administrator can switch to a LAN NAS or a shared folder on another computer. System data (accounts, review snapshots, configuration, work index) always stays on the platform server. |

## 4. Roles and Permissions

Permissions build on SwarmUI's existing roles and permission system, with new EcomCraft permissions assigned by the administrator.

| Permission (suggested ID) | Description | Default |
|---|---|---|
| `ecomcraft_use_published_workflows` (existing) | Call published workflows | Ordinary users |
| `ecomcraft_submit_workflows` | Submit one's own workflows for review | Ordinary users |
| `ecomcraft_review_workflows` | Review, publish, reject and unpublish workflows; configure user-visible parameters | Administrator |
| `ecomcraft_edit_workflows` | Change workflow content (changes require re-review) | Administrator |
| `ecomcraft_manage_servers` | Register and disable execution servers | Administrator |
| `ecomcraft_share_results` | Share one's own works with group members | Ordinary users |

Role conventions:

- **Administrator**: holds all permissions, assigns permissions and manages user groups.
- **Ordinary user**: calls published workflows, submits their own workflows for review, and views and manages their own tasks and works.
- Reviewer is not a fixed role; the administrator grants `ecomcraft_review_workflows` as needed.

## 5. Functional Requirements

### R1 Connecting Execution Servers

- R1.1 The administrator registers execution servers: display name, address (such as `http://192.168.1.21:8188`), enable switch and maximum concurrency. Prefer reusing SwarmUI's existing "connect to ComfyUI by URL" backend (`ComfyUIAPIBackend`).
- R1.2 The local self-starting ComfyUI (`ComfyUISelfStartBackend`) also takes part in scheduling as an execution server.
- R1.3 The platform periodically checks each server: online / offline, queue length, available node types and model lists (from `/object_info`).
- R1.4 Ordinary users see only the server display name, never its IP address, port or paths.
- R1.5 Only addresses registered by the administrator may be contacted; the LAN is never scanned.

**Acceptance criteria**

- Within 1 minute of registering B, the platform shows B as online with its queue length.
- After ComfyUI on B is closed, the platform shows B as offline and stops assigning tasks to it.
- No API response for ordinary users contains a server address.

### R2 Workflow Discovery

- R2.1 Read the workflows saved on each server through ComfyUI's HTTP API (list with `/api/userdata?dir=workflows`, then download each one). The local server may continue to read its workflow directories.
- R2.2 The list is grouped by server, and the workflow ID is "server name/workflow name", for example "A/Workflow1" and "B/Workflow1", so identical names do not collide. The ID must stay unchanged after SwarmUI's `StrictFilenameClean` (no spaces around `/`, no `.` and similar characters); otherwise the workflow cannot be found at generation time.
- R2.3 The cache refreshes in the background about every 30 seconds; page requests only read the cache and never scan on the request thread.
- R2.4 When a server is offline, its pending workflows are marked "source offline"; published snapshots are unaffected.
- R2.5 Imports keep the existing validation: workflow structure, subgraph connections, and positive / negative prompt inputs. Workflows that fail validation show the reason and cannot be submitted for publishing.

**Acceptance criteria**

- When A and B each save a workflow with the same name, the review page shows both "A/Workflow1" and "B/Workflow1".
- A workflow newly saved in ComfyUI on B appears in the pending review list within 1 minute.

### R3 Workflow Review and Snapshots

State transitions:

```
Discovered on a server / submitted by a user → Pending → Approved → Published → Unpublished
                                                   └→ Rejected (with a reason)
Published + source file changed → Pending re-review (users keep using the old snapshot)
```

- R3.1 Workflows discovered automatically on servers default to "Pending" and are invisible to ordinary users.
- R3.2 After saving their own workflow in ComfyUI, a user selects it in EcomCraft and clicks "Submit for review", optionally with a note.
- R3.3 Reviewers can preview workflow parameters, configure user-visible parameters, approve, reject (a reason is required) and unpublish.
- R3.4 **User-visible parameter configuration**: for each parameter the reviewer decides whether it is shown and sets its display name, default value, range or options; hidden parameters always use the value in the snapshot.
- R3.5 The review page shows the "runnable servers", for example "A, B" or "A only (B lacks model xxx / node yyy)".
- R3.6 Approval saves a snapshot: converted execution graph, parameter configuration, content fingerprint, origin server, reviewer and review time.
- R3.7 When the platform detects that the source file content has changed, it asks reviewers to review again; until the new version is approved, users keep using the old snapshot.
- R3.8 Users can view the status of workflows they submitted and the rejection reasons.

**Acceptance criteria**

- Unreviewed workflows never appear in ordinary users' selection lists and cannot be called through the API.
- After a published workflow is changed in ComfyUI, user calls still run the old snapshot, and the review page shows "Pending re-review".

### R4 Calling Workflows

- R4.1 Users can only choose from the "Published" list.
- R4.2 Once a workflow is chosen, the parameter form configured by the reviewer appears and can be submitted directly, without showing the canvas or raw node graph.
- R4.3 Model parameter options include only models the current user is allowed to use that exist on at least one runnable server.
- R4.4 The client may submit only the workflow ID, published parameters and ordinary generation parameters; the server validates each item and rejects arbitrary ComfyUI JSON, backend selectors and unpublished parameters.
- R4.5 Inputs such as reference images are uploaded to the chosen execution server (`/upload/image`) at submission, before the task is queued.

### R5 Scheduling

- R5.1 **Rule 1**: first filter online servers that have every node type and model the workflow needs, then choose the one with the fewest queued tasks; on a tie, choose the one whose last task finished earliest.
- R5.2 Queue count = running and pending tasks in that server's ComfyUI queue (including tasks others submitted to that ComfyUI directly) + tasks the platform has assigned but not yet submitted.
- R5.3 A server running a task with nothing queued shows "Busy" (yellow light); a server with a queue shows "Congested" (red light). The page shows each server's queue count in real time, for example "A: Congested (3 queued) | B: Busy (1 running, 0 queued) | C: Idle (0 queued)".
- R5.4 When the user ticks "run only on a specific server", the task is not reassigned and waits on that server. If that server is congested while another runnable server is idle, the page suggests "consider removing the restriction".
- R5.5 When no server can run the task, the reason is stated clearly (all offline, or which nodes / models are missing) and nothing is submitted.
- R5.6 Results are labelled with the server that actually ran them.

### R6 Failure Handling

- R6.1 If an execution server goes offline or the connection drops during a task, the task fails immediately with no automatic retry.
- R6.2 The failure message offers a "Reassign to another server" button; when clicked, a server is chosen again by R5 and the task is resubmitted.
- R6.3 Errors returned to the browser never contain exception stack traces, server addresses, raw prompts or raw node graphs.

### R7 Gallery

- R7.1 Whichever server runs a task, the results are sent back to the platform server, saved in the current user's output directory and shown in the gallery. When a work library location is set, results are first saved locally and then synced to the work library (see R11).
- R7.2 The gallery shows the workflow name, the server that ran it and the generation time. Both images and videos are supported.
- R7.3 Within the platform, works are visible only to their owner by default. Users can choose "Share with group" for individual works so that members of the same group can see them, and can unshare at any time. (Phase 2)
- R7.4 Visibility inside the platform does not extend to the storage layer: when works are kept on shared storage, the storage itself is not isolated per user (see D9 and R11).

### R8 Task Queue

- R8.1 The queue page shows each execution server's status, queue count and the progress of the current task.
- R8.2 Users see details only for their own tasks and can only end their own tasks; other people's tasks show only a count, without prompts or parameters.

### R9 Multiple Users

- R9.1 Account login is enabled (`UserAuthorization.AuthorizationRequired`), with one account per person.
- R9.2 Each user's queue limit reuses the existing SwarmUI role setting `MaxQueued`, configured per role by the administrator.
- R9.3 User groups (phase 2): the administrator maintains user groups, used for gallery sharing and grouped workflow authorization.

### R10 Cloud Integration (capability reserved only; not developed in this phase)

- R10.1 The scheduling layer (step 4) is implemented around a unified "executor" abstraction with these types: local ComfyUI, LAN ComfyUI, cloud ComfyUI (such as RunningHub or Comfy Cloud), and cloud model APIs (image generation services in China and abroad). This phase implements only the first two; the last two keep only their type and interface slots.
- R10.2 A template may have several execution plans (local and cloud) that show users the same fields; each plan must be reviewed. This phase implements only local plans.
- R10.3 Generation is implemented in three stages: "local pre-processing → generation → local post-processing". When cloud services are added, only the "generation" stage goes to the cloud; background removal, product masks and product-preserving compositing always run locally.
- R10.4 To be specified when cloud integration starts: API keys configured by the administrator (stored only on the server), consent for sending data out, team budget and per-user quotas, cost records, and downloading results back to the local gallery.
- R10.5 When all LAN servers are busy, tasks never switch to the cloud automatically (that costs money); at most a future version prompts the user, who must confirm.
- R10.6 Before the cloud phase starts, the rule "never send requests to cloud generation services" must first be changed in AGENTS.md, the READMEs and this document.

### R11 Work Storage

- R11.1 The administrator can set the **work library location** (original product images and generated results) and the **export location** (export packages) separately. Both default to the platform server itself.
- R11.2 Supported locations: a local directory; a Windows UNC path (such as `\\NAS\share\EcomCraft`); a directory mounted by the operating system on Linux / macOS. Mapped drive letters are not supported (background services cannot see drives mapped by a logged-in user).
- R11.3 The platform does not implement the SMB protocol or store NAS passwords; authentication for shared storage is handled by the operating system (the deployment guide explains how to configure it).
- R11.4 Only the administrator can change storage locations. Saving checks that the path is writable, the free space and the read/write speed; if a check fails, nothing is saved and the reason is shown. The client can never submit a file path.
- R11.5 **Write locally first, then sync**: results are first saved on the platform server, then copied asynchronously in the background to the work library location, with failed copies queued for retry. Shared storage going offline does not affect generation; the gallery shows "Pending sync" for works not yet synced.
- R11.6 The work index and thumbnails are kept on the platform server; browsing the gallery never scans shared storage, and original images are read only when opened.
- R11.7 After the work library location changes, new works are written to the new location; old works stay where they are and remain browsable. An optional "Migrate old works" action copies, verifies and only then deletes the originals, shows progress and can be cancelled midway. Nothing is moved silently.
- R11.8 When exporting, users can choose "Download zip" or "Export to shared folder" (the export location is set by the administrator).
- R11.9 System data (account database, review snapshots, template and platform preset configuration, work index) always stays under `Data/` on the platform server and is never placed on shared storage.

**Acceptance criteria**

- After the work library location is set to a NAS, newly generated works appear in the corresponding NAS directory.
- If the NAS is disconnected during generation, generation still completes and the gallery shows "Pending sync"; once the NAS is back, syncing completes automatically.
- No API for ordinary users can set or read storage paths.

## 6. Non-Functional Requirements

### Security

- Requests go only to execution servers registered by the administrator; unregistered addresses are never contacted; before the cloud phase, no requests are sent to any cloud generation service.
- ComfyUI itself has no login. The deployment guide must state: the firewall on LAN servers opens the ComfyUI port only to the platform server; the platform server has account login enabled; no server is exposed to the public internet.
- Minimal disclosure still applies: APIs for ordinary users never return server addresses, raw workflows, other users' task IDs, session IDs or keys.
- When works are kept on shared storage, the deployment guide must state that anyone who can access the shared directory can see everyone's works (D9); to restrict this, grant access on the NAS only to the platform's service account. APIs for ordinary users never return storage paths.

### Performance

- Workflow discovery and server status checks run in the background and are cached; API requests never perform blocking scans.
- Every request to an execution server has a timeout (3 seconds suggested for status checks, 10 seconds for workflow downloads).

### Reliability

- One server going offline does not affect discovery or scheduling on other servers.
- After the platform restarts, review states, snapshots and server registrations are not lost (they are saved under `Data/EcomCraft/`).

## 7. Development Phases and Tasks

### Phase 1: Get LAN multi-server working

| Step | Content | Output |
|---|---|---|
| 0 | **Technical validation**: A connects to ComfyUI on B (`--listen`) and verifies reading the workflow list, submitting tasks and returning results; confirm whether B needs the SwarmUI companion nodes | Findings added to section 9 |
| 1 | **Server registration and execution limits**: reuse `ComfyUIAPIBackend`; change `ValidateLocalBackend` to allow only the local self-starting ComfyUI and registered LAN ComfyUI; update the security rules in AGENTS.md, the READMEs and related documents | R1 |
| 2 | **Workflow discovery**: read each server's workflows, nodes and queue over HTTP and cache them in the background | R2 |
| 3 | **Review and snapshots**: new permissions, review page, visible-parameter configuration, runnable-server check, snapshot storage | R3 |
| 4 | **Calling and scheduling**: published list, parameter form, Rule 1 scheduling, server pinning, reference image upload; scheduling is implemented on the "executor" abstraction with room for cloud types | R4, R5, R10.1 |
| 5 | **Queue, failures, gallery**: multi-server queue, fail on disconnect with a reassign button, gallery shows the executing server | R6, R7.1–R7.2, R8 |
| 6 | **Multi-user configuration and documentation**: account login and `MaxQueued` configuration, LAN deployment and firewall guide | R9.1–R9.2, security documentation |
| 7 | **Work storage**: work library and export location settings, write locally then sync, migrate old works | R11 |

### Phase 2: Polish and operations

- User groups, sharing works with a group (R7.3, R9.3), grouped workflow authorization
- Persistent task records (who, when, which workflow, which server, result)
- Server maintenance mode (accept no new tasks, finish current ones)
- After approval, distribute a workflow to other servers in one click (write to B's workflow directory through the ComfyUI API; models still need manual installation)
- Per-workflow task timeouts
- Cleaning output directories on remote servers
- Review result notifications, workflow version rollback
- Workflow categories, tags, cover images, descriptions, search
- Operation logs, usage statistics

### Phase 3: Cloud integration

See R10. This phase only reserves the capability; before it starts, the requirements will be specified separately and the related rules changed first.

## 8. Changes to Existing Project Rules

The original AGENTS.md and README allowed the workspace to use only the local self-starting ComfyUI and forbade switching to remote or cloud services. Phase 1 step 1 has changed this rule to:

> Generation requests may only be sent to execution servers registered by the administrator (the local self-starting ComfyUI and registered LAN ComfyUI); unregistered addresses must never be contacted; until cloud integration is complete and confirmed, no request may be sent to any cloud generation service.

AGENTS.md, the READMEs and the workflow documentation have been updated accordingly.

## 9. Items to Verify and Assumptions

| ID | Item | Status |
|---|---|---|
| V1 | How results return from LAN ComfyUI: directly over WebSocket, or downloaded through `/history` + `/view` after completion; whether B needs the SwarmUI companion nodes | Static analysis: `ComfyUIAPIAbstractBackend.AwaitJobLive` fetches `SaveImage` / `PreviewImage` outputs through `/history` + `/view` after the task ends, and reference images fall back to `/upload/image`, so native workflows are not expected to need the SwarmUI companion nodes; the standard workspace mode does. To be tested in an environment with ComfyUI |
| V2 | Whether `/api/userdata` and the workflow file format are consistent across ComfyUI versions | To verify |
| V3 | How subgraph blueprints are read on LAN servers | To verify |
| A1 | Assume all execution servers and the platform server are on the same LAN and can reach each other directly | Assumption |
| A2 | Assume model files are installed on each server manually by the administrator, and the platform never copies models between servers | Assumption |
| V4 | Whether the platform can write to a NAS through a UNC path when running as a Windows service or background process; whether writing many small files to the NAS is fast enough for gallery syncing | To verify |
| A3 | Assume shared storage is authenticated or mounted by the operating system, and the platform reads and writes it as an ordinary directory | Assumption |

## 10. Development Progress

| Step | Status | Notes |
|---|---|---|
| 0 Technical validation | Partially done | No ComfyUI in the development environment yet, so static analysis was done first (see V1); real tests pending |
| 1 Server registration and execution limits | Code complete, awaiting build and real tests | Added `Studio/ExecutionServers.cs`; renamed `ValidateLocalBackend` to `ValidateExecutionBackend`; updated AGENTS.md, the READMEs and the workflow documentation |
| 2 Workflow discovery | Code complete, awaiting build and real tests | Added `Studio/WorkflowDiscovery.cs`; local directories and LAN HTTP reads both run in the background |
| 3 Review and snapshots | Code complete, awaiting build and real tests | Added `Studio/WorkflowReview.cs` (review records and snapshots), `Studio/WorkflowReviewApi.cs` (review API) and the workspace "Workflows" view |
| 4 Calling and scheduling | Not started | |
| 5 Queue, failures, gallery | Partially done | The queue already shows server display names, queue counts and congestion status |
| 6 Multi-user configuration and documentation | Not started | |
| 7 Work storage | Not started | |

Implementation notes for steps 1–2:

- An execution server's display name is the backend Title; if none is set it is `Local #N` / `Server #N` (shown in Chinese in the current UI).
- Address restriction: only loopback addresses, private-range IPs and LAN host names (no dot, or ending in `.local`, `.lan`, `.home.arpa` or `.internal`) are accepted; `ComfyUI API By URL` backends with other addresses are never used by the workspace.
- The node graph of the standard workspace mode (no workflow selected) is generated by SwarmUI and depends on the SwarmUI companion nodes, so it is assigned only to the local self-starting backend or to servers with nodes such as `SwarmKSampler` installed; native workflows are assigned by their own node and model dependencies; manually published SwarmUI workflows require servers to have all of their node types.
- The pre-submission server check changed from "an idle server exists" to "an online server exists"; when all servers are busy, tasks wait in the SwarmUI queue instead of being rejected. The `local_backends_observed` field now means the number of online servers; the field name is kept for front-end compatibility.
- This phase still uses SwarmUI's original assignment logic (choose the least-occupied runnable server), which does not yet count tasks others submit to ComfyUI directly; full Rule 1 is implemented in step 4.
- A background task started by the extension refreshes the discovery cache about every 30 seconds, even when nobody has a page open, so newly saved or changed workflows reach the review list without a manual refresh; page requests also trigger a refresh when the cache is stale.
- Workflow IDs are prefixed with the execution server's display name. Set the server name (backend Title) when registering it and do not change it after review: renaming it, or a changing backend number when no name is set, sends every workflow on that server back to review under a new ID, while published snapshots under the old ID keep working but show "source missing". Stable IDs that do not depend on the name are handled in step 4.

Implementation notes for step 3:

- New permissions `ecomcraft_submit_workflows` (default: ordinary users) and `ecomcraft_review_workflows` (default: administrator). `ecomcraft_edit_workflows`, `ecomcraft_manage_servers` and `ecomcraft_share_results` will be registered when needed; for now workflow content is edited in ComfyUI and server registration uses SwarmUI backend management.
- Discovered workflows are only review candidates and no longer go straight into the user's selection list. Approval saves a snapshot (source canvas, converted execution graph, parameter bindings at discovery, reviewer's parameter configuration, content fingerprint, origin server, reviewer, review time), and users call the snapshot. Review records are saved in `Data/EcomCraft/workflow-reviews.json` and survive platform restarts; if the file is damaged it is renamed to `.damaged-<time>` and records start empty; if the file cannot be read, saving is refused so it is never overwritten.
- The content fingerprint covers the converted execution graph, parameter bindings and values saved in the workflow, but not the model options published by each server, so it changes only when the workflow itself changes; installing new models does not trigger re-review.
- States: Pending, Published, Pending re-review (published but the source changed; users keep the old snapshot), Rejected (returns to Pending when the source changes again or it is resubmitted), Unpublished (stays unpublished until approved again), Cannot import (shows the reason; cannot be submitted or published). When a new version of a published workflow is rejected, it still shows as Published and keeps the rejection reason.
- For each parameter, reviewers can set visibility, display name, default value, range (only narrowed within the workflow's original range) and dropdown options (a subset of the original options). Creative description, negative content, width and height are fixed page fields and cannot be hidden. Hidden parameters always use the snapshot's default; a hidden seed keeps its fixed value, and `-1` means random each time. Published workflows can have only their parameter configuration changed; if the source is temporarily unavailable, changes are based on the snapshot.
- The snapshot keeps the source canvas, so accounts with SwarmUI workflow read permission can still open it in the native ComfyUI editor; review and submission APIs never return the canvas.
- For hidden model parameters, the snapshot's model is always used; if the current account is not allowed to use that model, the workflow does not appear in its selection list.
- When two reviewers handle the same workflow at once, whoever saves second is told "the status was just changed by someone else" and nothing is overwritten.
- Rejection reasons are returned only to the submitter and reviewers; the submission list does not return other people's submitters or notes. The review API returns parameter definitions and missing node / model names, never raw node graphs or server addresses.
- Dropdown and model parameter options are frozen at approval; R4.3's requirement to list "only models present on at least one runnable server" is handled in step 4.
- Upgrade note: native workflows that were discovered automatically and directly usable before the upgrade must be approved before they reappear in user lists. Workflows enabled with **Enable in Simple** in the SwarmUI workflow editor are unaffected.
- The content fingerprint is computed from the converted execution graph. A future upgrade of the workflow conversion logic may show every published workflow as "needs re-review"; until re-approved, users keep using the old snapshot and generation is unaffected.

## 11. Change Log

| Date | Version | Description |
|---|---|---|
| 2026-10-26 | v1.0 | First version, compiled from requirement discussions |
| 2026-10-26 | v1.1 | Completed code for steps 1–2; added V1 static analysis findings, server limits for the standard workspace mode and development progress |
| 2026-10-26 | v1.2 | Fix: workflow IDs changed from "server / path" to "server/path" cleaned with `StrictFilenameClean`. The old format was renamed by SwarmUI at generation time, so the workflow could not be found and generation failed |
| 2026-10-27 | v1.3 | Completed step 3 code: review permissions, review records and snapshots, parameter configuration, runnable-server check and the workspace "Workflows" view; added implementation and upgrade notes |
| 2026-10-27 | v1.4 | D9 changed to "visible only to the owner within the platform; storage layer not isolated"; D11 changed to reserve cloud capability without developing it in this phase; added D12 and R11 work storage (local / NAS / shared folder, write locally then sync); R10 details the executor abstraction and three-stage generation flow; added step 7, V4 and A3; added this English version |
| 2026-10-27 | v1.5 | Static review of steps 1–3 (T33): workflow discovery now refreshes on a background timer started by the extension instead of only when a page is opened; added notes on server names and workflow IDs, and on re-review after conversion logic upgrades |
