using System.IO;
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

        /// <summary>Time of the last save, Unix milliseconds.</summary>
        public long ChangedAt;
    }

    /// <summary>Protects <see cref="Current"/> and the file.</summary>
    private static readonly object Lock = new();

    /// <summary>The loaded values, or null before the first read.</summary>
    private static Values Current;

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
                try
                {
                    if (File.Exists(FilePath))
                    {
                        Current = JObject.Parse(File.ReadAllText(FilePath)).ToObject<Values>() ?? new Values();
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    Logs.Error($"EcomCraft could not read the storage settings; using the platform server only until fixed: {ex.Message}");
                }
            }
            return JObject.FromObject(Current).ToObject<Values>();
        }
    }

    /// <summary>Saves new settings through a temporary file. Returns false when the file cannot be written.</summary>
    public static bool Save(Values values)
    {
        lock (Lock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                string temp = $"{FilePath}.tmp";
                File.WriteAllText(temp, JObject.FromObject(values).ToString(Formatting.Indented));
                File.Move(temp, FilePath, true);
                Current = JObject.FromObject(values).ToObject<Values>();
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logs.Error($"EcomCraft could not save the storage settings: {ex.Message}");
                return false;
            }
        }
    }
}
