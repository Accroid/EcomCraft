using System.IO;
using System.Text.RegularExpressions;
using EcomCraftStudio.Studio.Common;
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

    /// <summary>Whether the index file exists but could not be read. It is then never overwritten, and each use tries to read it again, so a temporary problem clears by itself.</summary>
    private static bool Unreadable;

    /// <summary>Loads the index once. Callers hold <see cref="Lock"/>. While the file is unreadable the index is empty and <see cref="Save"/> refuses.</summary>
    private static ProductIndex Loaded()
    {
        if (Index is not null && !Unreadable)
        {
            return Index;
        }
        Index = new ProductIndex();
        JsonFileState state = JsonFile.TryRead<List<ProductRecord>>(IndexFile, root => root["products"]?.ToObject<List<ProductRecord>>()?.Where(item => item is not null).ToList() ?? [], out List<ProductRecord> items, out string error);
        if (state == JsonFileState.Unreadable && !Unreadable)
        {
            Logs.Error($"EcomCraft could not read the product index: {error}");
        }
        Unreadable = state == JsonFileState.Unreadable;
        if (state == JsonFileState.Ok)
        {
            Index.Items = items;
            // Records saved before angles existed (F84) become one main angle here, in memory; the next successful save writes the angle list.
            Index.Migrate();
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
        if (!JsonFile.TryWriteAtomic(IndexFile, new JObject() { ["version"] = 1, ["products"] = JArray.FromObject(Index.Items) }.ToString(Formatting.Indented), out string error))
        {
            Logs.Error($"EcomCraft could not save the product index: {error}");
            return false;
        }
        return true;
    }

    /// <summary>The answer after <see cref="Save"/> returned false.</summary>
    private static JObject SaveFailed()
    {
        if (Unreadable)
        {
            return StudioApi.Error("商品库文件暂时读不了，这次没有保存，原有数据没有动。请稍后重试或联系管理员。", "data_file_unreadable");
        }
        return StudioApi.Error("商品库索引无法保存到服务器数据目录。", "product_save_failed");
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
        if (data?.Length > MaxDataLength || !ImageDataUrl.TryParse(data, ImageDataUrl.ImageDataFormats.PngJpegWebp, int.MaxValue, out byte[] bytes, out string extension))
        {
            return (null, null);
        }
        return (bytes, extension);
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

    /// <summary>What generation needs from a product's main angle: the original or cut-out as a data URL and the confirmed placement. Null with a reason
    /// when it cannot be used. Existing callers keep this behavior (F84): generation unchanged uses the main image.</summary>
    public static (string Image, Placement Placement, string Error) ForGeneration(string userId, string productId, bool cutout)
    {
        return ForGeneration(userId, productId, null, cutout);
    }

    /// <summary>What generation needs from one angle of a product (F84): the original or cut-out as a data URL and the angle's confirmed placement.
    /// A null or empty <paramref name="angleId"/> picks the main angle; a missing or malformed angle is refused with a reason.</summary>
    public static (string Image, Placement Placement, string Error) ForGeneration(string userId, string productId, string angleId, bool cutout)
    {
        ProductRecord record;
        ProductAngle angle;
        lock (Lock)
        {
            (record, angle) = Loaded().FindAngle(userId, productId, angleId);
        }
        if (record is null)
        {
            return (null, null, "商品不存在，请刷新商品库。");
        }
        if (angle is null)
        {
            return (null, null, "商品里没有这张角度图，请刷新商品库。");
        }
        if (cutout && angle.Cutout is null)
        {
            return (null, null, "这个商品还没有确认过抠图，请先在出图页完成抠图。");
        }
        string image = ReadDataUrl(record, cutout ? angle.Cutout : angle.Original);
        if (image is null)
        {
            return (null, null, "商品图暂时无法读取：作品库位置可能离线，请稍后重试。");
        }
        Placement.TryCreate(angle.X, angle.Y, angle.Size, out Placement placement);
        return (image, placement ?? Placement.Default, null);
    }

    /// <summary>The IDs of a saved product's angles in order, the main image first (F84 trial), so the direct-fusion batch knows how many photos it holds
    /// without reading any file. Null with a reason when the product cannot be used. The count never exceeds <see cref="ProductAngleRules.MaxAngles"/>.</summary>
    public static (List<string> Ids, string Error) AngleIdsFor(string userId, string productId)
    {
        ProductRecord record;
        lock (Lock)
        {
            record = Loaded().Find(userId, productId);
        }
        if (record is null)
        {
            return (null, "商品不存在，请刷新商品库。");
        }
        if (record.MainAngle() is null)
        {
            return (null, "这个商品还没有照片，请重新保存。");
        }
        return ([.. record.Angles.Take(ProductAngleRules.MaxAngles).Select(angle => angle.Id)], null);
    }

    /// <summary>Whether the account has a saved product with this ID (files are not read).</summary>
    public static bool Exists(string userId, string productId)
    {
        lock (Lock)
        {
            return Loaded().Find(userId, productId) is not null;
        }
    }

    /// <summary>The account's product file for the image route ("original" or "cutout") of one angle — the main angle when <paramref name="angleId"/>
    /// is null or empty — or null. A malformed angle ID is never looked up, so it can never reach a file path.</summary>
    public static (byte[] Bytes, string Extension) FileFor(string userId, string productId, string angleId, string kind)
    {
        ProductRecord record;
        ProductAngle angle;
        lock (Lock)
        {
            (record, angle) = Loaded().FindAngle(userId, productId, angleId);
        }
        string file = angle is null ? null : kind == "cutout" ? angle.Cutout : angle.Original;
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

    /// <summary>A product as its owner sees it: no file locations. The first angle is the main image (F84); the historical top-level fields mirror it
    /// and keep the plain main-image address, while every entry of the angle list names its own angle (see <see cref="ProductAngleRules.ImageRoute"/>).</summary>
    private static JObject Json(ProductRecord record)
    {
        ProductAngle main = record.MainAngle() ?? new ProductAngle() { Original = record.Original, Cutout = record.Cutout, X = record.X, Y = record.Y, Size = record.Size };
        return new JObject()
        {
            ["id"] = record.Id,
            ["name"] = record.Name,
            ["created"] = record.Created,
            ["has_cutout"] = main.Cutout is not null,
            ["placement"] = new JObject() { ["x"] = main.X, ["y"] = main.Y, ["size"] = main.Size },
            ["image"] = ProductAngleRules.ImageRoute(record.Id, null, false),
            ["cutout_image"] = main.Cutout is null ? null : ProductAngleRules.ImageRoute(record.Id, null, true),
            ["angles"] = new JArray(record.Angles.Select(angle => new JObject()
            {
                ["id"] = angle.Id,
                ["has_cutout"] = angle.Cutout is not null,
                ["placement"] = new JObject() { ["x"] = angle.X, ["y"] = angle.Y, ["size"] = angle.Size },
                ["image"] = ProductAngleRules.ImageRoute(record.Id, angle.Id, false),
                ["cutout_image"] = angle.Cutout is null ? null : ProductAngleRules.ImageRoute(record.Id, angle.Id, true)
            }))
        };
    }

    /// <summary>Edge warnings of a saved cut-out's rim (F83), analyzed at the work size: the cut-out is scaled down first, because a client can send
    /// up to 100 megapixels. Any failure only means no warnings — a save that already succeeded is never failed by the analysis, and there are none
    /// without a cut-out.</summary>
    private static List<string> EdgeWarnings(byte[] cut)
    {
        if (cut is null || cut.Length == 0)
        {
            return [];
        }
        try
        {
            byte[] fitted = PostProcess.ProductComposite.FitForWork(cut) ?? [];
            return ProductEdgeRules.TryAnalyze(fitted);
        }
        catch (Exception ex) when (ex is IOException or SixLabors.ImageSharp.UnknownImageFormatException or SixLabors.ImageSharp.InvalidImageContentException
            or SixLabors.ImageSharp.Memory.InvalidMemoryOperationException or NotSupportedException)
        {
            return [];
        }
    }

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

    [API.APIDescription("Save a product photo (PNG, JPEG or WebP data URL, at most 5 MB and 100 megapixels) to the current account's product library, optionally with its cut-out and placement; phone photos are turned upright by their EXIF orientation and their EXIF, XMP and IPTC metadata, GPS included, is removed before saving. Returns the product and edge_warnings codes about the cut-out rim's quality (F83).", "product: { id: \"…\" }, edge_warnings: []")]
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
        try
        {
            // Phone photos arrive upright only by their EXIF orientation: turn them upright and drop the metadata blocks, GPS included (F85),
            // so the saved file is what every later step (cut-out mask, placement preview) expects, and carries no location. An image over
            // the pixel limit is refused here — its size is read from the header and it is never decoded (T111).
            original = PostProcess.ProductComposite.AutoOrient(original, out extension);
            if (original is null)
            {
                return Task.FromResult(StudioApi.Error("商品图尺寸过大，请缩小到 1 亿像素以内。", "invalid_reference"));
            }
            if (cut is not null)
            {
                cut = PostProcess.ProductComposite.AutoOrient(cut, out _);
                if (cut is null)
                {
                    return Task.FromResult(StudioApi.Error("抠图尺寸过大，请缩小到 1 亿像素以内。", "invalid_reference"));
                }
            }
        }
        catch (Exception ex) when (ex is SixLabors.ImageSharp.UnknownImageFormatException or SixLabors.ImageSharp.InvalidImageContentException
            or SixLabors.ImageSharp.Memory.InvalidMemoryOperationException or NotSupportedException)
        {
            return Task.FromResult(StudioApi.Error("无法读取商品图，请重新导出为 PNG、JPEG 或 WebP。", "invalid_reference"));
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
        // The first photo is the product's main angle (F84); the historical single-photo fields mirror it.
        record.Angles = [new ProductAngle() { Id = ProductAngleRules.MainId, Original = record.Original, Cutout = record.Cutout }];
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
        JObject saved;
        lock (Lock)
        {
            string error = Loaded().Add(record);
            if (error is null && !Save())
            {
                Index.Items.Remove(record);
                TryDeleteFolder(record.Folder);
                return Task.FromResult(SaveFailed());
            }
            if (error is not null)
            {
                TryDeleteFolder(record.Folder);
                return Task.FromResult(StudioApi.Error(error, "product_save_failed"));
            }
            saved = new JObject() { ["product"] = Json(record) };
        }
        // Edge quality becomes plain-language notices at the upload confirmation; it never blocks anything. The analysis runs outside the index
        // lock and on a work-size copy, so neither a huge cut-out nor a failed analysis can hold up or fail the save.
        saved["edge_warnings"] = new JArray(EdgeWarnings(cut));
        return Task.FromResult(saved);
    }

    [API.APIDescription("Update one of the current account's products: name, confirmed cut-out (data URL) and placement (x, y as 0–1, size 0.1–1.5). The cut-out and placement apply to one angle — the main angle, or the one named by the optional angle_id (F84). Only given fields change. rawInput is the root request JSON.", "product: { id: \"…\" }")]
    public static Task<JObject> EcomCraftStudioProductUpdate(Session session, JObject rawInput)
    {
        if (!StudioApi.CanView(session))
        {
            return Task.FromResult(StudioApi.Error("无权访问创作工作台。", "bad_permissions"));
        }
        string id = rawInput["product_id"]?.ToString();
        string angleId = rawInput["angle_id"]?.ToString();
        if (!string.IsNullOrEmpty(angleId) && !ProductAngleRules.IsValidId(angleId))
        {
            return Task.FromResult(StudioApi.Error("角度图编号无效。", "invalid_parameters"));
        }
        lock (Lock)
        {
            ProductIndex index = Loaded();
            ProductRecord record = index.Find(session.User.UserID, id);
            if (record is null)
            {
                return Task.FromResult(StudioApi.Error("商品不存在，请刷新商品库。", "task_not_found"));
            }
            ProductAngle angle = string.IsNullOrEmpty(angleId) ? record.MainAngle() : record.Angles.FirstOrDefault(item => item.Id == angleId);
            if (angle is null)
            {
                return Task.FromResult(StudioApi.Error("商品里没有这张角度图，请刷新商品库。", "task_not_found"));
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
                (angle.X, angle.Y, angle.Size) = (parsed.CenterX, parsed.CenterY, parsed.Size);
                record.SyncLegacy();
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
                    // A cut-out goes upright and metadata-free like the photo it belongs to (F85), so it lines up with the rotated photo.
                    cut = PostProcess.ProductComposite.AutoOrient(cut, out _);
                    if (cut is null)
                    {
                        return Task.FromResult(StudioApi.Error("抠图尺寸过大，请缩小到 1 亿像素以内。", "invalid_reference"));
                    }
                    string cutoutFile = ProductAngleRules.CutoutFile(angle.Id);
                    File.WriteAllBytes(Path.Combine(record.Folder, cutoutFile), cut);
                    angle.Cutout = cutoutFile;
                    record.SyncLegacy();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Logs.Warning($"EcomCraft could not save a product cut-out: {ex.Message}");
                    return Task.FromResult(StudioApi.Error("抠图无法保存：作品库位置可能离线，请稍后重试。", "product_save_failed"));
                }
                catch (Exception ex) when (ex is SixLabors.ImageSharp.UnknownImageFormatException or SixLabors.ImageSharp.InvalidImageContentException
                    or SixLabors.ImageSharp.Memory.InvalidMemoryOperationException or NotSupportedException)
                {
                    return Task.FromResult(StudioApi.Error("无法读取抠图，请重新抠图或换一张透明 PNG。", "invalid_reference"));
                }
            }
            if (!Save())
            {
                return Task.FromResult(SaveFailed());
            }
            return Task.FromResult(new JObject() { ["product"] = Json(record) });
        }
    }

    [API.APIDescription("Add another angle photo (PNG, JPEG or WebP data URL, at most 5 MB and 100 megapixels; at most 3 angles per product, F84) to one of the current account's saved products, optionally with its cut-out. Phone photos are turned upright and stripped like first uploads (F85). Returns the product and edge_warnings codes about the cut-out rim's quality (F83).", "product: { id: \"…\" }, angle_id: \"…\", edge_warnings: []")]
    public static Task<JObject> EcomCraftStudioProductAngleAdd(Session session, string product_id, string image, string cutout = "")
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
        try
        {
            // An angle photo goes through the same turning and metadata removal as a first upload (F85, F84).
            original = PostProcess.ProductComposite.AutoOrient(original, out extension);
            if (original is null)
            {
                return Task.FromResult(StudioApi.Error("商品图尺寸过大，请缩小到 1 亿像素以内。", "invalid_reference"));
            }
            if (cut is not null)
            {
                cut = PostProcess.ProductComposite.AutoOrient(cut, out _);
                if (cut is null)
                {
                    return Task.FromResult(StudioApi.Error("抠图尺寸过大，请缩小到 1 亿像素以内。", "invalid_reference"));
                }
            }
        }
        catch (Exception ex) when (ex is SixLabors.ImageSharp.UnknownImageFormatException or SixLabors.ImageSharp.InvalidImageContentException
            or SixLabors.ImageSharp.Memory.InvalidMemoryOperationException or NotSupportedException)
        {
            return Task.FromResult(StudioApi.Error("无法读取商品图，请重新导出为 PNG、JPEG 或 WebP。", "invalid_reference"));
        }
        string angleId = ProductAngleRules.NewId();
        ProductAngle angle = new() { Id = angleId, Original = ProductAngleRules.OriginalFile(angleId, extension), Cutout = cut is null ? null : ProductAngleRules.CutoutFile(angleId) };
        string folder;
        lock (Lock)
        {
            ProductRecord record = Loaded().Find(session.User.UserID, product_id);
            if (record is null)
            {
                return Task.FromResult(StudioApi.Error("商品不存在，请刷新商品库。", "task_not_found"));
            }
            folder = record.Folder;
        }
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, angle.Original), original);
            if (cut is not null)
            {
                File.WriteAllBytes(Path.Combine(folder, angle.Cutout), cut);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logs.Warning($"EcomCraft could not save product files: {ex.Message}");
            return Task.FromResult(StudioApi.Error("商品图无法保存：作品库位置可能离线或空间不足，请稍后重试。", "product_save_failed"));
        }
        JObject saved;
        lock (Lock)
        {
            ProductIndex index = Loaded();
            string error = index.AddAngle(session.User.UserID, product_id, angle);
            if (error is not null)
            {
                TryDeleteFile(folder, angle.Original);
                TryDeleteFile(folder, angle.Cutout);
                return Task.FromResult(StudioApi.Error(error, "invalid_parameters"));
            }
            ProductRecord record = index.Find(session.User.UserID, product_id);
            if (!Save())
            {
                record.Angles.Remove(angle);
                TryDeleteFile(folder, angle.Original);
                TryDeleteFile(folder, angle.Cutout);
                return Task.FromResult(SaveFailed());
            }
            saved = new JObject() { ["product"] = Json(record), ["angle_id"] = angleId };
        }
        // Edge quality becomes plain-language notices like a first upload's; the analysis runs outside the index lock and on a work-size copy.
        saved["edge_warnings"] = new JArray(EdgeWarnings(cut));
        return Task.FromResult(saved);
    }

    [API.APIDescription("Delete one angle of one of the current account's saved products (F84). The next angle becomes the main image; the last angle cannot be deleted — delete the whole product instead.", "product: { id: \"…\" }")]
    public static Task<JObject> EcomCraftStudioProductAngleDelete(Session session, string product_id, string angle_id)
    {
        if (!StudioApi.CanView(session))
        {
            return Task.FromResult(StudioApi.Error("无权访问创作工作台。", "bad_permissions"));
        }
        lock (Lock)
        {
            ProductIndex index = Loaded();
            ProductRecord record = index.Find(session.User.UserID, product_id);
            if (record is null || !ProductAngleRules.IsValidId(angle_id) || record.MainAngle() is null)
            {
                return Task.FromResult(StudioApi.Error("商品里没有这张角度图，请刷新商品库。", "task_not_found"));
            }
            string error = index.RemoveAngle(session.User.UserID, product_id, angle_id, out ProductAngle angle, out int position);
            if (error is not null)
            {
                return Task.FromResult(StudioApi.Error(error, "invalid_parameters"));
            }
            if (!Save())
            {
                record.Angles.Insert(Math.Clamp(position, 0, record.Angles.Count), angle);
                record.SyncLegacy();
                return Task.FromResult(SaveFailed());
            }
            // The index is saved first; a file that cannot be deleted (for example an offline share) is only left behind and logged.
            TryDeleteFile(record.Folder, angle.Original);
            TryDeleteFile(record.Folder, angle.Cutout);
            return Task.FromResult(new JObject() { ["product"] = Json(record) });
        }
    }

    [API.APIDescription("Make one angle of one of the current account's saved products the main image (F84); the previous main image becomes a normal angle and the angles keep their IDs and order.", "product: { id: \"…\" }")]
    public static Task<JObject> EcomCraftStudioProductAngleMain(Session session, string product_id, string angle_id)
    {
        if (!StudioApi.CanView(session))
        {
            return Task.FromResult(StudioApi.Error("无权访问创作工作台。", "bad_permissions"));
        }
        lock (Lock)
        {
            ProductIndex index = Loaded();
            ProductRecord record = index.Find(session.User.UserID, product_id);
            if (record is null || !ProductAngleRules.IsValidId(angle_id) || record.MainAngle() is null)
            {
                return Task.FromResult(StudioApi.Error("商品里没有这张角度图，请刷新商品库。", "task_not_found"));
            }
            string previousMain = record.Angles[0].Id;
            string error = index.SetMain(session.User.UserID, product_id, angle_id);
            if (error is not null)
            {
                return Task.FromResult(StudioApi.Error(error, "invalid_parameters"));
            }
            if (!Save())
            {
                index.SetMain(session.User.UserID, product_id, previousMain);
                return Task.FromResult(SaveFailed());
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
                return Task.FromResult(SaveFailed());
            }
            TryDeleteFolder(record.Folder);
        }
        return Task.FromResult(new JObject() { ["success"] = true });
    }

    /// <summary>Removes one file of a product folder; a failure (for example an offline share) only leaves the file behind and is logged.</summary>
    private static void TryDeleteFile(string folder, string file)
    {
        if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(file))
        {
            return;
        }
        try
        {
            File.Delete(Path.Combine(folder, file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logs.Warning($"EcomCraft could not delete product files: {ex.Message}");
        }
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
