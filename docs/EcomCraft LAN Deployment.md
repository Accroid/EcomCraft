# EcomCraft LAN Deployment Guide

> **Language:** English · [简体中文](EcomCraft%20局域网部署指南.zh-CN.md)

This guide explains how to deploy EcomCraft on a team's local network so several people can use it at the same time. It covers enabling account login, limiting how much each person can queue, firewalling execution servers, who can see works kept on shared storage, and which browser features are restricted when the workspace is opened through an `http://` LAN address.

For SwarmUI's account system in general, see [Sharing Your Swarm](Sharing%20Your%20Swarm.md); for the network settings that let other devices connect, see [Advanced Usage: Accessing SwarmUI From Other Devices](Advanced%20Usage.md#accessing-swarmui-from-other-devices). This guide only adds what EcomCraft needs and does not repeat them.

## 1. Deployment layout

| Role | Purpose | Example |
|---|---|---|
| Platform server | Runs SwarmUI + EcomCraft and handles accounts, review, scheduling and saving works; it can also be an execution server | A: `192.168.1.10` |
| LAN execution server | Only has ComfyUI installed and is called by the platform server | B: `192.168.1.21:8188` |
| Team members' computers or phones | Open the workspace on the platform server in a browser | `http://192.168.1.10:7801/EcomCraft` |

Team members only access the platform server. ComfyUI on the execution servers is reachable only by the platform server, never opened by team members directly.

**Do not expose any server to the internet**: do not forward ports on the router, and do not open ComfyUI ports to the whole network. SwarmUI is currently only validated for "sharing with friends and family", and ComfyUI has no login of its own.

## 2. Platform server: make it reachable on the LAN

1. In the native engine under **Server → Server Configuration → Network**, set `Host` to `*` (Windows) or `0.0.0.0` (Linux); `Port` defaults to `7801`. Save and restart the service.
2. In the platform server's firewall, allow only the LAN to reach that port. Windows example (PowerShell as administrator):

   ```powershell
   New-NetFirewallRule -DisplayName "EcomCraft 7801 (LAN)" -Direction Inbound -Protocol TCP -LocalPort 7801 -RemoteAddress LocalSubnet -Action Allow
   ```

3. On another computer, open `http://192.168.1.10:7801/EcomCraft` (use your platform server's address) and confirm you see the login page or the workspace.

## 3. Enable account login (R9.1)

With several users, account login must be enabled, with one account per person. The steps are in the "Basic Setup Guide" of [Sharing Your Swarm](Sharing%20Your%20Swarm.md#basic-setup-guide); the key points:

1. **Set your own password first**: under **Server → Users**, set a password for the default `local` account; you can also create an account with the `Owner` role for yourself.
2. **Turn on login**: under **Server → Server Configuration → User Authorization**, enable `AuthorizationRequired`.
   - `AllowLocalhostBypass` is on by default: opening the page on the platform server itself needs no login. If someone uses the platform server day to day, turn it off.
   - `AuthBypassIPs` in the **Network** group lists addresses that skip login, by default only the local machine. **Do not add LAN addresses**, or anyone at those addresses gets administrator access without logging in.
3. **Create an account for each member**: ordinary members get the `User` role; people who review workflows or manage servers get `Admin`.
4. **Grant EcomCraft permissions** per role under **Server → Users → Roles**:

| Permission | Purpose | Default |
|---|---|---|
| `ecomcraft_use_published_workflows` | Run published workflows in the workspace; with generation rights, upload own workflows in professional mode | Ordinary users |
| `ecomcraft_submit_workflows` | Submit workflows the user saved in ComfyUI or uploaded in professional mode for review | Ordinary users |
| `ecomcraft_review_workflows` | Review, publish, reject and unpublish workflows, and configure user-visible parameters | Administrators |

Generating also needs SwarmUI's basic generation permission, which the `User` role has by default. Do not give ordinary members the native ComfyUI workflow permissions: they can bypass EcomCraft's parameter allowlist.

If you lock yourself out after enabling login, stop the service, open `Data/Settings.fds` in a text editor, set `AuthorizationRequired` back to `false`, and start the service again.

## 4. Per-person queue limit (R9.2)

How many images each person can have queued at once is set by the `MaxQueued` value of their role. Set it per role under **Server → Users → Roles**:

- Defaults: `User` and `PowerUser` are 100,000 (effectively unlimited), `Owner` and `Admin` are higher, `Guest` is 10.
- Give `User` a smaller value, for example `8`–`20`, so one person cannot occupy every execution server with a single large submission.
- The count is in images: one submission of 4 images counts as 4. An account with several roles gets the largest value among them.
- Above the limit, the workspace says the queued tasks have reached the limit allowed for the account's role (error code `queue_limit`) and nothing is submitted.
- The same role also has `MaxT2ISimultaneous`, which limits how many images one person can have actively generating; the default usually needs no change.

## 5. Firewall for LAN execution servers

ComfyUI has no login: anyone who can reach its port can submit any workflow and read its saved workflows and outputs. So the ComfyUI port of every LAN execution server must be **open only to the platform server**.

1. Start ComfyUI on the execution server with `--listen 0.0.0.0` (choose the port with `--port` if needed).
2. Configure the firewall to allow only the platform server to reach the ComfyUI port.
   - Windows example (PowerShell as administrator; replace the address and port with yours):

     ```powershell
     New-NetFirewallRule -DisplayName "ComfyUI only from EcomCraft platform" -Direction Inbound -Protocol TCP -LocalPort 8188 -RemoteAddress 192.168.1.10 -Action Allow
     ```

     The first time ComfyUI listens on the network, Windows may ask whether to allow Python to communicate. If "Allow" was clicked, Windows created a program rule open to every address; find it among the inbound rules in "Windows Defender Firewall with Advanced Security" and delete it or restrict it to the platform server's address.
   - Linux (ufw) example:

     ```bash
     sudo ufw allow from 192.168.1.10 to any port 8188 proto tcp
     sudo ufw deny 8188/tcp
     ```

3. On the platform server, register the server as a **ComfyUI API By URL** backend and enter its display name as the backend title. Set the name before reviewing the workflows on that server and do not change it afterwards: workflow IDs start with the server name. For detailed steps and the address ranges that can be registered, see [Workflows: LAN execution servers](EcomCraft%20ComfyUI%20Workflows.md#lan-execution-servers).
4. Check: on another member's computer, `http://192.168.1.21:8188` should **not** open; on the platform server it should.

## 6. Who can see works on shared storage (D9)

In the workspace, works are visible only to their owner by default. That is only a platform-level restriction; **the storage layer is not isolated per user**:

- Works are currently saved in the platform server's output folder (by default `Output/`, one subfolder per account). Anyone who can access that folder directly (for example other Windows users on the platform server, or anyone if you shared it) can see everyone's works. Do not share it.
- Administrators can set the work library to a NAS or a shared folder on another computer under "Storage settings" on the workspace gallery page (on Windows use the `\\server\share\folder` form, mapped drive letters are not supported; on Linux / macOS mount it in the system first). Saving checks writability, free space and write speed, and nothing is saved when a check fails. Works are saved on the platform server first and copied in the background, so generation continues while the share is offline; the gallery shows "Pending sync" and the copies catch up once it is back.
- Whoever can access the library's shared folder can see everyone's works. To restrict it, grant access on the NAS only to the account the platform server's service runs as, not to team members' accounts. The platform stores no NAS password; the operating system handles authentication to the share: on Windows let the service account access the share (for example run the service as a domain or local account with access), and on Linux / macOS mount it in the system with suitable permissions.
- No workspace API ever returns storage paths to ordinary users.

## 7. Browser restrictions with an `http://` LAN address

Browsers treat addresses like `http://192.168.x.x` as an "insecure context" and disable some features (`http://localhost` is the exception).

| Feature | Under an `http://` LAN address | Effect on EcomCraft |
|---|---|---|
| Writing to the clipboard (copy image, copy text) | Unavailable | Not used by the workspace; buttons such as "copy image" in the native SwarmUI interface stop working, use download instead |
| Opening the camera from the page (`getUserMedia`) | Unavailable | Not used by the workspace. The "Take a photo" button invokes the camera through a file picker with the `capture` attribute, which is unaffected; phones can upload, generate and browse the gallery |
| Browser system notifications (Notification) | Unavailable | Not used by the workspace. The planned "completion notice" can only be shown inside the page under `http://` |
| Installing as an app (PWA / Service Worker) | Unavailable | No effect |
| Upload, download, generation, gallery, queue | Work normally | No effect |

If you need these features, configure HTTPS for the platform server: put a reverse proxy (for example Caddy, Nginx or IIS) in front of it with a certificate issued by an internal certificate authority, and make every member device trust that certificate. A self-signed LAN certificate that devices do not trust keeps triggering browser warnings and still counts as an insecure context. Do not expose the service to the internet just to get HTTPS.

## 8. Deployment checklist

- [ ] The platform server's `Host` allows LAN access, and its firewall opens `7801` only to the LAN
- [ ] The `local` account has a password, `AuthorizationRequired` is on, and `AuthBypassIPs` lists only local addresses
- [ ] Every member has their own account; the review permission is given only to those who need it
- [ ] `MaxQueued` of the `User` role is set for the team size
- [ ] Every LAN execution server runs ComfyUI with `--listen`, its firewall lets only the platform server reach the ComfyUI port, and opening that port from a member's computer fails
- [ ] Execution servers have their display names set before their workflows are reviewed
- [ ] The platform server's output folder is not shared with team members
- [ ] No server has a port forwarded to the internet
