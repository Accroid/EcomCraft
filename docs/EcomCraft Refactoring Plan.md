# EcomCraft Refactoring Plan

> **Version:** v1.0 · **Date:** 2026-10-31 · **Baseline commit for the code review:** `2a7e84ec` (`dev` branch)
>
> **Executor:** the development AI, executing strictly per this document, which is self-contained. Companion file: [EcomCraft Implementation Plan](EcomCraft%20Implementation%20Plan.md) (the new-feature plan; Chinese original [EcomCraft 实施计划](EcomCraft%20实施计划.zh-CN.md), in the same folder).
>
> **Line numbers in this document refer to the baseline commit** and will drift as the code changes. When moving code, rely on class names, method names and block comments.

---

## 1. Refactoring policy (decided, do not change)

**This is an incremental, behavior-preserving refactoring, not a rewrite.** The project already has about 38,000 lines of extension code and many business rules; a rewrite is far riskier than it is worth, and the features have not passed acceptance.

| Do | Do not |
|---|---|
| Split large files and long methods | Rewrite, or introduce React, TypeScript, bundlers, npm dependencies |
| Merge truly duplicated utility code | Change API names, requests or response fields (except R08's `raw_prompt`) |
| Fix the defects confirmed in section 2 | Change the format of anything under `Data/` |
| Build tests and a CI safety net | Change element IDs, CSS visuals, UI wording |
| Reserve integration points for new features | Delete or hide features under the cover of refactoring, or implement new features from the Implementation Plan |
| | Refactor `MagicPrompt/` (third-party-origin code, see 2.3) |
| | Bulk-change `===` to `==` (type coercion may change behavior) |
| | Port core changes upstream or massively move core files |

## 2. Code-review findings

### 2.1 Confirmed defects (fixed by R04–R08)

| Severity | Problem | Location | Consequence |
|---|---|---|---|
| **High** | Index files start from empty data when a read fails, and the next modification overwrites the original file. `ProjectLibrary` and `WorkflowReview` have protection; the others do not | `Products/ProductLibrary.cs:33-70`; `Brand/BrandKitLibrary.cs` has the same comment, verify it | If a file is temporarily unreadable (locked or corrupt), saving one product loses the whole product index |
| **Medium** | Backup restore paths can escape. `BackupRules.Validate` checks the whole name only, while `DestinationFor` strips the prefix and then calls `Path.Combine`; names like `ecomcraft/C:/x` or `ecomcraft//x` can write outside the data folder | `BackupService.cs:183-190` | A malicious backup package can write anywhere (only administrators can restore) |
| **Medium** | Batch image data is cleaned up only on the next `StartSet`, with no periodic cleanup | `Generation/BatchService.cs:173, 743` | Product and reference images can stay in memory beyond the documented 6 hours |
| **Medium** | `raw_prompt` is an internal marker but the public API accepts it, letting clients bypass template prompts. The front end does not send it (verified) | `Templates/TemplateApi.cs:312` | Internal marker exposed; new features need more internal parameters |

> These come from reading the code, not from reproducing them. Fixes must start with a failing test.

### 2.2 Structural problems

| Problem | Data | Handling |
|---|---|---|
| `studio.js` is one closure containing all views | 6,088 lines; cannot be loaded or tested individually | R17–R19 |
| `simple.js` is a single class | 3,712 lines, 158 methods, `attach` is 230 lines | R15–R16 |
| `StudioApi.cs` has too many responsibilities | 2,106 lines mixing APIs, validation, queue and progress | R07 |
| Overlong methods | `EcomCraftStudioGenerate` ~308 lines, `StartSet` ~308 lines, `GeneratePublishedWorkflow` ~244 lines | R09, R10 |
| `BatchService` calls the public API through a `JObject` | `BatchService.cs:622` | R08 |
| Post-generation processing is hard-coded inside the API method | `StudioApi.cs:772-795` | R09 (T86 integration point) |
| The blueprint is hard-coded to one file | `StandardWorkflows.Blueprint()` | R13 (T84 integration point) |
| Duplicated atomic writes | 10 places with `{file}.tmp` + `File.Move` | R04 |
| Duplicated image data-URL parsing | At least 8 places, with differing formats and limits | R12 |
| Cluttered `Studio/` root | Workflows, backup, diagnostics mixed together | R14 |
| Many core changes | Excluding the extension and `docs/`, 92 files differ from upstream (+4561 / −2042); `Installation.cs` +402 lines | R21 |

### 2.3 Standards and engineering problems

| Problem | Data | Handling |
|---|---|---|
| `studio.js` violates the JS rules | `const` 165×, `forEach` 4×, `} else` 10×, `===` 146× | R20 (no bulk `===` change) |
| `{ get; set; }` in `MagicPrompt/` | 136 places, concentrated in third-party-origin model classes, some serialization-dependent | **Exempt**; record in AGENTS.md |
| CI does not cover the extension | `build-and-check.yml` builds `SwarmUI.sln` while the main project excludes `Extensions/**`; no extension unit tests or front-end tests; Ubuntu only | R01 |
| The local machine cannot compile | No `dotnet` | R00 |
| Front-end tests bind file names | Several tests read `studio.js`, `simple.js` directly | R02 |
| Acceptance backlog | 79 tasks in the tracker: 33 complete, 23 in progress, 19 not started, 3 deferred, 1 pending acceptance | The user accepts at checkpoints |
| Inconsistent document naming | `Prompt Enhancement.md` is Chinese, `.en.md` is English — the opposite of every other document | R22 |
| AGENTS.md outdated | Says "the repository has no automated test projects"; there are actually 30 test files | R23 |
| Leftovers | Merged branch `wt/t56` and worktree `.claude/worktrees/t56`; `.claude/` not ignored | R22 |

### 2.4 Baseline

- **Front-end tests:** 50, all passing (actually run for this review, Node 24).
- **C# unit tests:** no SDK locally, not run; count and results unknown, to be confirmed by R00.
- **Main flows:** not run; to be confirmed by R03's regression checklist.

## 3. Execution rules (must follow)

1. **Strictly follow the order in section 4, one R task per session.** Do not start a task whose dependencies are unfinished. **The very first thing to do is the Implementation Plan's T81, putting the goals into the repository. Before starting each later task, re-read sections 1 and 2 of `docs/EcomCraft Architecture.zh-CN.md` and this task's section to confirm no drift from the goal.**
2. **Do only what the task says.** Other findings go into the completion report; do not fix them in passing.
3. **Stop immediately and report — do not improvise — when:**
   - This document's premises do not match the code (a method does not exist, dependencies point the other way, etc.).
   - Completing the task would change user-visible behavior (except the section 2.1 fixes).
   - A check fails for unknown reasons, or two fix attempts still fail.
   - Software must be installed, code pushed, or branches deleted without the user's agreement.
4. **Commit pure moves and logic changes separately.** A move commit changes no logic and no formatting. Self-check with `git diff --color-moved=dimmed-zebra`.
5. **Branches and commits:** `dev` only. At least one commit per task, message format `refactor(R07): Chinese description` (defect fixes use `fix(R04): …`). Before committing run `git status --short`, `git diff --stat`, `git diff --check`. **Pushing needs the user's agreement.** Do not commit `.claude/`, `pic/`, build outputs.
6. **Follow `AGENTS.md`:** do not touch `Data/`, `Output/`, `Models/`, `dlbackend/`; do not edit `docs/APIRoutes/`; no `var` in C#, `///` on fields; `let` only in JS, `/** */` on functions.
7. **Report honestly:** write "not run" for checks not run. A task counts as complete only after the user verifies it.
8. **After front-end changes** update every `?v=` in `index.html`; **after C# changes** rebuild the extension per `.agents/skills/ecomcraft-build-run`.

### Gates per task

| Change type | Must pass |
|---|---|
| All tasks | `git diff --check` |
| Front end | `node --test src/Extensions/EcomCraft/Tests/*.test.cjs`; after R02 also the eslint check |
| C# | `dotnet build src/Extensions/EcomCraft/EcomCraft.csproj -c Debug -o <temp dir>`; `dotnet test src/Extensions/EcomCraft/Tests/EcomCraft.Tests.csproj` |
| If `src/` core changed | also `dotnet build src/SwarmUI.csproj -c Debug -o <temp dir>` (never output to `src/bin/live_release`) |

**A C# task is not complete — and its dependents may not start — until it compiles.**

### Completion-report format

```
Task: R__
Changes: files and why
Checks: commands and results (paste failures verbatim)
Not verified: what was not run and how the user should verify
Behavior changes: none / specifics
Out-of-scope findings: list only, not fixed
```

## 4. Task overview

| Phase | Task | Content | Depends | Size |
|---|---|---|---|---|
| A Safety net | R00 | Build environment and baseline | — | S |
| | R01 | CI covers the extension | R00 | S |
| | R02 | Front-end tests and static checks | — | M |
| | R03 | API snapshot and regression checklist | — | S |
| | **CP0** | **User runs the checklist, records the baseline** | R03 | — |
| B Defect fixes | R04 | Unified file I/O; no overwrite after failed reads | R00 | M |
| | R05 | Backup-restore paths | R00 | S |
| | R06 | Periodic batch-memory release | R00 | S |
| | **CP1** | **User regression** | R04–R06 | — |
| C Back end | R07 | Split `StudioApi` into partials | CP1 | M |
| | R08 | Internal calls; take back `raw_prompt` | R07 | M |
| | R09 | Split `EcomCraftStudioGenerate`; extract post-processing | R08 | L |
| | R10 | Split `BatchService` and `StartSet` | R08 | M |
| | R11 | Split `PromptService` into partials | CP1 | S |
| | R12 | Unify image data-URL parsing | R07, R10, R11 | M |
| | R13 | Standard workflows support multiple blueprints | CP1 | S |
| | R14 | Tidy the `Studio/` folder | R07–R13 | S |
| | R24 | Remove the dormant cloud executor types | R14 | S |
| | **CP2** | **User regression** | R14 | — |
| D Front end | R15 | Split `simple.js` into files | R02, CP0 | L |
| | R16 | Split `SimpleMode.attach` by feature | R15 | S |
| | R17 | `studio.js`: shared layer and standalone views | R02, CP0 | L |
| | R18 | `studio.js`: admin, gallery, queue | R17 | L |
| | R19 | `studio.js`: create page and event binding | R18 | L |
| | R20 | Front-end standards unification | R16, R19 | S |
| | **CP3** | **User regression** | R20 | — |
| E Wrap-up | R21 | Core-change inventory; split `Installation` additions | CP2 | M |
| | R22 | Repository and documentation cleanup | — | S |
| | R23 | Update AGENTS, skills and the tracker | R14, R20 | S |

**Parallelism:** phases B, C and D touch mostly disjoint files and can run in parallel; within a phase, go in order.

## 5. Task details

### Phase A: safety net

#### R00 Build environment and baseline

The local machine has no .NET SDK. **Ask the user which option (do not install on your own):**

1. **(Recommended)** With the user's agreement, install the .NET 8 SDK into the user profile (no administrator rights needed) using Microsoft's official `dotnet-install.ps1`, into `%LOCALAPPDATA%\Microsoft\dotnet`.
2. Do the whole refactoring on a computer that can compile.

Then:

1. Run the gate's C# commands; record test counts and results in the completion report.
2. Run the front-end tests; confirm the 50 pass.
3. If existing tests fail, **do not fix them**; record them as baseline problems for the user.
4. Tag locally `refactor-baseline` (do not push).

**Acceptance:** the extension compiles; baseline numbers are explicit.

#### R01 CI covers the extension

Edit `.github/workflows/build-and-check.yml`, **adding jobs only, without changing existing ones**:

```yaml
    ecomcraft:
        strategy:
            fail-fast: false
            matrix:
                os: [windows-latest, macos-latest]
        runs-on: ${{ matrix.os }}
        steps:
        - uses: actions/checkout@v6
        - uses: actions/setup-dotnet@v5
          with:
            dotnet-version: '8.x'
        - uses: actions/setup-node@v4
          with:
            node-version: '22'
        - run: dotnet build src/Extensions/EcomCraft/EcomCraft.csproj -c Debug
        - run: dotnet test src/Extensions/EcomCraft/Tests/EcomCraft.Tests.csproj
        - run: node --test src/Extensions/EcomCraft/Tests/
```

- Verify how the extension depends on the main project; if `SwarmUI.extension.props` requires building the main project first, add that step.
- Adjust `node --test <dir>` to the actual Node version so it covers all `*.test.cjs`.
- The existing Ubuntu job's status cannot be checked locally (no `gh`); the user checks after pushing. Existing-job failures are out of scope; record them.

**Acceptance:** confirmed by the user after agreeing to push. Until then the report says "not verified".

#### R02 Front-end tests and static checks

**Goal:** file splits must not break tests, and missing variables from the split must be caught.

1. Create `Tests/helpers/studio-sources.cjs` providing:
   - `scriptFiles()`: parse `index.html`'s `<script src="/EcomCraft/...">` and return local paths in load order.
   - `readAllScripts()`: concatenate all workspace scripts in order (excluding `lang/en.js`).
   - `loadSimpleMode()`: put all `simple*.js` in order into one `vm` context and return `SimpleMode`.
2. Modify `simple-mode.test.cjs`, `mobile.test.cjs`, `error-codes.test.cjs`, `studio-i18n.test.cjs`: use those helpers where they read `studio.js`, `simple.js`. **Assertions unchanged.**
3. Add `Tests/frontend-structure.test.cjs`:
   - Every script in `index.html` has a route in `StudioRoutes.cs`'s `Assets` and exists on disk.
   - Scripts and styles share the same `?v=` version.
   - Every static ID referenced by `$('xxx')` in the scripts exists in `index.html`; dynamically generated IDs go on an explicit allow-list (produced on first run, hand-checked, then written in).
4. Add `Tools/lint/eslint.config.mjs` enabling only `no-undef`, `no-redeclare`, `sourceType: 'script'`, declaring browser globals plus `SimpleMode`, `I18n`, `StudioCore`. Command:

   ```
   npx --yes eslint@9 -c src/Extensions/EcomCraft/Tools/lint/eslint.config.mjs src/Extensions/EcomCraft/Studio
   ```

   Do not commit `node_modules` and do not add a `package.json`. The baseline must be 0 errors; report errors first, do not change code.

**Acceptance:** front-end tests pass with more tests; eslint 0 errors.

#### R03 API snapshot and regression checklist

1. Add `Tests/api-surface.test.cjs` and the snapshot `Tests/api-surface.json`: extract every `API.RegisterAPICall` method name and permission from `EcomCraft.cs` and `MagicPromptAPI.cs`, sort, and compare with the snapshot. Later API changes must update the snapshot explicitly.
2. Add `docs/EcomCraft 回归清单.zh-CN.md` and `docs/EcomCraft Regression Checklist.md`. Every item states the steps and expectations:

| # | Flow |
|---|---|
| 1 | Log in, open the workspace, no console errors, language switching works |
| 2 | Upload a product: local, product library, gallery pick, auto cut-out, drag placement |
| 3 | Follow a reference: upload 2 references, descriptions complete, generate, cancel one task |
| 4 | Make one image: platform, size and type, generate |
| 5 | Make a full set (including white-background); storyboard; retry failed items |
| 6 | Candidates: star, download, selling-point image, refine in professional mode, platform packaging |
| 7 | Professional mode: text-to-image, image-to-image, inpaint, outpaint, upscale |
| 8 | Upload an own workflow and run it; submit for review |
| 9 | Workflow review: publish, reject, take down; initialize standard workflows |
| 10 | Brand kits, project archive, make again with same settings |
| 11 | Queue, reassignment, gallery filters and bulk actions |
| 12 | Settings: prompt enhancement, vision model, storage, wizard, diagnostics, backup and restore |
| 13 | Narrow screens: simple mode only |
| 14 | With a regular account, no admin entries or others' tasks are visible |

#### CP0 User baseline regression

The user runs the checklist on a GPU-capable computer and records each item. **Known failures go into the checklist as the baseline**; afterwards only regressions matter. The development AI cannot do this step.

### Phase B: defect fixes

#### R04 Unified file I/O

1. Create `Studio/Common/JsonFile.cs` (depending only on .NET and Newtonsoft; add it to the test project's `Compile Include`):

   ```csharp
   /// <summary>Result of reading a data file.</summary>
   public enum JsonFileState { Missing, Ok, Unreadable }

   public static class JsonFile
   {
       public static JsonFileState TryReadObject(string path, out JObject value, out string error);
       /// <summary>Writes through a temporary file next to the target and then replaces it.</summary>
       public static bool TryWriteAtomic(string path, string content, out string error);
   }
   ```

   Catch `IOException`, `UnauthorizedAccessException`, `JsonException`; callers do the logging.

2. Write the `JsonFileTests` first: missing, normal, corrupt JSON, content correct after write, original file unchanged after a failed write.

3. Migrate each site, **file format and indentation unchanged**:

| Site | Requirement on read failure |
|---|---|
| `Products/ProductLibrary` | **Fix:** record `Unreadable`; `Save()` refuses and returns an error |
| `Brand/BrandKitLibrary` | **Fix:** same |
| `Projects/ProjectLibrary` | Already protected; swap the implementation only |
| `Workflows` (`WorkflowReview`) | Keep the current "rename the corrupt file aside + `LoadFailed`" behavior |
| `Storage/CandidateCleanup`, `Storage/WorkSync`, `Storage/StorageSettings`, `Templates/TemplateStore` | Comments claim the original is never overwritten. **Verify each `Save` really protects; if not, fix like the product library** and report |
| `Prompting/PromptService` (settings file) | Keep "unreadable means off"; the comment at line 831 explains it replaces with defaults — leave as is and note it in the report |
| `MagicPrompt/VisionDeviceCheck` | Swap the write method only (AGENTS.md requires no overwrite after failed reads; verify then handle) |

4. When the product library or brand kits fail to save, return the error `data_file_unreadable` with plain wording: "商品库文件暂时读不了，这次没有保存，原有数据没有动。请稍后重试或联系管理员。" Add it to `safeErrors` with an English translation per the `ecomcraft-api-route` skill.

**Acceptance (tests or manual):** write garbage into `products.json`, start, saving fails, the file is unchanged; after fixing the file and restarting, everything works.

**Behavior change:** read failures only.

#### R05 Backup-restore paths

1. Add a pure function `ResolveDestination(string entryName, string ecomcraftRoot)` to `BackupRules`: after stripping the prefix, resolve with `Path.GetFullPath`; the result must stay inside the root, otherwise return `null`.
2. `Validate` rejects such names too: if the whole-name check fails, no file is written.
3. `BackupService.DestinationFor` uses it.
4. Write the failing tests first (`BackupRulesTests`): `ecomcraft/C:/x`, `ecomcraft//x`, `ecomcraft/\x`, `ecomcraft/a/../../x`, a normal name.
5. Tests must cover Windows separators; cases that behave differently on other systems branch by OS — do not skip the Windows cases.

#### R06 Periodic batch-memory release

1. Following `Storage/CandidateCleanup.Start/Shutdown`, add a background loop to `BatchService` calling `Cleanup()` every 10 minutes.
2. Register and stop it in `EcomCraft.cs`'s `OnInit` / `OnShutdown`.
3. Before cleaning, `Cleanup()` checks for in-flight tasks: **batches that have not finished are not cleaned**, to avoid dropping running tasks (the current logic looks at time only; first confirm its effect on running tasks; if handled, keep it).
4. Expiry stays 6 hours; no API change.

### Phase C: back-end structure

#### R07 Split `StudioApi` into partials

**Moves only, no logic changes.** Make `public static class StudioApi` `partial`; no caller changes (including the 248 `StudioApi.Error` uses). `[API.APIClass]` stays exactly once. Files go in `Studio/Api/`, **namespace unchanged**.

| File | Members (baseline lines) |
|---|---|
| `StudioApi.cs` | Constants, `CanView`, `IsCloudModelConfigured`, model-list helpers (389–458), `LocalBackendStatus`, `EcomCraftStudioBackendStatus`, `EcomCraftStudioCatalog` (606–661), `Error` |
| `Api/StudioApi.Progress.cs` | `ComfyProgress`, `ComfyQueueProgress`, `ObserveComfyProgress`, `NativeQueueEntries` (45–187) |
| `Api/StudioApi.Execution.cs` | Workflow parameter sets (188–205), `PrepareNativeWorkflowSeeds`, `ValidateExecutionBackend`, problem checks, `TryReadPinnedServer`, `ScheduleAndSubmit` (206–388) |
| `Api/StudioApi.Queue.cs` | `EcomCraftStudioQueue`, `EcomCraftStudioCancelTask` (459–605) |
| `Api/StudioApi.Generate.cs` | `EcomCraftStudioGenerate`, prompt helpers (662–996), the tail-validation helpers used only by it |
| `Api/StudioApi.Preparing.cs` | `PreparingTask` etc. (997–1134) |
| `Api/StudioApi.Background.cs` | `BackgroundTask`, `SubmitBackgroundGeneration`, `FinishedResponse` (1135–1297) |
| `Api/StudioApi.PublishedCatalog.cs` | Permission overrides, dynamic parameters, catalog, input, `InferWorkflowKind`, `TryGetPublishedWorkflow`, `ParseWorkflowObject` (1298–1567) |
| `Api/StudioApi.PublishedGenerate.cs` | `GeneratePublishedWorkflow` (1568–1811) |
| `Api/StudioApi.Validation.cs` | `ValidatePublishedWorkflowInput` plus numeric, option and image validation, `GenerationError` (1812–2104) |

**Notes:**
- C# does not guarantee static-field initialization order across partial files. Fields that depend on each other stay in the same file; verify each before moving.
- Confirm the `docs/APIRoutes` generation and `[API.APIDescription]` are unaffected.

**Acceptance:** compiles; the `api-surface` snapshot is unchanged; `git diff --color-moved` shows almost pure moves.

#### R08 Internal API calls; take back `raw_prompt`

**Problem:** `BatchService` puts an internal marker into a `JObject` and calls the public API, which clients can also send.

1. Add `internal sealed class GenerateOptions` with field `bool RawPrompt` (with `///`).
2. Add `internal static Task<JObject> GenerateInternal(Session, JObject, GenerateOptions)` holding the logic. The public `EcomCraftStudioGenerate` just calls it with `new()`.
3. `TemplateApi.Resolve` reads from the options instead of `rawInput["raw_prompt"]`, and **removes that field from requests**. Check `TemplateApi.WithTemplatePrompt` (the background path at `StudioApi.cs:1080`) for the same treatment, or the reference-mode background submission loses the original prompt.
4. `BatchService.GenerateAsync` calls `GenerateInternal`.
5. Add tests asserting at least: the public path no longer reads `raw_prompt`.

**Behavior change:** a client-sent `raw_prompt` no longer has an effect. The front end does not use it (verified). Reference-mode behavior must not change; confirm with checklist item 3.

#### R09 Split `EcomCraftStudioGenerate` and the post-processing flow

1. Following the method's existing block comments (`Reference / edit images`, `Mask`, `Upscale`, `LoRAs`, `Sampler / scheduler`), extract private methods that return an error or `null`. **Order and error wording unchanged.**
2. Extract the long stretches of `GeneratePublishedWorkflow`. Target: no method over ~100 lines; where impossible, say why.
3. Create `PostProcess/GenerationFinish.cs`, moving the post-generation steps near `StudioApi.cs:772-795` (`OutputResize.ApplyAsync`, `ProductPreserve.ApplyAsync`, `Discard`, failure counting) into one `internal static` entry. **Order unchanged:** resize first, paste-back after (commit `c2fab63c` depends on this order). The background path (around line 1096) uses it too.

**This is T86's integration point.** Do not add locating or validation logic early.

#### R10 Split `BatchService`

1. Make it `partial`, in `Studio/Generation/`: `BatchService.cs` (data types, interface), `BatchService.Start.cs` (`StartSet` and helpers), `BatchService.Run.cs` (`RunJobAsync`, `GenerateAsync`, cut-out).
2. Split `StartSet` into per-step methods: read products, read references (238–310), pick templates, expand tasks, submit. Error wording and order unchanged.
3. Move validation that can live without SwarmUI into `SetRules`, with tests.
4. Make the total cap of `20` a `SetRules.MaxImages` constant (lines 218, 365 and the error wording). **Value unchanged.**

**This is the integration point for T85 and T98.**

#### R11 Split `PromptService` into partials

Moves only, into `Studio/Prompting/`:

| File | Content |
|---|---|
| `PromptService.cs` | Initialization, data types, settings |
| `PromptService.Catalog.cs` | Styles, product types |
| `PromptService.Compose.cs` | Composition, expansion, translation |
| `PromptService.Vision.cs` | Vision status, models, device, `EcomCraftStudioReferenceDescribe` |
| `PromptService.Storyboard.cs` | Storyboards |
| `PromptService.Model.cs` | `AskModelRawAsync` and other low-level calls |

Same static-field care as R07.

#### R12 Unify image data-URL parsing

Create `Studio/Common/ImageDataUrl.cs` (unit-testable):

```csharp
public static bool TryParse(string dataUrl, ImageDataFormats allowed, int maxBytes, out byte[] bytes, out string extension);
public static bool IsValid(string dataUrl, ImageDataFormats allowed, int maxBytes);
public static string ToDataUrl(byte[] bytes, string mime);
```

Migrate `CutoutService`, `CutoutOnly`, `ProductPreserve`, `ProductLibrary`, `BrandKitLibrary`, `TemplateAdminApi`, `TemplateApi`, `BatchService`, `StudioApi.ValidReference`, `PromptService`.

**First list every site's current allowed formats and size limits; after migrating keep each identical — no loosening or tightening anywhere.** Put the list in the completion report. Error wording and codes unchanged.

#### R13 Standard workflows with multiple blueprints

Edit `StandardWorkflows.cs`, behavior unchanged:

1. `Kinds` becomes a list of records with fields `Kind`, `Id`, `Description`, `BlueprintFile`, `AddProductInpaint` (bool). The two existing entries both use `text-to-image.json` and `true`.
2. `Blueprint()` takes the record; the same file's conversion result is cached once.
3. `Ensure` iterates.

**Acceptance:** published workflow fingerprints unchanged (otherwise republishing triggers). Provide a comparison runnable in unit tests, or explain in the report how to verify. **This is T84's integration point.**

#### R14 Tidy the `Studio/` folder

Moves only, **namespace unchanged**, no content edits.

| Target | Files |
|---|---|
| `Studio/Workflows/` | `WorkflowReview`, `WorkflowReviewApi`, `WorkflowDiscovery`, `WorkflowCatalogFilter`, `NativeWorkflowConverter`, `NativeWorkflowDiscovery`, `NativeWorkflowParameters`, `OwnWorkflowApi`, `OwnWorkflowRules`, `StandardWorkflows`, `ExecutionServers` |
| `Studio/Ops/` | `BackupService`, `BackupRules`, `DiagnosticsService`, `DiagnosticsSanitizer`, `VisionInstallRules`, `VisionModelInstall` |

Sync: `Tests/EcomCraft.Tests.csproj` links, paths reading C# files such as `simple-mode.test.cjs:166`, skill files, AGENTS.md paths. Use `git mv`. Front-end files stay.

#### R24 Remove the dormant cloud executor types

Background: after the 2026-10-31 requirement revision there is no cloud generation (LAN requirements D11 and R10), but dormant cloud types remain in the code: the cloud ComfyUI and cloud model API values of `ExecutorKind`, and the `cloud_comfy` / `cloud_model_api` reservation in `execution_plans` validation.

1. Delete the two enum values and their branches. They never produced an executor or a request; deleting them must change no existing behavior.
2. `execution_plans` validation keeps rejecting: submitting `cloud_comfy` / `cloud_model_api` still returns the existing error with the same code and wording; add a unit test covering it.
3. Update the "awaits a later refactoring cleanup" notes in the LAN requirements' step-4 implementation notes and the workflow guide to say the types are gone while validation still rejects them.

**Acceptance:** compiles; the `api-surface` snapshot is unchanged (update it explicitly first if any API changes); documents synced. Size S, depends on R14 (move first, modify later — never in one commit).

### Phase D: front end

#### R15 Split `simple.js`

**Method:** mixins; callers and `this` unchanged.

In `simple.js`'s `SimpleMode` add:

```js
/** Copies the static methods of a part class onto SimpleMode, so simple mode can live in several files while callers and this stay the same. */
static adopt(part) {
    for (let name of Object.getOwnPropertyNames(part)) {
        if (typeof part[name] == 'function' && !['length', 'name', 'prototype'].includes(name)) {
            this[name] = part[name];
        }
    }
}
```

Each file:

```js
/** Simple mode: brand kits (F12). */
class SimpleBrandPart {
    static loadBrandKits() { ... }
}
SimpleMode.adopt(SimpleBrandPart);
```

**Rules:** all static fields stay in `simple.js`; confirmed there are no `super.` or private `#` members in the files.

Files go in `Studio/js/simple/`:

| File | Methods (baseline) |
|---|---|
| `simple.js` (in place) | Fields, `adopt`, `viewAccess`, `attach`, `setTask`, `renderTask`, `remember`, `enter`, `refresh`, `load`, `message`, `blocker`, `setBlocker`, `update`, platform and template rendering (`preferredPlatforms`–`imageField`) |
| `rules.js` | `placementRect`–`cutoutIssues` (103–250), `compressionScale`, `countValue`, `countProblem`, `qualityText` |
| `prompt.js` | `promptSource`–`renderPromptUsed` (543–677) |
| `export.js` | `loadExportOptions`–`slotName` (727–968) |
| `products.js` | `loadLibrary`–`toDataUrl` (969–1288 minus the above), `readFile`, `addPhoto`, `loadImage`, `runCutout`, `clearProduct`, `originText`, `renderProducts`, `selectProduct`, `renderQuality` |
| `placement.js` | `renderCutout`–`endDrag` (1590–1744) |
| `brand.js` | `loadBrandKits`–`deleteBrand` (1976–2234) |
| `storyboard.js` | `storyboardProblem`–`runStoryboard` (2235–2392) |
| `reference.js` | `addReferenceClick`–`generateReference` (2393–2941, including vision) |
| `batch.js` | `setTemplates`–`cancelBatch` (2987–3219) |
| `tasks.js` | `applyRecipe`–`download` (3220–end) |

Steps:

1. One commit per file, starting with standalone parts like `brand.js`, `storyboard.js`.
2. Each time sync `StudioRoutes.Assets` and `index.html` (after `simple.js`, before `studio-core`/`studio.js`).
3. Run the front-end tests and eslint each step.

**Acceptance:** all tests pass; `simple.js` is at most ~1,000 lines; CP3 regression.

#### R16 Split `SimpleMode.attach`

Split the 230-line `attach` into `bindUpload`, `bindReference`, `bindBrand`, `bindStoryboard`, `bindExport`, `bindPicker` etc., placed in their files and called by `attach` in the original order. Event-binding order unchanged.

#### R17 `studio.js`: shared layer and standalone views

**Method:** every file gets its own closure (top-level `let` in classic scripts shares the global scope; duplicate declarations throw), sharing through one global object.

New `Studio/js/studio-core.js`:

```js
/** Shared helpers and views of the professional workspace; each script adds what it owns. */
let StudioCore = {};
(() => {
    'use strict';
    /* moved from studio.js: $, el, svgIcon, tr, debounce, ApiError, state, request, storage helpers, error text */
    Object.assign(StudioCore, { $, el, svgIcon, tr, debounce, ApiError, state, request, ... });
})();
```

Other files:

```js
(() => {
    'use strict';
    let { $, el, tr, request, state } = StudioCore;
    class Lightbox { ... }
    StudioCore.Lightbox = Lightbox;
})();
```

**Rules:**
- Destructure only members provided by `studio-core.js` that exist at load time.
- Members of other files are always accessed at runtime as `StudioCore.X`; never destructure them (that would depend on load order).
- **Closure-level `let` variables read or written by several files must move into `state`.** If `state` itself is wholesale-assigned, report first.
- `studio.js` loads last and registers anything not yet moved (`Object.assign(StudioCore, { refreshCatalog, ... })`) and boots.

This task moves (baseline lines):

| File (`Studio/js/`) | Content |
|---|---|
| `studio-core.js` | Helpers (5–315, minus the next two) |
| `time-estimate.js` | `TimeEstimate`, `TitleProgress` |
| `startup-overlay.js` | `StartupOverlay` |
| `dialogs.js` | `DialogFocus` (5206–5259) |
| `lightbox.js` | `Lightbox` (5260–5675) |

#### R18 `studio.js`: admin, gallery, queue

One commit per line, in order:

| File (`Studio/js/`) | Content (baseline lines) |
|---|---|
| `admin-standard.js` | `StandardWorkflowView` (5018–5094) |
| `admin-menu.js` | `AdminMenu` (5095–5205) |
| `admin-review.js` | `WorkflowReviewView` (4259–4819) |
| `own-workflows.js` | `OwnWorkflows` (4820–5017) |
| `admin-storage.js` | `StorageSettingsView` (3270–3475) |
| `admin-setup.js` | `SetupWizard`, `TemplateRequirementsView` (3476–3763) |
| `gallery.js` | Gallery (2962–3269), `GallerySelection`, `GalleryProjects` (3764–4258) |
| `queue.js` | `Reassign`, `ExecutionServerPicker`, Queue (2628–2961) |

#### R19 `studio.js`: create page and event binding

| File (`Studio/js/`) | Content (baseline lines) |
|---|---|
| `prompt-settings.js` | Prompt enhancement, vision model install (1430–2413) |
| `create-catalog.js` | Catalog, `NativeWorkflowForm`, `WorkflowCatalogRefresh` (547–1044) |
| `create-form.js` | Type and mode, references, mask, outpaint, upscale, LoRA, status (1045–1429) |
| `create-generate.js` | Results, Generate (2414–2627) |
| `studio.js` (kept) | Theme, view routing, Init; `bind()` split per view into each file's `bindXxx()`, called by `studio.js` in the original order |

`SimpleMode.attach({...})`'s arguments are unchanged, sourced from `StudioCore`.

**Acceptance:** `studio.js` is at most ~600 lines; no file over ~1,000 lines (`prompt-settings.js` may be an exception with a stated reason); eslint 0 errors.

#### R20 Front-end standards unification

Only `studio*` and view files under `Studio/js/`, committed separately:

1. `const` → `let`.
2. 4 `forEach` → standard `for` (a `return` inside the callback becomes `continue`).
3. `} else` → its own line.
4. **No bulk `===`/`!==` changes**; touch them only where strict comparison is truly unnecessary; default to keeping them.
5. Add braces to single-line braceless `if`s.

Update test assertions that depend on the old text (e.g. the regex at `mobile.test.cjs:41`).

### Phase E: wrap-up

#### R21 Core-change inventory

1. Add `docs/EcomCraft Upstream Changes.md` and `docs/EcomCraft 上游改动清单.zh-CN.md`: from `git diff $(git merge-base dev origin/master) dev`, list every non-extension file with its purpose, whether it is branding, and the future upstream-merge risk.
2. Move the ~400 added lines of `src/Core/Installation.cs` into a new `src/Core/EcomCraftInstallation.cs`: make `Installation` a `partial class`, **moving only the EcomCraft-added methods**, leaving upstream code untouched. Fewer merge conflicts, and easier changes for T95, T96.
3. Other core changes **do not move**.

#### R22 Repository and documentation cleanup

**Confirm with the user before deleting anything:**

1. Remove the worktree and merged branch: `git worktree remove .claude/worktrees/t56`, `git branch -d wt/t56` (stop if `-d` refuses).
2. Add `.claude/` to `.gitignore`.

**No confirmation needed:**

3. Rename: `EcomCraft Prompt Enhancement.md` → `EcomCraft 提示词优化指南.zh-CN.md`, `EcomCraft Prompt Enhancement.en.md` → `EcomCraft Prompt Enhancement.md`. Order the renames to avoid overwriting. Update `README.md`, `README.en.md`, `docs/README.md`, `docs/README.zh-CN.md` and links inside both documents.
4. Leave `docs/EcomCraft 简易模式改版原型.html` alone.

#### R23 Update AGENTS, skills and the tracker

1. **AGENTS.md:**
   - Correct "the repository has no automated test projects"; state the C#, front-end and eslint commands.
   - Describe the extension structure: `Studio/Api`, `Common`, `Workflows`, `Ops`, `js/`.
   - Add rules:
     - Data-file reads and writes must use `JsonFile`.
     - Image data URLs must use `ImageDataUrl`.
     - Server-side internal options go through `GenerateOptions`, never into a client-sendable `JObject`.
     - New scripts sync `StudioRoutes.Assets` and `index.html`.
     - `{ get; set; }` in `MagicPrompt/` is exempt (serialization models).
2. **Skills:** update `ecomcraft-studio-ui`, `ecomcraft-api-route`, `ecomcraft-build-run`, `dev-branch-commit` for `studio.js`, `simple.js` and the check commands.
3. **`docs/EcomCraft 工作台界面设计.zh-CN.md` and architecture section 6.5 (code organization)**, Chinese and English in sync.
4. **Tracker:** add the phase "基础重构" and R00–R24; add R to the ID rules. Back up before editing with openpyxl; confirm formulas and dropdowns survive the save.

## 6. Checkpoints (run by the user on a GPU-capable computer)

| Checkpoint | When | Content | On failure |
|---|---|---|---|
| CP0 | after R03 | the full checklist, establishing the baseline | — |
| CP1 | after R06 | items 2, 3, 5, 12; the corrupt-file test | stop and fix |
| CP2 | after R14 | items 3–12, 14 | locate the commit with `git bisect`, fix, continue |
| CP3 | after R20 | all items, plus desktop and phone browsers | same |

If any checkpoint regresses from the baseline, **do not enter the next phase**.

## 7. Interleaving with the Implementation Plan

| Order | Work |
|---|---|
| 1 | **Implementation Plan T81: put the goals and both plans into the repository first** |
| 2 | R00–R03, CP0. T82 and N12 (the route trial) can run in parallel with later work; neither touches platform code |
| 3 | R04–R20 (CP1–CP3) |
| 4 | **After G1 passes**, start T83 and beyond |
| 5 | R21–R24 parallel with the above, but R23 waits for R14 and R20, and R24 waits for R14 |

**If G1 fails:** phases A and B still happen; C and D can also continue since these problems are route-independent. But ask the user whether to keep investing.

**Integration points for new features:**

| New task | Integration point | From |
|---|---|---|
| T84 fusion workflow | `StandardWorkflows` multiple blueprints | R13 |
| T85 generation flow | `BatchService.Start/Run`, `GenerateOptions` | R08, R10 |
| T86 product verification | `PostProcess/GenerationFinish` | R09 |
| T87 aspect fitting | `OutputSizeRules` (no change) | — |
| T89 single entry | `simple/reference.js`, `storyboard.js`, `brand.js` etc. can be hidden individually | R15 |
| T103+ refinement bench | `create-form.js`, `create-generate.js` | R19 |
| T109 edition split | easier interfaces after `Workflows/`, `Ops/`, scheduling separation | R14 |

## 8. Risks

| Risk | Mitigation |
|---|---|
| Missing variables after the front-end split, failing only on click | eslint `no-undef`; ID checks; CP3 manual regression |
| Cross-file shared closure variables destructured as stale values | R17's rules: shared variables into `state`, cross-file members accessed at runtime |
| Wrong script load order | the `frontend-structure` test; the `boot-check` fallback notice stays |
| Static initialization order across partial files | dependent fields in the same file |
| Tests compare source text only, missing behavior errors | the regression checklist; refactoring is not a substitute for manual testing |
| Dragging on and delaying new features | section 7 schedules the trial in parallel; back end C and front end D can each pause; the repo is usable after every task |
| Cannot compile | R00 is a hard prerequisite |

## 9. Definition of done

- Front-end tests, C# unit tests, eslint, CI (Windows, macOS) all pass.
- No C# or JS file over ~1,000 lines (exceptions: JSON, `lang/en.js`, `MagicPrompt/`, stated reasons).
- The API snapshot (names and permissions) is exactly what R03 created.
- All four defects of section 2.1 have tests.
- CP3 is no worse than CP0.
- AGENTS.md, skills, the architecture document and the tracker are updated, Chinese and English in sync.
