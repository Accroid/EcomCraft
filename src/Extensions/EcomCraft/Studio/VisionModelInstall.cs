using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using EcomCraftStudio.MagicPrompt.WebAPI;
using EcomCraftStudio.Studio.Prompting;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio;

/// <summary>One-click install of the local vision model (F69, from the settings page): runs the repository's launchtools install script on this
/// server, which installs Ollama when it is missing and pulls the chosen model. The progress view keeps a short tail of the script's plain output;
/// only one install runs at a time and closing the settings dialog does not stop it.</summary>
[API.APIClass("EcomCraft one-click install of the local vision model.")]
public static class VisionModelInstall
{
    /// <summary>How many output lines are kept for the progress view.</summary>
    private const int MaxLines = 30;

    /// <summary>Protects the install state below.</summary>
    private static readonly object InstallLock = new();

    /// <summary>The running or finished install process; null before the first install of this server run.</summary>
    private static Process CurrentProcess;

    /// <summary>The state of the current or last install: "running", "done" or "failed"; "idle" before the first one.</summary>
    private static string State = "idle";

    /// <summary>The model of the current or last install.</summary>
    private static string CurrentModel = "";

    /// <summary>When the current or last install started, UTC.</summary>
    private static DateTime StartedUtc;

    /// <summary>When the last install finished, UTC; null while one runs or before the first.</summary>
    private static DateTime? FinishedUtc;

    /// <summary>The kept output lines of the install script, oldest first.</summary>
    private static readonly List<string> Lines = [];

    /// <summary>Whether the model is one the button may install; the only names the install API accepts.</summary>
    public static bool IsKnownModel(string model)
    {
        return VisionInstallRules.IsKnownModel(model);
    }

    /// <summary>Appends one script output line to the kept tail.</summary>
    private static void AddLine(string raw)
    {
        string line = VisionInstallRules.CleanLine(raw);
        if (line.Length == 0)
        {
            return;
        }
        lock (InstallLock)
        {
            Lines.Add(line);
            while (Lines.Count > MaxLines)
            {
                Lines.RemoveAt(0);
            }
        }
    }

    /// <summary>The install script for this server's platform, or null when the platform has none.</summary>
    private static string ScriptPath()
    {
        string name = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "install-vision-model-windows.ps1"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "install-vision-model-linux.sh" : null;
        if (name is null)
        {
            return null;
        }
        string relative = Path.Combine("launchtools", name);
        // The launcher scripts start the server from the repository root; the output folder is a fallback for other setups.
        string candidate = Path.Combine(Environment.CurrentDirectory, relative);
        if (!File.Exists(candidate))
        {
            candidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", relative));
        }
        return File.Exists(candidate) ? candidate : null;
    }

    /// <summary>Starts the install script for one model; returns an error object instead when it cannot start.</summary>
    private static JObject TryStart(string model, out bool started)
    {
        started = false;
        string script = ScriptPath();
        if (script is null)
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? StudioApi.Error("一键安装目前支持 Windows 和 Linux；macOS 请按文档手动安装 Ollama 后拉取模型。", "vision_install_unsupported")
                : StudioApi.Error("找不到安装脚本，请确认服务器运行的是完整的 EcomCraft 目录。", "vision_install_missing");
        }
        lock (InstallLock)
        {
            if (State == "running")
            {
                return StudioApi.Error("已有一次安装正在进行，请等它完成后再试。", "vision_install_busy");
            }
            ProcessStartInfo info = new();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                info.FileName = "powershell.exe";
                info.ArgumentList.Add("-NoProfile");
                info.ArgumentList.Add("-ExecutionPolicy");
                info.ArgumentList.Add("Bypass");
                info.ArgumentList.Add("-File");
                info.ArgumentList.Add(script);
                info.ArgumentList.Add("-Model");
                info.ArgumentList.Add(model);
            }
            else
            {
                info.FileName = "/bin/bash";
                info.ArgumentList.Add(script);
                info.ArgumentList.Add(model);
            }
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            Process process;
            try
            {
                process = Process.Start(info);
                if (process is null)
                {
                    return StudioApi.Error("安装脚本没有启动，请查看服务器日志或改用命令行安装。", "vision_install_start_failed");
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Logs.Warning($"EcomCraft could not start the vision model install: {ex.GetType().Name}");
                return StudioApi.Error("安装脚本没有启动，请查看服务器日志或改用命令行安装。", "vision_install_start_failed");
            }
            CurrentProcess = process;
            CurrentModel = model;
            State = "running";
            StartedUtc = DateTime.UtcNow;
            FinishedUtc = null;
            Lines.Clear();
            Lines.Add(model);
            _ = Task.Run(async () =>
            {
                string line;
                while ((line = await process.StandardOutput.ReadLineAsync()) is not null)
                {
                    AddLine(line);
                }
            });
            _ = Task.Run(async () =>
            {
                string line;
                while ((line = await process.StandardError.ReadLineAsync()) is not null)
                {
                    AddLine(line);
                }
            });
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                bool succeeded;
                lock (InstallLock)
                {
                    if (CurrentProcess != process || State != "running")
                    {
                        return;
                    }
                    try
                    {
                        succeeded = process.ExitCode == 0;
                    }
                    catch (Exception)
                    {
                        succeeded = false;
                    }
                    if (!succeeded)
                    {
                        State = "failed";
                        FinishedUtc = DateTime.UtcNow;
                    }
                }
                if (succeeded)
                {
                    // The installed model becomes the vision model before the install reports "done", also when the settings dialog was closed meanwhile.
                    _ = FinishInstallAsync(process, model);
                }
                else
                {
                    PromptService.ForgetVisionStatus();
                }
            };
            started = true;
            return null;
        }
    }

    /// <summary>The address of the Ollama the install script sets up: always this server.</summary>
    private const string LocalOllamaUrl = "http://localhost:11434";

    /// <summary>Saves a successfully installed model as the vision model, then reports the install as done.</summary>
    private static async Task FinishInstallAsync(Process process, string model)
    {
        await SaveInstalledModelAsync(model);
        lock (InstallLock)
        {
            if (CurrentProcess == process && State == "running")
            {
                State = "done";
                FinishedUtc = DateTime.UtcNow;
            }
        }
    }

    /// <summary>Saves a successfully installed model as the vision model: Ollama on this server and the pulled model (F69).
    /// The image description switch stays as the administrator set it.</summary>
    private static async Task SaveInstalledModelAsync(string model)
    {
        try
        {
            JObject result = await SessionSettings.SaveMagicPromptSettings(new JObject()
            {
                ["settings"] = new JObject() { ["visionbackend"] = "ollama", ["visionmodel"] = model, ["visionbaseurl"] = LocalOllamaUrl }
            });
            if (result?.Value<bool?>("success") != true)
            {
                Logs.Warning("EcomCraft could not save the installed vision model as the vision model.");
            }
        }
        catch (Exception ex)
        {
            Logs.Warning($"EcomCraft could not save the installed vision model: {ex.GetType().Name}");
        }
        // The newly pulled model changes what the vision status should say, so the cached answer goes away.
        PromptService.ForgetVisionStatus();
    }

    [API.APIDescription("Start the one-click install of the local vision model (F69, administrators): runs the repository install script on this server, "
        + "which installs Ollama when missing and pulls the chosen model (one of the documented vision models); a successful install saves that model "
        + "on this server's Ollama as the vision model before reporting done. Only one install runs at a time; "
        + "progress comes from EcomCraftStudioVisionInstallStatus.", "state: \"running\", model: \"qwen3-vl:8b\"")]
    public static Task<JObject> EcomCraftStudioVisionInstall(Session session, string model = "")
    {
        if (!StudioApi.CanView(session) || !session.User.HasPermission(Permissions.EditServerSettings))
        {
            return Task.FromResult(StudioApi.Error("此账号没有管理提示词服务的权限。", "bad_permissions"));
        }
        if (!IsKnownModel(model))
        {
            return Task.FromResult(StudioApi.Error($"请从列表中选择要安装的模型（支持：{string.Join('、', VisionInstallRules.KnownModels)}）。", "invalid_model"));
        }
        JObject error = TryStart(model.Trim(), out bool started);
        if (!started)
        {
            return Task.FromResult(error);
        }
        return Task.FromResult(new JObject() { ["state"] = "running", ["model"] = model.Trim() });
    }

    [API.APIDescription("The progress of the one-click local vision model install (F69, administrators): state is idle, running, done or failed, "
        + "with the model, unix start and finish times and the last plain lines of the install script output. Requires the server-settings permission.",
        "state: \"running\", model: \"qwen3-vl:8b\", started_at: 0, finished_at: null, lines: [\"\"]")]
    public static Task<JObject> EcomCraftStudioVisionInstallStatus(Session session)
    {
        if (!StudioApi.CanView(session) || !session.User.HasPermission(Permissions.EditServerSettings))
        {
            return Task.FromResult(StudioApi.Error("此账号没有管理提示词服务的权限。", "bad_permissions"));
        }
        lock (InstallLock)
        {
            return Task.FromResult(new JObject()
            {
                ["state"] = State,
                ["model"] = CurrentModel,
                ["started_at"] = (long)(StartedUtc - DateTime.UnixEpoch).TotalSeconds,
                ["finished_at"] = FinishedUtc is null ? null : (long)((DateTime)FinishedUtc - DateTime.UnixEpoch).TotalSeconds,
                ["lines"] = new JArray(Lines)
            });
        }
    }

    /// <summary>Stops a still-running install when the server shuts down; a partially pulled model resumes on the next run.</summary>
    public static void Shutdown()
    {
        lock (InstallLock)
        {
            if (CurrentProcess is not null)
            {
                try
                {
                    if (!CurrentProcess.HasExited)
                    {
                        CurrentProcess.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // The process already ended or cannot be signaled; nothing more to do.
                }
                CurrentProcess.Dispose();
                CurrentProcess = null;
            }
            State = "idle";
        }
    }
}
