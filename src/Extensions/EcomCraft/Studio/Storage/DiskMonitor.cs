using System.IO;
using EcomCraftStudio.Studio.Storage;
using SwarmUI.Core;
using SwarmUI.Utils;

namespace EcomCraftStudio.Studio.Storage;

/// <summary>Watches the free space of the platform server's works folder and the work library (F46): a background check every few minutes feeds the
/// administrator warning, and a critical shortage rejects new generations with a plain reason instead of letting saves fail midway.</summary>
public static class DiskMonitor
{
    /// <summary>Interval of the background check.</summary>
    private const int LoopMs = 5 * 60 * 1000;

    /// <summary>The local output folder, checked because generations are always saved there first (R11.5).</summary>
    private static string OutputDir => Path.GetFullPath(Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, Program.ServerSettings.Paths.OutputPath));

    /// <summary>Latest snapshot: the free bytes of the works folder and the work library (null when unset or unknowable) and when it was taken.</summary>
    public sealed record class Snapshot(long? OutputFree, long? LibraryFree, long Taken);

    /// <summary>Latest snapshot, or null before the first check finished.</summary>
    private static volatile Snapshot Latest;

    /// <summary>Stops the background loop when the extension shuts down.</summary>
    private static readonly CancellationTokenSource StopSource = new();

    /// <summary>Starts the background loop; the first check runs right away so a full disk is visible immediately after start.</summary>
    public static void Start()
    {
        _ = Task.Run(async () =>
        {
            while (!StopSource.IsCancellationRequested && !Program.GlobalProgramCancel.IsCancellationRequested)
            {
                Check();
                try
                {
                    await Task.Delay(LoopMs, StopSource.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        });
    }

    /// <summary>Stops the loop.</summary>
    public static void Shutdown()
    {
        StopSource.Cancel();
    }

    /// <summary>Reads the free space of both locations; failures leave the last snapshot in place.</summary>
    public static void Check()
    {
        try
        {
            string library = StorageSettings.Get().LibraryPath;
            long? output = StorageProbe.FreeSpace(OutputDir);
            long? remote = string.IsNullOrEmpty(library) ? null : StorageProbe.FreeSpace(library);
            Latest = new Snapshot(output, remote, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            Logs.Debug($"EcomCraft disk check failed: {ex.Message}");
        }
    }

    /// <summary>The worst level of the watched locations right now, from the latest snapshot.</summary>
    public static string Level()
    {
        Snapshot snapshot = Latest;
        if (snapshot is null)
        {
            return "ok";
        }
        StorageSettings.Values settings = StorageSettings.Get();
        return Worst(
            DiskRules.Level(snapshot.OutputFree, DiskRules.Bytes(settings.DiskWarnGB), DiskRules.Bytes(settings.DiskCriticalGB)),
            DiskRules.Level(snapshot.LibraryFree, DiskRules.Bytes(settings.DiskWarnGB), DiskRules.Bytes(settings.DiskCriticalGB)));
    }

    /// <summary>A plain user-facing reason when new generations must be rejected (F46), or null when there is room.</summary>
    public static string CriticalText()
    {
        Snapshot snapshot = Latest;
        if (snapshot is null)
        {
            return null;
        }
        StorageSettings.Values settings = StorageSettings.Get();
        string outputLevel = DiskRules.Level(snapshot.OutputFree, DiskRules.Bytes(settings.DiskWarnGB), DiskRules.Bytes(settings.DiskCriticalGB));
        string libraryLevel = DiskRules.Level(snapshot.LibraryFree, DiskRules.Bytes(settings.DiskWarnGB), DiskRules.Bytes(settings.DiskCriticalGB));
        if (outputLevel == "critical")
        {
            return "平台服务器的磁盘空间不足，暂时无法生成新图片。请管理员清理磁盘或调低警告阈值后再试。";
        }
        if (libraryLevel == "critical")
        {
            return "作品库位置的剩余空间不足，暂时无法生成新图片。请管理员清理作品库或调低警告阈值后再试。";
        }
        return null;
    }

    /// <summary>The snapshot for administrator views.</summary>
    public static Snapshot Status() => Latest;

    private static string Worst(string a, string b) => a == "critical" || b == "critical" ? "critical" : a == "low" || b == "low" ? "low" : "ok";
}
