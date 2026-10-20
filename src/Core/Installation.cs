using FreneticUtilities.FreneticExtensions;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Backends;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Text2Image;
using SwarmUI.Utils;
using SwarmUI.WebAPI;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Runtime.InteropServices;

namespace SwarmUI.Core;

/// <summary>Core helper class for handling installing a new instance of SwarmUI.</summary>
public class Installation
{
    /// <summary>The current websocket trying to install Swarm.</summary>
    public static WebSocket InstallSocket = null;

    /// <summary>Tracker of step progress.</summary>
    public static int StepsThusFar;

    /// <summary>Tracker of how many steps are expected.</summary>
    public static int TotalSteps;

    /// <summary>Language code selected for the current install/upgrade, used to translate server-side installer messages.</summary>
    public static string InstallLanguage = "en";

    /// <summary>Chinese translations of server-side installer messages, keyed by the English message template.</summary>
    public static Dictionary<string, string> InstallMessagesZh = new()
    {
        ["Installation request received, processing..."] = "已收到安装请求，正在处理……",
        ["Setting theme to {0}."] = "正在将主题设置为 {0}。",
        ["Theme {0} is not valid!"] = "主题 {0} 无效！",
        ["Configuring settings as 'just yourself' install."] = "正在按“仅本人使用”模式配置。",
        ["Configuring settings as 'just yourself (LAN)' install."] = "正在按“仅本人使用（局域网）”模式配置。",
        ["Downloaded! Extracting... (look in terminal window for details)"] = "下载完成！正在解压……（详细过程可查看终端窗口）",
        ["Still extracting backend files... ({0} files so far)"] = "仍在解压后端文件……（目前已解压 {0} 个文件）",
        ["Installing prereqs..."] = "正在安装前置依赖……",
        ["Prepping ComfyUI's git repo..."] = "正在准备 ComfyUI 的 git 仓库……",
        ["Ensuring all current Comfy requirements are installed..."] = "正在确保已安装 ComfyUI 的全部最新依赖……",
        ["Installing AMD ROCm multi-architecture torch (this may take a while)..."] = "正在安装 AMD ROCm 多架构版 torch（可能需要一段时间）……",
        ["Downloading ComfyUI backend... please wait..."] = "正在下载 ComfyUI 后端……请稍候……",
        ["Enabling ComfyUI..."] = "正在启用 ComfyUI……",
        ["Not installing any backend."] = "不安装任何后端。",
        ["Downloading model from '{0}'... please wait..."] = "正在从“{0}”下载模型……请稍候……",
        ["Failed to download '{0}' (IO): {1}: {2}"] = "下载“{0}”失败（IO）：{1}：{2}",
        ["Failed to download '{0}' (HTTP): {1}: {2}"] = "下载“{0}”失败（HTTP）：{1}：{2}",
        ["Model download complete."] = "模型下载完成。",
        ["Reloading backends..."] = "正在重新加载后端……",
        ["Installed!"] = "安装完成！",
        ["Existing installation detected, reusing existing backend and configuration, applying upgrade..."] = "检测到已有安装，将复用现有后端与配置并进行升级……",
        ["Re-registering existing ComfyUI backend..."] = "正在重新注册现有的 ComfyUI 后端……",
        ["Upgrade complete!"] = "升级完成！"
    };

    /// <summary>Translate a server-side installer message template. The English template is the canonical key; falls back to English if no translation exists.</summary>
    public static string T(string englishTemplate, params object[] args)
    {
        string baseText = englishTemplate;
        if (InstallLanguage is not null && InstallLanguage.StartsWith("zh") && InstallMessagesZh.TryGetValue(englishTemplate, out string translated))
        {
            baseText = translated;
        }
        return args is null || args.Length == 0 ? baseText : string.Format(baseText, args);
    }

    /// <summary>Send a JSON object to the install websocket, but do not crash the installation if the socket dropped mid-way.</summary>
    public static async Task SafeSocketSend(JObject obj)
    {
        try
        {
            await InstallSocket.SendJson(obj, API.WebsocketTimeout);
        }
        catch (Exception ex)
        {
            // A long-running step (eg extracting tens of thousands of files on a slow/scanned disk) can
            // outlast an idle WebSocket timeout. The installation itself should keep running regardless;
            // the socket is only a progress channel.
            Logs.Error($"[Installer] Failed to send a message to the install websocket (installation will continue): {ex.Message}");
        }
    }

    /// <summary>Output a message to logs and the websocket.</summary>
    public static async Task Output(string str)
    {
        Logs.Init($"[Installer] {str}");
        await SafeSocketSend(new JObject() { ["info"] = str });
    }

    /// <summary>Send a progress update for the installation down the websocket.</summary>
    public static void UpdateProgress(long progress, long total, long perSec)
    {
        // TODO: better way to send these out without waiting
        try
        {
            InstallSocket.SendJson(new JObject() { ["progress"] = progress, ["total"] = total, ["steps"] = StepsThusFar, ["total_steps"] = TotalSteps, ["per_second"] = perSec }, API.WebsocketTimeout).Wait();
        }
        catch (Exception ex)
        {
            Logs.Error($"[Installer] Failed to send progress to the install websocket (installation will continue): {ex.Message}");
        }
    }

    /// <summary>Configure the theme during installation.</summary>
    public static async Task Theme(string theme)
    {
        if (Program.Web.RegisteredThemes.ContainsKey(theme))
        {
            await Output(T("Setting theme to {0}.", theme));
            Program.ServerSettings.DefaultUser.Theme = theme;
        }
        else
        {
            await Output(T("Theme {0} is not valid!", theme));
            throw new SwarmUserErrorException("Invalid theme input!");
        }
    }

    /// <summary>Configure the "installed for" setting during installation.</summary>
    public static async Task InstalledFor(string installed_for)
    {
        switch (installed_for)
        {
            case "just_self":
                await Output(T("Configuring settings as 'just yourself' install."));
                Program.ServerSettings.Network.Host = "localhost";
                Program.ServerSettings.Network.Port = 7801;
                Program.ServerSettings.Network.PortCanChange = true;
                Program.ServerSettings.LaunchMode = "web"; // TODO: Electron?
                break;
            case "just_self_lan":
                await Output(T("Configuring settings as 'just yourself (LAN)' install."));
                Program.ServerSettings.Network.Host = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "*" : "0.0.0.0";
                Program.ServerSettings.Network.Port = 7801;
                Program.ServerSettings.Network.PortCanChange = true;
                Program.ServerSettings.LaunchMode = "web";
                break;
            default:
                throw new SwarmUserErrorException($"Invalid install type '{installed_for}'!");
        }
    }

    /// <summary>Configure the backend as ComfyUI specifically for Windows during installation.</summary>
    public static async Task<(string, string, bool)> BackendComfyWindows(bool install_amd)
    {
        try
        {
            if (install_amd)
            {
                await Utilities.DownloadFile("https://github.com/comfyanonymous/ComfyUI/releases/latest/download/ComfyUI_windows_portable_amd.7z", "dlbackend/comfyui_dl.7z", UpdateProgress);
            }
            else
            {
                await Utilities.DownloadFile("https://github.com/comfyanonymous/ComfyUI/releases/latest/download/ComfyUI_windows_portable_nvidia.7z", "dlbackend/comfyui_dl.7z", UpdateProgress);
            }
        }
        catch (HttpRequestException ex)
        {
            Logs.Error($"Comfy download failed: {ex.ReadableString()}");
            Logs.Info("Will try alternate download...");
            await Utilities.DownloadFile("https://github.com/comfyanonymous/ComfyUI/releases/download/latest/ComfyUI_windows_portable_nvidia_or_cpu_nightly_pytorch.7z", "dlbackend/comfyui_dl.7z", UpdateProgress);
        }
        StepsThusFar++;
        UpdateProgress(0, 0, 0);
        await Output(T("Downloaded! Extracting... (look in terminal window for details)"));
        Directory.CreateDirectory("dlbackend/tmpcomfy/");
        Process sevenZip = Process.Start("launchtools/7z/win/7za.exe", $"x dlbackend/comfyui_dl.7z -o\"dlbackend/tmpcomfy/\" -y");
        Task extractTask = sevenZip.WaitForExitAsync(Program.GlobalProgramCancel);
        while (!extractTask.IsCompleted)
        {
            // Keep the websocket alive during a potentially long extraction (slow disks or antivirus scans
            // can make extracting tens of thousands of files take several minutes).
            Task heartbeatDelay = Task.Delay(TimeSpan.FromSeconds(15), Program.GlobalProgramCancel);
            if (await Task.WhenAny(extractTask, heartbeatDelay) == extractTask)
            {
                break;
            }
            long fileCount = 0;
            try
            {
                if (Directory.Exists("dlbackend/tmpcomfy"))
                {
                    fileCount = Directory.EnumerateFiles("dlbackend/tmpcomfy", "*", SearchOption.AllDirectories).Count();
                }
            }
            catch (Exception)
            {
                // Counting can race with extraction or file locks; not important, the heartbeat still gets sent.
            }
            await SafeSocketSend(new JObject() { ["info"] = T("Still extracting backend files... ({0} files so far)", fileCount) });
        }
        await extractTask;
        static void moveFolder()
        {
            if (Directory.Exists("dlbackend/tmpcomfy/ComfyUI_windows_portable"))
            {
                Directory.Move("dlbackend/tmpcomfy/ComfyUI_windows_portable", "dlbackend/comfy");
            }
            else
            {
                Directory.Move("dlbackend/tmpcomfy/ComfyUI_windows_portable_nightly_pytorch", "dlbackend/comfy");
            }
        }
        try
        {
            moveFolder();
        }
        catch (Exception)
        {
            // This might fail if eg an antivirus program locks up the folder, so give it a few seconds to do its job then try the move again
            await Task.Delay(TimeSpan.FromSeconds(5));
            try
            {
                moveFolder();
            }
            catch (Exception)
            {
                // Just in case the lock up is slow.
                await Task.Delay(TimeSpan.FromSeconds(15));
                moveFolder();
                // This has been a 20 second delay now, so either it's done now and works, or the problem can't be resolved by waiting, so don't waste the user's time with more tries.
            }
        }
        await Output(T("Installing prereqs..."));
        try
        {
            await Utilities.DownloadFile("https://aka.ms/vs/16/release/vc_redist.x64.exe", "dlbackend/vc_redist.x64.exe", UpdateProgress);
            UpdateProgress(0, 0, 0);
            await Process.Start(new ProcessStartInfo(Path.GetFullPath("dlbackend/vc_redist.x64.exe"), "/quiet /install /passive /norestart") { UseShellExecute = true }).WaitForExitAsync(Program.GlobalProgramCancel);
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to install VC Redist: {ex}");
        }
        string path = "dlbackend/comfy/ComfyUI/main.py";
        string comfyFolderPath = Path.GetFullPath("dlbackend/comfy");
        await Output(T("Prepping ComfyUI's git repo..."));
        string fetchResp = await Utilities.RunGitProcess($"fetch", $"{comfyFolderPath}/ComfyUI");
        Logs.Debug($"ComfyUI Install git fetch response: {fetchResp}");
        string checkoutResp = await Utilities.RunGitProcess($"checkout master --force", $"{comfyFolderPath}/ComfyUI");
        Logs.Debug($"ComfyUI Install git checkout master response: {checkoutResp}");
        string response = await Utilities.RunGitProcess($"pull", $"{comfyFolderPath}/ComfyUI");
        Logs.Debug($"ComfyUI Install git pull response: {response}");
        await Output(T("Ensuring all current Comfy requirements are installed..."));
        string requirementsRaw = File.ReadAllText($"{comfyFolderPath}/ComfyUI/requirements.txt");
        // Exclude torch requirements here as pip installing those just breaks things
        string[] requirements = [.. requirementsRaw.Replace('\r', '\n').Split('\n').Select(r => r.Trim()).Where(r => !string.IsNullOrWhiteSpace(r) && !r.StartsWith('#') && !r.StartsWith("torch"))];
        if (install_amd)
        {
            // The official ComfyUI Windows AMD portable bundles an older ROCm torch (rocm 7.2.1) that fails
            // on AMD GPU architectures not in its validated manifest - notably gfx1010/gfx1012 (RDNA1,
            // e.g. RX 5600/5700 series) - it crashes with an access violation during torch initialization.
            // Replace it with AMD's current TheRock multi-arch wheel index, which covers all supported GPUs.
            await Output(T("Installing AMD ROCm multi-architecture torch (this may take a while)..."));
            ProcessStartInfo amdTorchInfo = new($"{comfyFolderPath}/python_embeded/python.exe")
            {
                WorkingDirectory = comfyFolderPath
            };
            foreach (string arg in new string[] { "-s", "-m", "pip", "install",
                "--index-url", "https://repo.amd.com/rocm/whl-multi-arch/",
                "--extra-index-url", "https://pypi.org/simple",
                "torch[device-all]==2.12.0+rocm7.14.0",
                "torchvision[device-all]==0.27.0+rocm7.14.0",
                "torchaudio==2.11.0+rocm7.14.0" })
            {
                amdTorchInfo.ArgumentList.Add(arg);
            }
            await NetworkBackendUtils.RunProcessWithMonitoring(amdTorchInfo, "ComfyUI Install (AMD ROCm torch)", "comfyinstall");
        }
        await NetworkBackendUtils.RunProcessWithMonitoring(new ProcessStartInfo($"{comfyFolderPath}/python_embeded/python.exe", $"-s -m pip install{(install_amd ? " -U " : "")} {requirements.JoinString(" ")}") { WorkingDirectory = comfyFolderPath }, "ComfyUI Install (python requirements)", "comfyinstall");
        string extraArgs = "";
        bool enablePreviews = true;
        return (path, extraArgs, enablePreviews);
    }

    /// <summary>Configure the backend as ComfyUI during installation.</summary>
    public static async Task BackendComfy(bool install_amd)
    {
        string existingComfy = FindExistingComfyScript();
        if (!string.IsNullOrWhiteSpace(existingComfy))
        {
            await Output(T($"Found existing ComfyUI at '{existingComfy}', using it as the default backend..."));
            NvidiaUtil.NvidiaInfo[] detectedNvidia = NvidiaUtil.QueryNvidia();
            int detectedGpu = detectedNvidia is null || detectedNvidia.Length == 0
                ? 0
                : detectedNvidia.OrderByDescending(n => n.TotalMemory.InBytes).First().ID;
            Program.Backends.AddNewOfType(Program.Backends.BackendTypes["comfyui_selfstart"], new ComfyUISelfStartBackend.ComfyUISelfStartSettings()
            {
                StartScript = existingComfy,
                GPU_ID = $"{detectedGpu}",
                EnablePreviews = "true"
            });
            return;
        }
        await Output(T("Downloading ComfyUI backend... please wait..."));
        Directory.CreateDirectory("dlbackend/");
        string path;
        string extraArgs = "";
        bool enablePreviews = true;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            (path, extraArgs, enablePreviews) = await BackendComfyWindows(install_amd);
        }
        else
        {
            StepsThusFar++;
            UpdateProgress(0, 0, 0);
            string gpuType = install_amd ? "amd" : "nv";
            Logs.LogLevel level = Logs.MinimumLevel;
            Logs.MinimumLevel = Logs.LogLevel.Verbose;
            try
            {
                Process installer = Process.Start(new ProcessStartInfo("/bin/bash", $"launchtools/comfy-install-linux.sh {gpuType}") { RedirectStandardOutput = true, UseShellExecute = false, RedirectStandardError = true });
                NetworkBackendUtils.ReportLogsFromProcess(installer, "ComfyUI Install (Linux Script)", "comfyinstall");
                await installer.WaitForExitAsync(Program.GlobalProgramCancel);
                if (installer.ExitCode != 0)
                {
                    throw new SwarmReadableErrorException("ComfyUI install failed! Check debug logs for details.");
                }
                path = "dlbackend/ComfyUI/main.py";
            }
            finally
            {
                Logs.MinimumLevel = level;
            }
        }
        NvidiaUtil.NvidiaInfo[] nv = NvidiaUtil.QueryNvidia();
        int gpu = 0;
        if (nv is not null && nv.Length > 0)
        {
            NvidiaUtil.NvidiaInfo mostVRAM = nv.OrderByDescending(n => n.TotalMemory.InBytes).First();
            gpu = mostVRAM.ID;
        }
        await Output(T("Enabling ComfyUI..."));
        Program.Backends.AddNewOfType(Program.Backends.BackendTypes["comfyui_selfstart"], new ComfyUISelfStartBackend.ComfyUISelfStartSettings() { StartScript = path, GPU_ID = $"{gpu}", ExtraArgs = extraArgs.Trim(), EnablePreviews = enablePreviews ? "true" : "false" });
    }

    /// <summary>Configure the backend during installation.</summary>
    public static async Task Backend(string backend, bool install_amd)
    {
        switch (backend)
        {
            case "comfyui":
                {
                    await BackendComfy(install_amd);
                    break;
                }
            case "none":
                await Output(T("Not installing any backend."));
                break;
            default:
                throw new SwarmReadableErrorException($"Invalid backend type '{backend}'!");
        }
    }

    /// <summary>Run model downloads during installation.</summary>
    public static async Task Models(string models, Session session)
    {
        if (models == "none")
        {
            return;
        }
        foreach (string model in models.Split(','))
        {
            if (!CommonModels.Known.TryGetValue(model.Trim(), out CommonModels.ModelInfo modelInfo))
            {
                throw new SwarmReadableErrorException($"Invalid model {model}!");
            }
            await Output(T("Downloading model from '{0}'... please wait...", modelInfo.URL));
            try
            {
                await modelInfo.DownloadNow(UpdateProgress, session);
            }
            catch (SwarmReadableErrorException ex)
            {
                Logs.Error($"Failed to download '{modelInfo.URL}': {ex.Message}");
            }
            catch (IOException ex)
            {
                Logs.Error($"Failed to download '{modelInfo.URL}' (IO): {ex.GetType().Name}: {ex.Message}");
                Logs.Debug($"Download exception: {ex.ReadableString()}");
                await Output(T("Failed to download '{0}' (IO): {1}: {2}", modelInfo.URL, ex.GetType().Name, ex.Message));
            }
            catch (HttpRequestException ex)
            {
                Logs.Error($"Failed to download '{modelInfo.URL}' (HTTP): {ex.GetType().Name}: {ex.Message}");
                Logs.Debug($"Download exception: {ex.ReadableString()}");
                await Output(T("Failed to download '{0}' (HTTP): {1}: {2}", modelInfo.URL, ex.GetType().Name, ex.Message));
            }
            StepsThusFar++;
            UpdateProgress(0, 0, 0);
            await Output(T("Model download complete."));
        }
        Program.MainSDModels.Refresh();
    }

    /// <summary>Make a desktop shortcut (Windows only).</summary>
    public static void MakeShortcut()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }
        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        string curDir = Directory.GetCurrentDirectory().TrimEnd('\\');
        string shortcutPath = Path.Combine(desktopPath, "EcomCraft Studio.lnk");
        string legacyShortcutPath = Path.Combine(desktopPath, "EcomCraft Studio.url");
        string legacyPath = Path.Combine(desktopPath, "SwarmUI.url");
        string launcher = Path.Combine(curDir, "launch-windows.bat");
        string icon = Path.Combine(curDir, "src", "wwwroot", "ecomcraft.ico");
        try
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is not null)
            {
                object shell = Activator.CreateInstance(shellType);
                object shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, [shortcutPath]);
                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, [launcher]);
                shortcutType.InvokeMember("Arguments", System.Reflection.BindingFlags.SetProperty, null, shortcut, ["--launch_mode app"]);
                shortcutType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, [curDir]);
                shortcutType.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, shortcut, [$"{icon},0"]);
                shortcutType.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, shortcut, ["EcomCraft Studio"]);
                shortcutType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
                if (File.Exists(legacyShortcutPath))
                {
                    File.Delete(legacyShortcutPath);
                }
            }
        }
        catch (Exception ex)
        {
            Logs.Warning($"Could not create the EcomCraft desktop application shortcut: {ex.Message}");
        }
        if (File.Exists(legacyPath))
        {
            string legacyContent = File.ReadAllText(legacyPath);
            if (legacyContent.Contains($"URL=\"{curDir}\\launch-windows.bat\"", StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(legacyPath);
            }
        }
        if (!File.Exists(shortcutPath))
        {
            string fallbackPath = Path.Combine(desktopPath, "EcomCraft Studio.bat");
            string content =
                $"""
                @echo off
                cd /d "{curDir}"
                call launch-windows.bat --launch_mode app
                """;
            File.WriteAllText(fallbackPath, content);
            if (File.Exists(legacyShortcutPath))
            {
                File.Delete(legacyShortcutPath);
            }
        }
    }

    /// <summary>Apply changes to server settings.</summary>
    public static void SettingsApply()
    {
        Program.ServerSettings.IsInstalled = true;
        Program.ServerSettings.InstallDate = $"{DateTimeOffset.Now:yyyy-MM-dd}";
        Program.ServerSettings.InstallVersion = Utilities.Version;
        if (Program.ServerSettings.LaunchMode == "install")
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Program.ServerSettings.LaunchMode = "app";
            }
            else
            {
                Program.ServerSettings.LaunchMode = "web";
            }
        }
        Program.SaveSettingsFile();
    }

    /// <summary>Returns true if a fully usable self-start ComfyUI backend already exists on disk (can be reused for an upgrade).</summary>
    public static bool IsCompleteBackendPresent()
    {
        return File.Exists("dlbackend/comfy/ComfyUI/main.py")
            && File.Exists("dlbackend/comfy/python_embeded/python.exe");
    }

    /// <summary>Finds an existing ComfyUI main.py in the usual Windows installation locations.</summary>
    public static string FindExistingComfyScript()
    {
        List<string> candidates =
        [
            Path.GetFullPath("dlbackend/comfy/ComfyUI/main.py"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ComfyUI", "main.py"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ComfyUI_windows_portable", "ComfyUI", "main.py"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ComfyUI", "main.py"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ComfyUI_windows_portable", "ComfyUI", "main.py")
        ];
        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        string[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        ];
        foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists))
        {
            try
            {
                foreach (string candidate in Directory.EnumerateFiles(root, "main.py", SearchOption.AllDirectories))
                {
                    string parent = Directory.GetParent(candidate)?.Name ?? "";
                    string grandParent = Directory.GetParent(candidate)?.Parent?.Name ?? "";
                    if (parent.Equals("ComfyUI", StringComparison.OrdinalIgnoreCase)
                        || grandParent.Equals("ComfyUI", StringComparison.OrdinalIgnoreCase))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
            }
            catch (Exception ex)
            {
                Logs.Debug($"Could not scan for an existing ComfyUI under '{root}': {ex.Message}");
            }
        }
        return null;
    }

    /// <summary>
    /// Upgrade path: a complete backend already exists on disk. Reuse the existing portable environment (including the
    /// expensive PyTorch install), the existing configuration (Data), and the existing models - nothing is re-downloaded.
    /// Only re-registers the backend if it is missing from the saved config, marks the install as complete, and reloads.
    /// </summary>
    public static async Task Upgrade()
    {
        await Output(T("Existing installation detected, reusing existing backend and configuration, applying upgrade..."));
        bool hasComfy = Program.Backends.EnumerateT2IBackends.Any(b => b.Backend is ComfyUISelfStartBackend);
        if (!hasComfy)
        {
            await Output(T("Re-registering existing ComfyUI backend..."));
            NvidiaUtil.NvidiaInfo[] nv = NvidiaUtil.QueryNvidia();
            int gpu = 0;
            if (nv is not null && nv.Length > 0)
            {
                gpu = nv.OrderByDescending(n => n.TotalMemory.InBytes).First().ID;
            }
            string discoveredComfy = FindExistingComfyScript();
            Program.Backends.AddNewOfType(Program.Backends.BackendTypes["comfyui_selfstart"], new ComfyUISelfStartBackend.ComfyUISelfStartSettings()
            {
                StartScript = discoveredComfy ?? "dlbackend/comfy/ComfyUI/main.py",
                GPU_ID = $"{gpu}",
                EnablePreviews = "true"
            });
        }
        Program.ServerSettings.IsInstalled = true;
        if (string.IsNullOrWhiteSpace(Program.ServerSettings.InstallDate))
        {
            Program.ServerSettings.InstallDate = $"{DateTimeOffset.Now:yyyy-MM-dd}";
        }
        Program.ServerSettings.InstallVersion = Utilities.Version;
        Program.SaveSettingsFile();
        await Output(T("Reloading backends..."));
        await Program.Backends.ReloadAllBackends();
        await Output(T("Upgrade complete!"));
        await InstallSocket.SendJson(new JObject() { ["success"] = true }, API.WebsocketTimeout);
    }

    /// <summary>Main install function entry point.</summary>
    public static async Task Install(WebSocket socket, string theme, string installed_for, string backend, string models, bool install_amd, string language, bool make_shortcut, Session session)
    {
        InstallSocket = socket;
        InstallLanguage = language;
        Program.ServerSettings.DefaultUser.Language = language;
        if (Directory.Exists("dlbackend/comfy"))
        {
            if (IsCompleteBackendPresent())
            {
                // A complete backend exists - reuse it (and the existing Data/models) instead of reinstalling.
                await Upgrade();
                if (make_shortcut)
                {
                    MakeShortcut();
                }
                return;
            }
            throw new SwarmUserErrorException("An incomplete previous backend install exists at 'dlbackend/comfy'. Please delete the 'dlbackend' folder (and optionally 'Data'), then rerun the installer.");
        }
        await Output(T("Installation request received, processing..."));
        await Theme(theme);
        await InstalledFor(installed_for);
        StepsThusFar = 1;
        TotalSteps = 4;
        if (backend == "comfyui")
        {
            TotalSteps++;
        }
        if (models != "none")
        {
            TotalSteps += models.Split(',').Length;
        }
        StepsThusFar++;
        UpdateProgress(0, 0, 0);
        await Backend(backend, install_amd);
        if (make_shortcut)
        {
            MakeShortcut();
        }
        SettingsApply();
        await Models(models, session);
        StepsThusFar++;
        UpdateProgress(0, 0, 0);
        await Program.Backends.ReloadAllBackends();
        StepsThusFar++;
        UpdateProgress(0, 0, 0);
        await Output(T("Installed!"));
        await socket.SendJson(new JObject() { ["success"] = true }, API.WebsocketTimeout);
    }
}
