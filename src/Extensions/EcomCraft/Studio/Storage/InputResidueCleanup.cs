using System.IO;
using SwarmUI.Backends;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Core;
using SwarmUI.Utils;

namespace EcomCraftStudio.Studio.Storage;

/// <summary>Deletes the image files older generations left in the local self-starting ComfyUI's input folder (A34, alongside the F72 startup cleanup).
/// The SwarmUI backend used to upload every image parameter as <c>init_image_sui_backend_&lt;backend&gt;-&lt;id&gt;.png</c> and delete it when the task
/// finished; a forced stop, crash or power loss skipped that deletion. EcomCraft's own generation no longer uploads (images ride inside the request),
/// but the professional mode still does, and this run's uploads must not be touched: only files older than this server's start are removed, and only
/// with that one prefix, in the input folder of a self-started local ComfyUI. Files on LAN execution servers cannot be reached from here; the LAN
/// deployment guide describes the manual cleanup.</summary>
public static class InputResidueCleanup
{
    /// <summary>Prefix of the backend's uploaded image files; nothing else in the input folder is ever touched.</summary>
    public const string UploadPrefix = "init_image_sui_backend_";

    /// <summary>How often the loop looks for self-started ComfyUI backends; they load shortly after the extension starts.</summary>
    private const int LoopMs = 15 * 1000;

    /// <summary>How long the loop keeps looking: after this, backends that never appeared are left to the next restart.</summary>
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(10);

    /// <summary>Stops the background loop when the extension shuts down.</summary>
    private static readonly CancellationTokenSource StopSource = new();

    /// <summary>Input folders already cleaned once in this run.</summary>
    private static readonly HashSet<string> Cleaned = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Files written before this moment are leftovers from earlier runs; anything newer belongs to this run and stays.</summary>
    private static DateTime StartedAt;

    /// <summary>Starts the background loop.</summary>
    public static void Start()
    {
        StartedAt = DateTime.UtcNow;
        _ = Task.Run(async () =>
        {
            for (int attempt = 0; attempt < GiveUpAfter.TotalMilliseconds / LoopMs && !StopSource.IsCancellationRequested && !Program.GlobalProgramCancel.IsCancellationRequested; attempt++)
            {
                try
                {
                    await Task.Delay(LoopMs, StopSource.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                try
                {
                    // Backends load over the first minutes and can be added later; each input folder is cleaned once, when first seen.
                    CleanOnce();
                }
                catch (Exception ex)
                {
                    Logs.Warning($"EcomCraft input residue cleanup loop failed: {ex.Message}");
                }
            }
        });
    }

    /// <summary>Stops the loop.</summary>
    public static void Shutdown() => StopSource.Cancel();

    /// <summary>Cleans the input folder of every self-started local ComfyUI found now and not cleaned yet.</summary>
    private static void CleanOnce()
    {
        if (Program.Backends is null)
        {
            return;
        }
        foreach (BackendHandler.T2IBackendData data in Program.Backends.EnumerateT2IBackends)
        {
            if (data.Backend is not ComfyUISelfStartBackend backend)
            {
                continue;
            }
            string inputFolder;
            try
            {
                inputFolder = Path.GetFullPath($"{backend.ComfyPathBase}/input");
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }
            lock (Cleaned)
            {
                if (!Cleaned.Add(inputFolder))
                {
                    continue;
                }
            }
            CleanFolder(inputFolder);
        }
    }

    /// <summary>Deletes the leftover upload files in one input folder; anything unexpected is only logged.</summary>
    private static void CleanFolder(string inputFolder)
    {
        string[] files;
        try
        {
            if (!Directory.Exists(inputFolder))
            {
                return;
            }
            files = Directory.GetFiles(inputFolder, UploadPrefix + "*");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logs.Warning($"EcomCraft could not list the local ComfyUI input folder for the leftover-image cleanup: {ex.GetType().Name}");
            return;
        }
        int removed = 0;
        foreach (string file in files)
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) >= StartedAt)
                {
                    continue;
                }
                File.Delete(file);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logs.Warning($"EcomCraft could not delete the leftover uploaded image {Path.GetFileName(file)}: {ex.GetType().Name}");
            }
        }
        if (removed > 0)
        {
            Logs.Info($"EcomCraft deleted {removed} leftover uploaded image(s) from the local ComfyUI input folder (A34).");
        }
    }
}
