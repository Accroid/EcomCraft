using System.IO;
using System.Text.RegularExpressions;
using EcomCraftStudio.Studio.PostProcess;
using EcomCraftStudio.Studio.Storage;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio.Products;

/// <summary>Simple product library (F36): uploaded product photos are kept with their cut-out and confirmed placement, so the next generation can pick a product
/// instead of uploading and cutting it out again. Files go to the work library location when one is set (R11), otherwise to Data/EcomCraft/product-files on the
/// platform server, never into the gallery folder; the index stays in Data/EcomCraft/products.json. Accounts only see and change their own products,
/// and clients refer to products by ID only.</summary>
[API.APIClass("EcomCraft product library: save product photos with their cut-out and placement, list, rename and delete them.")]
public static class ProductLibrary
{
    /// <summary>Largest photo accepted, as a data URL.</summary>
    private const int MaxDataLength = (5 * 1024 * 1024 + 2) / 3 * 4 + 40;

    /// <summary>The loaded index.</summary>
    private static ProductIndex Index;

    /// <summary>Protects the index and its file.</summary>
    private static readonly object Lock = new();

    /// <summary>Index file.</summary>
    private static string IndexFile => Path.Combine(Program.DataDir, "EcomCraft", "products.json");

    /// <summary>Loads the index once. Callers hold <see cref="Lock"/>. An unreadable file starts empty and is not overwritten until a change is made.</summary>
    private static ProductIndex Loaded()
    {
        if (Index is not null)
        {
            return Index;
        }
        Index = new ProductIndex();
        try
        {
            if (File.Exists(IndexFile))
            {
                Index.Items = JObject.Parse(File.ReadAllText(IndexFile))["products"]?.ToObject<List<ProductRecord>>()?.Where(item => item is not null).ToList() ?? [];
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Logs.Error($"EcomCraft could not read the product index: {ex.Message}");
        }
        return Index;
    }

    /// <summary>Writes the index through a temporary file. Callers hold <see cref="Lock"/>.</summary>
    private static bool Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(IndexFile));
            string temp = $"{IndexFile}.tmp";
            File.WriteAllText(temp, new JObject() { ["version"] = 1, ["products"] = JArray.FromObject(Index.Items) }.ToString(Formatting.Indented));
            File.Move(temp, IndexFile, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logs.Error($"EcomCraft could not save the product index: {ex.Message}");
            return false;
        }
    }

    /// <summary>Folder for a new product: the work library when set, otherwise the platform server's data folder.</summary>
    private static string NewFolder(string userId, string id)
    {
        string library = StorageSettings.Get().LibraryPath;
        string root = string.IsNullOrEmpty(library) ? Path.Combine(Program.DataDir, "EcomCraft", "product-files") : Path.Combine(library, "products");
        return Path.GetFullPath(Path.Combine(root, userId, id));
    }

    /// <summary>Decodes a PNG, JPEG or WebP data URL, returning the bytes and a file extension, or null.</summary>
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

    /// <summary>A product file as a data URL, or null when it cannot be read (for example while the work library is offline).</summary>
    private static string ReadDataUrl(ProductRecord record, string file)
    {
        if (file is null)
        {
            return null;
        }
        try
        {
            string extension = Path.GetExtension(file).TrimStart('.');
            string mime = extension == "jpg" ? "image/jpeg" : $"image/{extension}";
            byte[] bytes = File.ReadAllBytes(Path.Combine(record.Folder, file));
            // Products saved before uploads were shrunk in the browser can pass 40 megapixels, which generation refuses; they are scaled down here
            // (JPEG stays JPEG, anything else becomes PNG). Absurd sizes are passed on unchanged for generation to refuse with its own message.
            byte[] fitted = PostProcess.ProductComposite.FitForWork(bytes);
            if (fitted is not null && fitted != bytes)
            {
                bytes = fitted;
                mime = extension == "jpg" ? "image/jpeg" : "image/png";
            }
            return $"data:{mime};base64," + Convert.ToBase64String(bytes);
        }
        catch (Exception ex) when (ex is SixLabors.ImageSharp.UnknownImageFormatException or SixLabors.ImageSharp.InvalidImageContentException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>What generation needs from a product: the original or cut-out as a data URL and the confirmed placement. Null with a reason when it cannot be used.</summary>
    public static (string Image, Placement Placement, string Error) ForGeneration(string userId, string productId, bool cutout)
    {
        ProductRecord record;
        lock (Lock)
        {
            record = Loaded().Find(userId, productId);
        }
        if (record is null)
        {
            return (null, null, "商品不存在，请刷新商品库。");
        }
        if (cutout && record.Cutout is null)
        {
            return (null, null, "这个商品还没有确认过抠图，请先在出图页完成抠图。");
        }
        string image = ReadDataUrl(record, cutout ? record.Cutout : record.Original);
        if (image is null)
        {
            return (null, null, "商品图暂时无法读取：作品库位置可能离线，请稍后重试。");
        }
        Placement.TryCreate(record.X, record.Y, record.Size, out Placement placement);
        return (image, placement ?? Placement.Default, null);
    }

    /// <summary>Whether the account has a saved product with this ID (files are not read).</summary>
    public static bool Exists(string userId, string productId)
    {
        lock (Lock)
        {
            return Loaded().Find(userId, productId) is not null;
        }
    }

    /// <summary>The account's product file for the image route ("original" or "cutout"), or null.</summary>
    public static (byte[] Bytes, string Extension) FileFor(string userId, string productId, string kind)
    {
        ProductRecord record;
        lock (Lock)
        {
            record = Loaded().Find(userId, productId);
        }
        string file = record is null ? null : kind == "cutout" ? record.Cutout : record.Original;
        if (file is null)
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

    /// <summary>A product as its owner sees it: no file locations.</summary>
    private static JObject Json(ProductRecord record) => new()
    {
        ["id"] = record.Id,
        ["name"] = record.Name,
        ["created"] = record.Created,
        ["has_cutout"] = record.Cutout is not null,
        ["placement"] = new JObject() { ["x"] = record.X, ["y"] = record.Y, ["size"] = record.Size },
        ["image"] = $"/EcomCraft/product-image/{record.Id}/original",
        ["cutout_image"] = record.Cutout is null ? null : $"/EcomCraft/product-image/{record.Id}/cutout"
    };

    [API.APIDescription("List the current account's saved products (newest first), without file locations.", "products: [{ id: \"…\", name: \"\", has_cutout: true }]")]
    public static Task<JObject> EcomCraftStudioProducts(Session session)
    {
        if (!StudioApi.CanView(session))
        {
            return Task.FromResult(StudioApi.Error("无权访问创作工作台。", "bad_permissions"));
        }
        lock (Lock)
        {
            return Task.FromResult(new JObject() { ["products"] = new JArray(Loaded().ForUser(session.User.UserID).Select(Json)) });
        }
    }

    [API.APIDescription("Save a product photo (PNG, JPEG or WebP data URL, at most 5 MB) to the current account's product library, optionally with its cut-out and placement. Returns the product.", "product: { id: \"…\" }")]
    public static Task<JObject> EcomCraftStudioProductSave(Session session, string image, string name = "", string cutout = "")
    {
        if (!StudioApi.CanView(session) || !session.User.HasPermission(Permissions.BasicImageGeneration))
        {
            return Task.FromResult(StudioApi.Error("此账号没有生成权限。", "bad_permissions"));
        }
        (byte[] original, string extension) = Decode(image);
        (byte[] cut, _) = string.IsNullOrEmpty(cutout) ? (null, null) : Decode(cutout);
        if (original is null || (!string.IsNullOrEmpty(cutout) && cut is null))
        {
            return Task.FromResult(StudioApi.Error("商品图须为不超过 5 MB 的 PNG、JPEG 或 WebP 图片。", "invalid_reference"));
        }
        string id = Guid.NewGuid().ToString("N")[..16];
        ProductRecord record = new()
        {
            Id = id,
            UserId = session.User.UserID,
            Name = ProductIndex.CleanName(name) ?? $"商品 {DateTime.Now:MM-dd HH:mm}",
            Created = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Folder = NewFolder(session.User.UserID, id),
            Original = $"original.{extension}",
            Cutout = cut is null ? null : "cutout.png"
        };
        try
        {
            Directory.CreateDirectory(record.Folder);
            File.WriteAllBytes(Path.Combine(record.Folder, record.Original), original);
            if (cut is not null)
            {
                File.WriteAllBytes(Path.Combine(record.Folder, record.Cutout), cut);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logs.Warning($"EcomCraft could not save product files: {ex.Message}");
            return Task.FromResult(StudioApi.Error("商品图无法保存：作品库位置可能离线或空间不足，请稍后重试。", "product_save_failed"));
        }
        lock (Lock)
        {
            string error = Loaded().Add(record);
            if (error is null && !Save())
            {
                Index.Items.Remove(record);
                error = "商品库索引无法保存到服务器数据目录。";
            }
            if (error is not null)
            {
                TryDeleteFolder(record.Folder);
                return Task.FromResult(StudioApi.Error(error, "product_save_failed"));
            }
            return Task.FromResult(new JObject() { ["product"] = Json(record) });
        }
    }

    [API.APIDescription("Update one of the current account's products: name, confirmed cut-out (data URL) and placement (x, y as 0–1, size 0.1–1.5). Only given fields change. rawInput is the root request JSON.", "product: { id: \"…\" }")]
    public static Task<JObject> EcomCraftStudioProductUpdate(Session session, JObject rawInput)
    {
        if (!StudioApi.CanView(session))
        {
            return Task.FromResult(StudioApi.Error("无权访问创作工作台。", "bad_permissions"));
        }
        string id = rawInput["product_id"]?.ToString();
        lock (Lock)
        {
            ProductIndex index = Loaded();
            ProductRecord record = index.Find(session.User.UserID, id);
            if (record is null)
            {
                return Task.FromResult(StudioApi.Error("商品不存在，请刷新商品库。", "task_not_found"));
            }
            if (rawInput["name"]?.Type == JTokenType.String)
            {
                string error = index.Rename(session.User.UserID, id, rawInput.Value<string>("name"));
                if (error is not null)
                {
                    return Task.FromResult(StudioApi.Error(error, "invalid_parameters"));
                }
            }
            if (rawInput["placement"] is JObject placement)
            {
                if (!Placement.TryCreate(placement.Value<double?>("x"), placement.Value<double?>("y"), placement.Value<double?>("size"), out Placement parsed))
                {
                    return Task.FromResult(StudioApi.Error("商品位置无效：中心须在画面内，大小在 0.1–1.5 之间。", "invalid_parameters"));
                }
                (record.X, record.Y, record.Size) = (parsed.CenterX, parsed.CenterY, parsed.Size);
            }
            if (rawInput["cutout"]?.Type == JTokenType.String)
            {
                (byte[] cut, _) = Decode(rawInput.Value<string>("cutout"));
                if (cut is null)
                {
                    return Task.FromResult(StudioApi.Error("抠图须为不超过 5 MB 的 PNG、JPEG 或 WebP 图片。", "invalid_reference"));
                }
                try
                {
                    File.WriteAllBytes(Path.Combine(record.Folder, "cutout.png"), cut);
                    record.Cutout = "cutout.png";
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Logs.Warning($"EcomCraft could not save a product cut-out: {ex.Message}");
                    return Task.FromResult(StudioApi.Error("抠图无法保存：作品库位置可能离线，请稍后重试。", "product_save_failed"));
                }
            }
            if (!Save())
            {
                return Task.FromResult(StudioApi.Error("商品库索引无法保存到服务器数据目录。", "product_save_failed"));
            }
            return Task.FromResult(new JObject() { ["product"] = Json(record) });
        }
    }

    [API.APIDescription("Delete one of the current account's products and its files.", "success: true")]
    public static Task<JObject> EcomCraftStudioProductDelete(Session session, string product_id)
    {
        if (!StudioApi.CanView(session))
        {
            return Task.FromResult(StudioApi.Error("无权访问创作工作台。", "bad_permissions"));
        }
        lock (Lock)
        {
            ProductRecord record = Loaded().Remove(session.User.UserID, product_id);
            if (record is null)
            {
                return Task.FromResult(StudioApi.Error("商品不存在，请刷新商品库。", "task_not_found"));
            }
            if (!Save())
            {
                Index.Items.Add(record);
                return Task.FromResult(StudioApi.Error("商品库索引无法保存到服务器数据目录。", "product_save_failed"));
            }
            TryDeleteFolder(record.Folder);
        }
        return Task.FromResult(new JObject() { ["success"] = true });
    }

    /// <summary>Removes a product folder; a failure (for example an offline share) only leaves files behind and is logged.</summary>
    private static void TryDeleteFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logs.Warning($"EcomCraft could not delete product files: {ex.Message}");
        }
    }
}
