using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace EcomCraftStudio.Studio.Storage;

/// <summary>Checks a location before it is saved (R11.4): reachable, writable, free space and write speed. A hung network share is abandoned after a timeout.</summary>
public static class StorageProbe
{
    /// <summary>Size of the test file.</summary>
    private const int ProbeBytes = 8 * 1024 * 1024;

    /// <summary>Smallest free space accepted when the system can report it.</summary>
    public const long MinFreeBytes = 1024L * 1024 * 1024;

    /// <summary>Longest time a check may take.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>The outcome of one check. <see cref="Error"/> is a user-facing reason without exception details; free space is null when the system cannot report it (for example UNC paths).</summary>
    public sealed record class Result(bool Ok, string Error, long? FreeBytes, double? WriteMBps);

    /// <summary>Runs the check on a background thread and gives up after the timeout, so a disconnected share cannot block the request.</summary>
    public static async Task<Result> CheckAsync(string path)
    {
        Task<Result> work = Task.Run(() => Check(path));
        Task finished = await Task.WhenAny(work, Task.Delay(Timeout));
        if (finished != work)
        {
            return new Result(false, "位置在 30 秒内没有响应。请确认网络共享在线，并且平台服务器能访问它。", null, null);
        }
        return await work;
    }

    /// <summary>Creates the folder if needed, writes, reads back and deletes a test file, and reads the free space.</summary>
    private static Result Check(string path)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && path.Length >= 2 && path[1] == ':')
            {
                DriveInfo drive = new(path[..1]);
                if (drive.DriveType == DriveType.Network)
                {
                    return new Result(false, "不支持映射的网络盘符：后台运行的服务看不到登录用户映射的盘符。请改用 \\\\服务器\\共享 形式的路径。", null, null);
                }
            }
            Directory.CreateDirectory(path);
            string probe = Path.Combine(path, $".ecomcraft-probe-{Guid.NewGuid():N}.tmp");
            byte[] data = new byte[ProbeBytes];
            Random.Shared.NextBytes(data);
            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                using (FileStream stream = new(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.WriteThrough))
                {
                    stream.Write(data, 0, data.Length);
                    stream.Flush(true);
                }
                watch.Stop();
                if (new FileInfo(probe).Length != data.Length)
                {
                    return new Result(false, "测试文件写入后大小不一致，请检查存储设备。", null, null);
                }
            }
            finally
            {
                try
                {
                    File.Delete(probe);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A leftover probe file is harmless; the check result still stands.
                }
            }
            double seconds = Math.Max(watch.Elapsed.TotalSeconds, 0.001);
            double speed = Math.Round(ProbeBytes / 1024.0 / 1024.0 / seconds, 1);
            long? free = FreeSpace(path);
            if (free is not null && free < MinFreeBytes)
            {
                return new Result(false, "剩余空间不足 1 GB。", free, speed);
            }
            return new Result(true, null, free, speed);
        }
        catch (UnauthorizedAccessException)
        {
            return new Result(false, "平台服务器运行服务的账号没有写入权限。请在共享或文件夹上给该账号授予读写权限。", null, null);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            return new Result(false, "无法访问或写入这个位置。请确认路径存在、共享在线，并且平台服务器能访问它。", null, null);
        }
    }

    /// <summary>Free bytes of the volume holding the path, or null when it cannot be determined (UNC paths and unusual mounts).</summary>
    internal static long? FreeSpace(string path)
    {
        try
        {
            if (path.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return null;
            }
            // The most specific mount point holding the path, so a NAS mounted under /mnt reports its own space rather than the root volume's.
            DriveInfo best = null;
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady && StorageRules.IsSameOrInside(path, drive.Name) && (best is null || drive.Name.Length > best.Name.Length))
                {
                    best = drive;
                }
            }
            return best?.AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
