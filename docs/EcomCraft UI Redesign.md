# EcomCraft UI and Workflow Redesign (v4)

> **Language:** English · [简体中文](EcomCraft%20界面改版需求.zh-CN.md)
>
> **Status:** Confirmed (2026-10-29), built task by task · **Version:** v1.1 · **Date:** 2026-10-29
>
> Related: [EcomCraft Architecture and Roadmap](EcomCraft%20Architecture.md) (decisions A2, A12), [EcomCraft Workspace UI Design](EcomCraft%20Workspace%20UI%20Design.md) (control list), [EcomCraft development table](EcomCraft%20开发总表.xlsx) (F60–F69 / T63–T72).

## 1. Background

There are three kinds of users (architecture A2): operators and shop owners create images quickly in simple mode, graphic designers create richer results in professional mode, and administrators install and configure. Pricing is per account with no complex roles or permissions (A12).

After the simple mode redesign v3, step 2 holds more than a dozen controls (platform, references, image type, product type, style, model, brand kit, one-line description, AI expand, bilingual prompt, count, one-click set, selling points and storyboard), so it is no longer simple. What operators and owners do most is **follow a competitor's reference image**, yet that entry sits in the middle. This redesign reworks simple mode, professional mode, the gallery, the queue and the administration screens together.

## 2. Confirmed Decisions

| ID | Decision |
|---|---|
| U1 | Scope: simple mode, professional mode, gallery, queue and administration together; built task by task as listed in section 7, directly in code without a separate prototype. |
| U2 | Keep the visual direction: the neutral grey "photo studio" style (UI design document section 2); only layout and hierarchy change. |
| U3 | Simple mode is "choose a task, then fill it in": three task entries at the top, **Follow a reference** (first and default), **Make one image** and **Make a full set**; each task shows only the options it needs. |
| U4 | Visual style, model, brand kit, one-line description with AI expand, and the bilingual prompt are folded under "More options" by default, with the open state remembered in the browser; selling points and storyboard appear only in "Make a full set". |
| U5 | Candidates can be starred after generation (the same star as the gallery) to weed out weak results. Personal analysis of stars feeding back into prompts is a to-do (F67, draft in section 6). |
| U6 | Designers (any account) may upload a ComfyUI workflow JSON in professional mode and **use it right away themselves** ("My workflows", visible to the uploader only); other accounts can use it after an administrator reviews and publishes it. Own workflows get the same server-side structure checks and an automatically generated parameter whitelist, run only on registered execution servers, and are rejected when they contain node types the administrator disabled (by default nodes that can run arbitrary code or read and write arbitrary paths); generation requests still send only the workflow ID and whitelisted parameters. (Revised 2026-10-29: formerly "usable only after publishing".) |
| U7 | One-way hand-off from simple to professional mode: "Refine in professional mode" on a candidate carries the image and the English prompt actually sent; professional mode never writes back. |
| U8 | The "Native engine" link is shown to administrators only. |
| U9 | Phones get simple mode only (plus gallery viewing and the queue); narrow screens hide the professional mode entry and ask the user to switch to a computer. |
| U10 | Consistent wording (section 3). |
| U11 | Administration moves into an "Admin" menu at the right of the top bar, visible to administrators only (section 4). |
| U12 | Image description is on by default (used to borrow compositions from references); administrators can turn it off in the prompt enhancement settings. When no vision model is available, the page says what is missing and how to fix it: administrators see the concrete steps (run the one-click install script, or install Ollama and pull a vision model, or switch to another vision service in the prompt enhancement settings) and other accounts see "ask your administrator". Sending product photos to the vision model to describe products stays off by default (A5). |

## 3. Wording

| Use | Avoid | Notes |
|---|---|---|
| Create images | Create, generate (inside simple mode) | Simple mode navigation and buttons |
| Professional mode | Creation view, standard workbench | The designers' view |
| Image type | Template (to operators) | "Template" appears only in administration |
| Workflow | — | Only in professional mode and administration |
| Reference image | Competitor image, sample | The image to follow |
| Star | Like, favorite | Shared by candidates and the gallery |
| Execution server | Backend, GPU computer | In administration and the queue |

## 4. Navigation and Administration

- Top bar: **Create images**, **Professional mode** (every account, hidden on narrow screens), **Gallery**, **Queue**.
- Right side: server status, language, theme, account name; administrators also get an "Admin" menu: workflow review, first-setup wizard, prompt enhancement settings, storage and backup, native engine (new tab).
- Workflow upload lives in professional mode (U6) and the uploader can use it at once; review and publishing for everyone lives in the "Admin" menu.

## 5. Simple Mode

Three task cards sit at the top; the chosen task is remembered in the browser and the first visit defaults to "Follow a reference". All tasks share step 1 (upload product photos) and step 3 (pick and download); step 2 depends on the task:

| Step 2 content | Follow a reference | Make one image | Make a full set |
|---|---|---|---|
| Platform | Shown | Shown | Shown (with optional slots) |
| References and per-reference prompts | Shown (required, 1–8) | — | — |
| Image type | — | Shown | — (the platform's set) |
| Product type | — | Shown | Shown |
| More options (style, model, brand kit, one-line description and AI expand, prompt) | — | Folded | Folded |
| Candidate count | — (one per reference) | Shown | Shown |
| Selling points and storyboard | — | — | Shown (folded) |
| Main button | Generate by reference | Start | One-click set / Set by storyboard |

- Switching tasks keeps uploaded product photos and references; references only apply in "Follow a reference".
- Step 3: candidates can be starred (U5), downloaded, exported to a platform, turned into a selling-point image, or refined in professional mode (U7).

## 6. Stars and Personal Analysis (F67, draft to-do)

This section is a direction only; confirm it again before development, and add data rules to AGENTS.md first.

1. **Star as a first filter (F65, in this redesign)**: the star on candidates reuses the gallery star, and starred images are never removed by candidate cleanup.
2. **Preference counts (phase one)**: per account, count which image types, product types, styles, model choice and platforms get starred most, storing only option IDs and counts, never images or prompts; used to sort or preselect favorite options.
3. **Prompt feedback (phase two)**: compare prompts of starred and unstarred works to find phrases that appear more often in starred ones (light, background, composition words) and suggest them as "descriptions you may like"; they are added to the one-line description or brand kit only after the user confirms, never silently. Because this reads prompts, its records follow the project archive rules (owner only, deletable, written through a temporary file).
4. **Model level (long term)**: training a brand LoRA from starred works belongs to F17 (custom service) and is out of scope here.

## 7. Tasks

| Task | Feature | Content | Order |
|---|---|---|---|
| T64 | F61 | Simple mode task-first: three task cards, step 2 by task, "More options" fold | 1 |
| T65 | F62 | Navigation, "Admin" menu and wording; native engine for administrators only | 2 |
| T63 | F60 | Professional mode for every account; ComfyUI workflow upload, usable by the uploader at once | 3 |
| T67 | F64 | One-way hand-off from simple to professional mode | 4 |
| T68 | F65 | Star candidates (first filter) | 5 |
| T66 | F63 | Phones get simple mode only | 6 |
| T69 | F66 | Gallery and queue cleanup (star filter, own tasks first) | 7 |
| T71 | F68 | Image description on by default, with setup guidance | 1.5 (right after T64) |
| T72 | F69 | One-click vision model install script (installs Ollama and pulls the default vision model) | 2.5 (right after T71) |
| T70 | F67 | Star preference analysis and prompt feedback | Deferred, design pending |

## 8. Change Log

| Date | Version | Notes |
|---|---|---|
| 2026-10-29 | v1.0 | First version: decisions U1–U11, wording, navigation and administration, simple mode by task, stars and personal analysis draft, task list |
| 2026-10-29 | v1.1 | U6 now lets the uploader use a workflow at once (other accounts still need review and publishing); new U12 image description on by default with setup guidance, F68/T71, and the one-click install script F69/T72 |
