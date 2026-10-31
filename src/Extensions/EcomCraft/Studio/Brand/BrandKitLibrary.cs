using System.IO;
using System.Text.RegularExpressions;
using EcomCraftStudio.Studio.Common;
using EcomCraftStudio.Studio.Prompting;
using EcomCraftStudio.Studio.Storage;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio.Brand;

/// <summary>Brand kits (F12): a logo, brand colors, a preferred background and up to four style reference images, saved per account.
/// Files go to the work library location when one is set (R11), otherwise to Data/EcomCraft/brand-files; the index stays in Data/EcomCraft/brand-kits.json.
/// Clients only ever send a kit ID: the server reads the files, turns colors and background into prompt text and fills only the image parameters a template
/// declares in its brand_kit slot.</summary>
[API.APIClass("EcomCraft brand kits: logo, brand colors, preferred background and style references, per account.")]
public static class BrandKitLibrary
{
    /// <summary>Largest image accepted, as a data URL.</summary>
    private const int MaxDataLength = (5 * 1024 * 1024 + 2) / 3 * 4 + 40;

    /// <summary>The loaded index.</summary>
    private static BrandKitIndex Index;

    /// <summary>Protects the index and its file.</summary>
    private static readonly object Lock = new();

    /// <summary>Index file.</summary>
    private static string IndexFile => Path.Combine(Program.DataDir, "EcomCraft", "brand-kits.json");

    /// <summary>Whether the index file exists but could not be read. It is then never overwritten, and each use tries to read it again, so a temporary problem clears by itself.</summary>
    private static bool Unreadable;

    /// <summary>Loads the index once. Callers hold <see cref="Lock"/>. While the file is unreadable the index is empty and <see cref="Save"/> refuses.</summary>
    private static BrandKitIndex Loaded()
    {
        if (Index is not null && !Unreadable)
        {
            return Index;
        }
        Index = new BrandKitIndex();
        JsonFileState state = JsonFile.TryRead<List<BrandKitRecord>>(IndexFile, root => root["kits"]?.ToObject<List<BrandKitRecord>>()?.Where(item => item is not null).ToList() ?? [], out List<BrandKitRecord> items, out string error);
        if (state == JsonFileState.Unreadable && !Unreadable)
        {
            Logs.Error($"EcomCraft could not read the brand kit index: {error}");
        }
        Unreadable = state == JsonFileState.Unreadable;
        if (state == JsonFileState.Ok)
        {
            Index.Items = items;
        }
        return Index;
    }

    /// <summary>Writes the index through a temporary file. Callers hold <see cref="Lock"/>. Refuses while the original file is unreadable.</summary>
    private static bool Save()
    {
        if (Unreadable)
        {
            return false;
        }
        if (!JsonFile.TryWriteAtomic(IndexFile, new JObject() { ["version"] = 1, ["kits"] = JArray.FromObject(Index.Items) }.ToString(Formatting.Indented), out string error))
        {
            Logs.Error($"EcomCraft could not save the brand kit index: {error}");
            return false;
        }
        return true;
    }

    /// <summary>The answer after <see cref="Save"/> returned false.</summary>
    private static JObject SaveFailed()
    {
        if (Unreadable)
        {
            return StudioApi.Error("品牌资料包文件暂时读不了，这次没有保存，原有数据没有动。请稍后重试或联系管理员。", "data_file_unreadable");
        }
        return StudioApi.Error("品牌资料包索引无法保存到服务器数据目录。", "brand_kit_save_failed");
    }

    /// <summary>Folder for a new kit: the work library when set, otherwise the platform server's data folder.</summary>
    private static string NewFolder(string userId, string id)
    {
        string library = StorageSettings.Get().LibraryPath;
        string root = string.IsNullOrEmpty(library) ? Path.Combine(Program.DataDir, "EcomCraft", "brand-files") : Path.Combine(library, "brand-kits");
        return Path.GetFullPath(Path.Combine(root, userId, id));
    }

    /// <summary>Decodes a PNG, JPEG or WebP data URL of at most 5 MB, returning the bytes and a file extension, or null.</summary>
    private static (byte[] Bytes, string Extension) Decode(string data)
    {
        if (string.IsNullOrEmpty(data) || data.Length > MaxDataLength)
        {
            return (null, null);
        }
        Match match = Regex.Match(data[..Math.Min(data.Length, 30)], "^data:image/(png|jpeg|webp);base64,");
        if (!match.Success)
        {
            return (null, null);
        }
        try
        {
            byte[] bytes = Convert.FromBase64String(data[match.Length..]);
            return bytes.Length > 0 ? (bytes, match.Groups[1].Value == "jpeg" ? "jpg" : match.Groups[1].Value) : (null, null);
        }
        catch (FormatException)
        {
            return (null, null);
        }
    }

    /// <summary>A kit file as a data URL, or null when it cannot be read (for example while the work library is offline).</summary>
    private static string ReadDataUrl(BrandKitRecord record, string file)
    {
        if (file is null || !BrandKitIndex.FileName.IsMatch(file))
        {
            return null;
        }
        try
        {
            string extension = Path.GetExtension(file).TrimStart('.');
            string mime = extension == "jpg" ? "image/jpeg" : $"image/{extension}";
            return $"data:{mime};base64," + Convert.ToBase64String(File.ReadAllBytes(Path.Combine(record.Folder, file)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The account's kit as generation needs it (F12): its prompt fragment, and the logo and first reference image as data URLs (null when absent or unreadable).
    /// Null with a reason when the kit does not exist for this account.</summary>
    public static (string Fragment, string Logo, string Reference, string Error) ForGeneration(string userId, string kitId)
    {
        BrandKitRecord record;
        lock (Lock)
        {
            record = Loaded().Find(userId, kitId);
        }
        if (record is null)
        {
            return (null, null, null, "品牌资料包不存在，请刷新后重新选择。");
        }
        return (BrandKitIndex.PromptFragment(record), ReadDataUrl(record, record.Logo), ReadDataUrl(record, record.References.FirstOrDefault()), null);
    }

    /// <summary>The account's kit prompt fragment (F12) without reading its images, or a reason when the kit does not exist for this account.</summary>
    public static (string Fragment, string Error) FragmentFor(string userId, string kitId)
    {
        lock (Lock)
        {
            BrandKitRecord record = Loaded().Find(userId, kitId);
            return record is null ? (null, "品牌资料包不存在，请刷新后重新选择。") : (BrandKitIndex.PromptFragment(record), null);
        }
    }

    /// <summary>The account's kit file for the image route, or null. Only file names the kit lists are ever read.</summary>
    public static (byte[] Bytes, string Extension) FileFor(string userId, string kitId, string file)
    {
        BrandKitRecord record;
        lock (Lock)
        {
            record = Loaded().Find(userId, kitId);
        }
        if (record is null || file is null || !BrandKitIndex.FileName.IsMatch(file) || (record.Logo != file && !record.References.Contains(file)))
        {
            return (null, null);
        }
        try
        {
            return (File.ReadAllBytes(Path.Combine(record.Folder, file)), Path.GetExtension(file).TrimStart('.'));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, null);
        }
    }

    /// <summary>A kit as its owner sees it: no file locations, only image addresses its owner can open.</summary>
    private static JObject Json(BrandKitRecord record) => new()
    {
        ["id"] = record.Id,
        ["name"] = record.Name,
        ["created"] = record.Created,
        ["colors"] = new JArray(record.Colors),
        ["background"] = record.Background,
        ["background_ready"] = string.IsNullOrEmpty(record.Background) || !string.IsNullOrEmpty(record.BackgroundPrompt),
        ["logo"] = record.Logo is null ? null : $"/EcomCraft/brand-image/{record.Id}/{record.Logo}",
        ["references"] = new JArray(record.References.Select(file => new JObject() { ["id"] = file, ["image"] = $"/EcomCraft/brand-image/{record.Id}/{file}" }))
    };

    [API.APIDescription("List the current account's brand kits (newest first), without file locations.", "brand_kits: [{ id: \"…\", name: \"\", colors: [\"#112233\"] }]")]
    public static Task<JObject> EcomCraftStudioBrandKits(Session session)
    {
        if (!StudioApi.CanView(session))
        {
            return Task.FromResult(StudioApi.Error("无权访问创作工作台。", "bad_permissions"));
        }
        lock (Lock)
        {
            return Task.FromResult(new JObject() { ["brand_kits"] = new JArray(Loaded().ForUser(session.User.UserID).Select(Json)) });
        }
    }

    [API.APIDescription("Create or update one of the current account's brand kits. Fields: brand_kit_id (omit to create), name, colors (up to 5 \"#RRGGBB\"), background (up to 200 characters; Chinese is translated once when saved), logo (data URL to replace it), remove_logo, add_references (data URLs) and remove_references (reference IDs); at most 4 references, each image at most 5 MB. rawInput is the root request JSON.", "brand_kit: { id: \"…\" }, notice_code: null")]
    public static async Task<JObject> EcomCraftStudioBrandKitSave(Session session, JObject rawInput)
    {
        if (!StudioApi.CanView(session) || !session.User.HasPermission(Permissions.BasicImageGeneration))
        {
            return StudioApi.Error("此账号没有生成权限。", "bad_permissions");
        }
        string kitId = rawInput["brand_kit_id"]?.Type == JTokenType.String ? rawInput.Value<string>("brand_kit_id") : null;
        string name = BrandKitIndex.CleanName(rawInput["name"]?.Type == JTokenType.String ? rawInput.Value<string>("name") : null);
        if (name is null)
        {
            return StudioApi.Error("请填写品牌资料包名称。", "invalid_parameters");
        }
        List<string> colors = BrandKitIndex.CleanColors(rawInput["colors"] is JArray colorItems ? colorItems.Select(item => item?.Type == JTokenType.String ? item.ToString() : "") : Enumerable.Empty<string>());
        if (colors is null)
        {
            return StudioApi.Error($"品牌色须为 #RRGGBB 格式，最多 {BrandKitIndex.MaxColors} 个。", "invalid_parameters");
        }
        string background = rawInput["background"]?.Type == JTokenType.String ? Regex.Replace(rawInput.Value<string>("background"), @"\s+", " ").Trim() : "";
        if (background.Length > BrandKitIndex.MaxBackgroundLength)
        {
            return StudioApi.Error($"偏好背景描述最多 {BrandKitIndex.MaxBackgroundLength} 个字符。", "invalid_prompt");
        }
        (byte[] Bytes, string Extension) logo = (null, null);
        if (rawInput["logo"]?.Type == JTokenType.String && rawInput.Value<string>("logo").Length > 0)
        {
            logo = Decode(rawInput.Value<string>("logo"));
            if (logo.Bytes is null)
            {
                return StudioApi.Error("Logo 须为不超过 5 MB 的 PNG、JPEG 或 WebP 图片。", "invalid_reference");
            }
        }
        List<(byte[] Bytes, string Extension)> added = [];
        foreach (JToken item in rawInput["add_references"] as JArray ?? new JArray())
        {
            (byte[] Bytes, string Extension) reference = item.Type == JTokenType.String ? Decode(item.ToString()) : (null, null);
            if (reference.Bytes is null)
            {
                return StudioApi.Error("参考图须为不超过 5 MB 的 PNG、JPEG 或 WebP 图片。", "invalid_reference");
            }
            added.Add(reference);
        }
        HashSet<string> removed = [.. (rawInput["remove_references"] as JArray ?? new JArray()).Select(item => item.ToString())];
        bool removeLogo = rawInput["remove_logo"]?.Type == JTokenType.Boolean && rawInput.Value<bool>("remove_logo");
        BrandKitRecord existing;
        lock (Lock)
        {
            existing = kitId is null ? null : Loaded().Find(session.User.UserID, kitId);
        }
        if (kitId is not null && existing is null)
        {
            return StudioApi.Error("品牌资料包不存在，请刷新后重试。", "task_not_found");
        }
        if ((existing?.References.Count(file => !removed.Contains(file)) ?? 0) + added.Count > BrandKitIndex.MaxReferences)
        {
            return StudioApi.Error($"参考图最多 {BrandKitIndex.MaxReferences} 张。", "invalid_parameters");
        }
        // The background becomes English prompt text once, when it changes; a failed translation keeps the text but leaves it out of generation.
        string backgroundPrompt = existing is not null && existing.Background == background ? existing.BackgroundPrompt
            : background.Length == 0 ? "" : PromptRules.IsAscii(background) ? PromptRules.CleanUserPrompt(background)
            : PromptRules.CleanUserPrompt(await PromptService.TranslateToEnglishAsync(session, background));
        string id = existing?.Id ?? Guid.NewGuid().ToString("N")[..16];
        string folder = existing?.Folder ?? NewFolder(session.User.UserID, id);
        string logoFile = removeLogo ? null : existing?.Logo;
        List<string> references = [.. existing?.References.Where(file => !removed.Contains(file)) ?? Enumerable.Empty<string>()];
        List<string> obsolete = [.. existing?.References.Where(removed.Contains) ?? Enumerable.Empty<string>()];
        try
        {
            Directory.CreateDirectory(folder);
            if (logo.Bytes is not null)
            {
                if (logoFile is not null && logoFile != $"logo.{logo.Extension}")
                {
                    obsolete.Add(logoFile);
                }
                logoFile = $"logo.{logo.Extension}";
                File.WriteAllBytes(Path.Combine(folder, logoFile), logo.Bytes);
            }
            else if (removeLogo && existing?.Logo is not null)
            {
                obsolete.Add(existing.Logo);
            }
            foreach ((byte[] bytes, string extension) in added)
            {
                string file = $"ref-{Guid.NewGuid().ToString("N")[..8]}.{extension}";
                File.WriteAllBytes(Path.Combine(folder, file), bytes);
                references.Add(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logs.Warning($"EcomCraft could not save brand kit files: {ex.Message}");
            return StudioApi.Error("品牌资料包的图片无法保存：作品库位置可能离线或空间不足，请稍后重试。", "brand_kit_save_failed");
        }
        JObject answer;
        lock (Lock)
        {
            BrandKitIndex index = Loaded();
            BrandKitRecord record = existing is null ? null : index.Find(session.User.UserID, id);
            if (existing is not null && record is null)
            {
                return StudioApi.Error("品牌资料包已被删除，请刷新后重试。", "task_not_found");
            }
            bool created = record is null;
            record ??= new BrandKitRecord() { Id = id, UserId = session.User.UserID, Created = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Folder = folder };
            BrandKitRecord before = created ? null : new BrandKitRecord() { Name = record.Name, Colors = record.Colors, Background = record.Background, BackgroundPrompt = record.BackgroundPrompt, Logo = record.Logo, References = record.References };
            record.Name = name;
            record.Colors = colors;
            record.Background = background;
            record.BackgroundPrompt = backgroundPrompt;
            record.Logo = logoFile;
            record.References = references;
            string error = created ? index.Add(record) : null;
            if (error is null && !Save())
            {
                if (created)
                {
                    index.Items.Remove(record);
                    TryDelete(folder, null);
                }
                else
                {
                    (record.Name, record.Colors, record.Background, record.BackgroundPrompt, record.Logo, record.References) = (before.Name, before.Colors, before.Background, before.BackgroundPrompt, before.Logo, before.References);
                }
                return SaveFailed();
            }
            if (error is not null)
            {
                if (created)
                {
                    TryDelete(folder, null);
                }
                return StudioApi.Error(error, "brand_kit_save_failed");
            }
            answer = new JObject()
            {
                ["brand_kit"] = Json(record),
                ["notice_code"] = background.Length > 0 && backgroundPrompt.Length == 0 ? "background_untranslated" : null
            };
        }
        foreach (string file in obsolete.Distinct())
        {
            TryDelete(folder, file);
        }
        return answer;
    }

    [API.APIDescription("Delete one of the current account's brand kits and its files.", "success: true")]
    public static Task<JObject> EcomCraftStudioBrandKitDelete(Session session, string brand_kit_id)
    {
        if (!StudioApi.CanView(session))
        {
            return Task.FromResult(StudioApi.Error("无权访问创作工作台。", "bad_permissions"));
        }
        lock (Lock)
        {
            BrandKitRecord record = Loaded().Remove(session.User.UserID, brand_kit_id);
            if (record is null)
            {
                return Task.FromResult(StudioApi.Error("品牌资料包不存在，请刷新后重试。", "task_not_found"));
            }
            if (!Save())
            {
                Index.Items.Add(record);
                return Task.FromResult(SaveFailed());
            }
            TryDelete(record.Folder, null);
        }
        return Task.FromResult(new JObject() { ["success"] = true });
    }

    /// <summary>Removes one kit file (when <paramref name="file"/> is a known file name) or the whole kit folder; a failure only leaves files behind and is logged.</summary>
    private static void TryDelete(string folder, string file)
    {
        try
        {
            if (file is null)
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            }
            else if (BrandKitIndex.FileName.IsMatch(file))
            {
                File.Delete(Path.Combine(folder, file));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logs.Warning($"EcomCraft could not delete brand kit files: {ex.Message}");
        }
    }
}
