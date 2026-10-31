using System.IO;
using EcomCraftStudio.Studio.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Core;
using SwarmUI.Utils;

namespace EcomCraftStudio.Studio.Storage;

/// <summary>Administrator-set work library and export locations (R11.1), saved in Data/EcomCraft/storage.json. Empty means "platform server only".</summary>
public static class StorageSettings
{
    /// <summary>One saved configuration. Paths are full, normalized and already checked when saved.</summary>
    public sealed class Values
    {
        /// <summary>Where finished works are copied after they are saved locally, or empty to keep them only on the platform server.</summary>
        public string LibraryPath = "";

        /// <summary>The library location before the last change, used by the optional migration (R11.7).</summary>
        public string PreviousLibraryPath = "";

        /// <summary>Where exports are written when users choose "export to shared folder" (R11.8), or empty when not offered.</summary>
        public string ExportPath = "";

        /// <summary>Account that last saved the settings.</summary>
        public string ChangedBy = "";

        /// <summary>How many days an unkept simple-mode candidate stays before it is deleted automatically (F39); 0 disables the cleanup.</summary>
        public int CandidateRetentionDays = 14;

        /// <summary>Free space of the works folder and the work library at or below this many GB shows an administrator warning (F46); 0 disables it.</summary>
        public int DiskWarnGB = 10;

        /// <summary>Free space at or below this many GB rejects new generations (F46); 0 disables it.</summary>
        public int DiskCriticalGB = 2;

        /// <summary>Time of the last save, Unix milliseconds.</summary>
        public long ChangedAt;
    }

    /// <summary>Protects <see cref="Current"/> and the file.</summary>
    private static readonly object Lock = new();

    /// <summary>The loaded values, or null before the first read.</summary>
    private static Values Current;

    /// <summary>Whether the settings file exists but could not be read. It is then never overwritten until the server restarts with a readable file.</summary>
    private static bool Unreadable;

    /// <summary>Location of the settings file.</summary>
    private static string FilePath => Path.Combine(Program.DataDir, "EcomCraft", "storage.json");

    /// <summary>A copy of the current settings. A missing or unreadable file means defaults; an unreadable file is logged and left untouched.</summary>
    public static Values Get()
    {
        lock (Lock)
        {
            if (Current is null)
            {
                Current = new Values();
                JsonFileState state = JsonFile.TryRead<Values>(FilePath, root => root.ToObject<Values>() ?? new Values(), out Values values, out string error);
                if (state == JsonFileState.Unreadable)
                {
                    Logs.Error($"EcomCraft could not read the storage settings; using the platform server only until fixed: {error}");
                    Unreadable = true;
                }
                else if (state == JsonFileState.Ok)
                {
                    Current = values;
                }
            }
            return JObject.FromObject(Current).ToObject<Values>();
        }
    }

    /// <summary>Saves new settings through a temporary file. Returns false when the file cannot be written, or while the original file is unreadable.</summary>
    public static bool Save(Values values)
    {
        lock (Lock)
        {
            if (Unreadable)
            {
                return false;
            }
            if (!JsonFile.TryWriteAtomic(FilePath, JObject.FromObject(values).ToString(Formatting.Indented), out string error))
            {
                Logs.Error($"EcomCraft could not save the storage settings: {error}");
                return false;
            }
            Current = JObject.FromObject(values).ToObject<Values>();
            return true;
        }
    }
}
