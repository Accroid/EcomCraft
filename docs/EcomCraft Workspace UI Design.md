# EcomCraft Workspace UI Design

> **Language:** English · [简体中文](EcomCraft%20工作台界面设计.zh-CN.md)
>
> **Status:** Implemented, pending real-environment acceptance · **Version:** v1.29 · **Date:** 2026-10-29
>
> Confirmed: the "gray-card neutral + chroma-key action color + backdrop preview" direction; generation count became an editable number; "continue from this image" and "view in gallery" were added under creation results; the full-size preview supports previous / next and arrow keys. No dedicated mobile layout for now (narrow screens only need to stay usable).
>
> Scope: the `/EcomCraft` workspace (`index.html`, `studio.css`, `studio.js` under `src/Extensions/EcomCraft/Studio/`). SwarmUI's native pages are out of scope.
>
> Design basis: the `frontend-design` skill (visual direction) and the Vercel Web Interface Guidelines (`web-design-guidelines` skill, interaction and accessibility checks).

## 1. Product Positioning (to be confirmed)

| Item | Content |
|---|---|
| Theme | An in-house production tool for e-commerce product images and videos: the team's shared "online photo studio" |
| Users | E-commerce operations, designers, product photographers; shared by many people, some non-technical; desktop browsers first, occasionally phones to check results |
| Primary task | Pick a workflow or model → describe the product picture clearly → submit to ComfyUI execution servers → check and download results |
| Secondary tasks | Find, star and delete works in the gallery; watch each server's queue; administrators configure prompt enhancement |

## 2. Design

### 2.1 Core Idea

**The interface should be as neutral as a photographer's gray card.** Product photos suffer when color judgment is skewed, so the interface itself must not carry a strong hue that interferes with judging product colors. The body stays neutral gray; color is reserved for exactly two kinds of information: "actions" and "states".

The single visual focus is the **seamless backdrop of the preview area**: the preview is shaped like a studio cyclorama (wall into floor with one soft bend), and generated results sit on it like products. The empty state uses viewfinder corner marks instead of the generic dashed box with a plus. While generating, a red "tally light" blinks in a corner of the backdrop together with the elapsed seconds — the page's only automatic animation.

Everything else stays quiet: no hover-lift cards, no fade-ins per section, no gradient decoration.

### 2.2 Colors

| Name | Light (gray card) | Dark (darkroom) | Use |
|---|---|---|---|
| Backdrop | `#E8E8E6` | `#1E1F20` | Page base, preview backdrop |
| Panel | `#F7F7F5` | `#28292A` | Forms, lists, dialogs |
| Ink | `#1F2123` | `#ECECEA` | Body text, headings |
| 18% gray | `#616365` | `#9A9C9E` | Secondary text, hints (≥ 4.5:1 on panels and backdrop) |
| Chroma key | `#0B7A53` | `#3FBF8A` | Primary actions, selection, focus ring |
| Tally | `#D93A26` | `#FF6A55` | Generating, congested, delete and other "pay attention" states |
| Amber | `#D97706` | `#FBBF24` | A server "working" (running tasks but no queue) indicator |

- Contrast (WCAG AA needs 4.5:1): in light theme, white on chroma key is about 5.3:1 and on tally about 4.6:1; in dark theme buttons use dark text on chroma key, about 7.1:1; 18% gray is about 5.6:1 on light panels, 4.9:1 on the backdrop and 5.3:1 on dark panels.
- Borders and dividers derive from ink via opacity; no extra colors.
- No stacked shadows for elevation; hierarchy comes from the lightness difference between panel and backdrop plus 1px dividers. Only dialogs and the full-size preview use one shadow.

### 2.3 Typography

The workspace CSP only allows same-origin resources, so no web fonts; system fonts only:

| Role | Stack | Use |
|---|---|---|
| Headings | `"Noto Serif SC", "Source Han Serif SC", "Songti SC", "STSong", "SimSun", serif` | Page titles, empty-state titles, 24px and up; like a product catalog |
| Body and controls | `"PingFang SC", "Microsoft YaHei UI", "Microsoft YaHei", "Noto Sans SC", system-ui, sans-serif` | Everything else |

- Type scale: 12 / 13 / 14 / 16 / 20 / 28 px; body 14px, line height 1.6; hints 12px.
- Numbers (sizes, counts, seconds, queue lengths) use `font-variant-numeric: tabular-nums`, not a monospace font.
- No all-caps labels, letterspaced micro-labels or partially colored words.

### 2.4 Layout

Desktop (≥ 1100px):

```
┌────────────────────────────────────────────────────────────────────┐
│ EcomCraft   Create  Gallery 12  Queue 3     ●2 servers online  ☀  User │  Top bar 56px
├───────────────────────────┬────────────────────────────────────────┤
│ Image | Video              │                                        │
│ Text2Image Image2Image …    │        ╭────── Backdrop ──────╮        │
│ ─────────────────────────  │        │                        │      │
│ Workflow      [Refresh]    │        │      ┌────────┐        │      │
│ [Standard workspace ▾]     │        │      │ Result │         │      │
│ Description *      0/12000 │        │      └────────┘        │      │
│ [                        ] │        ╰────────────────────────╯      │
│ Enhance · Insert example   │  Status: engine returned 2 results     │
│ Model / LoRA / Size / Count│  [Download] [Continue from] [Gallery]  │
│ ▸ Advanced parameters      │                                        │
│ [       Generate image   ] │                                        │
└───────────────────────────┴────────────────────────────────────────┘
      Form column 400px, left-aligned      Preview takes the rest, centered
```

- The old dark sidebar became a top bar: three views need only three entries, and the 232px sidebar wasted preview width.
- The form column has a fixed width, is left-aligned and groups fields with dividers instead of per-field cards.
- Narrow screens (< 1100px): form and preview stack, preview on top, so results stay visible first.
- Phones get no dedicated layout; the page only needs to stay intact without horizontal scrolling.

Gallery (contact sheet):

```
┌────────────────────────────────────────────────────────────────────┐
│ Gallery                                                 12 works   │
│ [All | Starred]  [Search file names…]  [Newest ▾]  [Refresh] [Select] │
├────────────────────────────────────────────────────────────────────┤
│ ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐                       │
│ │      │ │      │ │      │ │ ▶video│ │      │   square crop-safe  │
│ └──────┘ └──────┘ └──────┘ └──────┘ └──────┘                       │
│ 0312-serum  ★   0311-sneaker  …            file names below        │
└────────────────────────────────────────────────────────────────────┘
```

- Like a photographer's contact sheet: square frames, images fitted whole (products never cropped), file names permanently visible below each image.
- Generation time sits on its own line below the name (HH:mm:ss, full date on hover); date groups show date and count.
- Star buttons are always visible (outline when not starred); delete appears on hover or keyboard focus, always on touch screens.
- Deleting a starred work removes both the starred copy (Starred/) and the original, so it disappears from "All" and "Starred".

Task queue:

```
┌────────────────────────────────────────────────────────────────────┐
│ Task queue                                  Updated 14:02:11  [Refresh] │
│ Server        State       Running   Waiting                           │
│ ● Local #1    Idle           0        0                                 │
│ ● C           Working        1        0                                 │
│ ● B           Congested      1        3                                 │
│ Current tasks (2)                                                       │
│ B · Running · Node 12 KSampler  ▓▓▓▓▓░░░ about 62%                    │
│ My request #18 · Running · 2 images · 1m 05s             [Stop]       │
└────────────────────────────────────────────────────────────────────┘
```

- Servers form a real `<table>`; states pair indicator color with text, never color alone.

### 2.5 Principles

1. **Neutral first**: the interface never competes with product colors; color only says "clickable" and "attention".
2. **One focus**: only the preview backdrop has shape and animation; everything else stays restrained.
3. **Say the next step**: every empty state, error and disabled button says why and what to do.
4. **Button names are outcomes**: "Generate image" is followed by "2 images generated"; "Delete selected" by "3 works deleted".

### 2.6 Self-review Against Common Templates

| Draft | Problem | Change |
|---|---|---|
| Keep the blue-violet `#4f6bff` primary | Generic SaaS blue, unrelated to product photography | Chroma-key green, only for actions and selection |
| White rounded cards + one soft shadow everywhere | Template category 4 | No field cards, no generic shadows; dividers group fields |
| Brand subline "STUDIO" in monospace micro caps, an `IMAGE` tag | All-caps labels, monospace micro text, middot chains | Drop the tag and micro text; no subline |
| Generate button with a `→` arrow, "Download original ↓" | Template category 5 | No arrows; button text states the outcome |
| Homepage slogan "Let products find new expression" | Marketing copy that helps nobody | State the current task instead, e.g. "Generate product images" |
| Footer "Built on SwarmUI · for e-commerce teams" | Middot chain with no information | Footer keeps the version and the native engine link only |

## 3. Pages and Controls

Notes: "current" lists present behavior and found defects; "after" is the implemented behavior. All element IDs stayed, so existing logic keeps working.

### 3.1 Global (top bar / navigation)

| Control | Purpose | Current | After |
|---|---|---|---|
| Skip link | Keyboard users skip navigation | OK | "Skip to main content", shared by all views |
| Brand | Return to the workspace start | Gradient block + monospace subline | Plain text "EcomCraft", returns to the creation view |
| Create / Gallery / Queue navigation | Switch views, URL follows `#create` etc. | OK; gallery and queue carry counts | Top-bar tabs; the current item gets an underline and `aria-current="page"`; counts use tabular numerals and appear only after data loaded, never a wrong 0 |
| Workflows navigation | Opens `#workflows` submission | New | Only for accounts that can submit but not review; reviewers use the "Manage" menu (F62) |
| Standard workflows dialog (administrators) | Check and initialize the built-in templates' standard workflows (F57) | New | An entry of the "Manage" menu; lists the scene and detail states (published / taken down / missing nodes or models with names / no server online, …) and the default model names; "Initialize standard workflows" generates and publishes now, disabled while running; Escape closes and focus returns to "Manage" |
| "Manage" menu (administrators) | Holds administration (F62) | New | Button at the right of the top bar, visible to accounts with server-settings or review permission, with the pending review count; lists workflow review (reviewers), first-setup wizard, prompt enhancement settings, storage and backup, download diagnostics and the native engine (new tab; these five need server-settings permission); `aria-expanded` shows the state, Escape, an outside click or choosing an entry closes it, and dialogs opened from it return focus to the button |
| Mobile menu button `rail-toggle` | Expand navigation on small screens | **Defect: clicking errors out** (`$('.rail')` is gone) | Removed; top-bar tabs stay directly clickable when narrow |
| Server health `health` | Online execution server count | Dot + text; text hidden when narrow | Clickable, opens the task queue; narrow screens show the number |
| Language toggle `lang-toggle` | Chinese / English | New | Shows the language it switches to ("EN" or "中文"), no page reload; choice remembered, first visit follows the browser; the button's own text is never translated (`data-i18n-skip`) |
| Theme toggle `theme-toggle` | Light / dark | **Defect: sun and moon both show** | Only the target icon; `aria-label` follows state |
| User name / avatar | Current account | Sidebar footer, fixed caption | Top-bar right, account name only |
| Native engine link | Opens SwarmUI pages | Sidebar link, same tab | Only in the "Manage" menu, administrators only, new tab (F62) |
| Footer | Version | Hardcoded 0.3.0, stale | The extension's real version |

### 3.1.1 Create (simple mode, default view)

| Control | Purpose | Current | After |
|---|---|---|---|
| Navigation "Create" / "Professional mode" | Switch views | New | Everyone starts in "Create"; "Professional mode" is the former creation view, open to every account (A2, F60); hidden on phone-sized screens (≤720px), where opening `#create` returns to creating images with a note to use a computer (F63) |
| Professional mode Upload workflow / My workflows | Upload a workflow saved with ComfyUI's Save for one's own use (F60) | New | "Upload workflow" button and list under the workflow picker; each entry shows the name, how many execution servers can run it or which nodes and models are missing, its review state (pending / not approved with the reason / published), "Submit for review" while never submitted or rejected (with an optional note, T73), and "Delete" (with confirmation); a refused upload lists the disallowed nodes and inputs; own workflows form their own "My workflows" group in the picker; hidden for accounts without generation rights |
| Task cards: Follow a reference / Make one image / Make a full set | Choose what to make, then fill it in (F61) | New | Three cards at the top; "Follow a reference" comes first and is the first-visit default; the chosen card has the green-screen border and `aria-pressed`; remembered in the browser; step 2 shows only that task's fields (marked with `data-tasks` in HTML, hidden by CSS from `data-simple-task`), and its title becomes "Pick platform and references / image type / product type"; switching tasks keeps product photos and references |
| Step 2 References (Follow a reference) | Upload 1–8 references with an editable prompt each (F59) | New | Shown and required only in "Follow a reference"; clicking "Add a reference" checks the vision model first and, when unset, keeps the picker closed and shows the guidance instead (its steps open the settings); the main button "Generate by reference" makes one image per reference for every product photo |
| Step 2 Image description notice (Follow a reference) | Says what is missing when image description cannot work (F68) | New | Calls `EcomCraftStudioVisionStatus` when the page opens; when it is off, no model is set, the vision service cannot be reached or the model is missing, a yellow-edged note under the references lists the reason and steps: administrators see the fix (the one-click install script command first, then installing Ollama by hand or `ollama pull <model>`) and a "Check again" button, and the words "prompt enhancement settings" in a step are themselves clickable and open that dialog, other accounts are asked to contact one; a failed description checks again automatically |
| Step 2 More options | Folds visual style, model, brand kit, one-line description with AI expand, and the prompt panel (F61) | New | Shown in "Make one image" and "Make a full set", folded by default with the open state remembered in the browser; selling points and storyboard appear only in "Make a full set" |
| Step 1 Upload product photo | Click or drop PNG / JPEG / WebP (≤5 MB) | New | Previewed in page memory only, sent on submit; "Use another photo" clears |
| Step 1 Camera upload | Opens the phone camera (F42) | New | Hidden file input with `capture`, works over `http://` LAN addresses; on desktop equals choosing a file |
| Step 1 Upload quality notice | Resolution, sharpness or product share too low (F38) | New | Checked right after upload (Laplacian variance, cut-out share); amber left bar explains why and suggests what to do; the user can continue |
| Step 1 Cut-out confirmation | Shown for product-preserving templates | New | Checkerboard preview; "Auto cut-out" runs on an execution server; drag anywhere (touch included), resize slider, "Reset placement"; generating is blocked until confirmed |
| Step 1 Four ways to add a photo | From this computer, camera, product library, gallery (F52) | New | Four equal buttons under the upload area (two columns on narrow screens); the library and the gallery open the same picker dialog (Esc closes it, focus stays inside); the gallery lists this account's latest 60 generated images, and a picked image over 5 MB or in another format is compressed to JPEG in the browser; every entry goes through the upload quality notice (F38) and is saved to the library |
| Step 1 Multiple photos | Pick or drop several (max 20) | New | Thumbnails appear as soon as there is a photo, labelled with their source (computer / camera / library / gallery) in the lower left; "Use another photo" removes the current one |
| Step 1 Product library | Auto-save on upload; "Choose from the product library" dialog; rename and delete of the current product | New | Thumbnails in the dialog (cut-out when present), with an explanation when the library is empty; name saves on blur; delete asks; placement saves about a second after dragging |
| Step 2 Platform | Filters the image types | New | "All platforms" lists every template; choice remembered in the browser; common platforms come first and the first is preselected (F40) |
| Step 2 Image type | Pick a template | New | Cards with example images when available; reasons shown when none is usable |
| Step 2 Product type | Two-level pick: category and subtype (F53, primary) | New | Category chips + subtype cards; data in `Templates/product-types.json`, subtypes contribute English prompt fragments; choice remembered; defaults to "General product｜Unspecified" |
| Step 2 Visual style | Optional visual style (F53) | New | Cards, the first being "Auto" (unspecified); choice remembered |
| Step 2 Needs a model | Adds a model fragment to the prompt when checked (F53) | New | Mutually exclusive with cut-out-only white main images: selecting that type disables and unchecks it; gender / hand-model attributes come later |
| Step 2 Brand kit | Choose the brand kit for generation (F12) | New | List defaults to "None" and is remembered in the browser; "Manage brand kits" opens a dialog with the kits as chips and "New brand kit" (up to 20); the editor has the name, brand colors (color pickers, up to 5), preferred background, logo and style references (up to 4, compressed in the browser when over 5 MB); "Save" and "Delete brand kit" (asks first); explains when the background could not be translated; Esc closes it and focus stays inside |
| Step 2 One-line description / AI expand | Optional, Chinese or English; "AI expand" writes a bilingual prompt (F54) | New | Single-line input; "AI expand" turns the description (or, when empty, the product type) into Chinese and English versions in the prompt panel, showing "Expanding…" and disabled while it works; when the model is unavailable a notice says the one-line description still works, and generation is not blocked |
| Step 2 Prompt panel | Optional Chinese / English tabs (F54) | New | One text box per tab (up to 1500 characters), without sampler, LoRA or other professional parameters; status chip "Empty / In sync / Chinese edited; translated when you submit / English edited; sent as is", in sync in the key color and edited in the tally color; when both languages were edited the last one wins; the server's English translation is filled into the English tab after submitting; with an empty panel the server composes the prompt from the one-line description |
| Step 2 Candidate count / Start | Manual candidate count (F55) | New | Number input, positive integers 1–20 only with an immediate notice and disabled buttons on invalid input; single generations and one-click sets share the 20-image cap; the hint below "Start" names the current product × type × count; disabled with a reason while a photo, template or product type is missing |
| Step 2 Whole set in one click | Every photo × every image type of the platform's set | New | Below, the estimated total of all photos × set types × per-type count (cap 20), disabling with the reason above the cap; disabled without set templates; with a storyboard the button reads "Generate set from storyboard" and the estimate becomes photos × storyboard images × candidates |
| Step 2 Selling points and storyboard | Optional folded section that plans the set (F25) | New | Selling points are single-line inputs (up to 5, add/remove); "Plan the storyboard" calls the prompt service and shows "Planning the storyboard…" while working; each shot's image type (only those of the current platform), purpose and scene description can be edited or removed, "Add an image" allows up to 8, "Clear the storyboard" returns to the ordinary set; a shot with a type the platform lacks disables generation with the reason; no multi-line text boxes or professional parameters |
| Step 3 Prompt sent to the engine | Folded view of the prompt the latest submission actually used (F54) | New | Collapsible block under the status line showing "Chinese: …" (when Chinese was the source) and "English (sent): …"; white main images (cut-out only) do not update it |
| Step 3 Generation tasks | Stages of single generations (F56) | New | Under the status line, the last 10 submissions as "Product｜Type: Writing the prompt… / Queued (server) / Running (server) / Done (server) / Failed", hovering a failed one shows why; when the model writes the prompt, the task appears as "Writing the prompt…" right after submitting |
| Step 3 Task list | State of each set / batch task | New | "Product n｜type: state", failed items retryable with the reason on hover; "Cancel remaining"; finished candidates join the list below; finishing shows a banner and the tab title counts progress in the background (F43) |
| Step 3 Export for platform | Scale and pad to the platform preset, then zip or shared folder | New | Appears with candidates; platform dropdown carries the size; only picked images export, one folder per product; unverified presets warn; the folder option hides without an export location |
| Step 3 Make a selling-point image | Lays out a title and selling points on one picked candidate in a fixed layout (F47) | New | Folded section shown once there are candidates; title (up to 20 characters), three point inputs (up to 16 characters each, prefilled once from the storyboard), layout chips (bottom banner / left text column / title with badges) and an accent color (defaults to the brand kit's first color); "Make the image" works only with exactly one picked image and shows "Making the image…"; the result goes to the front of the candidates |
| Step 3 Refine in professional mode | Takes one candidate to professional mode for further work (F64) | New | Shown next to "Download selected" when exactly one image candidate is picked; loads it as the image-to-image start picture and fills in the prompt read from the work's metadata, or asks the user to write one; hidden on narrow screens (≤720px); professional mode never writes back |
| Step 3 Candidates | Pick the images you like | New | Chroma-key border and check when picked; "Download selected" one by one, "View all in gallery"; picking or downloading tells the server to keep the file forever (F39); a "☆/★" star at the top left (F65) is the gallery star itself, so starred works show under "Starred" in the gallery and are kept forever; `aria-pressed` shows the state and it is disabled while the request runs |

### 3.2 Creation view — form

| Control | Purpose | Current | After |
|---|---|---|---|
| Image / video switch | Creation type | OK; video disabled without reason | Disabled states say "no video permission" in `title` and hint |
| Connection banner | Server, model count or error | OK | One status row atop the form; errors tint and offer "open the login page" / "view the setup guide" |
| View setup guide | Opens the guide dialog | OK | For administrators this opens the first-setup wizard (F40) |
| Mode tabs (text2image…upscale) | Switch image mode | `role=tab` without panels (wrong semantics) | Segmented buttons with `aria-pressed`; hidden when a workflow decides the mode, with a note |
| Workflow dropdown | Standard workspace or a published workflow | OK | Kept; options grouped by "source server / workflow" (`optgroup`) |
| Refresh workflows / models | Reread lists | Two buttons, one effect | One "Refresh list"; "Refreshing…" while busy |
| Workflow parameters | Published inputs | OK | Kept, styled like standard fields |
| Description | Positive prompt, required, 12000 chars | OK; counter in monospace micro text | Kept; counter warns near the cap; placeholders end with "…" |
| Enhance prompt | MagicPrompt | OK; result card with apply / retry / dismiss | Kept; the button disables while running to avoid duplicates |
| Settings (enhancer) | Administrator opens the settings dialog | Admin-only | Kept |
| Insert example | Fill a sample description | Confirms when overwriting | Kept |
| Initial image dropzone | Pick / drop a product photo | OK; **no visible keyboard focus** | Focus ring when focused; highlight while dragging |
| Remove initial image | Clear the photo | OK | Kept, `aria-label` carries the file name |
| Mask canvas (inpaint) | Paint the area to change | OK; undo / clear / brush size | Kept; undo disabled with no history |
| Creativity slider | Image2image strength | OK | Kept, ends labeled "closer to original / freer" |
| Outpaint direction pad | Pick directions | **Defect: buttons inside an `aria-hidden` container** | `role=group`, each direction an `aria-pressed` button |
| Outpaint margin | Pixels to extend | OK | Kept |
| Upscale slider | Factor | OK | Kept |
| Model dropdown | Standard-mode model | OK; hidden for native workflows | Kept |
| LoRA add / remove / weight | Up to 5 | OK; unnamed remove buttons | Kept; "Add" disables at 5 with a reason |
| Width / height / swap | Frame size | OK | Kept; swap button has an `aria-label` |
| Count | 1–4 images | Dropdown | Editable number (integer 1–4); out-of-range values explained before submit |
| Reassign to another server | Failure reason plus one-click resubmission by Rule 1 | New | Under the result status row, styled like the disconnect notice; after a reload or on uncertain results only the reason shows; dismissible |
| Execution server | "Automatic (recommended)" or pin one server | New | Shows with two or more servers; options carry state and queue; the status row names the server that ran the job and hints to drop a pin when a pinned server queues while another is idle |
| Video frames / fps | Video parameters | OK | Kept |
| Advanced (collapsed) | Negative, steps, CFG, sampler, scheduler, seed | OK | Kept; summary shows how many were edited, e.g. "Advanced (2 edited)" |
| Generate image / video | Submit | OK; `→` arrow | No arrow; submission queues immediately without locking the form; brief "Submitting…"; results announced and saved to the gallery; disabled reasons below |
| Submit hint | Why submission is blocked | OK | Kept |
| Privacy note | Prompts never stored in the browser | OK | Kept, small print at the form bottom |

### 3.3 Creation view — preview

| Control / state | Purpose | Current | After |
|---|---|---|---|
| Empty state | Invite creation | Dashed box + plus | Viewfinder corners on the backdrop with one line "fill a description and press generate" |
| Generating | Wait for the engine | Spinner + seconds | Red tally light slowly blinking on the backdrop + seconds; no blink under reduced motion |
| Result card | Show results | **Defect: zoom cursor but no click**; two columns | Click opens the full-size preview; batches lay out 1×1 / 2×1 / 2×2 |
| Download | Download one result | Text link with arrow | "Download" under each result (browsers block multi-download bursts, so no "download all") |
| Continue from this image | Result as image2image input | Only in the gallery | Also under results; loads straight as the initial image |
| View in gallery | Jump to the gallery | None | New, available once saved |
| Status line | Progress and outcomes | OK, `aria-live` | Kept |
| Resume after checking | Anti-double-submit after disconnects | OK | Kept; "open the native engine" opens a new tab |

### 3.4 Gallery

| Control | Purpose | Current | After |
|---|---|---|---|
| All / Starred | Filter | OK | Kept, `aria-pressed` |
| Work source | All sources / Create images / Professional mode (F66) | New | A select; works whose metadata names a template (`ecomcraft_template`) count as "Create images", the rest (professional mode, native engine) as "Professional mode"; "Clear search and show all" also resets the source |
| Search | Filter by file name, 350ms debounce | OK | Kept; own clear button; empty state offers "clear search" |
| Sort | Newest / name | OK | Kept |
| Refresh | Reread the list | Icon button | Kept; spins and disables while loading |
| Batch delete (Select) | Selection mode | Implemented | "Select", for every account; then "Select all visible / Add to current project / Remove from current project / Delete selected / Cancel"; "Delete selected" only shows for accounts allowed to delete, "Remove from current project" only while a project is chosen |
| Current project | Switch the current project and view its works (F04) | New | A select on the row below the toolbar: "All works" and the account's projects (with work counts), remembered in this browser; with a project chosen only its works show, with the project note and the number of works deleted from the gallery (records only) below; "Clear search and show all" also returns to "All works" |
| Manage projects | Create, rename, delete projects (F04) | New | Opens a dialog (Esc closes, focus stays inside); name up to 40 characters, note up to 200; a new project becomes the current one; deleting confirms and only removes the records, the works stay in the gallery |
| Work card | Open the full-size preview | **Defect: a `div`, not keyboard reachable** | Card body becomes a button, Tab-focusable, Enter opens; in selection mode Enter toggles |
| Star | Star / unstar | **Defect: a `span`**; hover-only | A button, always visible, `aria-pressed` |
| Delete button | Delete one work | Implemented, hover-only | Hover or keyboard focus; always on touch |
| Video works | Show videos | **Defect: `<img>` shows a broken thumb** | Muted `<video preload="metadata">` first frame, labeled "video" |
| File name | Identify works | Hover-only | Permanently below the image, ellipsized with a `title` |
| Pending-sync mark | Not yet copied to the work library (R11.5) | New | "Pending sync" (tally color) in the card info row, explained on hover; read when entering |
| Storage settings (admin) | Work library and export locations, sync queue, migration; candidate retention and disk thresholds; backup and restore | New | Opened from "Storage and backup" in the "Manage" menu (F62), server-settings accounts only; "Check and save" may take 30s with reasons on failure; migration polls every 2s and can stop; the status line carries disk-space notices (F46); backup download and restore both confirm (F45) |
| Download diagnostics (admin) | Logs, server states and sanitized config (F44) | New | An entry of the "Manage" menu (F62), administrators only; builds a zip and starts the download, or shows the reason on the gallery page |
| Workflow and execution server | Where a work came from (R7.2) | New | "workflow｜server" under the time from work metadata; standard-workspace works say so; older works show nothing; ellipsized with `title` |
| Empty state | No works / no match | OK | "Nothing yet" (start creating) vs. "no matches" (clear search) |
| Result banner | Delete and star outcomes | OK | Kept, `aria-live` |

### 3.5 Task queue

| Control | Purpose | Current | After |
|---|---|---|---|
| Refresh | Immediate; every 5s while open | OK | Kept; "Updated …" in local time |
| Server list | Name, state, running, waiting | Grid | A table; idle / working / congested / offline / unresponsive pair color and text; "waiting" includes platform-assigned tasks not yet handed to ComfyUI |
| Native tasks | ComfyUI running / waiting with progress | OK | Kept; progress bars with percent text |
| Registered requests | SwarmUI claims | OK | Kept; others' requests show count and state only; order is my requests (green-screen left edge) → server tasks → other accounts' requests (F66) |
| Stop task | End one's own task | OK, confirmed | Kept; "Stopping…" while busy |
| Empty state | No tasks | OK | Kept with guidance |

### 3.5.1 Workflow review

| Control | Purpose | Current | After |
|---|---|---|---|
| Refresh | Reread the list | New | Every 30s while open (paused while a dialog is open); "Updated …" |
| State filter | Pending / published / rejected / unpublished / invalid / all | New | Segmented buttons, `aria-pressed`; wraps when narrow |
| Workflow list | Name, state, source server, runnable servers, submission note, rejection reason | New | Pending, needs-review and invalid states in tally color; offline sources noted |
| Review / re-review / view config | Reviewer opens the dialog | New | Pending items get the primary button |
| Submit / resubmit | Submitter opens the dialog | New | Only for submittable workflows |
| Review dialog | Runnable servers, description, parameter config, rejection reason | New | One row per parameter: "show" checkbox, display name, default, range or options (collapsed); fixed page fields cannot be hidden |
| Publish / reject / unpublish | Review actions | New | Rejection needs a reason; unpublish confirms; published items say "Save config", changed ones "Approve new version" |
| Submit dialog | Note for the reviewer, then submit | New | Buttons disable while submitting |

### 3.6 Full-size preview

| Control | Purpose | Current | After |
|---|---|---|---|
| Star / download / continue / delete / close | Act on the current work | Icon buttons with `title` only, **no `aria-label`**; no `role=dialog` | `role="dialog"`, `aria-modal`, `aria-label`; focus moves in and returns on close |
| Previous / next | Browse neighbors | None | Buttons and arrow keys; arrows stay with focused videos or inputs |
| Zoom (− / % / +) | Resize the image or video | None | Wheel, double click, pinch or buttons; `+` / `-` / `0` keys; pan by dragging or arrows when zoomed; Esc resets first, then closes; videos keep native controls; switching resets to 100% |
| Esc / backdrop | Close | OK | Kept |
| Opened from results | — | Unsupported | Supported; then only download, continue and view-in-gallery show |
| Add to current project | File this work into the project chosen in the gallery (F04) | New | Only while a project is chosen and the work is not in it yet |
| Make again with same settings | Generate again from the filed recipe (F04) | New | Only for images of the current project with a template recipe; opens the simple mode's single-image task filled with the image type, platform, the product from the library and the English prompt, and shows "The next generation reuses the original work's seed … and prompt" (with "Stop reusing"); an untouched prompt is sent as the original prompt, an edited one follows the bilingual panel rules; the seed applies to the next submission only; a disabled image type is explained |

### 3.7 Setup guide dialog

| Control | Purpose | Current | After |
|---|---|---|---|
| Setup status | Execution servers / cloud model readiness | OK | Kept; ready steps say "done" |
| Step 1: execution servers | Explanation + native engine link | OK; numbers are real order | Kept; link opens a new tab |
| Step 2: cloud prompt model | Explanation + settings | OK | Kept |
| Got it / close / Esc | Close | OK | Kept; focus handling like other dialogs |

### 3.7.1 First-setup wizard dialog (administrators, F40)

| Control | Purpose | Current | After |
|---|---|---|---|
| Entry | Opens automatically on an administrator's first visit; reopenable from "View setup guide" | New | Regular accounts keep the two-step guide |
| Step navigation | Language → storage location → register servers → model check → common platforms | New | Header shows "Step n of 5"; "Skip this step" and "Next" advance, the last step is "Finish" |
| Language step | Switch Chinese / English | New | Applies immediately; later steps follow the language |
| Storage step | Explanation + "Open storage settings" | New | Reuses the storage dialog; closing the wizard opens it |
| Servers step | Registering local and LAN servers | New | Reuses the native engine backend settings link |
| Model check step | Missing-template summary (F08) | New | All-ready message or a link to the workflows page |
| Platforms step | Pick common sales platforms | New | Saved in the browser; the simple mode lists them first and preselects the first |

### 3.8 Prompt enhancement settings dialog (administrators)

| Control | Purpose | Current | After |
|---|---|---|---|
| Backend | Pick a provider | OK | Kept |
| Endpoint | Local service address | OK | Kept, `type="url"`, `inputmode="url"` |
| Model + fetch | List available models | OK; disabled while fetching | Kept |
| API key | Provider key | OK | Kept |
| Vision toggle + status | Allow or forbid image description, showing the current vision service and model | Added (F59/F68) | Kept; unchecked hides the vision fields, so saving never requires them |
| Vision model service + address | The service for image description, Ollama or OpenAI-compatible only; its own address when it differs from the prompt service | New | New; the address is required only for an OpenAI-compatible service, Ollama can stay empty (defaults to this machine) |
| Vision model + fetch | Fetch or enter the vision model name (Qwen3-VL recommended) | New | New |
| One-click local model install | Installs Ollama and the chosen vision model on the server (F69) | New | Opens the model tiers (Qwen3-VL 2B / 4B / 8B / 30B, Llama 3.2 Vision, with download-size and VRAM hints); clicking one starts the install, the elapsed time and the tail of the script output show while it runs, the dialog can be closed (the install continues) and reopening restores the progress; when it finishes the vision service switches to Ollama with that model preselected for saving; one install at a time |
| Cancel / save | Close or save | OK; **Esc did not close** | Kept; Esc supported; "Saving…" while busy |

### 3.9 General requirements (all pages)

- Every clickable element is a `<button>` or `<a>`; icon buttons carry `aria-label`; decorative icons are `aria-hidden="true"`.
- Keyboard: everything works with Tab / Enter / Space; focus rings in chroma key, clearly visible.
- Dialogs: focus moves in, Tab cycles inside, focus returns to the trigger; dialog scrolling never scrolls the page (`overscroll-behavior: contain`).
- Animation: `transform` / `opacity` only; no `transition: all`; respects reduced motion.
- Copy: loading states end with "…"; errors give cause and next step; buttons name their outcome, not "OK".
- Dark theme: `color-scheme` follows; dropdowns set explicit colors (Windows dark mode).
- No zoom blocking; click targets are at least 32px.

## 4. Implementation Order

1. Design tokens and base styles (colors, type, buttons, forms, focus) rewrote `studio.css`.
2. Top-bar navigation; fixed the broken menu and theme icon.
3. Creation view: form grouping, mode segments, outpaint pad accessibility, count control, advanced summary.
4. Preview: backdrop, viewfinder empty state, tally light, clickable results and result actions.
5. Gallery: contact sheet, keyboard-operable cards and stars, video thumbs, split empty states.
6. Queue table, full-size preview (previous / next, focus), unified dialogs (Esc, focus cycling).
7. Re-check against the Web Interface Guidelines, add tests, update the README.

## 5. Implementation and Acceptance Notes

### 5.1 Differences from the checklist (kept deliberately or not yet done)

| Rule | Status | Reason |
|---|---|---|
| Submit buttons stay enabled until a request starts | Generate disables while conditions miss, with the reason visible | Generation occupies GPUs; invalid submissions are wasted; reasons stay visible |
| Drag gestures need click / keyboard alternatives | The inpaint mask needs painting | Painting is the feature itself; no substitute |
| Images need explicit dimensions | Results and preview images have none | Sizes come from generation parameters; fixed-height containers prevent layout shifts |
| Filter state in the URL | Views are in the URL; gallery filters and search are not | Out of scope; can be added later |
| Mobile layout | Narrow screens only stay intact | Confirmed decision; camera upload (F42) and the single-column gallery (≤560px) are implemented |

### 5.3 Fixed Defects Along the Way

1. The mobile menu button errored (`$('.rail')` missing) — removed with the sidebar.
2. Theme button showed sun and moon at once.
3. Results looked zoomable but did nothing on click.
4. Gallery videos rendered as broken images.
5. Gallery cards and stars were keyboard-unreachable; outpaint buttons sat inside `aria-hidden`.
6. Preview icon buttons lacked accessible names; the enhancer dialog ignored Esc; dialogs did not take focus.
7. The upload dropzone had no visible keyboard focus.
8. LoRA remove buttons lacked `type="button"` and submitted the form.
9. "Refresh workflows" and "refresh models" duplicated each other.
10. Queue timestamps followed the browser locale (Korean "오후"); now fixed formatting.
11. The footer version was hardcoded; the server now supplies the extension version.

## 6. Changelog

| Date | Version | Notes |
|---|---|---|
| 2026-10-26 | v0.1 | First draft, pending confirmation |
| 2026-10-26 | v0.2 | Per confirmation: count as number input, no dedicated mobile layout, no "download all" |
| 2026-10-26 | v1.0 | Implemented; local preview method, checklist differences and fixed defects added |
| 2026-10-26 | v1.2 | Real-environment fixes: stale cached `studio.js` (served from disk per request now, with a 4-second boot failure notice); native workflow IDs breaking on ` / ` (cleaned-stable IDs); clearer messages for rejected workflows and no runnable server; preview server matches real CSP and self-test (`?selftest=1`, 27 checks) |
| 2026-10-26 | v1.1 | Six gaps fixed: counts hidden before load, 32px targets, remove-image aria, own search-clear, guarded storage access, video arrow keys; added `Tests/studio-ui.test.cjs` |
| 2026-10-28 | v1.11 | Platform export in simple mode step 3 |
| 2026-10-28 | v1.10 | Product library in simple mode: auto-save, pick, rename, delete |
| 2026-10-28 | v1.9 | Multiple photos, one-click sets and the task list |
| 2026-10-28 | v1.8 | Cut-out confirmation and placement adjustment in step 1 |
| 2026-10-28 | v1.7 | Three-step simple mode as the default view; former creation view became the administrators' "professional mode" |
| 2026-10-28 | v1.6 | "Pending sync" marks and the administrators' storage dialog in the gallery |
| 2026-10-28 | v1.5 | Chinese / English interface: header toggle; all text through the translation table (`i18n.js`, `lang/en.js`); `Tests/studio-i18n.test.cjs` keeps English mode free of Chinese |
| 2026-10-27 | v1.4 | Failure notice with "reassign to another server"; gallery cards show "workflow｜server" |
| 2026-10-27 | v1.3 | Execution server choice (automatic or pinned) with the server that ran each job and busy hints; queue states computed server-side as idle / working / congested, "waiting" counting platform-assigned tasks |
| 2026-10-28 | v1.12 | Usability and operations controls: camera upload and single-column narrow gallery (F42), upload quality notices (F38), keep-forever on pick (F39), administrator first-setup wizard (F40), storage dialog extended with disk thresholds and backup/restore (F45/F46), gallery "Download diagnostics" button (F44), batch completion banners and background title progress (F43); English version added |
| 2026-10-28 | v1.13 | Simple-mode redesign v3, step one (F53/T55): "Style" became a two-level "Product type" (category chips + subtype cards, `Templates/product-types.json`), the visual style is now optional (first card "Auto"), and a "Needs a model" checkbox mutually exclusive with white main images was added; subtype and model fragments join the composed prompt; new `EcomCraftStudioProductTypes` API returning IDs and names only |
| 2026-10-28 | v1.14 | Simple-mode redesign v3, step two (F55/T57): the candidate count became a number input (positive integers 1–20 with immediate notices and disabled buttons on invalid input); single generations and one-click sets share a 20-image cap (enforced server-side too; the workflow path widened to 1–20 per batch); the hints under "Start" and "Whole set" name the current product and the estimated totals respectively |
| 2026-10-28 | v1.15 | Simple-mode redesign v3, step three (F54/T56): "AI expand" next to the one-line description; a Chinese / English tabbed prompt panel with a status chip (edited Chinese is translated by the server on submit, edited English is sent as is); step 3 gained a folded "Prompt sent to the engine" block; new `EcomCraftStudioExpandPrompt` API |
| 2026-10-28 | v1.16 | Simple-mode redesign v3, step four (F56/T58): submissions return at once; step 3 gained a stage list for single generations (Writing the prompt → Queued / Running with the server's name → Done); set tasks gained a cancellable "Writing the prompt…" state |
| 2026-10-28 | v1.17 | Simple-mode redesign v3, step five (F52/T54): step 1 offers four equal entries (this computer, camera, product library, gallery); the product library moved from a folded list into a picker dialog; gallery picks are compressed to within 5 MB in the browser; thumbnails show their source |
| 2026-10-28 | v1.18 | Selling points and set storyboard (F25/T30): step 2 gained the optional "Selling points and storyboard" section and "Generate set from storyboard"; the task list shows each shot's purpose; new `EcomCraftStudioStoryboard` API |
| 2026-10-28 | v1.19 | Brand kits (F12/T17): step 2 gained the brand kit list and management dialog; new `EcomCraftStudioBrandKits` / `EcomCraftStudioBrandKitSave` / `EcomCraftStudioBrandKitDelete` APIs |
| 2026-10-28 | v1.20 | Selling-point images (F47/T50): step 3 gained the "Make a selling-point image" section with three fixed layouts, the server draws the text with Noto Sans SC; new `EcomCraftStudioSellingImage` API |
| 2026-10-29 | v1.21 | Task-first simple mode (F61/T64): three task cards at the top, step 2 fields by task, secondary options under "More options"; requirements in the EcomCraft UI and Workflow Redesign |
| 2026-10-29 | v1.22 | Navigation and "Manage" menu (F62/T65): the top bar is Create images / Professional mode / Gallery / Queue, and administration plus the native engine moved into the administrators' "Manage" menu; consistent wording ("standard workspace" is now "standard parameters", the empty gallery points to "Create images") |
| 2026-10-29 | v1.23 | Professional mode open to every account, with workflow upload and "My workflows" (F60/T63) |
| 2026-10-29 | v1.24 | Step 3 gained "Refine in professional mode" (F64/T67) |
| 2026-10-29 | v1.25 | Star button on candidates (F65/T68) |
| 2026-10-29 | v1.26 | Phones get simple mode only (F63/T66): narrow screens hide the professional mode entry, the lightbox "Create from this image" and "Refine in professional mode" |
| 2026-10-29 | v1.27 | Gallery source filter, own requests first in the queue (F66/T69) |
| 2026-10-29 | v1.28 | My workflows: submit for review and review state (T73) |
| 2026-10-29 | v1.29 | "Manage" menu gained the "Standard workflows" dialog (F57/T59) |
| 2026-10-29 | v1.30 | The image-description notice's "prompt enhancement settings" steps now open the dialog directly; the settings dialog gained "One-click local model install" (F69/T71) |
| 2026-10-29 | v1.31 | "Add a reference" checks the vision model first and shows the guidance when unset; unchecked image description saves without vision fields; the Ollama vision address can stay empty |
