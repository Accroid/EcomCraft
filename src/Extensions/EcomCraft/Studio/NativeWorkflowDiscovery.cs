using System.IO;
using System.Text.RegularExpressions;
using SwarmUI.Core;
using SwarmUI.Utils;

namespace EcomCraftStudio.Studio;

/// <summary>Finds workflow folders inside verified local ComfyUI installations, without scanning arbitrary JSON files.</summary>
public static class NativeWorkflowDiscovery
{
    /// <summary>Installation discovery is bounded by the existing installer scanner and cached between catalog polls.</summary>
    private static List<string> Installations = [];

    /// <summary>Next allowed rescan time.</summary>
    private static long NextScan;

    /// <summary>Protects installation discovery across simultaneous catalog requests.</summary>
    private static readonly object ScanLock = new();

    /// <summary>Combines administrator paths, configured backends and automatically discovered installation folders.</summary>
    public static List<string> Directories(IEnumerable<(string Root, string Args)> backends, IEnumerable<string> configured)
    {
        List<string> installations;
        lock (ScanLock)
        {
            if (Environment.TickCount64 >= NextScan)
            {
                Installations = Installation.FindComfyScripts().Select(Path.GetDirectoryName).ToList();
                NextScan = Environment.TickCount64 + 60000;
            }
            installations = [.. Installations];
        }
        return Collect(backends, configured, installations);
    }

    /// <summary>Enumerates only standard workflow/blueprint folders; exposed separately for filesystem regression tests.</summary>
    public static List<string> Collect(IEnumerable<(string Root, string Args)> backends, IEnumerable<string> configured, IEnumerable<string> installations)
    {
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        void Add(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)
                && !File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            {
                paths.Add(Path.GetFullPath(path));
            }
        }
        void AddInstallation(string root, string args)
        {
            Add(Path.Combine(root, "blueprints"));
            Add(Path.Combine(root, "workflows"));
            string users = Path.Combine(root, "user");
            Match match = Regex.Match(args ?? "", @"(?:^|\s)--user-directory(?:=|\s+)(?:""([^""]+)""|'([^']+)'|([^\s]+))");
            if (match.Success)
            {
                string value = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
                users = Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(root, value));
            }
            Add(Path.Combine(users, "default", "workflows"));
            Add(Path.Combine(users, "default", "blueprints"));
        }
        foreach (string path in configured)
        {
            try
            {
                Add(path?.Trim());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                Logs.Warning("EcomCraft could not read a configured workflow directory.");
            }
        }
        HashSet<string> roots = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string root, string args) in backends.Concat(installations.Select(root => (root, ""))))
        {
            try
            {
                if (string.IsNullOrWhiteSpace(root) || !roots.Add(Path.GetFullPath(root)))
                {
                    continue;
                }
                AddInstallation(Path.GetFullPath(root), args);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                Logs.Warning("EcomCraft could not read a local ComfyUI workflow directory.");
            }
        }
        return [.. paths];
    }

    /// <summary>Returns a short installation label for same-name templates without revealing an absolute path.</summary>
    public static string SourceLabel(string directory)
    {
        DirectoryInfo current = new(directory);
        for (int depth = 0; current is not null && depth < 8; depth++, current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "main.py")) && File.Exists(Path.Combine(current.FullName, "folder_paths.py")))
            {
                return current.Name.Equals("ComfyUI", StringComparison.OrdinalIgnoreCase) ? current.Parent?.Name ?? current.Name : current.Name;
            }
        }
        return Path.GetFileName(directory);
    }
}
