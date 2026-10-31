using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EcomCraftStudio.Studio.Common;

/// <summary>Result of reading a data file.</summary>
public enum JsonFileState
{
    /// <summary>The file does not exist.</summary>
    Missing,

    /// <summary>The file was read and parsed.</summary>
    Ok,

    /// <summary>The file exists but could not be read or parsed; it must not be overwritten.</summary>
    Unreadable
}

/// <summary>Reads and writes the JSON data files under Data/EcomCraft. Nothing here logs; callers write their own messages from the returned error.</summary>
public static class JsonFile
{
    /// <summary>Reads a file holding one JSON object.</summary>
    public static JsonFileState TryReadObject(string path, out JObject value, out string error)
    {
        value = null;
        error = null;
        try
        {
            if (!File.Exists(path))
            {
                return JsonFileState.Missing;
            }
            value = JObject.Parse(File.ReadAllText(path));
            return JsonFileState.Ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = ex.Message;
            return JsonFileState.Unreadable;
        }
    }

    /// <summary>Reads a file and converts its object. A conversion that fails on bad data counts as unreadable, so the file is protected the same way.</summary>
    public static JsonFileState TryRead<T>(string path, Func<JObject, T> convert, out T value, out string error)
    {
        value = default;
        JsonFileState state = TryReadObject(path, out JObject root, out error);
        if (state != JsonFileState.Ok)
        {
            return state;
        }
        try
        {
            value = convert(root);
            return JsonFileState.Ok;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return JsonFileState.Unreadable;
        }
    }

    /// <summary>Writes through a temporary file next to the target and then replaces it, so a failure leaves the original unchanged.</summary>
    public static bool TryWriteAtomic(string path, string content, out string error)
    {
        error = null;
        string temp = $"{path}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(temp, content);
            File.Move(temp, path, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            TryDelete(temp);
            return false;
        }
    }

    /// <summary>Removes a leftover temporary file, ignoring failure.</summary>
    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Backups skip *.tmp, and the next write replaces it.
        }
    }
}
