using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

namespace EcomCraftStudio.Studio;

/// <summary>Manifest rules of the system-data backup (F45), free of SwarmUI dependencies so they can be unit tested.
/// A backup is valid only when every file it lists is present with the recorded size and SHA-256, and the zip holds nothing else.</summary>
public static class BackupRules
{
    /// <summary>The backup entry name of a local file: files under the EcomCraft data root are "ecomcraft/…", other files under the data root
    /// (the account database) are "accounts/…".</summary>
    public static string EntryName(string file, string ecomcraftRoot, string dataRoot)
    {
        if (file.StartsWith(ecomcraftRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return "ecomcraft/" + file[(ecomcraftRoot.Length + 1)..].Replace('\\', '/');
        }
        return "accounts/" + file[(dataRoot.Length + 1)..].Replace('\\', '/');
    }

    /// <summary>Builds the manifest entry of one file.</summary>
    public static JObject Entry(string name, long size, string sha256) => new()
    {
        ["name"] = name,
        ["size"] = size,
        ["sha256"] = sha256
    };

    /// <summary>The SHA-256 of a file's content, hex encoded. Files the server holds open (the account database) are read with write sharing.</summary>
    public static string Hash(string path)
    {
        using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>The SHA-256 of zip entry content, hex encoded.</summary>
    public static string Hash(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>Validates a whole backup: a "problems" list that is empty when every listed file matches its size and hash and the zip contains
    /// no unexpected file. Manifest field access is defensive, so a damaged manifest never throws.</summary>
    public static List<string> Validate(ZipArchive zip, JObject manifest)
    {
        List<string> problems = [];
        if (manifest?["files"] is not JArray files || files.Count == 0)
        {
            return ["备份清单缺失或为空。"];
        }
        Dictionary<string, ZipArchiveEntry> entries = zip.Entries.ToDictionary(entry => entry.FullName, entry => entry, StringComparer.Ordinal);
        HashSet<string> listed = [];
        foreach (JToken item in files)
        {
            string name = item["name"]?.ToString();
            if (string.IsNullOrWhiteSpace(name) || name.Contains("..") || Path.IsPathRooted(name))
            {
                problems.Add("清单包含不安全的路径。");
                continue;
            }
            listed.Add(name);
            if (!entries.TryGetValue(name, out ZipArchiveEntry entry))
            {
                problems.Add($"缺少文件：{name}");
                continue;
            }
            long size = item.Value<long?>("size") ?? -1;
            string hash = item["sha256"]?.ToString();
            if (entry.Length != size)
            {
                problems.Add($"大小不一致：{name}");
            }
            else if (string.IsNullOrEmpty(hash) || Hash(entry) != hash)
            {
                problems.Add($"内容校验失败：{name}");
            }
        }
        foreach (string extra in entries.Keys.Where(name => name != "manifest.json" && !listed.Contains(name)))
        {
            problems.Add($"备份中有清单未记录的文件：{extra}");
        }
        return problems;
    }
}
