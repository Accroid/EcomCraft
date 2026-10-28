using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Utils;

namespace EcomCraftStudio.Studio.Storage;

/// <summary>Copies finished workspace works from the platform server to the work library in the background (R11.5): works are always saved locally first,
/// a missing or offline library never blocks generation, and failed copies retry with backoff. The queue survives restarts in Data/EcomCraft/storage-sync.json.
/// Local copies stay, so the gallery keeps reading local files and thumbnails (R11.6). Paths never leave the server.</summary>
public static class WorkSync
{
    /// <summary>Interval of the background loop.</summary>
    private const int LoopMs = 5_000;

    /// <summary>A queued work is dropped when its local file has not appeared after this long.</summary>
    private const long MissingFileGiveUpMs = 10 * 60_000;

    /// <summary>Longest time one file copy may take before it counts as failed.</summary>
    private static readonly TimeSpan CopyTimeout = TimeSpan.FromMinutes(2);

    /// <summary>One work waiting to be copied. <see cref="Relative"/> is the path inside the user's output folder, as the gallery lists it.</summary>
    public sealed class Item
    {
        /// <summary>Owner account.</summary>
        public string UserId;

        /// <summary>Path relative to the owner's output folder.</summary>
        public string Relative;

        /// <summary>Full local path of the saved work.</summary>
        public string LocalPath;

        /// <summary>Library root at the time the work was queued; later location changes do not move queued works.</summary>
        public string Target;

        /// <summary>Failed attempts so far.</summary>
        public int Attempts;

        /// <summary>Earliest next attempt, Unix milliseconds.</summary>
        public long NextTryAt;

        /// <summary>When the work was queued, Unix milliseconds.</summary>
        public long QueuedAt;

        /// <summary>User-facing reason of the last failure, without paths or exception details.</summary>
        public string LastError;
    }

    /// <summary>Progress of the optional migration of old works to the current library (R11.7).</summary>
    public sealed class Migration
    {
        /// <summary>Whether a migration is running.</summary>
        public bool Running;

        /// <summary>Files found in the old location.</summary>
        public int Total;

        /// <summary>Files copied, verified and removed from the old location.</summary>
        public int Done;

        /// <summary>Files that could not be moved; they stay in the old location.</summary>
        public int Failed;

        /// <summary>Whether the last run was cancelled.</summary>
        public bool Cancelled;

        /// <summary>User-facing summary of the last problem, or null.</summary>
        public string Error;
    }

    /// <summary>Queued works.</summary>
    private static List<Item> Items;

    /// <summary>Protects <see cref="Items"/> and the queue file.</summary>
    private static readonly object Lock = new();

    /// <summary>Current or last migration.</summary>
    private static Migration MigrationState = new();

    /// <summary>Protects <see cref="MigrationState"/> and <see cref="MigrationCancel"/>.</summary>
    private static readonly object MigrationLock = new();

    /// <summary>Cancels the running migration, or null.</summary>
    private static CancellationTokenSource MigrationCancel;

    /// <summary>Stops the background loop when the extension shuts down.</summary>
    private static readonly CancellationTokenSource StopSource = new();

    /// <summary>Location of the queue file.</summary>
    private static string FilePath => Path.Combine(Program.DataDir, "EcomCraft", "storage-sync.json");

    /// <summary>Current Unix time in milliseconds.</summary>
    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>Loads the queue and starts the background loop.</summary>
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
                    await ProcessDueAsync();
                }
                catch (Exception ex)
                {
                    Logs.Warning($"EcomCraft work sync loop failed: {ex.Message}");
                }
            }
        });
    }

    /// <summary>Stops the loop and any migration.</summary>
    public static void Shutdown()
    {
        StopSource.Cancel();
        lock (MigrationLock)
        {
            MigrationCancel?.Cancel();
        }
    }

    /// <summary>Reads the queue file once. Callers hold <see cref="Lock"/>. An unreadable file starts an empty queue and is left in place.</summary>
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
                Items = JObject.Parse(File.ReadAllText(FilePath))["items"]?.ToObject<List<Item>>()?.Where(item => item is not null).ToList() ?? [];
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Logs.Error($"EcomCraft could not read the work sync queue; queued works will not be copied until it is fixed: {ex.Message}");
        }
    }

    /// <summary>Writes the queue through a temporary file. Callers hold <see cref="Lock"/>.</summary>
    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temp = $"{FilePath}.tmp";
            File.WriteAllText(temp, new JObject() { ["version"] = 1, ["items"] = JArray.FromObject(Items) }.ToString(Formatting.Indented));
            File.Move(temp, FilePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logs.Error($"EcomCraft could not save the work sync queue: {ex.Message}");
        }
    }

    /// <summary>Queues the works of a finished generation when a library is set. <paramref name="result"/> is the generation API result with "images" as
    /// "View/&lt;user&gt;/path" or "Output/path" strings; anything else (data URLs when saving is off, unexpected forms) is ignored.</summary>
    public static void EnqueueResult(User user, JObject result)
    {
        string library = StorageSettings.Get().LibraryPath;
        if (string.IsNullOrEmpty(library) || user is null || result?["images"] is not JArray images || !StorageRules.IsSafeRelative(user.UserID))
        {
            return;
        }
        List<Item> added = [];
        foreach (JToken image in images)
        {
            string local = LocalPathFor(user, image.Type == JTokenType.String ? image.ToString() : image["image"]?.ToString(), out string relative);
            if (local is null)
            {
                continue;
            }
            added.Add(new Item() { UserId = user.UserID, Relative = relative, LocalPath = local, Target = library, QueuedAt = Now });
        }
        if (added.Count == 0)
        {
            return;
        }
        lock (Lock)
        {
            EnsureLoaded();
            Items.AddRange(added);
            Save();
        }
    }

    /// <summary>The local file of a generation result URL ("View/&lt;user&gt;/path" or "Output/path") and its path relative to the user's output folder,
    /// or null for anything else (data URLs, other users' paths, unsafe paths).</summary>
    public static string LocalPathFor(User user, string url, out string relative)
    {
        relative = url is null ? null
            : url.StartsWith($"View/{user.UserID}/", StringComparison.Ordinal) ? url[$"View/{user.UserID}/".Length..]
            : url.StartsWith("Output/", StringComparison.Ordinal) ? url["Output/".Length..] : null;
        if (relative is null || !StorageRules.IsSafeRelative(relative))
        {
            relative = null;
            return null;
        }
        try
        {
            return Path.GetFullPath(UserImageHistoryHelper.GetRealPathFor(user, $"{user.OutputDirectory}/{relative}"));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            relative = null;
            return null;
        }
    }

    /// <summary>Gallery paths of the account's works still waiting to be copied, for the "pending sync" mark. No locations are returned.</summary>
    public static List<string> PendingFor(string userId)
    {
        lock (Lock)
        {
            EnsureLoaded();
            return [.. Items.Where(item => item.UserId == userId).Select(item => item.Relative)];
        }
    }

    /// <summary>Queue size, how many have failed at least once, and the latest failure reason, for administrators.</summary>
    public static (int Pending, int Failing, string LastError) Stats()
    {
        lock (Lock)
        {
            EnsureLoaded();
            Item latest = Items.Where(item => item.Attempts > 0).OrderByDescending(item => item.NextTryAt).FirstOrDefault();
            return (Items.Count, Items.Count(item => item.Attempts > 0), latest?.LastError);
        }
    }

    /// <summary>Copies the works whose next attempt is due, one at a time.</summary>
    private static async Task ProcessDueAsync()
    {
        List<Item> due;
        lock (Lock)
        {
            EnsureLoaded();
            long now = Now;
            due = [.. Items.Where(item => item.NextTryAt <= now).Take(50)];
        }
        foreach (Item item in due)
        {
            if (StopSource.IsCancellationRequested)
            {
                return;
            }
            string outcome = await CopyAsync(item);
            lock (Lock)
            {
                if (outcome == "done" || outcome == "drop")
                {
                    Items.Remove(item);
                }
                else if (outcome != "wait")
                {
                    item.Attempts++;
                    item.NextTryAt = Now + StorageRules.RetryDelayMs(item.Attempts);
                    item.LastError = outcome;
                }
                Save();
            }
        }
    }

    /// <summary>Copies one work and its metadata sidecar through a temporary file. Returns "done", "wait" (still saving), "drop" (gone for good) or a failure reason.</summary>
    private static async Task<string> CopyAsync(Item item)
    {
        if (Session.StillSavingFiles.ContainsKey(item.LocalPath) || Session.StillSavingFiles.ContainsKey(item.LocalPath.Replace('\\', '/')))
        {
            return "wait";
        }
        if (!File.Exists(item.LocalPath))
        {
            if (Now - item.QueuedAt > MissingFileGiveUpMs)
            {
                Logs.Warning($"EcomCraft dropped a queued work that never appeared locally: {item.Relative}");
                return "drop";
            }
            return "wait";
        }
        string destination = Path.Combine(item.Target, item.UserId, item.Relative.Replace('/', Path.DirectorySeparatorChar));
        Task<string> work = Task.Run(() =>
        {
            try
            {
                CopyVerified(item.LocalPath, destination, false);
                string sidecar = Path.ChangeExtension(item.LocalPath, ".swarm.json");
                if (File.Exists(sidecar))
                {
                    CopyVerified(sidecar, Path.ChangeExtension(destination, ".swarm.json"), false);
                }
                return "done";
            }
            catch (UnauthorizedAccessException)
            {
                return "作品库拒绝写入，请管理员检查共享权限；会自动重试。";
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
            {
                return "作品库暂时无法写入（可能离线或空间不足）；会自动重试。";
            }
        });
        if (await Task.WhenAny(work, Task.Delay(CopyTimeout)) != work)
        {
            return "作品库在 2 分钟内没有响应；会自动重试。";
        }
        return await work;
    }

    /// <summary>Copies a file to a temporary name beside the destination, checks the size (and optionally the SHA-256), then renames it into place.</summary>
    private static void CopyVerified(string source, string destination, bool hash)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        string temp = $"{destination}.ecomcraft-tmp";
        File.Copy(source, temp, true);
        if (new FileInfo(temp).Length != new FileInfo(source).Length || (hash && !SameContent(source, temp)))
        {
            File.Delete(temp);
            throw new IOException("Copy verification failed.");
        }
        File.Move(temp, destination, true);
    }

    /// <summary>Whether two files have the same SHA-256.</summary>
    private static bool SameContent(string a, string b)
    {
        using FileStream first = File.OpenRead(a);
        using FileStream second = File.OpenRead(b);
        return SHA256.HashData(first).AsSpan().SequenceEqual(SHA256.HashData(second));
    }

    /// <summary>A copy of the migration progress.</summary>
    public static Migration MigrationStatus()
    {
        lock (MigrationLock)
        {
            return JObject.FromObject(MigrationState).ToObject<Migration>();
        }
    }

    /// <summary>Starts moving the works of <paramref name="from"/> to <paramref name="to"/>: copy, verify by SHA-256, then delete the original.
    /// Returns an error when one is already running. Files that fail stay where they were.</summary>
    public static string StartMigration(string from, string to)
    {
        lock (MigrationLock)
        {
            if (MigrationState.Running)
            {
                return "迁移已经在进行中。";
            }
            MigrationCancel?.Dispose();
            MigrationCancel = CancellationTokenSource.CreateLinkedTokenSource(StopSource.Token);
            MigrationState = new Migration() { Running = true };
        }
        CancellationToken cancel = MigrationCancel.Token;
        _ = Task.Run(() => Migrate(from, to, cancel));
        return null;
    }

    /// <summary>Asks the running migration to stop after the current file.</summary>
    public static void CancelMigration()
    {
        lock (MigrationLock)
        {
            MigrationCancel?.Cancel();
        }
    }

    /// <summary>Moves every file under the old root to the same relative place under the new root.</summary>
    private static void Migrate(string from, string to, CancellationToken cancel)
    {
        try
        {
            List<string> files = [.. Directory.EnumerateFiles(from, "*", new EnumerationOptions() { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
                .Where(file => !file.EndsWith(".ecomcraft-tmp", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(file).StartsWith(".ecomcraft-probe-", StringComparison.Ordinal))];
            lock (MigrationLock)
            {
                MigrationState.Total = files.Count;
            }
            foreach (string file in files)
            {
                if (cancel.IsCancellationRequested)
                {
                    lock (MigrationLock)
                    {
                        MigrationState.Cancelled = true;
                    }
                    break;
                }
                bool ok;
                try
                {
                    CopyVerified(file, Path.Combine(to, Path.GetRelativePath(from, file)), true);
                    File.Delete(file);
                    ok = true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    ok = false;
                }
                lock (MigrationLock)
                {
                    if (ok)
                    {
                        MigrationState.Done++;
                    }
                    else
                    {
                        MigrationState.Failed++;
                        MigrationState.Error = "部分作品无法迁移，已保留在原位置。请检查两个位置的权限和空间后重试。";
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            lock (MigrationLock)
            {
                MigrationState.Error = "无法读取原作品库位置，迁移没有开始。请确认它仍然在线。";
            }
        }
        finally
        {
            lock (MigrationLock)
            {
                MigrationState.Running = false;
            }
        }
    }
}
