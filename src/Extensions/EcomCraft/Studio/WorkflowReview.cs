using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Core;
using SwarmUI.Utils;

namespace EcomCraftStudio.Studio;

/// <summary>Review records and frozen snapshots of discovered workflows, saved under Data/EcomCraft.
/// Only published snapshots reach the workflow registry; discovered content never runs directly, so a changed source file has no effect until it is reviewed again.</summary>
public static class WorkflowReview
{
    /// <summary>Discovered and not yet decided, or changed since it was rejected.</summary>
    public const string Pending = "pending";

    /// <summary>A snapshot is published and the source matches it, or its newer version was rejected.</summary>
    public const string Published = "published";

    /// <summary>A snapshot is published but the source has changed; users keep running the snapshot.</summary>
    public const string Rereview = "rereview";

    /// <summary>Rejected with a reason; returns to pending when the source changes or the workflow is resubmitted.</summary>
    public const string Rejected = "rejected";

    /// <summary>Taken down by a reviewer; stays down until approved again.</summary>
    public const string Unpublished = "unpublished";

    /// <summary>The source file failed validation and cannot be submitted or published.</summary>
    public const string Invalid = "invalid";

    /// <summary>Uploaded by an account for its own use (F60, decision U6): only the owner sees and runs it; it never appears in the review list.</summary>
    public const string Own = "own";

    /// <summary>Returned by <see cref="Update"/> when the file cannot be written.</summary>
    public const string SaveFailedMessage = "审核记录无法保存到服务器数据目录，请联系管理员检查磁盘空间和文件权限。";

    /// <summary>Upper bound for reviewer-entered parameter names.</summary>
    public const int MaxNameLength = 40;

    /// <summary>Upper bound for reviewer-entered descriptions, notes and reasons.</summary>
    public const int MaxTextLength = 500;

    /// <summary>Page fields every published workflow keeps visible: the engine requires them and the page always shows them.</summary>
    private static readonly HashSet<string> LockedForms = new(StringComparer.Ordinal) { "prompt", "negativeprompt", "width", "height" };

    /// <summary>One frozen, approved workflow. The base bindings are kept so a reviewer can change the configuration later without the source.</summary>
    public sealed class Snapshot
    {
        /// <summary>Source canvas, so users with SwarmUI's workflow-reading permission can still open the snapshot in the native ComfyUI editor.</summary>
        public string Canvas;

        /// <summary>Converted executable graph.</summary>
        public string Prompt;

        /// <summary>Parameter bindings as discovered, before the reviewer configuration.</summary>
        public string BaseParams;

        /// <summary>Saved parameter values as discovered.</summary>
        public string BaseValues;

        /// <summary>Reviewer parameter configuration, keyed by parameter ID.</summary>
        public string Config;

        /// <summary>User-facing description.</summary>
        public string Description;

        /// <summary>Fingerprint of the source version this snapshot was taken from.</summary>
        public string Fingerprint;

        /// <summary>Display name of the server the source came from.</summary>
        public string Server;

        /// <summary>Account that approved it.</summary>
        public string Reviewer;

        /// <summary>Approval time, Unix milliseconds.</summary>
        public long ReviewedAt;
    }

    /// <summary>The persisted review state of one workflow ID.</summary>
    public sealed class Record
    {
        /// <summary>Last decision: <see cref="Published"/>, <see cref="Rejected"/>, <see cref="Unpublished"/>, or null.</summary>
        public string Status;

        /// <summary>The published snapshot, or null.</summary>
        public Snapshot Snapshot;

        /// <summary>Source version the last rejection applies to.</summary>
        public string RejectedFingerprint;

        /// <summary>Reason given with the last rejection.</summary>
        public string RejectReason;

        /// <summary>Account that made the last decision.</summary>
        public string Reviewer;

        /// <summary>Time of the last decision, Unix milliseconds.</summary>
        public long ReviewedAt;

        /// <summary>Account that last submitted it for review.</summary>
        public string SubmittedBy;

        /// <summary>Note given with the last submission.</summary>
        public string SubmitNote;

        /// <summary>Time of the last submission, Unix milliseconds.</summary>
        public long SubmittedAt;

        /// <summary>User ID of the account that uploaded an <see cref="Own"/> workflow; null for discovered workflows.</summary>
        public string Owner;

        /// <summary>Display name the owner gave an <see cref="Own"/> workflow.</summary>
        public string Name;

        /// <summary>Upload time of an <see cref="Own"/> workflow, Unix milliseconds.</summary>
        public long UploadedAt;

        /// <summary>ID of the published copy made when a reviewer approved this <see cref="Own"/> workflow (T73), or null.</summary>
        public string PublishedAs;
    }

    /// <summary>Review state of an own workflow (T73): "draft" (never submitted), "pending" (submitted and not decided since),
    /// "rejected" (the last decision after the last submission), "published" (approved into a copy for everyone), or null for other records.</summary>
    public static string OwnState(Record record)
    {
        if (record?.Status != Own)
        {
            return null;
        }
        if (record.SubmittedAt <= 0)
        {
            return "draft";
        }
        if (record.ReviewedAt < record.SubmittedAt)
        {
            return Pending;
        }
        return record.PublishedAs is not null && record.RejectReason is null ? Published : Rejected;
    }

    /// <summary>Publishes an approved own workflow (T73): stores a published record under <paramref name="publishedId"/> with the reviewer's snapshot
    /// and marks the own record as published, in one save. The own workflow stays with its owner. Returns null, or an error message.</summary>
    public static string PublishOwn(string ownId, string publishedId, Snapshot snapshot, string reviewer)
    {
        lock (Lock)
        {
            EnsureLoaded();
            if (!Records.TryGetValue(ownId, out Record own) || OwnState(own) != Pending)
            {
                return "该工作流的审核状态刚被修改，请刷新后重新审核。";
            }
            if (Records.ContainsKey(publishedId) || ComfyUIBackendExtension.CustomWorkflows.ContainsKey(publishedId))
            {
                return "发布名称已被占用，请刷新后重试。";
            }
            long now = Now;
            Record published = new()
            {
                Status = Published,
                Snapshot = snapshot,
                Reviewer = reviewer,
                ReviewedAt = now,
                SubmittedBy = own.SubmittedBy,
                SubmitNote = own.SubmitNote,
                SubmittedAt = own.SubmittedAt
            };
            Record updated = Copy(own);
            updated.Reviewer = reviewer;
            updated.ReviewedAt = now;
            updated.RejectReason = null;
            updated.PublishedAs = publishedId;
            Records[publishedId] = published;
            Records[ownId] = updated;
            if (!Save())
            {
                Records.Remove(publishedId);
                Records[ownId] = own;
                return SaveFailedMessage;
            }
            ComfyUIBackendExtension.ComfyCustomWorkflow built = Build(publishedId, snapshot);
            Registered[publishedId] = built;
            ComfyUIBackendExtension.CustomWorkflows.TryAdd(publishedId, built);
            return null;
        }
    }

    /// <summary>Records by workflow ID.</summary>
    private static Dictionary<string, Record> Records = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registry objects built from published snapshots, by workflow ID. Reference identity marks registry entries owned by this store.</summary>
    private static readonly Dictionary<string, ComfyUIBackendExtension.ComfyCustomWorkflow> Registered = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Protects <see cref="Records"/>, <see cref="Registered"/> and the file.</summary>
    private static readonly object Lock = new();

    /// <summary>Whether the file has been read.</summary>
    private static bool Loaded;

    /// <summary>Set when the file exists but could not be read; saving is refused so it is not overwritten.</summary>
    private static bool LoadFailed;

    /// <summary>Location of the review file.</summary>
    private static string FilePath => Path.Combine(Program.DataDir, "EcomCraft", "workflow-reviews.json");

    /// <summary>Current Unix time in milliseconds.</summary>
    public static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>Reads the file once and builds the registry objects of published snapshots. Callers hold <see cref="Lock"/>.</summary>
    private static void EnsureLoaded()
    {
        if (Loaded)
        {
            return;
        }
        Loaded = true;
        string path = FilePath;
        try
        {
            if (!File.Exists(path))
            {
                return;
            }
            JObject root = JObject.Parse(File.ReadAllText(path));
            Dictionary<string, Record> loaded = root["records"]?.ToObject<Dictionary<string, Record>>() ?? new Dictionary<string, Record>();
            Records = new Dictionary<string, Record>(loaded.Where(pair => pair.Value is not null), StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, Record> pair in Records)
            {
                if (pair.Value.Snapshot is not null)
                {
                    Registered[pair.Key] = Build(pair.Key, pair.Value.Snapshot);
                }
            }
        }
        catch (JsonException ex)
        {
            // Keep the damaged file for the administrator and start empty, so reviews can continue.
            Logs.Error($"EcomCraft workflow review file is damaged and was set aside: {ex.Message}");
            Records = new(StringComparer.OrdinalIgnoreCase);
            Registered.Clear();
            try
            {
                File.Move(path, $"{path}.damaged-{DateTime.Now:yyyyMMddHHmmss}", true);
            }
            catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException)
            {
                LoadFailed = true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logs.Error($"EcomCraft could not read the workflow review file: {ex.Message}");
            LoadFailed = true;
        }
    }

    /// <summary>Writes all records through a temporary file. Callers hold <see cref="Lock"/>. Returns false when the file cannot be written.</summary>
    private static bool Save()
    {
        if (LoadFailed)
        {
            return false;
        }
        string path = FilePath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            JObject root = new() { ["version"] = 1, ["records"] = JObject.FromObject(Records) };
            string temp = $"{path}.tmp";
            File.WriteAllText(temp, root.ToString(Formatting.Indented));
            File.Move(temp, path, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logs.Error($"EcomCraft could not save the workflow review file: {ex.Message}");
            return false;
        }
    }

    /// <summary>Re-adds published snapshots after a ComfyUI workflow registry reload. Never replaces a stored SwarmUI workflow with the same name.</summary>
    public static void Restore()
    {
        lock (Lock)
        {
            EnsureLoaded();
            foreach (KeyValuePair<string, ComfyUIBackendExtension.ComfyCustomWorkflow> pair in Registered)
            {
                ComfyUIBackendExtension.CustomWorkflows.TryAdd(pair.Key, pair.Value);
            }
        }
    }

    /// <summary>Whether a registry entry is the snapshot this store published under that name.</summary>
    public static bool IsSnapshot(string name, ComfyUIBackendExtension.ComfyCustomWorkflow workflow)
    {
        if (name is null || workflow is null)
        {
            return false;
        }
        lock (Lock)
        {
            EnsureLoaded();
            return Registered.TryGetValue(name, out ComfyUIBackendExtension.ComfyCustomWorkflow own) && ReferenceEquals(own, workflow);
        }
    }

    /// <summary>Whether the named registry entry is a published EcomCraft snapshot (a native ComfyUI workflow) for every account,
    /// rather than a stored SwarmUI workflow or one account's own upload.</summary>
    public static bool IsPublished(string name) => IsRegisteredSnapshot(name) && OwnerOf(name) is null;

    /// <summary>Whether the named registry entry is a snapshot of this store, published or own.</summary>
    private static bool IsRegisteredSnapshot(string name) => name is not null
        && ComfyUIBackendExtension.CustomWorkflows.TryGetValue(name, out ComfyUIBackendExtension.ComfyCustomWorkflow workflow) && IsSnapshot(name, workflow);

    /// <summary>The owner's user ID when the ID is an account's own workflow, otherwise null.</summary>
    public static string OwnerOf(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }
        lock (Lock)
        {
            EnsureLoaded();
            return Records.TryGetValue(id, out Record record) && record.Status == Own ? record.Owner : null;
        }
    }

    /// <summary>Whether the named registry entry is this user's own uploaded workflow (F60).</summary>
    public static bool IsOwnBy(string name, string userId) => !string.IsNullOrEmpty(userId) && IsRegisteredSnapshot(name) && OwnerOf(name) == userId;

    /// <summary>Whether the named registry entry is an EcomCraft snapshot this user may run: published for everyone, or the user's own upload.</summary>
    public static bool IsUsableBy(string name, string userId) => IsPublished(name) || IsOwnBy(name, userId);

    /// <summary>Whether a registry entry may be used by this user: anything that is not someone else's own workflow.</summary>
    public static bool NotOthersOwn(string name, string userId)
    {
        string owner = OwnerOf(name);
        return owner is null || owner == userId;
    }

    /// <summary>Copies of this user's own workflow records by ID, newest first.</summary>
    public static List<KeyValuePair<string, Record>> OwnBy(string userId)
    {
        lock (Lock)
        {
            EnsureLoaded();
            return [.. Records.Where(pair => pair.Value.Status == Own && pair.Value.Owner == userId)
                .OrderByDescending(pair => pair.Value.UploadedAt).Select(pair => new KeyValuePair<string, Record>(pair.Key, Copy(pair.Value)))];
        }
    }

    /// <summary>Adds one own workflow for its owner (F60), refusing a taken ID or more than <paramref name="maxPerOwner"/> per account.
    /// Returns null on success, "limit" when the account is full, "taken" for an ID in use, or the save failure message.</summary>
    public static string AddOwn(string id, Record record, int maxPerOwner)
    {
        lock (Lock)
        {
            EnsureLoaded();
            if (Records.ContainsKey(id) || ComfyUIBackendExtension.CustomWorkflows.ContainsKey(id))
            {
                return "taken";
            }
            if (Records.Values.Count(existing => existing.Status == Own && existing.Owner == record.Owner) >= maxPerOwner)
            {
                return "limit";
            }
            return Update(id, stored =>
            {
                stored.Status = Own;
                stored.Owner = record.Owner;
                stored.Name = record.Name;
                stored.UploadedAt = record.UploadedAt;
                stored.Snapshot = record.Snapshot;
                return null;
            });
        }
    }

    /// <summary>Deletes one own workflow of this owner and removes it from the registry. Returns null on success, "not_found" when the ID is not
    /// this owner's own workflow, or the save failure message.</summary>
    public static string DeleteOwn(string id, string userId)
    {
        lock (Lock)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(id) || !Records.TryGetValue(id, out Record existing) || existing.Status != Own || existing.Owner != userId)
            {
                return "not_found";
            }
            Records.Remove(id);
            if (!Save())
            {
                Records[id] = existing;
                return SaveFailedMessage;
            }
            if (Registered.Remove(id, out ComfyUIBackendExtension.ComfyCustomWorkflow old))
            {
                ComfyUIBackendExtension.CustomWorkflows.TryRemove(new KeyValuePair<string, ComfyUIBackendExtension.ComfyCustomWorkflow>(id, old));
            }
            return null;
        }
    }

    /// <summary>A copy of the record for this ID, or null. Copies keep callers from changing shared state outside the lock.</summary>
    public static Record Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }
        lock (Lock)
        {
            EnsureLoaded();
            return Records.TryGetValue(id, out Record record) ? Copy(record) : null;
        }
    }

    /// <summary>Copies of all records, including those whose source is gone.</summary>
    public static Dictionary<string, Record> All()
    {
        lock (Lock)
        {
            EnsureLoaded();
            return Records.ToDictionary(pair => pair.Key, pair => Copy(pair.Value), StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Deep copy through JSON; records are small.</summary>
    private static Record Copy(Record record) => JObject.FromObject(record).ToObject<Record>();

    /// <summary>The state shown for a discovery and its record. Either may be null.</summary>
    public static string State(WorkflowDiscovery.Entry entry, Record record)
    {
        Snapshot snapshot = record?.Snapshot;
        if (snapshot is not null)
        {
            bool unchanged = entry is null || entry.Problem is not null || entry.Fingerprint == snapshot.Fingerprint
                || entry.Fingerprint == record.RejectedFingerprint;
            return unchanged ? Published : Rereview;
        }
        if (entry is null || entry.Problem is not null)
        {
            return Invalid;
        }
        if (record?.Status == Unpublished)
        {
            return Unpublished;
        }
        if (record?.Status == Rejected && record.RejectedFingerprint == entry.Fingerprint)
        {
            return Rejected;
        }
        return Pending;
    }

    /// <summary>Applies one change to the record for this ID under the lock, saves it and updates the registry.
    /// The change returns an error message to abort without saving. A failed save restores the previous record.</summary>
    public static string Update(string id, Func<Record, string> change)
    {
        lock (Lock)
        {
            EnsureLoaded();
            Records.TryGetValue(id, out Record existing);
            Record record = existing is null ? new Record() : Copy(existing);
            string error = change(record);
            if (error is not null)
            {
                return error;
            }
            Records[id] = record;
            if (!Save())
            {
                if (existing is null)
                {
                    Records.Remove(id);
                }
                else
                {
                    Records[id] = existing;
                }
                return SaveFailedMessage;
            }
            Registered.TryGetValue(id, out ComfyUIBackendExtension.ComfyCustomWorkflow old);
            if (record.Snapshot is null)
            {
                Registered.Remove(id);
                if (old is not null)
                {
                    ComfyUIBackendExtension.CustomWorkflows.TryRemove(new KeyValuePair<string, ComfyUIBackendExtension.ComfyCustomWorkflow>(id, old));
                }
            }
            else
            {
                ComfyUIBackendExtension.ComfyCustomWorkflow built = Build(id, record.Snapshot);
                Registered[id] = built;
                if (old is null || !ComfyUIBackendExtension.CustomWorkflows.TryUpdate(id, built, old))
                {
                    ComfyUIBackendExtension.CustomWorkflows.TryAdd(id, built);
                }
            }
            return null;
        }
    }

    /// <summary>The registry object users run for a snapshot.</summary>
    private static ComfyUIBackendExtension.ComfyCustomWorkflow Build(string id, Snapshot snapshot)
    {
        Apply(StudioApi.ParseWorkflowObject(snapshot.BaseParams) ?? new JObject(), StudioApi.ParseWorkflowObject(snapshot.BaseValues) ?? new JObject(),
            StudioApi.ParseWorkflowObject(snapshot.Config), out JObject metadata, out JObject values);
        return new(id, snapshot.Canvas, snapshot.Prompt, metadata.ToString(Formatting.None), values.ToString(Formatting.None), null, snapshot.Description, true);
    }

    /// <summary>Whether a parameter feeds a page field that must stay visible.</summary>
    public static bool IsLocked(JObject definition) => LockedForms.Contains(definition?["form"]?.ToString() ?? "");

    /// <summary>Applies a reviewer configuration to copies of the discovered bindings: visibility, name, numeric range, dropdown subset and default.
    /// Settings that do not fit the binding are ignored here; reviewers get explicit errors from <see cref="WorkflowReviewApi"/> before saving.</summary>
    public static void Apply(JObject baseParams, JObject baseValues, JObject config, out JObject metadata, out JObject values)
    {
        metadata = (JObject)baseParams.DeepClone();
        values = (JObject)baseValues.DeepClone();
        if (config is null)
        {
            return;
        }
        foreach (JProperty property in metadata.Properties())
        {
            if (property.Value is not JObject definition || config[property.Name] is not JObject setting)
            {
                continue;
            }
            string type = definition["type"]?.ToString();
            if (setting["visible"]?.Type == JTokenType.Boolean && !IsLocked(definition))
            {
                definition["visible"] = setting.Value<bool>("visible");
            }
            string name = setting["name"]?.Type == JTokenType.String ? setting.Value<string>("name").Trim() : "";
            if (name.Length is > 0 and <= MaxNameLength)
            {
                definition["name"] = name;
            }
            if (type is "integer" or "decimal" && TryRange(definition, setting, out double min, out double max))
            {
                definition["min"] = type == "integer" ? new JValue((long)min) : new JValue(min);
                definition["max"] = type == "integer" ? new JValue((long)max) : new JValue(max);
            }
            if (type == "dropdown" && setting["values"] is JArray chosen && definition["values"] is JArray all)
            {
                HashSet<string> wanted = new(chosen.Select(value => value.ToString()), StringComparer.Ordinal);
                JArray subset = new(all.Where(value => wanted.Contains(value.ToString())).Select(value => value.DeepClone()));
                if (subset.Count > 0)
                {
                    definition["values"] = subset;
                }
            }
            if (setting.TryGetValue("default", out JToken value) && value is JValue && value.Type != JTokenType.Null)
            {
                values[property.Name] = Normalize(type, (JValue)value);
            }
        }
    }

    /// <summary>Reads a narrowed range: finite, ordered, inside the binding's own range when it has one, and integral for integers.</summary>
    public static bool TryRange(JObject definition, JObject setting, out double min, out double max)
    {
        min = 0;
        max = 0;
        if (!TryNumber(setting["min"], out min) || !TryNumber(setting["max"], out max) || min > max || (min == 0 && max == 0))
        {
            return false;
        }
        if (definition["type"]?.ToString() == "integer" && (min != Math.Floor(min) || max != Math.Floor(max)))
        {
            return false;
        }
        TryNumber(definition["min"], out double baseMin);
        TryNumber(definition["max"], out double baseMax);
        bool bounded = baseMin != 0 || baseMax != 0;
        return !bounded || (min >= baseMin && max <= baseMax);
    }

    /// <summary>Parses a finite number from a JSON number or numeric string.</summary>
    private static bool TryNumber(JToken token, out double value)
    {
        value = 0;
        return token is JValue && token.Type != JTokenType.Null
            && double.TryParse(((JValue)token).ToString(CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    /// <summary>Converts form text to the binding's JSON type; values that do not convert are kept for the validator to reject.</summary>
    private static JToken Normalize(string type, JValue value)
    {
        string raw = value.ToString(CultureInfo.InvariantCulture);
        if (type == "integer" && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer))
        {
            return integer;
        }
        if (type == "decimal" && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number))
        {
            return number;
        }
        if (type == "boolean" && bool.TryParse(raw, out bool flag))
        {
            return flag;
        }
        if (type is "text" or "dropdown")
        {
            return raw;
        }
        return value.DeepClone();
    }
}
