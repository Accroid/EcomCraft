namespace EcomCraftStudio.Studio.Products;

/// <summary>Rules for a product's angle photos (F84, trial version): how many angles one product holds, what an angle ID may look like and where
/// each angle's files are named. Pure logic, independent of SwarmUI, so the boundaries can be unit tested.</summary>
public static class ProductAngleRules
{
    /// <summary>Most angle photos per product, the main image included (A25 trial version).</summary>
    public const int MaxAngles = 3;

    /// <summary>ID of the main angle: the first angle of every product, and the one records saved before angles existed migrate to.</summary>
    public const string MainId = "main";

    /// <summary>Whether an angle ID is well-formed: 1–16 ASCII letters or digits, so a client's ID can never carry a path separator, a dot or "..".</summary>
    public static bool IsValidId(string id)
    {
        return !string.IsNullOrEmpty(id) && id.Length <= 16 && id.All(char.IsAsciiLetterOrDigit);
    }

    /// <summary>A fresh ID for an added angle, distinct from <see cref="MainId"/>.</summary>
    public static string NewId()
    {
        return Guid.NewGuid().ToString("N")[..8];
    }

    /// <summary>The file name of an angle's original photo: the main angle keeps the historical names, added angles carry their ID.</summary>
    public static string OriginalFile(string angleId, string extension)
    {
        return angleId == MainId ? $"original.{extension}" : $"angle-{angleId}-original.{extension}";
    }

    /// <summary>The file name of an angle's cut-out: the main angle keeps the historical name, added angles carry their ID.</summary>
    public static string CutoutFile(string angleId)
    {
        return angleId == MainId ? "cutout.png" : $"angle-{angleId}-cutout.png";
    }
}
