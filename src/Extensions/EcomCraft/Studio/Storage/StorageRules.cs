using System.IO;

namespace EcomCraftStudio.Studio.Storage;

/// <summary>Rules for the work library and export locations (R11) that do not touch the disk, so they can be unit tested.</summary>
public static class StorageRules
{
    /// <summary>First retry delay of a failed sync.</summary>
    public const long FirstRetryMs = 10_000;

    /// <summary>Longest delay between sync retries.</summary>
    public const long MaxRetryMs = 10 * 60_000;

    /// <summary>Checks the form of an administrator-entered location and returns its normalized full path, or a user-facing reason.
    /// Accepts absolute local or mounted directories and Windows UNC paths; rejects relative paths and locations inside the system data or the local output folder,
    /// because system data stays local (R11.9) and syncing into the output folder would copy works onto themselves.</summary>
    public static string Normalize(string raw, string dataDir, string outputDir, out string normalized)
    {
        normalized = null;
        string path = raw?.Trim();
        if (string.IsNullOrEmpty(path))
        {
            return "请填写位置。";
        }
        if (path.Length > 400 || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || path.Contains('\0'))
        {
            return "位置包含无效字符或过长。";
        }
        if (!Path.IsPathFullyQualified(path))
        {
            return "请填写完整路径，例如 D:\\EcomCraft、\\\\NAS\\share\\EcomCraft 或 /mnt/nas/ecomcraft。";
        }
        string full;
        try
        {
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "无法识别这个路径。";
        }
        if (IsSameOrInside(full, dataDir) || IsSameOrInside(dataDir, full))
        {
            return "不能使用系统数据目录（Data）或包含它的目录；系统数据固定保存在平台服务器本机。";
        }
        if (IsSameOrInside(full, outputDir) || IsSameOrInside(outputDir, full))
        {
            return "不能使用平台服务器的输出目录或包含它的目录。";
        }
        normalized = full;
        return null;
    }

    /// <summary>Whether <paramref name="path"/> equals <paramref name="root"/> or lies inside it. Comparison ignores case, as Windows and most NAS shares do.</summary>
    public static bool IsSameOrInside(string path, string root)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root))
        {
            return false;
        }
        string a = Path.TrimEndingDirectorySeparator(path.Replace('\\', '/'));
        string b = Path.TrimEndingDirectorySeparator(root.Replace('\\', '/'));
        // Roots such as "C:/" or "/" keep their trailing separator after trimming.
        string prefix = b.EndsWith('/') ? b : b + "/";
        return a.Equals(b, StringComparison.OrdinalIgnoreCase) || a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether a relative work path stays inside its root: no rooted path, drive, "..", or empty segment.</summary>
    public static bool IsSafeRelative(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':') || relative.Contains('\0'))
        {
            return false;
        }
        foreach (string part in relative.Replace('\\', '/').Split('/'))
        {
            if (part.Length == 0 || part == "." || part == "..")
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Delay before the next sync attempt: doubles from <see cref="FirstRetryMs"/> up to <see cref="MaxRetryMs"/>.</summary>
    public static long RetryDelayMs(int attempts)
    {
        if (attempts <= 0)
        {
            return 0;
        }
        int shift = Math.Min(attempts - 1, 16);
        return Math.Min(MaxRetryMs, FirstRetryMs << shift);
    }
}
