using System.IO;
using System.Net.Http;
using System.Text;
using EcomCraftStudio.Studio.Common;
using EcomCraftStudio.Studio.Generation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SwarmUI.Accounts;
using SwarmUI.Backends;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio.PostProcess;

/// <summary>Product cut-out for the confirmation step (F35). Runs a fixed, server-built four-node graph (load image → SwarmRemBg → mask to image → preview)
/// on a registered execution server chosen by Rule 1, then combines the returned mask with the user's own photo locally, so the product pixels stay the original ones.
/// Clients only send the photo; they never send a graph or choose a server here.</summary>
[API.APIClass("EcomCraft product cut-out: remove the background of a product photo on a registered execution server.")]
public static class CutoutService
{
    /// <summary>ComfyUI nodes the cut-out graph needs.</summary>
    private static readonly string[] RequiredNodes = ["SwarmLoadImageB64", "SwarmRemBg", "MaskToImage", "PreviewImage"];

    /// <summary>Longest time for one cut-out.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    /// <summary>Largest photo accepted, in bytes; its size in pixels is limited by <see cref="ProductComposite.FitForWork"/>.</summary>
    private const int MaxBytes = 5 * 1024 * 1024;

    /// <summary>What a server lacks to run the cut-out, or an empty list.</summary>
    private static List<string> Capability(BackendHandler.T2IBackendData data)
    {
        HashSet<string> nodes = (data.Backend as ComfyUIAPIAbstractBackend)?.NodeTypes ?? [];
        return [.. RequiredNodes.Where(node => !nodes.Contains(node)).Select(node => $"节点 {node}")];
    }

    [API.APIDescription("Remove the background of a product photo (PNG, JPEG or WebP data URL, at most 5 MB; it is turned upright by its EXIF orientation and the EXIF block, GPS included, is dropped first) on a registered execution server that has the SwarmUI nodes. Returns the cut-out as a PNG data URL with the original product pixels, plus edge_warnings codes about the rim's quality (white or dark fringe, stray islands, a product cut off by the canvas, jagged edges, much semi-transparency).", "cutout: \"data:image/png;base64,...\", execution_server: \"B\", edge_warnings: [\"fringe_white\"]")]
    public static async Task<JObject> EcomCraftStudioCutout(Session session, string product_image)
    {
        if (!StudioApi.CanView(session) || !session.User.HasPermission(Permissions.BasicImageGeneration))
        {
            return StudioApi.Error("此账号没有生成权限。", "bad_permissions");
        }
        int comma = product_image?.IndexOf(',') ?? -1;
        if (!ImageDataUrl.IsValid(product_image, ImageDataUrl.ImageDataFormats.PngJpegWebp, (MaxBytes + 2) / 3 * 4 + 40))
        {
            return StudioApi.Error("请选择不超过 5 MB 的 PNG、JPEG 或 WebP 图片。", "invalid_reference");
        }
        byte[] bytes;
        try
        {
            // A big photo is scaled down before cutting out (the cut-out comes back at the smaller size); only absurd sizes are refused.
            bytes = ProductComposite.FitForWork(Convert.FromBase64String(product_image[(comma + 1)..]));
            if (bytes is null)
            {
                return StudioApi.Error("商品图尺寸过大，请缩小到 1 亿像素以内。", "invalid_reference");
            }
        }
        catch (Exception ex) when (ex is FormatException or UnknownImageFormatException or InvalidImageContentException)
        {
            return StudioApi.Error("无法读取商品图，请重新导出为 PNG、JPEG 或 WebP。", "invalid_reference");
        }
        (byte[] cutout, string server, string error, string errorId) = await CutoutAsync(bytes);
        if (cutout is null)
        {
            return StudioApi.Error(error, errorId);
        }
        // Edge quality of the rim (F83) becomes plain-language notices at the upload confirmation; it never blocks anything.
        return new JObject() { ["cutout"] = ImageDataUrl.ToDataUrl(cutout, "image/png"), ["execution_server"] = server,
            ["edge_warnings"] = new JArray(ProductEdgeRules.TryAnalyze(cutout)) };
    }

    /// <summary>Removes the background of a photo on a registered execution server chosen by Rule 1. Returns the cut-out PNG and the server name,
    /// or null with a user-facing error and its code.</summary>
    internal static async Task<(byte[] Png, string Server, string Error, string ErrorId)> CutoutAsync(byte[] bytes)
    {
        SchedulerRules.Decision decision = await ExecutionScheduler.PlanAsync(Capability, null);
        if (!decision.Success)
        {
            return (null, null, "没有执行服务器能抠图：" + decision.ErrorText + " 也可以上传已经去掉背景的透明 PNG。", "no_execution_server");
        }
        BackendHandler.T2IBackendData data = ExecutionServers.All.FirstOrDefault(server => server.ID == decision.ChosenId);
        if (data?.Backend is not ComfyUIAPIAbstractBackend backend)
        {
            return (null, null, "所选执行服务器已不可用，请稍后重试。", "no_execution_server");
        }
        try
        {
            using CancellationTokenSource timeout = new(Timeout);
            byte[] mask = await RunAsync(backend.APIAddress, Convert.ToBase64String(bytes), timeout.Token);
            return (Combine(bytes, mask), decision.ChosenName, null, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or IOException or InvalidDataException
            or UnknownImageFormatException or InvalidImageContentException)
        {
            Logs.Warning($"EcomCraft cut-out failed on execution server {data.ID}: {ex.GetType().Name}");
            return (null, null, "抠图没有完成，请稍后重试，或上传已经去掉背景的透明 PNG。", "cutout_failed");
        }
    }

    /// <summary>Queues the cut-out graph on the server and returns the mask image it produces.</summary>
    private static async Task<byte[]> RunAsync(string api, string imageBase64, CancellationToken cancel)
    {
        JObject graph = new()
        {
            ["1"] = new JObject() { ["class_type"] = "SwarmLoadImageB64", ["inputs"] = new JObject() { ["image_base64"] = imageBase64 } },
            ["2"] = new JObject() { ["class_type"] = "SwarmRemBg", ["inputs"] = new JObject() { ["images"] = new JArray("1", 0) } },
            ["3"] = new JObject() { ["class_type"] = "MaskToImage", ["inputs"] = new JObject() { ["mask"] = new JArray("2", 1) } },
            ["4"] = new JObject() { ["class_type"] = "PreviewImage", ["inputs"] = new JObject() { ["images"] = new JArray("3", 0) } }
        };
        JObject body = new() { ["prompt"] = graph, ["client_id"] = Guid.NewGuid().ToString("N") };
        using HttpResponseMessage queued = await ComfyUIAPIAbstractBackend.HttpClient.PostAsync($"{api}/prompt",
            new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json"), cancel);
        queued.EnsureSuccessStatusCode();
        string promptId = JObject.Parse(await queued.Content.ReadAsStringAsync(cancel))["prompt_id"]?.ToString();
        if (string.IsNullOrEmpty(promptId))
        {
            throw new InvalidDataException("No prompt id.");
        }
        while (true)
        {
            await Task.Delay(500, cancel);
            using HttpResponseMessage history = await ComfyUIAPIAbstractBackend.HttpClient.GetAsync($"{api}/history/{Uri.EscapeDataString(promptId)}", cancel);
            history.EnsureSuccessStatusCode();
            JObject entry = JObject.Parse(await history.Content.ReadAsStringAsync(cancel))[promptId] as JObject;
            if (entry is null)
            {
                continue;
            }
            if (entry["status"]?["status_str"]?.ToString() == "error")
            {
                throw new InvalidDataException("Cut-out graph failed.");
            }
            JObject image = entry["outputs"]?["4"]?["images"]?.First as JObject;
            if (image is null)
            {
                if (entry["status"]?["completed"]?.Value<bool>() == true)
                {
                    throw new InvalidDataException("No cut-out output.");
                }
                continue;
            }
            string query = $"filename={Uri.EscapeDataString(image["filename"]?.ToString() ?? "")}&subfolder={Uri.EscapeDataString(image["subfolder"]?.ToString() ?? "")}&type={Uri.EscapeDataString(image["type"]?.ToString() ?? "temp")}";
            using HttpResponseMessage view = await ComfyUIAPIAbstractBackend.HttpClient.GetAsync($"{api}/view?{query}", cancel);
            view.EnsureSuccessStatusCode();
            return await view.Content.ReadAsByteArrayAsync(cancel);
        }
    }

    /// <summary>Puts the mask into the alpha of the user's own photo (see <see cref="CutoutMask.Combine"/>).</summary>
    public static byte[] Combine(byte[] photo, byte[] maskImage)
    {
        return CutoutMask.Combine(photo, maskImage);
    }
}
