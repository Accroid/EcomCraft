using System.IO;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio.Prompting;

/// <summary>Serves the advertising-claim word list (F79, T99): the built-in Templates/claim-words.json, or an administrator's whole-file override
/// Data/EcomCraft/claim-words.json (the same mechanism as platforms.json). The list is cached per file stamp; a damaged file falls back the same
/// way <see cref="ClaimRules.Load"/> describes. The check only notices wording — it never blocks saving or generating.</summary>
[API.APIClass("EcomCraft advertising-claim wording check: notices questioned wording with a suggestion, never blocks.")]
public static class ClaimWords
{
    /// <summary>Folder holding the built-in claim-words.json, set at startup.</summary>
    private static string TemplatesDir;

    /// <summary>Protects the cache.</summary>
    private static readonly object Lock = new();

    /// <summary>The cached word list.</summary>
    private static List<ClaimRules.ClaimWord> Cached;

    /// <summary>The built-in file the cache was built from.</summary>
    private static (string Path, DateTime Stamp) BuiltinStamp;

    /// <summary>The override file the cache was built from.</summary>
    private static (string Path, DateTime Stamp) CustomStamp;

    /// <summary>Sets the extension folder that holds Templates/claim-words.json.</summary>
    public static void Init(string extensionFolder)
    {
        TemplatesDir = Path.Combine(extensionFolder, "Templates");
    }

    /// <summary>Reads one file, or null when it does not exist or cannot be read; the reason names the file, never the contents.</summary>
    private static string ReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logs.Error($"EcomCraft could not read the claim-word file {Path.GetFileName(path)}: {ex.GetType().Name}");
            return null;
        }
    }

    /// <summary>The effective word list (F79, T99): the administrator's override when one parses, otherwise the built-in file. The list is cached
    /// until either file changes, so typing checks do not re-read disk on every request.</summary>
    public static List<ClaimRules.ClaimWord> Words()
    {
        string custom = Path.Combine(Program.DataDir, "EcomCraft", "claim-words.json");
        string builtin = Path.Combine(TemplatesDir ?? "", "claim-words.json");
        (string, DateTime) customStamp = FileStamp(custom);
        (string, DateTime) builtinStamp = FileStamp(builtin);
        lock (Lock)
        {
            if (Cached is not null && BuiltinStamp == builtinStamp && CustomStamp == customStamp)
            {
                return Cached;
            }
        }
        (List<ClaimRules.ClaimWord> words, string source) = ClaimRules.Load(ReadFile(builtin), ReadFile(custom));
        if (source == "none")
        {
            Logs.Error("EcomCraft could not read any claim-word file; the wording check finds nothing until the file is fixed.");
        }
        lock (Lock)
        {
            Cached = words;
            BuiltinStamp = builtinStamp;
            CustomStamp = customStamp;
            return words;
        }
    }

    /// <summary>A file's last write time for cache stamping; (null, minimal) when it does not exist or cannot be reached.</summary>
    private static (string, DateTime) FileStamp(string path)
    {
        try
        {
            return File.Exists(path) ? (path, File.GetLastWriteTimeUtc(path)) : (null, DateTime.MinValue);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, DateTime.MinValue);
        }
    }

    /// <summary>The hits of one text as the client shows them: the word, where it starts, its category, its basis, whether the basis cites the
    /// provision and the suggested rewrite. Never includes anything about the file or the server.</summary>
    internal static JArray HitsJson(List<ClaimRules.ClaimHit> hits) => new(hits.Select(hit => new JObject()
    {
        ["word"] = hit.Word,
        ["start"] = hit.Start,
        ["category"] = hit.Category,
        ["basis"] = hit.Basis,
        ["verified"] = hit.Verified,
        ["suggestion"] = hit.Suggestion
    }));

    [API.APIDescription("Check a text for advertising wording worth a second look (F79, T99), such as superlatives questioned by advertising law. Returns every hit — the word, its position, its category, its basis (with whether the basis cites the provision) and a suggested rewrite. The check only notices; it never blocks saving or generating. The word list ships in Templates/claim-words.json and an administrator can replace it as a whole with Data/EcomCraft/claim-words.json.", "hits: [{ word: \"最佳\", start: 6, category: \"绝对化用语\", basis: \"…\", verified: true, suggestion: \"…\" }]")]
    public static Task<JObject> EcomCraftStudioClaimCheck(Session session, string text)
    {
        if (!StudioApi.CanView(session))
        {
            return Task.FromResult(StudioApi.Error("无权访问创作工作台。", "bad_permissions"));
        }
        text ??= "";
        if (text.Length > 2000)
        {
            return Task.FromResult(StudioApi.Error("一次最多检查 2000 个字符。", "invalid_parameters"));
        }
        return Task.FromResult(new JObject() { ["hits"] = HitsJson(ClaimRules.Check(text, Words())) });
    }
}
