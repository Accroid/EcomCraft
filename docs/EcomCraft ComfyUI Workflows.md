# Calling ComfyUI Workflows in EcomCraft

> **Language:** English · [简体中文](EcomCraft%20ComfyUI%20工作流.zh-CN.md)

EcomCraft never exposes arbitrary ComfyUI JSON to ordinary clients. ComfyUI workflows saved on execution servers are discovered automatically and become available to users only after review (see "Review and publishing" below). Administrators can also open an existing workflow in SwarmUI's ComfyUI workflow editor, confirm its dependencies and parameters, enable **Enable in Simple** and save it; such workflows do not go through EcomCraft review.

## Web page

1. Make sure at least one execution server (the local `ComfyUI Self-Starting` backend, or a registered LAN ComfyUI, see "LAN execution servers" below) has the status `RUNNING`.
2. Open EcomCraft Studio. While the page is visible and not generating, the workflow list refreshes every 30 seconds; you can also click "Refresh" next to "Creative workflow" to update it immediately. The catalog also refreshes when an execution server becomes available again.
3. Choose a published workflow in the "Creative workflow" drop-down (an approved native workflow, or a workflow an administrator enabled for the simple workspace in SwarmUI).
4. Fill in the inputs the workflow shows and the standard prompt, then click Generate.

The workflow drop-down shows only workflows that meet all of these conditions: the current account may use them, the workflow has been approved and published (or enabled for the simple workspace in SwarmUI), and its JSON and parameter definitions are valid. If a reviewer hid a model parameter, the current account must also be allowed to use that model. Image and video workflows are listed separately by type.

Besides workflows published by administrators, EcomCraft reuses the local ComfyUI installation discovery to scan `workflows`, `blueprints`, `user/default/workflows` and `user/default/blueprints` in configured backends and other verified local installations. A configured backend's `--user-directory` override still applies. Installation scan results are cached for 60 seconds, so a newly added installation appears at the first catalog refresh after the cache expires; newly saved files in an already discovered installation appear at the next refresh. You do not need to enter file paths one by one; `Settings.EcomCraft.ComfyWorkflowSearchPaths` only adds non-standard directories and does not turn off automatic discovery. Registered LAN execution servers are read through the ComfyUI HTTP API, which lists the workflows saved in their `user/default/workflows`.

Workflow discovery runs in the background and refreshes about every 30 seconds; catalog and API requests never scan synchronously. The first request after the service starts waits at most 5 seconds for the first results. If a source temporarily cannot be read, its previous results are kept.

## Review and publishing

Automatically discovered native workflows are not available to users directly; they must be reviewed first:

1. Discovered workflows enter "Pending review". Accounts with the `ecomcraft_submit_workflows` permission (ordinary users by default) can choose a workflow they saved on the workspace "Workflows" page, add a note and click "Submit for review", and can see its status and any rejection reason.
2. Accounts with the `ecomcraft_review_workflows` permission (administrators by default) open the workflow on the "Workflow review" page, check which servers can run it (for example "Only A (B: missing node xxx)"), decide for each parameter whether it is shown and set its display name, default value, range and drop-down options, then click "Approve and publish", or "Reject" with a reason. Published workflows can be "Unpublished".
3. Approval saves a snapshot, and users run the snapshot. If the source file is later changed in ComfyUI, the status becomes "Needs re-review", and users keep using the old snapshot until the new version is approved. If the source server goes offline or the file is deleted, the published snapshot is unaffected.
4. Workflows that fail structural validation show "Cannot import" with the reason and cannot be submitted or published.

Hidden parameters do not appear in the user form and always use the default value from the snapshot. Creative description, negative content, width and height are always shown. Review records are saved on the server in `Data/EcomCraft/workflow-reviews.json`. Native workflows that were discovered and directly usable before the upgrade must be approved after the upgrade before they reappear in user lists.

Both the old array-style and the new object-style links are supported, as are embedded subgraph blueprints containing positive and negative prompt encoder nodes (for example the Qwen-Image text-to-image blueprint and Qwen 2.1 workflows using `TextEncodeQwenImage21` and `UnetLoaderGGUF`). The positive and negative conditioning outputs of `TextEncodeQwenImage21` are bound to `prompt` and `negative_prompt` respectively; the two text boxes of the same encoder node are not merged. `SaveImageAdvanced` is rewired to the workspace image output so the original author's output path is not reused. A blueprint that has only an image output socket and no save node gets an image output node added on the server; subgraph instances are expanded separately, and connected controls such as steps and CFG keep their connections. Invalid links, cycles, unrecognized prompt inputs, unsupported bypassed nodes and administrator default image inputs are never published as executable workflows. Discovery never sends the raw node graph or internal file paths to clients.

After you choose an automatically discovered native workflow, the page reads the saved positive and negative prompts, size, steps, CFG, seed, sampler and scheduler; model loaders and denoise parameters appear in the workflow input area. Changes are written back to the corresponding nodes according to the server-side bindings, and multiple nodes keep their own independent parameters. Connected known integer/decimal primitive nodes can have their values edited while the connection itself stays unchanged. Catalog refreshes keep unsubmitted changes. The positive and negative prompts are filled with defaults only the first time you enter a native workflow while the text boxes are empty; afterwards, switching models or workflows or returning to the standard workspace keeps the current prompts (including content you cleared on purpose). Choosing a workflow again reads the defaults of the other parameters, and returning to the standard workspace restores the other standard parameters you had before.

In the page connection status, "ComfyUI execution servers" is the number of online execution servers (busy servers count too, since tasks queue for them), and "Standard workspace models" is the number of image/video models in SwarmUI's global list available to the current account. It is not the number of cloud models and does not say whether workflow dependencies are complete.

Native workflows use their model file names directly; SwarmUI's global image model list does not need a model with the same name. The model drop-down lists only models the current account may use and that exist on at least one online execution server. On submission the scheduler checks node and model dependencies server by server; when no server can run the task it returns `no_execution_server` and lists what each server lacks (see "Execution server scheduling" below). The standard workspace and manually published legacy templates still use the original model selection logic; a legacy template with no model available for the current type, or whose model input only offers `(None)`, does not appear in the list.

The seed reads the saved value and supports values up to the JavaScript safe integer limit `9007199254740991`; enter `-1` to randomize on submission. When generating several images at once, the submitted seed is the starting point and increases by 1 per image, synchronized to the actual sampler nodes; other independent seed nodes change by the same increment, continuing from 0 after the limit. With `-1`, a random starting value is chosen first and then incremented per image. The randomize/increment/decrement front-end buttons of the ComfyUI canvas are not executed. The image count is read from the saved batch_size, still limited to 1–4, and each image is scheduled separately by SwarmUI so batch sizes are not multiplied twice.

The generation API normally returns `images`, an array of image URLs. Submitting again with a fixed starting seed and the same parameters reproduces the same set of images and may hit the ComfyUI cache; change the seed or enter `-1` when you want a new set.

When a local backend is located at `ComfyUI-Installs/<install name>/ComfyUI`, the node pack loaded with SwarmUI automatically adds the sibling `ComfyUI-Shared/models` under the same root to ComfyUI's search paths. Standard subfolders such as checkpoints, VAE, LoRA, text encoders and diffusion models are supported; existing path priorities are kept, models are not copied, user configuration files are not modified, and no custom code is loaded from the shared folder. Only the shared folder of the associated installation is used; other drives are not scanned and backends are not switched. The change takes effect after that local backend restarts; backends that disabled SwarmUI's built-in node pack must configure model paths themselves.

If a model file is in the shared folder but you get `ckpt_name not available`, first check whether the backend has loaded the shared folder; there is no need to download the same model again. Shared models are available to native workflows but are not added automatically to the global model list of SwarmUI's standard workspace.

Workflows found in other installations are only imported as templates; that installation is not started or switched to. The server a workflow was discovered on only indicates its source; the actual generation is assigned by the scheduler to an execution server that has the required nodes, models, text encoders and VAE (see "Execution server scheduling" below). A native workflow appears in the list only when at least one online execution server has every node and model its saved configuration needs; dependencies of several servers are never combined. While no server is ready or dependencies are missing, it is hidden, and later refreshes check again. This check does not mean the model combination has been verified or that GPU generation has succeeded. Leaving the prompt empty by default does not filter a workflow out.

A native workflow's ID is `source/relative path`, and the workspace drop-down groups workflows by source. The source is the name of the execution server it belongs to (the backend Title, or `本机 #N` / `服务器 #N` when none is set); local installations that do not belong to a registered server use the installation folder name. IDs are cleaned with SwarmUI's workflow file name rules: characters such as `.`, `#`, `&` and `%` and spaces around `/` are removed, and `/` inside the server name becomes `-`. This keeps the ID unchanged when SwarmUI cleans the name again at generation time; otherwise the workflow could not be found. Native entries with the same execution graph, parameter definitions and defaults are shown only once, keeping the one that sorts first by name; deduplication never deletes or rewrites the original files. On name conflicts an 8-character short hash computed from the source path is appended, for example `(1A2B3C4D)`. If a workflow still does not appear after a refresh, make sure the file is saved in one of the directories above, the selected image/video type is correct, and the workflow meets the import conditions; merely opening the editor canvas or starting a backend does not mean the workflow has been saved and validated.

The workspace "Task queue" page shows, per execution server, the running tasks, waiting tasks, current node and node progress; waiting tasks include tasks the platform has assigned to that server but not yet handed to ComfyUI. A server running a task with nothing waiting is marked "Working" (yellow light), and a server with a queue is marked "Congested" (red light). Accounts with generation permission can end active tasks they started; queue information is kept only while the service runs, and no persistent history is provided.

## Execution server scheduling

On each submission the platform chooses one execution server by the following rules, and all images of one submission run on that server:

1. Only servers that are online and have every node and model the task needs are considered. In the standard workspace mode SwarmUI builds the node graph, so LAN servers take part only if they have the SwarmUI ComfyUI nodes installed; published native workflows are judged by their own dependencies.
2. Among those servers, the one with the shortest queue is chosen; on a tie, the one that finished a task longest ago. Queue = tasks running and waiting in that server's ComfyUI (including tasks others submitted directly to that ComfyUI) + tasks the platform has assigned to it but not yet handed to ComfyUI. The latter is an estimate and may be undercounted for a few seconds while a task is being posted to ComfyUI.
3. With two or more execution servers, the creation panel shows an "Execution server" drop-down, defaulting to "Automatic (recommended)". If you choose a server, the task queues only on that server and is never reassigned; if that server is offline or lacks dependencies, you are told directly and the task is not submitted. If the chosen server has a queue while another capable server is idle, a hint suggesting removing the restriction appears after submission.
4. When no server can run the task, it is not submitted and the reason is given: all servers are offline, or which nodes and models each online server lacks (at most 3 per server).
5. After a successful submission the status bar shows the name of the server that runs the task; the name is also written into the work's metadata, and the gallery shows "workflow｜execution server" under each work's time (works from the standard workspace show "标准工作台", standard workspace).
6. When the executing server drops off or a task fails, the platform never retries automatically. The workspace shows the reason and a "Reassign to another server" button; clicking it submits the same request again without the server restriction, and the rules above choose again. The request is kept only in page memory, so after a reload only the reason is shown; uncertain results are never offered for reassignment, so check in the native engine first to avoid duplicate generation.

Scheduling only happens among registered execution servers and never moves to unregistered addresses, remote SwarmUI instances or cloud services. The scheduling layer is built on an "executor" abstraction that reserves two types, cloud ComfyUI and cloud model APIs; they are not implemented in this phase and no request is ever sent to them.

## E-commerce scene templates

A template wraps a published workflow snapshot as an e-commerce scene that shop staff can use directly, such as "White background main image" or "Lifestyle scene". A template has no node graph of its own; it references a published snapshot and uses that snapshot's parameter allowlist:

```json
{
  "id": "white-main",
  "name": { "zh-CN": "白底主图", "en": "White background main image" },
  "description": { "zh-CN": "纯白背景，适合平台主图", "en": "Pure white background for main images" },
  "example_image": "white-main.webp",
  "platforms": ["amazon", "taobao"],
  "sets": ["main-set"],
  "prompt": { "prefix": "product photo,", "suffix": "soft studio light", "negative": "text, watermark" },
  "categories": [],
  "brand_kit": null,
  "execution_plans": [
    { "kind": "local", "workflow": "B/white-main", "presets": { "parameterId": 30 }, "user_fields": ["parameterId"] }
  ],
  "enabled": true
}
```

- `id`: lowercase letters, digits and hyphens; `name` needs both Chinese and English, `description` is optional.
- `platforms`: IDs of the platforms the template suits, or `"*"` for any; `sets`: IDs of the image sets it belongs to, used by one-click sets.
- `prompt`: prompt fragments added before and after the user's description, and a negative prompt; `<…>` directives and `__wildcards__` are not allowed.
- `execution_plans`: in this phase every template needs exactly one local plan (`kind` is `local`), whose `workflow` is the ID of a published snapshot. `presets` fixes snapshot parameter values and cannot preset the prompt or an image; `user_fields` are the parameters users may change, which must be visible after review and not preset. Cloud plans (`cloud_comfy`, `cloud_model_api`) are reserved and rejected in this phase.
- `categories` (category presets) and `brand_kit` (brand kits) are reserved; `brand_kit` must be `null` in this phase.

Built-in templates ship with each release in the extension folder `Templates/builtin/` (one template per file, example images in the same folder); administrator templates are stored in `Data/EcomCraft/templates.json` with example images in `Data/EcomCraft/template-images/`. The two are kept apart, so upgrades never overwrite custom templates, and a custom template cannot reuse a built-in ID.

Invalid templates are never offered to users; the reasons are written to the server log and returned by the management APIs. A template also shows as unavailable when its snapshot is unpublished, a parameter is hidden, or a preset is out of range. The management APIs require the review permission (`ecomcraft_review_workflows`): `EcomCraftTemplateAdminList` lists every template with the reasons it cannot run, `EcomCraftTemplateSave` adds or replaces an administrator template (`template` is the template JSON; optional `example_image_data` is a PNG, JPEG or WebP image of at most 2 MB), and `EcomCraftTemplateDelete` removes an administrator template.

## LAN execution servers

A LAN device with only ComfyUI installed can be registered as an execution server and called by the platform server. For account login, firewalls and shared storage when several people use the platform, see the [LAN Deployment Guide](EcomCraft%20LAN%20Deployment.md). For the development plan and upcoming features, see [LAN Multi-Server Platform Requirements](EcomCraft%20LAN%20Multi-Server%20Platform%20Requirements.md).

1. Start ComfyUI on the LAN device with `--listen 0.0.0.0` (you can also choose the port with `--port`); otherwise ComfyUI only accepts local connections.
2. In that device's firewall, allow only the platform server to reach the ComfyUI port. ComfyUI has no login of its own; do not open the port to the whole LAN or the internet.
3. On the platform server, add a `ComfyUI API By URL` backend under **Server → Backends** in the native engine, enter its address (for example `http://192.168.1.21:8188`), and enter a display name (for example "B") as the backend title. Ordinary users see only the display name, never the address. Set the name before reviewing its workflows and do not change it afterwards: workflow IDs start with the server name.
4. After saving, wait until the backend status becomes `RUNNING`. Within about 30 seconds, the workflows saved in that device's ComfyUI appear in the list.

Only the following addresses are treated as execution servers: loopback addresses, private-range IPs (`10.x`, `172.16–31.x`, `192.168.x`, `169.254.x`, IPv6 unique-local / link-local addresses), and host names without a dot or ending in `.local`, `.lan`, `.home.arpa` or `.internal`. Any other address (including public domain names) is never used by the workspace even if registered as a backend. Other backend types such as remote SwarmUI are not used either.

After a task finishes, the platform server fetches the results through ComfyUI's `/history` and `/view` endpoints, saves them to the current user's output folder and shows them in the gallery; reference images are uploaded to the execution server first. This return path has not yet been verified on a real LAN device.

## Client API

Create a SwarmUI session first, then call `POST /API/EcomCraftStudioGenerate`. Submit only the workflow ID and published parameters, never raw ComfyUI JSON:

```json
{
  "session_id": "current session ID",
  "source": "local",
  "kind": "image",
  "mode": "text2img",
  "workflow_id": "Examples/my_product_workflow",
  "workflow_params": {
    "product_style": "clean studio",
    "reference_image": "data:image/png;base64,..."
  },
  "model": "official model name",
  "prompt": "a bottle of skincare product, commercial product photography",
  "negativeprompt": "blurry, low resolution",
  "width": 1024,
  "height": 1024,
  "images": 1
}
```

Optional fields include `seed`, `steps`, `cfgscale`, `sampler` and `scheduler`. Video workflows use `kind: "video"` and submit `text2videoframes` as the workspace requires; each batch can generate only one video.

`execution_server_id` (optional, integer) restricts the task to one execution server; its value is `backends[].id` from the queue API. Omit it for automatic assignment. The server only accepts IDs of registered execution servers; SwarmUI's native backend selector parameters (such as `exactbackendid`) are still rejected. With `async: true` the call returns `queued` and `task_id` right after submission. Every successful response includes `execution_server` (the display name of the server that runs the task, never its address), plus `server_hint` when the chosen server is congested while another is idle. Scheduling error codes: `invalid_execution_server` (the ID is not a registered execution server), `execution_server_unavailable` (the chosen server is offline or lacks dependencies; the task was not reassigned) and `no_execution_server` (no server can run the task; `error` explains why). Exceeding the `MaxQueued` limit of the account's role returns `queue_limit` and the task is not submitted (see the [LAN Deployment Guide](EcomCraft%20LAN%20Deployment.md#4-per-person-queue-limit-r92)).

Before calling, you can read `POST /API/EcomCraftStudioCatalog`; the workflow list is in the `workflows` field of the result, where `inputs` describes each fillable parameter's ID, type, default value and allowed options. Clients should build `workflow_params` from this list and must not guess parameter names. The catalog also returns `can_review_workflows` and `can_submit_workflows`, which say whether the current account can review or submit workflows. The review APIs are `EcomCraftStudioReviewList`, `EcomCraftStudioReviewDetail` and `EcomCraftStudioReviewDecide` (`action` is `approve`, `reject` or `unpublish`); the submission APIs are `EcomCraftStudioWorkflowSubmissions` and `EcomCraftStudioSubmitWorkflow`. They return only parameter definitions, states and the names of missing nodes or models, never the raw node graph or server addresses. Review error codes include `invalid_review` (invalid configuration), `workflow_state` (the current state does not allow the action, or someone else just changed it) and `review_save_failed` (the record could not be saved). Read `POST /API/EcomCraftStudioQueue` for each execution server's queue (`backends[]` contains `id`, `name`, `status`, `running`, `pending`, `assigned` (tasks the platform assigned but has not yet handed to ComfyUI), `queue` (the queue used for scheduling), `load` (`idle`, `working` or `congested`; `null` when the queue cannot be read) and `busy` (same as `load` being `congested`), and no address) and the current generation tasks, plus `finished_tasks` (the current account's background tasks that ended in the last 15 minutes: `id`, `state` as `done` or `failed`, `error`, `error_id` and `execution_server`, never other accounts' tasks); to end a generation task of the current account, call `POST /API/EcomCraftStudioCancelTask` with the task `id` returned by the queue.

Read `POST /API/EcomCraftStudioTemplates` (optional `language`: `zh-CN` or `en`) for the e-commerce scene templates the current account can run: `templates[]` contains only `id`, `name`, `description`, `example_image` (URL of the template's example image), `kind`, `platforms`, `sets` and `fields` (the parameters users may change, in the same format as a workflow's `inputs`); templates that cannot run are listed under `hidden[]` with `reason` `invalid` (invalid template configuration) or `unavailable` (no online server can run it, or the account may not use its models), and reviewers also get a `detail` explanation. To generate from a template, send `template_id` and optional `template_params` (parameter ID to value, only the template's `fields`) to `EcomCraftStudioGenerate`, along with ordinary fields such as `prompt`, `negativeprompt` and `images`; the server resolves the template into a published snapshot, preset parameters and prompt fragments, then checks them against the snapshot allowlist, ignoring any `workflow_id` and `workflow_params` from the client. Unavailable templates return `invalid_template` or `no_execution_server`, and the work's metadata records `ecomcraft_template`.

Prompt service: `EcomCraftStudioStyles` (optional `language`) returns the visual styles users can pick (`id`, `name`, `description`); `EcomCraftStudioComposePrompt` (`style_id`, `description`, optional `product_image`) turns a style and a one-line description (Chinese or English) into an English prompt with the configured prompt enhancement model, returning `prompt` and `source` (`model` or `template`); when the model is unavailable or does not answer within 45 seconds, the style defaults are used and `notice` and `notice_code` are returned (`description_used` when the English description was used as is, `description_unused` when a Chinese description could not be used this time). When generating from a template you may also omit `prompt` and send `description` and `style_id` instead; the server builds the prompt and the response carries `prompt_notice` / `prompt_notice_code`. The model's answer only ever becomes prompt text: the server removes native directives, wildcards, quotes and extra paragraphs, so it cannot affect the workflow, parameters or execution server. Sending product photos to a vision model to describe the product is off by default; only administrators with the server-settings permission can turn it on through `EcomCraftPromptSettings` (`vision_enabled` `true` / `false`), which also returns the provider (`vision_provider`) and model the photos would be sent to.

Native imported entries additionally return `native: true` and `defaults`. `inputs[].form` marks parameters bound to standard page fields (such as `width` and `seed`); other inputs are submitted through `workflow_params`. When a standard field is omitted, the corresponding published parameter or saved default is used; when both are given, the standard field wins. Native entries do not need a root-level `model`; model changes are submitted through the ID of the model drop-down in `inputs`, and the server accepts only the published options. Parameter IDs are opaque identifiers; clients must not construct them.

## Security boundaries

- The workspace uses only registered execution servers (the local self-starting ComfyUI and `ComfyUI API By URL` backends with LAN addresses) and never switches to unregistered addresses, remote SwarmUI or cloud services because an execution server is unavailable.
- The server re-checks whether the workflow is published, whether parameters are on the allowlist, and whether models and images are within what the current account may use.
- Default images in published workflows are never reused automatically; each client must upload its own image parameters.
- To edit the raw node graph, use unpublished workflows or debug nodes, use the ComfyUI workflow page of native SwarmUI and grant the corresponding administrator permissions.

## Startup fails after changing the local backend

If the log shows `huggingface-hub>=1.5.0,<2.0 ... found huggingface-hub==2.3.0`, this is a Python package version conflict. When the launcher updates diffusers, it passes the installed transformers and tokenizers versions to pip's resolver as well, so dependency upgrades do not exceed their upper version bounds. For an existing conflict, repair huggingface-hub in the virtual environment that backend uses, according to the common dependency range of the installed packages; do not upgrade the whole environment or change PyTorch/ROCm. After repairing, first verify `from transformers import CLIPTokenizer`, then restart the backend.

## Encoder errors during consecutive generations on Windows AMD

If the first image finishes and the next task fails in `TextEncodeQwenImage21` with `hipErrorInvalidValue`, and the background call stack is in `cast_to` / `copy_` of quantized weights, check the pinned memory compatibility of Windows ROCm rather than only whether the model files exist. In a two-round load verification of the Qwen quantized encoder, disabling only asynchronous offloading still failed, while disabling pinned memory made both positive and empty negative encoding pass. This project appends `--disable-pinned-memory` to the automatic startup arguments of local Windows AMD backends to avoid this pinned memory copy path; asynchronous offloading stays enabled, and transfer performance may vary by model and device. Administrators who enable `DisableInternalArgs` must set this argument themselves. The change takes effect after restarting the backend once the queue is idle; failed tasks are never retried automatically, and earlier successful results can be found in the personal history.
