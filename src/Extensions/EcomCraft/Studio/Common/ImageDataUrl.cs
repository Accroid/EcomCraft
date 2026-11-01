namespace EcomCraftStudio.Studio.Common;

/// <summary>Image data-URL parsing and building (R12). Every call site chooses the formats and the size limit it always had;
/// this helper never widens or tightens them. Character limits apply to the whole data URL, byte limits to the decoded payload.</summary>
public static class ImageDataUrl
{
    /// <summary>The image formats one parser call accepts.</summary>
    [Flags]
    public enum ImageDataFormats
    {
        /// <summary>No format at all.</summary>
        None = 0,

        /// <summary>PNG, data:image/png;base64.</summary>
        Png = 1,

        /// <summary>JPEG, data:image/jpeg;base64.</summary>
        Jpeg = 2,

        /// <summary>WebP, data:image/webp;base64.</summary>
        Webp = 4,

        /// <summary>Any image data URL whatever its subtype: only the data:image/ prefix and the comma are checked, the payload is taken as given.</summary>
        AnyImage = 8,

        /// <summary>The three formats the upload paths accept: PNG, JPEG and WebP.</summary>
        PngJpegWebp = Png | Jpeg | Webp,
    }

    /// <summary>The exact data-URL prefix of a named format, or null.</summary>
    private static string Prefix(ImageDataFormats format) => format switch
    {
        ImageDataFormats.Png => "data:image/png;base64",
        ImageDataFormats.Jpeg => "data:image/jpeg;base64",
        ImageDataFormats.Webp => "data:image/webp;base64",
        _ => null,
    };

    /// <summary>Whether the data URL's prefix names one of the accepted formats; outputs the matched format (AnyImage for an arbitrary subtype) and the comma's index.</summary>
    private static bool MatchPrefix(string dataUrl, ImageDataFormats allowed, out ImageDataFormats format, out int comma)
    {
        format = ImageDataFormats.None;
        comma = -1;
        if (dataUrl is null)
        {
            return false;
        }
        if (allowed.HasFlag(ImageDataFormats.AnyImage) && dataUrl.StartsWith("data:image/", StringComparison.Ordinal))
        {
            comma = dataUrl.IndexOf(',');
            if (comma >= 0)
            {
                format = ImageDataFormats.AnyImage;
                return true;
            }
        }
        foreach (ImageDataFormats candidate in (ImageDataFormats[])[ImageDataFormats.Png, ImageDataFormats.Jpeg, ImageDataFormats.Webp])
        {
            string prefix = Prefix(candidate);
            if (allowed.HasFlag(candidate) && dataUrl.StartsWith(prefix, StringComparison.Ordinal)
                && dataUrl.Length > prefix.Length && dataUrl[prefix.Length] == ',')
            {
                format = candidate;
                comma = prefix.Length;
                return true;
            }
        }
        return false;
    }

    /// <summary>Whether the data URL carries one of the accepted formats and stays within the character limit; the payload is not decoded.</summary>
    public static bool IsValid(string dataUrl, ImageDataFormats allowed, int maxChars) =>
        dataUrl is not null && dataUrl.Length <= maxChars && MatchPrefix(dataUrl, allowed, out _, out _);

    /// <summary>Whether the string starts with the image data-URL prefix (data:image/) and stays within the character limit;
    /// neither the comma nor the payload is checked, matching the call sites that only ever looked at the prefix.</summary>
    public static bool HasImagePrefix(string dataUrl, int maxChars) =>
        dataUrl is not null && dataUrl.Length <= maxChars && dataUrl.StartsWith("data:image/", StringComparison.Ordinal);

    /// <summary>Decodes the data URL's base64 payload when its prefix names one of the accepted formats and it decodes to at least one byte and at most
    /// <paramref name="maxBytes"/>; the extension follows the subtype (jpeg becomes jpg), an arbitrary subtype gives the subtype text.</summary>
    public static bool TryParse(string dataUrl, ImageDataFormats allowed, int maxBytes, out byte[] bytes, out string extension)
    {
        bytes = null;
        extension = null;
        if (!MatchPrefix(dataUrl, allowed, out ImageDataFormats format, out int comma))
        {
            return false;
        }
        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(dataUrl[(comma + 1)..]);
        }
        catch (FormatException)
        {
            return false;
        }
        if (decoded.Length == 0 || decoded.Length > maxBytes)
        {
            return false;
        }
        bytes = decoded;
        extension = format switch
        {
            ImageDataFormats.AnyImage => ExtensionOf(dataUrl, comma),
            ImageDataFormats.Jpeg => "jpg",
            ImageDataFormats.Png => "png",
            _ => "webp",
        };
        return true;
    }

    /// <summary>The subtype of an image data URL as a file extension (jpeg becomes jpg), for AnyImage matches.</summary>
    private static string ExtensionOf(string dataUrl, int comma)
    {
        string mime = dataUrl["data:".Length..comma];
        int semicolon = mime.IndexOf(';');
        if (semicolon >= 0)
        {
            mime = mime[..semicolon];
        }
        return mime == "image/jpeg" ? "jpg" : mime.StartsWith("image/", StringComparison.Ordinal) ? mime["image/".Length..] : mime;
    }

    /// <summary>Builds a data URL of the given mime type.</summary>
    public static string ToDataUrl(byte[] bytes, string mime) => $"data:{mime};base64," + Convert.ToBase64String(bytes);
}
