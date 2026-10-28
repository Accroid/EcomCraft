using System.IO;

namespace EcomCraftStudio.Studio.Storage;

/// <summary>Disk-free rules of the candidate cleanup (F39), so they can be unit tested.</summary>
public static class CleanupRules
{
    /// <summary>File extensions a candidate may have; anything else is never deleted automatically.</summary>
    private static readonly HashSet<string> WorkExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".mp4", ".webm"
    };

    /// <summary>Whether a candidate's retention time has passed. A disabled (0) or negative retention never expires anything.</summary>
    public static bool Expired(long created, long now, int retentionDays)
    {
        if (retentionDays <= 0 || created <= 0)
        {
            return false;
        }
        return now - created >= retentionDays * 86_400_000L;
    }

    /// <summary>Whether a resolved path lies strictly inside <paramref name="root"/> and is a work file: a picture or video, or the
    /// `.swarm.json` metadata sidecar that belongs to one.</summary>
    public static bool SafeToDelete(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
        {
            return false;
        }
        bool workFile = WorkExtensions.Contains(Path.GetExtension(path))
            || Path.GetFileName(path).EndsWith(".swarm.json", StringComparison.OrdinalIgnoreCase);
        if (!workFile)
        {
            return false;
        }
        try
        {
            string full = Path.GetFullPath(path);
            string rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            return full.Length > rootFull.Length + 1 && full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>Deletes one file when it lies inside <paramref name="root"/> and has a work extension; missing files count as deleted. Never throws.</summary>
    public static bool DeleteWork(string path, string root)
    {
        try
        {
            if (!SafeToDelete(path, root))
            {
                return false;
            }
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
