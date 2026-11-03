using Newtonsoft.Json.Linq;

namespace EcomCraftStudio.Studio;

/// <summary>The wiring of the reference-edit trial's standard workflows (F84, T123): a converted Image Edit blueprint gains SwarmLoadImageB64
/// inputs for the reference and the product's angle photos, and the encoder's pixel budget becomes a server-set parameter. The images ride inside the
/// request itself (A34): the parameters are marked <c>image_always_b64</c>, so the backend embeds their base64 into the node and never uploads a file to
/// the execution server's input folder. Pure logic, independent of SwarmUI, so the wiring of one, two and three angles can be unit tested.
/// The parameters are hidden: they are filled by the batch's server-side options, never by a client.</summary>
public static class ReferenceEditRules
{
    /// <summary>The workflow parameter carrying the reference image (image_1 of the encoder).</summary>
    public const string ReferenceParam = "ecreferenceimage";

    /// <summary>The base name of the product's angle parameters: the first angle keeps the name every standard workflow uses for its product
    /// image (<see cref="StandardWorkflows.ProductImageParam"/>). SwarmUI canonical parameter IDs keep letters only (digits are stripped on lookup,
    /// so "ecproductimage2" would collide with "ecproductimage"), so further angles append a letter instead of their number.</summary>
    public const string ProductImageParam = "ecproductimage";

    /// <summary>The workflow parameter carrying the product's angle photo at this index (0 = the main angle, image_2 of the encoder and up).</summary>
    public static string ProductParam(int index) => index == 0 ? ProductImageParam : $"{ProductImageParam}{(char)('a' + index)}";

    /// <summary>The workflow parameter carrying the encoder's pixel budget (the TextEncodeQwenImage21 resolution input), set by the server per request.</summary>
    public const string ResolutionParam = "eceditresolution";

    /// <summary>The ComfyUI node that reads an image from the request instead of the server's input folder (A34); execution servers without it cannot run these workflows.</summary>
    public const string ImageNode = "SwarmLoadImageB64";

    /// <summary>Node IDs of the added image nodes, above the converter's numbering: the reference first, then one per product angle.</summary>
    private const string ReferenceNode = "9101";

    /// <summary>The first product angle's image node ID; further angles count on from it.</summary>
    private const int ProductNodeBase = 9102;

    /// <summary>Rewires a converted Image Edit graph for the trial with <paramref name="angles"/> product photos (1–3): SwarmLoadImageB64 nodes feed the
    /// encoder's image slots (image_1 the reference, image_2 up the product's angles in order) and the encoder's pixel budget becomes the hidden
    /// <see cref="ResolutionParam"/>. Returns false when the graph has no single Qwen Image 2.1 encoder or no literal resolution to bind.</summary>
    public static bool Apply(JObject prompt, JObject metadata, JObject values, int angles)
    {
        List<JProperty> encoders = [.. prompt.Properties().Where(property => (property.Value as JObject)?["class_type"]?.ToString() == "TextEncodeQwenImage21")];
        if (encoders.Count != 1)
        {
            return false;
        }
        JObject encoder = (JObject)encoders[0].Value;
        JObject encoderInputs = (JObject)encoder["inputs"];
        if (encoderInputs?["resolution"] is not JValue resolution || resolution.Type == JTokenType.Null)
        {
            return false;
        }
        metadata[ReferenceParam] = ImageParam(ReferenceParam, "参考图", "照着做的参考图（编辑模型的 image 1）。");
        prompt[ReferenceNode] = ImageNodeInput(ReferenceParam);
        encoderInputs["images.image_1"] = new JArray(ReferenceNode, 0);
        for (int index = 0; index < Math.Clamp(angles, 1, 3); index++)
        {
            string param = ProductParam(index);
            metadata[param] = ImageParam(param, index == 0 ? "商品图" : $"商品角度图 {index + 1}", $"商品的第 {index + 1} 张角度照片（编辑模型的 image {index + 2}）。");
            string node = (ProductNodeBase + index).ToString(System.Globalization.CultureInfo.InvariantCulture);
            prompt[node] = ImageNodeInput(param);
            encoderInputs[$"images.image_{index + 2}"] = new JArray(node, 0);
        }
        metadata[ResolutionParam] = new JObject()
        {
            ["id"] = ResolutionParam, ["name"] = "清晰度预算", ["type"] = "integer", ["default"] = resolution.DeepClone(),
            ["min"] = 0, ["max"] = 2048, ["step"] = 1, ["visible"] = false, ["form"] = null,
            ["native_node"] = encoders[0].Name, ["native_input"] = "resolution", ["native_class"] = "TextEncodeQwenImage21",
            ["description"] = "编辑模型的像素总量预算，服务端按硬件自动设置。"
        };
        values[ResolutionParam] = resolution.DeepClone();
        encoderInputs["resolution"] = "%%_COMFYFIXME_${" + ResolutionParam + "}_ENDFIXME_%%";
        return true;
    }

    /// <summary>One hidden image parameter of the edit workflows: only the server-side batch fills it, so no page or request field can carry it.
    /// <c>image_always_b64</c> keeps the image inside the request (A34): the backend fills the SwarmLoadImageB64 node with its base64 instead of
    /// uploading a file to the execution server's input folder.</summary>
    private static JObject ImageParam(string id, string name, string description) => new()
    {
        ["id"] = id, ["name"] = name, ["type"] = "image", ["default"] = "", ["visible"] = false, ["form"] = null,
        ["image_always_b64"] = true,
        ["description"] = description
    };

    /// <summary>One SwarmLoadImageB64 node reading its image from the given parameter's base64 (A34).</summary>
    public static JObject ImageNodeInput(string param) => new()
    {
        ["class_type"] = ImageNode, ["inputs"] = new JObject() { ["image_base64"] = "${" + param + "}" }
    };
}
