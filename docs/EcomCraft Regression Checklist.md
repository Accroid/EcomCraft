# EcomCraft Regression Checklist

> **Language:** English · [简体中文](EcomCraft%20回归清单.zh-CN.md)
>
> **Purpose:** checkpoints CP0–CP3 of the [EcomCraft Refactoring Plan](EcomCraft%20Refactoring%20Plan.md) use it to confirm the refactoring has not changed behavior.

## How to use it

1. Run on a computer with a working GPU, with the same models and data each time, and keep the browser's developer console open.
2. Prepare one administrator account, one regular account, one transparent product image, one ordinary product photo and two reference images.
3. Work through the items, recording pass, fail or not tested in the "Result" column, along with the commit.
4. **CP0 records the baseline:** items known to fail stay in the table with a note of what happens. Later checkpoints only judge whether things got worse.

This checklist describes the current interface. Once the single-entry redesign (T89, T90) is done, items 3–5 must be rewritten.

## Checklist

Record: commit ______ · date ______ · system and GPU ______

| # | Flow | Steps | Expected | Result |
|---|---|---|---|---|
| 1 | Login and page | Log in and open the workspace; switch between Chinese and English | No console errors; no Chinese left in English mode | |
| 2 | Product upload | Upload from this computer, the product library and the gallery; auto-cut out an ordinary photo; drag to adjust placement | Previews are right; cutout succeeds; position and size follow the drag and apply to the output | |
| 3 | Follow a reference | Upload 2 references, wait for their descriptions and generate; submit again and cancel one task | One image per reference with the product intact; the cancelled task stops and others are unaffected; references do not appear in the product library or project archive | |
| 4 | Make one image | Choose platform, output size and image type, then generate | Output size matches the choice; after paste-back the logo and text stay sharp | |
| 5 | Make a full set | Generate a set with a white-background image; generate a set with a storyboard; retry after one task fails | All platform slots are filled; the white-background image is pure white; the storyboard applies; only failed items retry | |
| 6 | Candidates | Favorite, download, make a selling image, refine in professional mode, package for a platform | Each action works; refine carries the image and prompt; the package matches the slots | |
| 7 | Professional mode | Try text-to-image, image-to-image, inpainting, outpainting and upscaling | All produce images and parameters apply | |
| 8 | Own workflow | Upload a ComfyUI workflow, run it, then submit it for review | Validation passes and the uploader can use it at once; other accounts cannot see it; a pending item appears in the review list | |
| 9 | Workflow review | As administrator, publish, reject and take down workflows; run "Initialize standard workflows" | States are right; rejection reasons reach only the submitter and reviewer; taken-down items return only through initialization | |
| 10 | Brand and projects | Create a brand kit and generate; add a work to a project and redo with the same parameters | Brand fragments and images apply; redo restores template, platform, product, prompt and seed | |
| 11 | Queue and gallery | View the queue; reassign a failed task to another server; filter and bulk-edit the gallery | Only your own tasks show; reassignment works (not applicable with one server); filters and bulk actions are right | |
| 12 | Settings | Open prompt enhancement, vision model, storage and the setup wizard; create a diagnostics package; back up and restore | Settings save; the package holds no prompts or images; data matches after restore | |
| 13 | Narrow screen | Shrink the window to phone width, or open on a phone | Simple mode only; professional entries are hidden; `#create` returns to the generation page | |
| 14 | Regular account | Log in as a regular account and browse each page | No "Manage" entry and no other users' tasks; administrator API calls are refused | |

## Known baseline problems

Fill in at CP0:

| # | What happens | Date recorded |
|---|---|---|
| | | |
