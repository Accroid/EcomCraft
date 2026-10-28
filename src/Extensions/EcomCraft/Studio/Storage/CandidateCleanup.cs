using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Utils;

namespace EcomCraftStudio.Studio.Storage;

/// <summary>Deletes simple-mode candidates that were never picked, downloaded or exported (F39): after a configurable number of days the files are removed
/// from the owner's works folder and the work sync queue. Works that were picked, downloaded or exported are dropped from the registry and kept forever.
/// A file is only deleted after its path resolved inside its owner's output folder with a picture or video extension, so nothing outside the works area
/// can ever be touched. The registry survives restarts in Data/EcomCraft/candidates.json.</summary>
public static class CandidateCleanup
{
    /// <summary>One unkept candidate. <see cref="Relative"/> is the path inside the owner's output folder.</summary>
    public sealed class Entry
    {
        /// <summary>Owner account.</summary>
        public string UserId;

        /// <summary>Path relative to the owner's output folder.</summary>
        public string Relative;

        /// <summary>When the candidate was saved, Unix milliseconds.</summary>
        public long Created;
    }

    /// <summary>Interval of the background loop: candidates age in days, so an hourly pass is plenty.</summary>
    private const int LoopMs = 60 * 60 * 1000;

    /// <summary>Registry entries.</summary>
    private static List<Entry> Items;

    /// <summary>Protects <see cref="Items"/> and the registry file.</summary>
    private static readonly object Lock = new();

    /// <summary>Stops the background loop when the extension shuts down.</summary>
    private static readonly CancellationTokenSource StopSource = new();

    /// <summary>Location of the registry file.</summary>
    private static string FilePath => Path.Combine(Program.DataDir, "EcomCraft", "candidates.json");

    /// <summary>Current Unix time in milliseconds.</summary>
    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>Loads the registry and starts the background loop.</summary>
    public static void Start()
    {
        lock (Lock)
        {
            EnsureLoaded();
        }
        _ = Task.Run(async () =>
        {
            while (!StopSource.IsCancellationRequested && !Program.GlobalProgramCancel.IsCancellationRequested)
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
                    CleanupExpired();
                }
                catch (Exception ex)
                {
                    Logs.Warning($"EcomCraft candidate cleanup loop failed: {ex.Message}");
                }
            }
        });
    }

    /// <summary>Stops the loop.</summary>
    public static void Shutdown()
    {
        StopSource.Cancel();
    }

    /// <summary>Reads the registry file once. Callers hold <see cref="Lock"/>. An unreadable file starts an empty registry and is left in place.</summary>
    private static void EnsureLoaded()
    {
        if (Items is not null)
        {
            return;
        }
        Items = [];
        try
        {
            if (File.Exists(FilePath))
            {
                Items = JObject.Parse(File.ReadAllText(FilePath))["candidates"]?.ToObject<List<Entry>>()?.Where(item => item is not null).ToList() ?? [];
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Logs.Error($"EcomCraft could not read the candidate registry; expired candidates will not be cleaned until it is fixed: {ex.Message}");
        }
    }

    /// <summary>Writes the registry through a temporary file. Callers hold <see cref="Lock"/>.</summary>
    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temp = $"{FilePath}.tmp";
            File.WriteAllText(temp, new JObject() { ["version"] = 1, ["candidates"] = JArray.FromObject(Items) }.ToString(Formatting.Indented));
            File.Move(temp, FilePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logs.Error($"EcomCraft could not save the candidate registry: {ex.Message}");
        }
    }

    /// <summary>Registers the saved images of a simple-mode generation as unkept candidates. Accepts "View/&lt;user&gt;/path" and "Output/path" result URLs;
    /// anything else (inline data, other accounts) is ignored. Malicious or odd paths never enter the registry.</summary>
    public static void Register(User user, JArray images)
    {
        if (user is null || images is null)
        {
            return;
        }
        List<Entry> added = [];
        foreach (JToken image in images)
        {
            string url = image.Type == JTokenType.String ? image.ToString() : image["image"]?.ToString();
            string relative = RelativeFor(user, url);
            if (relative is not null && !added.Any(entry => entry.Relative == relative))
            {
                added.Add(new Entry() { UserId = user.UserID, Relative = relative, Created = Now });
            }
        }
        if (added.Count == 0)
        {
            return;
        }
        lock (Lock)
        {
            EnsureLoaded();
            foreach (Entry entry in added)
            {
                if (!Items.Any(item => item.UserId == entry.UserId && item.Relative == entry.Relative))
                {
                    Items.Add(entry);
                }
            }
            Save();
        }
    }

    /// <summary>Marks works kept (picked, downloaded or exported): their registry entries are dropped, so they are never cleaned. Only the account's own
    /// entries are touched. Returns how many were kept.</summary>
    public static int MarkKept(User user, JArray images)
    {
        if (user is null || images is null)
        {
            return 0;
        }
        List<string> relatives = [.. images.Select(image => RelativeFor(user, image.Type == JTokenType.String ? image.ToString() : image["image"]?.ToString())).Where(relative => relative is not null)];
        if (relatives.Count == 0)
        {
            return 0;
        }
        lock (Lock)
        {
            EnsureLoaded();
            int before = Items.Count;
            Items.RemoveAll(item => item.UserId == user.UserID && relatives.Contains(item.Relative));
            int kept = before - Items.Count;
            if (kept > 0)
            {
                Save();
            }
            return kept;
        }
    }

    /// <summary>The gallery-relative path of a result URL for its owner, or null for anything unsafe or foreign.</summary>
    private static string RelativeFor(User user, string url)
    {
        string relative = url is null ? null
            : url.StartsWith($"View/{user.UserID}/", StringComparison.Ordinal) ? url[$"View/{user.UserID}/".Length..]
            : url.StartsWith("Output/", StringComparison.Ordinal) ? url["Output/".Length..] : null;
        return relative is not null && StorageRules.IsSafeRelative(relative) ? relative : null;
    }

    /// <summary>Deletes every expired unkept candidate whose retention time has passed. Files that no longer exist only drop their entry.</summary>
    internal static void CleanupExpired()
    {
        int retentionDays = StorageSettings.Get().CandidateRetentionDays;
        if (retentionDays <= 0)
        {
            return;
        }
        List<Entry> expired;
        lock (Lock)
        {
            EnsureLoaded();
            long now = Now;
            expired = [.. Items.Where(item => CleanupRules.Expired(item.Created, now, retentionDays))];
        }
        if (expired.Count == 0)
        {
            return;
        }
        foreach (Entry entry in expired)
        {
            bool gone = true;
            User user = Program.Sessions.GetUser(entry.UserId, false);
            if (user is not null)
            {
                string local = WorkSync.LocalPathFor(user, $"View/{entry.UserId}/{entry.Relative}", out _);
                string root;
                try
                {
                    root = Path.GetFullPath($"{user.OutputDirectory}");
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    root = null;
                }
                if (local is not null && root is not null)
                {
                    // The path resolves through the owner's output folder by construction; check it again before the first delete (F39).
                    gone = CleanupRules.DeleteWork(local, root) | CleanupRules.DeleteWork(Path.ChangeExtension(local, ".swarm.json"), root);
                }
            }
            // The sync queue drops the candidate as well, together with its already-copied library file.
            foreach (WorkSync.Item queued in WorkSync.TakePending(entry.UserId, entry.Relative))
            {
                if (!string.IsNullOrEmpty(queued.Target) && StorageRules.IsSafeRelative(entry.Relative))
                {
                    CleanupRules.DeleteWork(Path.Combine(queued.Target, entry.UserId, entry.Relative.Replace('/', Path.DirectorySeparatorChar)), Path.Combine(queued.Target, entry.UserId));
                }
            }
            if (!gone)
            {
                // A blocked deletion is retried on the next pass; the entry stays.
                continue;
            }
            lock (Lock)
            {
                Items.Remove(entry);
            }
        }
        lock (Lock)
        {
            Save();
        }
    }
}
