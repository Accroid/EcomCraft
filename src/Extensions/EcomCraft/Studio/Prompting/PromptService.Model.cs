using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using EcomCraftStudio.MagicPrompt;
using EcomCraftStudio.MagicPrompt.WebAPI;
using EcomCraftStudio.MagicPrompt.WebAPI.Models;
using EcomCraftStudio.Studio.Common;
using EcomCraftStudio.Studio.Generation;
using EcomCraftStudio.Studio.Templates;
using FreneticUtilities.FreneticExtensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio.Prompting;


/// <summary>The low-level MagicPrompt calls. Part of the PromptService partial class (R11).</summary>
public static partial class PromptService
{
    /// <summary>Longest wait for the language model before falling back.</summary>
    private static readonly TimeSpan ModelTimeout = TimeSpan.FromSeconds(45);

    /// <summary>Longest wait for translating a reference description: the paragraph is long and nothing falls back meanwhile, so a slow cloud model gets more time.</summary>
    private static readonly TimeSpan DescriptionTranslateTimeout = TimeSpan.FromSeconds(90);

    /// <summary>Longest wait for the vision model: a local one may first have to load into memory, reading an image is slower than text,
    /// and a first Ollama request that hangs on the GPU is retried on the CPU.</summary>
    private static readonly TimeSpan VisionTimeout = TimeSpan.FromSeconds(180);

    /// <summary>Asks the configured MagicPrompt model; returns the sanitized answer, or an empty string on any failure or timeout. Errors are logged without the text.</summary>
    private static async Task<string> AskModelAsync(Session session, string instructions, string text, string imageDataUrl, TimeSpan? limit = null) =>
        PromptRules.Sanitize(await AskModelRawAsync(session, instructions, text, imageDataUrl, limit));

    /// <summary>What a reference image may be at most, as a data URL — the same cap as product photos.</summary>
    private const int MaxReferenceImageLength = (5 * 1024 * 1024 + 2) / 3 * 4 + 40;

    /// <summary>The instruction for describing one reference image (F59): the setup and style only, never the product, so the description can steer a
    /// different product into a similar frame without copying it. The real product is already on the generation canvas and is pasted back afterwards, so any
    /// product named in the prompt gets drawn as a second one: the product is never mentioned, and no containers or packages may appear.
    /// Small vision models still slip, so the answer also goes through <see cref="PromptRules.WithoutProduct"/>. It asks for a detailed paragraph,
    /// since a one-sentence answer gives the image model too little to go on.</summary>
    private const string ReferenceInstruction = "You write prompts for an image model. The product is already in the picture, so the prompt must describe "
        + "only the scene around it. Describe how this product photo is set up: shot type and camera angle; the surface and background (materials, colors, textures); "
        + "props and where they are placed; lighting (direction, softness, shadows, highlights); color palette and mood; the overall photographic style. "
        + "Never mention the product or describe it: not its type, shape, color, material, packaging, brand, logo or any text. Describe only "
        + "the surface it stands on and everything around it. Do not mention any bottles, jars, tubes, boxes, packages or containers, even if the photo has them; "
        + "props must be things that are not products, such as plants, fabric, stones, water or fruit. "
        + "Whatever the photo's angle, write it as an eye-level straight-on shot with an open flat clear surface in the center foreground and the props at the sides and behind, "
        + "so an upright item can stand there. "
        + "Reply with one English paragraph of 60 to 120 words, without lists or headings.";

    /// <summary>Asks the configured MagicPrompt model and returns its raw answer (at most <see cref="MaxRawAnswerLength"/> characters) for callers that parse
    /// a line format themselves, or an empty string on any failure or timeout. Callers must still clean everything they keep.</summary>
    private static async Task<string> AskModelRawAsync(Session session, string instructions, string text, string imageDataUrl, TimeSpan? limitOverride = null, bool translate = false)
    {
        try
        {
            JObject settings = (await SessionSettings.GetMagicPromptSettings())["settings"] as JObject;
            bool image = imageDataUrl is not null;
            bool vision = image;
            string model = settings?[vision ? "visionmodel" : "model"]?.ToString();
            // Translations run on the vision model when image description is on and a vision model is set, so they use the same service and model size
            // as the descriptions (a local Ollama vision model keeps them on this network); text-only, through the vision service's own address and key.
            string visionModel = settings?["visionmodel"]?.ToString();
            if (translate && !string.IsNullOrWhiteSpace(visionModel) && VisionEnabled())
            {
                vision = true;
                model = visionModel;
            }
            if (string.IsNullOrWhiteSpace(model))
            {
                return "";
            }
            JObject content = new() { ["text"] = text, ["instructions"] = instructions };
            if (image)
            {
                // Names the model actually asked, so administrators can confirm a switch took effect; never the address, key or image.
                Logs.Info($"EcomCraft is describing an image with {LLMAPICalls.VisionBackend(settings)} / {model}.");
                int comma = imageDataUrl.IndexOf(',');
                if (!ImageDataUrl.IsValid(imageDataUrl, ImageDataUrl.ImageDataFormats.AnyImage, int.MaxValue))
                {
                    return "";
                }
                content["media"] = new JArray(new JObject()
                {
                    ["Type"] = "base64",
                    ["Data"] = imageDataUrl[(comma + 1)..],
                    ["MediaType"] = imageDataUrl[5..comma].Replace(";base64", "")
                });
            }
            TimeSpan limit = limitOverride ?? (image ? VisionTimeout : ModelTimeout);
            // The limit also goes to the call, so a request given up here does not keep the model busy upstream.
            JObject request = new() { ["messageContent"] = content, ["modelId"] = model, ["messageType"] = vision ? "Vision" : "Text", ["action"] = image ? "vision" : "chat",
                ["timeout"] = (int)limit.TotalSeconds };
            Task<JObject> call = LLMAPICalls.MagicPromptPhoneHome(request, session);
            // A little extra over the call's own limit leaves time to unload a hung Ollama model.
            if (await Task.WhenAny(call, Task.Delay(limit + TimeSpan.FromSeconds(20))) != call)
            {
                Logs.Warning("EcomCraft prompt service: the language model did not answer in time; using the template defaults.");
                return "";
            }
            JObject result = await call;
            if (result?.Value<bool?>("success") != true)
            {
                Logs.Warning("EcomCraft prompt service: the language model returned an error; using the template defaults.");
                return "";
            }
            string answer = result["response"]?.ToString() ?? "";
            return answer.Length > MaxRawAnswerLength ? answer[..MaxRawAnswerLength] : answer;
        }
        catch (Exception ex)
        {
            Logs.Warning($"EcomCraft prompt service failed: {ex.GetType().Name}");
            return "";
        }
    }
}
