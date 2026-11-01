using System.Text.RegularExpressions;
using EcomCraftStudio.Studio.Export;
using EcomCraftStudio.Studio.PostProcess;
using EcomCraftStudio.Studio.Prompting;
using EcomCraftStudio.Studio.Templates;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using ISImage = SixLabors.ImageSharp.Image;
using SixLabors.ImageSharp.PixelFormats;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio.Generation;

/// <summary>Running one batch task: the per-person slot, the template request and the product cut-out. Part of the BatchService partial class (R10).</summary>
public static partial class BatchService
{
    /// <summary>Runs one task once a slot of its owner is free.</summary>
    private static async Task RunJobAsync(Session session, Batch batch, Job job)
    {
        SemaphoreSlim slot = Slots.GetOrAdd(batch.UserId, _ => new SemaphoreSlim(SetRules.Concurrency(session.User.CalcMaxT2ISimultaneous)));
        CancellationToken cancel;
        lock (batch.Lock)
        {
            cancel = batch.Cancel.Token;
        }
        try
        {
            await slot.WaitAsync(cancel);
        }
        catch (OperationCanceledException)
        {
            SetState(batch, job, "cancelled", null, null);
            return;
        }
        try
        {
            if (cancel.IsCancellationRequested)
            {
                SetState(batch, job, "cancelled", null, null);
                return;
            }
            SetState(batch, job, "running", null, null);
            // This task's own async flow learns the scheduled generation, so the status can show how long it still takes (F70).
            TaskTiming.Observer.Value = timing =>
            {
                lock (batch.Lock)
                {
                    job.Timing = timing;
                }
            };
            JObject result = await GenerateAsync(session, batch, job);
            lock (batch.Lock)
            {
                job.Timing = null;
            }
            if (result.ContainsKey("error"))
            {
                SetState(batch, job, "failed", result["error"]?.ToString(), result["error_id"]?.ToString());
                return;
            }
            lock (batch.Lock)
            {
                job.Server = result["execution_server"]?.ToString();
                job.Images = [.. (result["images"] as JArray ?? new JArray()).Select(image => image.Type == JTokenType.String ? image.ToString() : image["image"]?.ToString())
                    .Where(url => url is not null && (url.StartsWith("View/", StringComparison.Ordinal) || url.StartsWith("Output/", StringComparison.Ordinal)))];
            }
            // The engine can end without an error yet without images (an image task that threw is only logged); that is a failure, not a finished task.
            if (job.Images.Count == 0)
            {
                Logs.Warning($"EcomCraft batch task ended without images (template {job.Template}).");
                SetState(batch, job, "failed", "执行服务器没有返回图片，请重试；仍然失败请把服务器日志发给管理员。", "generation_failed");
                return;
            }
            SetState(batch, job, "done", null, null);
        }
        catch (Exception ex)
        {
            Logs.Warning($"EcomCraft batch task failed: {ex.GetType().Name}");
            SetState(batch, job, "failed", "任务没有完成，请重试。", "generation_uncertain");
        }
        finally
        {
            slot.Release();
        }
    }

    /// <summary>Builds the template request for one task and runs it through the normal generation path (synchronously, so results come back here).</summary>
    private static async Task<JObject> GenerateAsync(Session session, Batch batch, Job job)
    {
        (EcomTemplate Template, string WorkflowId, string ImageField) template = TemplateApi.RunnableTemplates(session).FirstOrDefault(item => item.Template.Id == job.Template);
        if (template.Template is null)
        {
            return StudioApi.Error("这个模板暂时不可用：执行服务器可能离线，或模板已被下架。", "no_execution_server");
        }
        Product product = batch.Products[job.Product];
        JObject parameters = new();
        if (template.ImageField is not null)
        {
            string image = product.Image;
            if (template.Template.PreserveProduct)
            {
                image = await CutoutFor(product);
                if (image is null)
                {
                    return StudioApi.Error(product.CutoutError ?? "抠图没有完成。", "cutout_failed");
                }
            }
            parameters[template.ImageField] = image;
        }
        Placement placement = job.Reference >= 0 && job.Reference < batch.ReferencePlacements.Count && batch.ReferencePlacements[job.Reference] is Placement forReference
            ? forReference : product.Placement;
        JObject payload = new()
        {
            ["source"] = "local",
            ["template_id"] = template.Template.Id,
            ["template_params"] = parameters,
            ["prompt"] = job.Reference >= 0 && job.Reference < batch.ReferencePrompts.Count ? batch.ReferencePrompts[job.Reference]
                : job.Shot >= 0 && job.Shot < batch.ShotPrompts.Count && batch.ShotPrompts[job.Shot].Length > 0
                ? string.Join(", ", new[] { batch.Prompt, batch.ShotPrompts[job.Shot] }.Where(part => !string.IsNullOrEmpty(part)))
                : batch.Prompt,
            ["negativeprompt"] = batch.Negative,
            ["images"] = batch.Candidates,
            ["placement"] = new JObject() { ["x"] = placement.CenterX, ["y"] = placement.CenterY, ["size"] = placement.Size },
            ["platform"] = batch.Platform
        };
        if (batch.OutputSize is not null)
        {
            payload["output_size"] = batch.OutputSize.DeepClone();
        }
        // A reference task's prompt is final and user-edited (F59): it is sent as is, without the template's own fragments (R08: server-side option, not a request field).
        GenerateOptions options = new() { RawPrompt = job.Reference >= 0 };
        if (batch.Server is int server)
        {
            payload["execution_server_id"] = server;
        }
        if (batch.BrandKitId is not null)
        {
            payload["brand_kit_id"] = batch.BrandKitId;
        }
        return await StudioApi.GenerateInternal(session, payload, options);
    }

    /// <summary>The product's cut-out: the photo itself when it is already transparent, otherwise made once on an execution server. Null when it failed.</summary>
    private static async Task<string> CutoutFor(Product product)
    {
        await product.CutoutLock.WaitAsync();
        try
        {
            if (product.Cutout is not null || product.CutoutError is not null)
            {
                return product.Cutout;
            }
            byte[] bytes = ProductComposite.FitForWork(Convert.FromBase64String(product.Image[(product.Image.IndexOf(',') + 1)..]));
            if (bytes is null)
            {
                product.CutoutError = "商品图尺寸过大，请缩小到 1 亿像素以内。";
                return null;
            }
            using (Image<Rgba32> image = ISImage.Load<Rgba32>(bytes))
            {
                if (ProductComposite.HasTransparency(image))
                {
                    product.Cutout = product.Image;
                    return product.Cutout;
                }
            }
            (byte[] png, _, string error, _) = await CutoutService.CutoutAsync(bytes);
            if (png is null)
            {
                product.CutoutError = error;
                return null;
            }
            product.Cutout = "data:image/png;base64," + Convert.ToBase64String(png);
            return product.Cutout;
        }
        catch (Exception ex) when (ex is FormatException or UnknownImageFormatException or InvalidImageContentException)
        {
            product.CutoutError = "无法读取商品图，请重新导出为 PNG、JPEG 或 WebP。";
            return null;
        }
        finally
        {
            product.CutoutLock.Release();
        }
    }

    /// <summary>Updates a task under the batch lock.</summary>
    private static void SetState(Batch batch, Job job, string state, string error, string errorId)
    {
        lock (batch.Lock)
        {
            job.State = state;
            job.Error = error;
            job.ErrorId = errorId;
        }
    }
}
