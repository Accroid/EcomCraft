# EcomCraft Upgrade and Data Guide

> **Language:** English · [简体中文](EcomCraft%20Upgrade.zh-CN.md)

This guide explains which data survives an EcomCraft / SwarmUI upgrade and how to back up and restore system data. It is written for administrators of a LAN deployment.

## Where the data lives

| Data | Location | On upgrade |
|---|---|---|
| System data (account database, review snapshots, templates, storage settings, product library index, candidate registry) | The platform server's `Data/` folder (including `Data/EcomCraft/`) | Kept; never delete it |
| Generated works | The platform server's output folder; additionally copied to the NAS or shared folder when a work library is set | Kept |
| Models | `Models/` (or each execution server's own model folder) | Kept |
| Program code | Repository folders such as `src/` | Replaced by the upgrade |

`Data/`, `Output/` and `Models/` are not code: upgrading (pulling new code and rebuilding) does not touch them. Do not edit or delete files inside these folders by hand.

## Upgrade steps

1. Back up the system data (see below) and keep the downloaded zip somewhere safe.
2. Stop the running service (close its window or stop the Windows service).
3. Pull the new code and check out the `dev` branch (`git fetch && git checkout dev && git pull`).
4. Start with the same launcher as before (for example `launch-windows.bat`). In Release mode extension assemblies are cached by Git commit, so a changed commit rebuilds them automatically; the build takes a few minutes.
5. Open the workspace, check the version and your usual features, then let the team continue.

### What to expect afterwards

- **Workflow re-review**: content fingerprints are computed over the converted execution graph. If an upgrade changes the conversion logic, published workflows may show "needs re-review"; until re-reviewed, users keep running the old snapshot, so generation is unaffected.
- **Storage locations stay**: the work library and export locations live in `Data/EcomCraft/storage.json` and keep working after the upgrade.
- **Candidate cleanup**: the cleanup registry lives in `Data/EcomCraft/candidates.json`; works that were picked, downloaded or exported are never affected.

## Backing up system data

An administrator clicks **Back up and download** under **Gallery → Storage settings → Back up and restore system data**:

- The zip contains the configuration, review snapshots, templates and product library index under `Data/EcomCraft/`, plus the account database (`Users.ldb` etc.) and the role settings (`Roles.fds`).
- It does not contain generated works, product image files or models; keep works through the work library location or a separate copy.
- It also contains the project archive records (`projects.json`), which include the prompts staff works were made with; only administrators can create and download backups, so keep backup files safe.
- Every file's size and SHA-256 are recorded in a manifest (`manifest.json`) and checked one by one on restore.
- The zip stays on the server for one day only; download it promptly.

## Restoring system data

1. An administrator clicks **Choose a backup to restore** under **Gallery → Storage settings** and picks the downloaded backup zip.
2. After confirming, the server validates the whole backup first (any size or content mismatch aborts the restore without changing anything) and then writes into `Data/EcomCraft/`.
3. The account database files are locked while the server runs and cannot be replaced directly: they are parked in `Data/EcomCraft/restore-staging/`. For a full account restore, stop the service, copy those files over the same-named files in `Data/`, and start again.
4. Restart the service after restoring so templates, review snapshots and storage settings all take effect.

A restore only touches system data; it never deletes or overwrites generated works.

## Related guides

- [EcomCraft LAN Deployment](EcomCraft%20LAN%20Deployment.md)
- [EcomCraft Architecture](EcomCraft%20Architecture.md)
- [EcomCraft ComfyUI Workflows](EcomCraft%20ComfyUI%20Workflows.md)
