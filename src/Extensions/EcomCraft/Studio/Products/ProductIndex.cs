namespace EcomCraftStudio.Studio.Products;

/// <summary>One angle photo of a saved product (F84): its own original, cut-out and placement. <see cref="ProductRecord.MainAngleId"/> names the
/// product's main image; angle IDs are only ever compared, never used to build file paths.</summary>
public sealed class ProductAngle
{
    /// <summary>Angle ID, unique within the product.</summary>
    public string Id;

    /// <summary>File name of this angle's original photo.</summary>
    public string Original;

    /// <summary>File name of this angle's cut-out, or null before one is confirmed.</summary>
    public string Cutout;

    /// <summary>Confirmed product center, fraction of the picture width.</summary>
    public double X = 0.5;

    /// <summary>Confirmed product center, fraction of the picture height.</summary>
    public double Y = 0.56;

    /// <summary>Confirmed product size, fraction of the picture's shorter side.</summary>
    public double Size = 0.55;
}

/// <summary>One saved product (F36): who owns it, its name, where its files are and the confirmed placement. File locations never leave the server.</summary>
public sealed class ProductRecord
{
    /// <summary>Product ID.</summary>
    public string Id;

    /// <summary>Owner account.</summary>
    public string UserId;

    /// <summary>Display name.</summary>
    public string Name;

    /// <summary>Creation time, Unix milliseconds.</summary>
    public long Created;

    /// <summary>Folder holding the files.</summary>
    public string Folder;

    /// <summary>File name of the original photo. Historical field of records saved before angles existed (F84); it always mirrors the main angle.</summary>
    public string Original;

    /// <summary>File name of the cut-out, or null before one is confirmed. Historical field of records saved before angles existed (F84); it always mirrors the main angle.</summary>
    public string Cutout;

    /// <summary>Confirmed product center, fraction of the picture width.</summary>
    public double X = 0.5;

    /// <summary>Confirmed product center, fraction of the picture height.</summary>
    public double Y = 0.56;

    /// <summary>Confirmed product size, fraction of the picture's shorter side. Historical field of records saved before angles existed (F84); it always mirrors the main angle.</summary>
    public double Size = 0.55;

    /// <summary>The angle photos (F84) in the order they were added; <see cref="MainAngleId"/> names the main image. Never null once
    /// <see cref="ProductIndex.Migrate"/> has run.</summary>
    public List<ProductAngle> Angles = [];

    /// <summary>ID of the main angle. Making another angle the main image only changes this ID, so the list keeps its order; null or an unknown ID
    /// (records saved before it existed, when the main angle was always the first) means the first angle.</summary>
    public string MainAngleId;

    /// <summary>The selling info by language (F79, T99): display name, selling points, parameters and a size line, written by the user for each
    /// market — never machine-translated. Null or missing entries mean "not filled in"; records saved before T99 simply have none.</summary>
    public Dictionary<string, ProductSpecs> Info;

    /// <summary>The main angle — the one <see cref="MainAngleId"/> names, else the first of the list — or null while the record has no angle at all.</summary>
    public ProductAngle MainAngle()
    {
        return Angles.FirstOrDefault(angle => angle.Id == MainAngleId) ?? (Angles.Count > 0 ? Angles[0] : null);
    }

    /// <summary>The angle IDs with the main angle first and the others in list order: generation reads the first as the main image.</summary>
    public List<string> AngleIdsMainFirst()
    {
        ProductAngle main = MainAngle();
        return main is null ? [] : [main.Id, .. Angles.Where(angle => angle != main).Select(angle => angle.Id)];
    }

    /// <summary>Mirrors the main angle into the historical single-photo fields, so files saved by either version of the index agree.</summary>
    public void SyncLegacy()
    {
        if (MainAngle() is not ProductAngle main)
        {
            return;
        }
        Original = main.Original;
        Cutout = main.Cutout;
        X = main.X;
        Y = main.Y;
        Size = main.Size;
    }
}

/// <summary>The product index with per-account isolation, independent of SwarmUI so isolation and deletion can be unit tested.
/// Every lookup takes the account: another account's product is never found, renamed or removed.</summary>
public sealed class ProductIndex
{
    /// <summary>Longest product name.</summary>
    public const int MaxNameLength = 60;

    /// <summary>Most products per account.</summary>
    public const int MaxPerUser = 500;

    /// <summary>All records.</summary>
    public List<ProductRecord> Items = [];

    /// <summary>The account's products, newest first.</summary>
    public List<ProductRecord> ForUser(string userId) => [.. Items.Where(item => item.UserId == userId).OrderByDescending(item => item.Created)];

    /// <summary>The account's product with this ID, or null (also for another account's product).</summary>
    public ProductRecord Find(string userId, string id) => Items.FirstOrDefault(item => item.Id == id && item.UserId == userId);

    /// <summary>A trimmed name of 1–60 characters, or null when invalid.</summary>
    public static string CleanName(string name)
    {
        string text = (name ?? "").Trim();
        return text.Length is > 0 and <= MaxNameLength && !text.Any(char.IsControl) ? text : null;
    }

    /// <summary>Adds a record; returns an error when the account already has the maximum.</summary>
    public string Add(ProductRecord record)
    {
        if (Items.Count(item => item.UserId == record.UserId) >= MaxPerUser)
        {
            return $"商品库最多 {MaxPerUser} 个商品，请先删除不用的商品。";
        }
        Items.Add(record);
        return null;
    }

    /// <summary>Renames the account's product; returns an error when not found or the name is invalid.</summary>
    public string Rename(string userId, string id, string name)
    {
        ProductRecord record = Find(userId, id);
        string clean = CleanName(name);
        if (record is null)
        {
            return "商品不存在。";
        }
        if (clean is null)
        {
            return $"商品名称须为 1–{MaxNameLength} 个字符。";
        }
        record.Name = clean;
        return null;
    }

    /// <summary>Saves the selling info of one language of the account's product (F79, T99): an empty entry is removed again, so nothing empty is
    /// kept. Returns an error when the product is not this account's or the language is unknown; the previous entry is returned so a failed save
    /// can put it back.</summary>
    public string SaveInfo(string userId, string id, string language, ProductSpecs specs, out ProductSpecs previous)
    {
        previous = null;
        ProductRecord record = Find(userId, id);
        if (record is null)
        {
            return "商品不存在。";
        }
        string known = ProductInfoRules.LanguageId(language);
        if (known is null)
        {
            return "不支持的语言。";
        }
        record.Info ??= [];
        previous = record.Info.TryGetValue(known, out ProductSpecs old) ? old : null;
        if (ProductInfoRules.IsFilled(specs))
        {
            record.Info[known] = specs;
        }
        else
        {
            record.Info.Remove(known);
        }
        return null;
    }

    /// <summary>Removes the account's product and returns it so its files can be deleted, or null when not found.</summary>
    public ProductRecord Remove(string userId, string id)
    {
        ProductRecord record = Find(userId, id);
        if (record is not null)
        {
            Items.Remove(record);
        }
        return record;
    }

    /// <summary>Fills every record's angle list (F84): a record saved before angles existed becomes one main angle carrying its photo, cut-out and
    /// placement, and nothing is renamed on disk. Angle entries without a photo or with a malformed or duplicate ID are dropped, and every record's
    /// historical fields mirror its main angle, so no reading loses data.</summary>
    public void Migrate()
    {
        foreach (ProductRecord record in Items)
        {
            record.Angles = (record.Angles ?? []).Where(angle => angle is not null && !string.IsNullOrEmpty(angle.Original) && ProductAngleRules.IsValidId(angle.Id))
                .GroupBy(angle => angle.Id).Select(group => group.First()).ToList();
            if (record.Angles.Count == 0 && !string.IsNullOrEmpty(record.Original))
            {
                record.Angles = [new ProductAngle() { Id = ProductAngleRules.MainId, Original = record.Original, Cutout = record.Cutout, X = record.X, Y = record.Y, Size = record.Size }];
            }
            // Selling info (F79, T99): records saved before it existed have none, which reads as "not filled in"; unknown language keys (only
            // writable by hand) are dropped so the index keeps only the languages the rules know.
            record.Info = (record.Info ?? []).Where(entry => ProductInfoRules.LanguageId(entry.Key) is not null && entry.Value is not null)
                .ToDictionary(entry => entry.Key, entry => entry.Value);
            record.MainAngleId = record.MainAngle()?.Id;
            record.SyncLegacy();
        }
    }

    /// <summary>The account's product and its angle with this ID (the main angle when the ID is null or empty), or nulls; another account's product is
    /// never found. A malformed angle ID is never looked up.</summary>
    public (ProductRecord Record, ProductAngle Angle) FindAngle(string userId, string productId, string angleId)
    {
        ProductRecord record = Find(userId, productId);
        if (record is null || record.MainAngle() is null)
        {
            return (record, null);
        }
        if (string.IsNullOrEmpty(angleId))
        {
            return (record, record.MainAngle());
        }
        return ProductAngleRules.IsValidId(angleId) ? (record, record.Angles.FirstOrDefault(item => item.Id == angleId)) : (record, null);
    }

    /// <summary>Adds an angle to the account's product; returns an error when the product is not found or already holds the maximum (F84).</summary>
    public string AddAngle(string userId, string productId, ProductAngle angle)
    {
        ProductRecord record = Find(userId, productId);
        if (record is null)
        {
            return "商品不存在。";
        }
        if (record.Angles.Count >= ProductAngleRules.MaxAngles)
        {
            return $"一个商品最多 {ProductAngleRules.MaxAngles} 张角度图，请先删除不用的角度图。";
        }
        record.Angles.Add(angle);
        return null;
    }

    /// <summary>Removes one angle of the account's product and reports its old position, so a failed save can put it back; when it was the main
    /// image, the first remaining angle becomes the main image. The last angle is never removed — the product itself has to be deleted — and the
    /// historical fields follow the main angle.</summary>
    public string RemoveAngle(string userId, string productId, string angleId, out ProductAngle removed, out int position)
    {
        removed = null;
        position = 0;
        ProductRecord record = Find(userId, productId);
        ProductAngle angle = record?.Angles.FirstOrDefault(item => item.Id == angleId);
        if (record is null || angle is null)
        {
            return "商品里没有这张角度图。";
        }
        if (record.Angles.Count <= 1)
        {
            return $"最后 1 张角度图不能删除；要移除整个商品，请删除商品“{record.Name}”。";
        }
        bool wasMain = record.MainAngle() == angle;
        position = record.Angles.IndexOf(angle);
        record.Angles.RemoveAt(position);
        removed = angle;
        if (wasMain)
        {
            record.MainAngleId = record.Angles[0].Id;
        }
        record.SyncLegacy();
        return null;
    }

    /// <summary>Makes one of the account's angles the product's main image (F84). Only the main ID changes: every angle keeps its ID and place in the
    /// list, so the page shows the angles in the same order.</summary>
    public string SetMain(string userId, string productId, string angleId)
    {
        ProductRecord record = Find(userId, productId);
        ProductAngle angle = record?.Angles.FirstOrDefault(item => item.Id == angleId);
        if (record is null || angle is null)
        {
            return "商品里没有这张角度图。";
        }
        record.MainAngleId = angle.Id;
        record.SyncLegacy();
        return null;
    }
}
