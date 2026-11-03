using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace EcomCraftStudio.Studio;

/// <summary>Secret-redaction rules of the diagnostics package (F44), kept free of side effects so they can be unit tested.</summary>
public static partial class DiagnosticsSanitizer
{
    /// <summary>Property names whose values never leave the server.</summary>
    [GeneratedRegex(@"(^|_)(api[_-]?key|key|secret|password|passphrase|token|authorization|credential)(_|$)", RegexOptions.IgnoreCase)]
    private static partial Regex SecretKey();

    /// <summary>Bearer headers, matched before secret assignments so their tokens are gone too.</summary>
    [GeneratedRegex(@"(?i)bearer\s+[a-z0-9._~+/=-]{8,}")]
    private static partial Regex BearerText();

    /// <summary>Bare secret assignments inside plain text such as logs.</summary>
    [GeneratedRegex(@"(?i)\b(api[_-]?key|secret|password|passphrase|token|authorization)(\s*[=:]\s*)([^\s,;""'}\]]+)")]
    private static partial Regex SecretText();

    /// <summary>Long base64 runs, as embedded images produce (A34): generation failures can log a whole workflow, and a single image is thousands of
    /// characters that no ordinary text reaches.</summary>
    [GeneratedRegex(@"[A-Za-z0-9+/]{200,}={0,2}")]
    private static partial Regex LongBase64();

    /// <summary>Replaces long base64 runs (embedded image content) with a marker, so logs and diagnostics never carry picture data (A34).</summary>
    public static string RemoveBase64(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }
        return LongBase64().Replace(text, "[removed]");
    }

    /// <summary>Removes every secret value from parsed JSON: secret-looking property names keep their name and lose their value, and long base64
    /// runs inside string values (embedded images) are replaced as well (A34).</summary>
    public static JToken RedactJson(JToken token)
    {
        switch (token)
        {
            case JObject obj:
                JObject clean = [];
                foreach (JProperty property in obj.Properties())
                {
                    clean[property.Name] = SecretKey().IsMatch(property.Name) ? new JValue("[removed]") : RedactJson(property.Value);
                }
                return clean;
            case JArray array:
                return new JArray(array.Select(RedactJson));
            case JValue value when value.Type == JTokenType.String:
                return new JValue(RemoveBase64(value.Value<string>()));
            default:
                return token?.DeepClone();
        }
    }

    /// <summary>Removes secret assignments, bearer tokens and long base64 runs (embedded image content, A34) from plain text, such as log lines.</summary>
    public static string RedactText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }
        string withoutBase64 = RemoveBase64(text);
        string withoutBearer = BearerText().Replace(withoutBase64, "bearer [removed]");
        return SecretText().Replace(withoutBearer, match => $"{match.Groups[1].Value}{match.Groups[2].Value}[removed]");
    }
}
