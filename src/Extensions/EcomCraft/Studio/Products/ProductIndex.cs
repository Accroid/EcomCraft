namespace EcomCraftStudio.Studio.Products;

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

    /// <summary>File name of the original photo.</summary>
    public string Original;

    /// <summary>File name of the cut-out, or null before one is confirmed.</summary>
    public string Cutout;

    /// <summary>Confirmed product center, fraction of the picture width.</summary>
    public double X = 0.5;

    /// <summary>Confirmed product center, fraction of the picture height.</summary>
    public double Y = 0.5;

    /// <summary>Confirmed product size, fraction of the picture's shorter side.</summary>
    public double Size = 0.7;
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
}
