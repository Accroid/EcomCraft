# EcomCraft UI and Workflow Redesign (v4)

> **Language:** English · [简体中文](EcomCraft%20界面改版需求.zh-CN.md)
>
> **Status:** Confirmed (2026-10-29); code complete pending acceptance, T70 deferred; revised 2026-10-31 (A14): the single-entry design of U3/U4/section 5 replaces the former "three task entries" — see T89/T90 (implemented once G1 passes; G2 acceptance follows when they are done) · **Version:** v1.4 · **Date:** 2026-10-31
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
| U3 | The simple-mode generation page is a single entry (revised 2026-10-31 (A14), replacing the former "choose a task first": the three task cards become "follow a reference" only): three steps — upload the product → pick platform and size, upload references (one makes a single image, several make a set, slots editable) → pick and export. The "make one image" and "make a full set" entries are removed; sets are reference-driven (see the [EcomCraft Implementation Plan](EcomCraft%20Implementation%20Plan.md) 6.1 and T89). |
| U4 | Image type, visual style, model checkbox, brand kit, one-line description with AI expand, bilingual prompt and selling-point storyboard are hidden from simple mode (revised 2026-10-31 (A14); formerly folded under "More options"); the code stays for now and G2 decides on cleanup; features tied to the old entries migrate per T90. |
| U5 | Candidates can be starred after generation (the same star as the gallery) to weed out weak results. Personal analysis of stars feeding back into prompts is a to-do (F67, draft in section 6). |
| U6 | Designers (any account) may upload a ComfyUI workflow JSON in professional mode and **use it right away themselves** ("My workflows", visible to the uploader only); other accounts can use it after an administrator reviews and publishes it. Own workflows get the same server-side structure checks and an automatically generated parameter whitelist, run only on registered execution servers, and are rejected when they contain node types the administrator disabled (by default nodes that can run arbitrary code or read and write arbitrary paths); generation requests still send only the workflow ID and whitelisted parameters. (Revised 2026-10-29: formerly "usable only after publishing".) |
| U7 | One-way hand-off from simple to professional mode: "Refine in professional mode" on a candidate carries the image and the English prompt actually sent; professional mode never writes back. |
| U8 | The "Native engine" link is shown to administrators only. |
| U9 | Phones get simple mode only (plus gallery viewing and the queue); narrow screens hide the professional mode entry and ask the user to switch to a computer. |
| U10 | Consistent wording (section 3). |
| U11 | Administration moves into an "Manage" menu at the right of the top bar, visible to administrators only (section 4). |
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
- Right side: server status, language, theme, account name; administrators also get an "Manage" menu: workflow review, first-setup wizard, prompt enhancement settings, storage and backup, native engine (new tab).
- Workflow upload lives in professional mode (U6) and the uploader can use it at once; review and publishing for everyone lives in the "Manage" menu.

## 5. Simple Mode (single entry, revised 2026-10-31 (A14), to be implemented by T89)

The page no longer has three task cards; reference mode is the default. Three steps:

1. **Upload the product**: transparent PNGs are used directly (no cut-out); normal photos are still auto-cut; the product library and camera entries stay.
2. **Platform and size → references**: pick the platform and output size first (keeping F73's "size before generating"); upload references — one makes a single image, several make a set — each showing its auto-detected slot (editable); "similarity" offers "borrow composition only / highly similar (default)"; an optional "also make a white-background image" needs no reference and uses the existing CutoutOnly; sets show the selling-point form (T99).
3. **Pick and export**: candidates can be starred, downloaded, re-made, "edit this one" (into professional mode), or packaged per platform.

- Image type, style, model, brand kit, one-line description, AI expand, bilingual prompt and storyboard are hidden (code kept, U4).
- Without a vision model there is no full-page block anymore (the new route's slot detection does not need one), only "pick the slot manually" (T89).
- Narrow screens likewise get the single entry only (U9); features tied to the old task cards migrate per T90.

(History: the former section 5 — three task cards with step 2 per task — was implemented (T64) and is superseded by the single entry above; see Git history for the original text.)

## 5.1 Own Workflows (F60, T63)

- The entry sits under the workflow picker in professional mode: "Upload workflow" takes a `.json` saved with ComfyUI's Save menu, named after the file; "My workflows" below lists them with how many execution servers can run them or which nodes and models are missing, and each can be deleted. In the picker own workflows form their own "My workflows" group.
- Only the Save format is accepted; the API export is refused with a hint to use Save. Unsupported structures, files over 2 MB and more than 20 per account are refused with the reason.
- Deny rules (three layers): node types whose words include `python`, `exec`, `execute`, `eval`, `script`, `shell`, `command`, `cmd`, `code` or `subprocess` are refused; text inputs that look like file paths (drive letters, network shares, absolute paths starting with `/`, or `..`) are refused; administrators can add node types in the server setting `EcomCraft.DeniedWorkflowNodes`. A refusal lists the nodes and input names.
- Own workflows are visible to the uploader only: the catalog, generation, parameter provider and execution server check all verify the owner; they never appear in the review list, review calls refuse them, and templates cannot reference them. The workflow JSON stays on the server and is never returned to clients.
- Review and publishing (T73): in "My workflows", one never submitted or rejected can be "Submitted for review" with an optional note; administrators see it under "Manage → Workflow review" with the source "uploaded by an account", configure parameters and approve or reject it like any workflow. Approval copies it into a published snapshot `上传/<name>` for every account, and unpublishing acts on that copy; the uploader keeps the own original. The rejection reason goes to the uploader only, who fixes it, uploads again and resubmits.

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
| T65 | F62 | Navigation, "Manage" menu and wording; native engine for administrators only | 2 |
| T63 | F60 | Professional mode for every account; ComfyUI workflow upload, usable by the uploader at once | 3 |
| T67 | F64 | One-way hand-off from simple to professional mode | 4 |
| T68 | F65 | Star candidates (first filter) | 5 |
| T66 | F63 | Phones get simple mode only | 6 |
| T69 | F66 | Gallery and queue cleanup (star filter, own tasks first) | 7 |
| T71 | F68 | Image description on by default, with setup guidance | 1.5 (right after T64) |
| T72 | F69 | One-click vision model install script (installs Ollama and pulls the default vision model) | 2.5 (right after T71) |
| T70 | F67 | Star preference analysis and prompt feedback | Deferred, design pending |
| T73 | F60 | Submit an own workflow for review and publish it to everyone once approved | Right after T63 |

## 8. Change Log

| Date | Version | Notes |
|---|---|---|
| 2026-10-29 | v1.0 | First version: decisions U1–U11, wording, navigation and administration, simple mode by task, stars and personal analysis draft, task list |
| 2026-10-29 | v1.1 | U6 now lets the uploader use a workflow at once (other accounts still need review and publishing); new U12 image description on by default with setup guidance, F68/T71, and the one-click install script F69/T72 |
| 2026-10-29 | v1.2 | New 5.1 own workflow rules (Save format only, three-layer deny rules, 20 per account, 2 MB); submitting for review and publishing split into T73 |
| 2026-10-29 | v1.3 | 5.1 adds own workflow review and publishing (T73) |
| 2026-10-31 | v1.4 | Single-entry revision (T81, decision A14): U3 becomes the "follow a reference" single entry; U4 becomes hiding the old controls (code kept); section 5 becomes the three-step single entry; the tied-feature migration and implementation are in the Implementation Plan's T89 and T90 |
