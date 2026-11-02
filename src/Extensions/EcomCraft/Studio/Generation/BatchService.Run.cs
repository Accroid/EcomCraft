using System.Text.RegularExpressions;
using EcomCraftStudio.Studio.Common;
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
        Product product = batch.Products[job.Product];
        if (batch.ReferenceEdit)
        {
            return await GenerateEditAsync(session, batch, job, product);
        }
        (EcomTemplate Template, string WorkflowId, string ImageField) template = TemplateApi.RunnableTemplates(session).FirstOrDefault(item => item.Template.Id == job.Template);
        if (template.Template is null)
        {
            return StudioApi.Error("这个模板暂时不可用：执行服务器可能离线，或模板已被下架。", "no_execution_server");
        }
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

    /// <summary>Runs one task of the direct-fusion trial (F84, T123): the reference and the product's angles go to the reference-edit workflow of the
    /// product's angle count, the prompt is written by the server, and the pixel budget is resolved for this server. Everything travels in the
    /// server-side options, so no request field can carry the images, the prompt tags or the workflow choice.</summary>
    private static async Task<JObject> GenerateEditAsync(Session session, Batch batch, Job job, Product product)
    {
        (EcomTemplate Template, string WorkflowId, string ImageField) template = TemplateApi.RunnableTemplates(session).FirstOrDefault(item => item.Template.Id == job.Template);
        if (template.Template is null)
        {
            return StudioApi.Error("直接融合试用版现在不可用：执行服务器可能离线，或工作流已被下架。", "no_execution_server");
        }
        List<string> angles = await PrepareEditInputsAsync(session, product);
        if (angles is null)
        {
            return StudioApi.Error(product.EditError ?? "商品图没有准备好。", "cutout_failed");
        }
        JObject serverParams = new() { [ReferenceEditRules.ReferenceParam] = batch.ReferenceImages[job.Reference] };
        for (int index = 0; index < angles.Count; index++)
        {
            serverParams[ReferenceEditRules.ProductParam(index)] = angles[index];
        }
        serverParams[ReferenceEditRules.ResolutionParam] = batch.EditResolution;
        GenerateOptions options = new()
        {
            RawPrompt = true,
            AllowImageTagPrompt = true,
            WorkflowOverride = StandardWorkflows.ReferenceEditId(angles.Count),
            ServerParams = serverParams
        };
        JObject payload = new()
        {
            ["source"] = "local",
            ["template_id"] = template.Template.Id,
            ["template_params"] = new JObject(),
            ["prompt"] = PromptRules.ReferenceEditPrompt(angles.Count),
            ["negativeprompt"] = batch.Negative,
            ["images"] = batch.Candidates,
            ["platform"] = batch.Platform
        };
        if (batch.OutputSize is not null)
        {
            payload["output_size"] = batch.OutputSize.DeepClone();
        }
        if (batch.Server is int server)
        {
            payload["execution_server_id"] = server;
        }
        return await StudioApi.GenerateInternal(session, payload, options);
    }

    /// <summary>The edit trial's per-angle product inputs (F84, T123), made once per product: each angle's cut-out (its confirmed cut-out, or one made now
    /// like <see cref="CutoutFor"/>) trimmed to the product and flattened onto white as PNG, because the edit model's LoadImage drops the alpha channel.
    /// Returns null and fills <see cref="Product.EditError"/> with a plain-language reason when an angle cannot be prepared.</summary>
    private static async Task<List<string>> PrepareEditInputsAsync(Session session, Product product)
    {
        await product.EditLock.WaitAsync();
        try
        {
            if (product.EditImages is not null || product.EditError is not null)
            {
                return product.EditImages;
            }
            List<string> inputs = [];
            if (product.ProductId is null)
            {
                // An inline photo: a single angle, cut out like every preserving template does.
                string cutout = await CutoutFor(product);
                if (cutout is null)
                {
                    product.EditError = product.CutoutError ?? "抠图没有完成。";
                    return null;
                }
                string inline = await WhiteInputAsync(product, cutout);
                if (inline is null)
                {
                    return null;
                }
                inputs.Add(inline);
            }
            else
            {
                foreach (string angleId in product.AngleIds.Take(3))
                {
                    // The confirmed cut-out when there is one; otherwise the angle's own photo, cut out now the same way as the main one.
                    (string saved, _, string problem) = Products.ProductLibrary.ForGeneration(session.User.UserID, product.ProductId, angleId, true);
                    if (saved is null)
                    {
                        (string original, _, string originalProblem) = Products.ProductLibrary.ForGeneration(session.User.UserID, product.ProductId, angleId, false);
                        if (original is null)
                        {
                            product.EditError = originalProblem ?? problem;
                            return null;
                        }
                        saved = await CutoutDataUrlAsync(product, original);
                        if (saved is null)
                        {
                            return null;
                        }
                    }
                    string input = await WhiteInputAsync(product, saved);
                    if (input is null)
                    {
                        return null;
                    }
                    inputs.Add(input);
                }
            }
            product.EditImages = inputs;
            return inputs;
        }
        finally
        {
            product.EditLock.Release();
        }
    }

    /// <summary>One cut-out data URL as the edit trial's white-flattened PNG input; null with <see cref="Product.EditError"/> set when it cannot be read.</summary>
    private static async Task<string> WhiteInputAsync(Product product, string dataUrl)
    {
        try
        {
            using (Image<Rgba32> image = ISImage.Load<Rgba32>(Convert.FromBase64String(dataUrl[(dataUrl.IndexOf(',') + 1)..])))
            {
                byte[] png = await Task.Run(() => ProductComposite.WhiteFlattened(image));
                return png is null ? null : ImageDataUrl.ToDataUrl(png, "image/png");
            }
        }
        catch (Exception ex) when (ex is FormatException or UnknownImageFormatException or InvalidImageContentException)
        {
            product.EditError = "无法读取商品图，请重新导出为 PNG、JPEG 或 WebP。";
            return null;
        }
    }

    /// <summary>A photo data URL cut out on an execution server when it is not transparent already (the per-angle form of <see cref="CutoutFor"/>, F84):
    /// the data URL of the PNG cut-out, or null with <see cref="Product.EditError"/> set to the cut-out service's plain-language error.</summary>
    private static async Task<string> CutoutDataUrlAsync(Product product, string photo)
    {
        byte[] bytes;
        try
        {
            bytes = ProductComposite.FitForWork(Convert.FromBase64String(photo[(photo.IndexOf(',') + 1)..]));
        }
        catch (FormatException)
        {
            product.EditError = "无法读取商品图，请重新导出为 PNG、JPEG 或 WebP。";
            return null;
        }
        if (bytes is null)
        {
            product.EditError = "商品图尺寸过大，请缩小到 1 亿像素以内。";
            return null;
        }
        using (Image<Rgba32> image = ISImage.Load<Rgba32>(bytes))
        {
            if (ProductComposite.HasTransparency(image))
            {
                return photo;
            }
        }
        (byte[] png, _, string error, _) = await CutoutService.CutoutAsync(bytes);
        if (png is null)
        {
            product.EditError = error;
            return null;
        }
        return ImageDataUrl.ToDataUrl(png, "image/png");
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
            product.Cutout = ImageDataUrl.ToDataUrl(png, "image/png");
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
