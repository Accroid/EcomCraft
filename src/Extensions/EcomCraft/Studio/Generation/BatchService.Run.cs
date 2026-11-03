using System.IO;
using System.Text.RegularExpressions;
using EcomCraftStudio.Studio.Common;
using EcomCraftStudio.Studio.Export;
using EcomCraftStudio.Studio.PostProcess;
using EcomCraftStudio.Studio.Prompting;
using EcomCraftStudio.Studio.Setup;
using EcomCraftStudio.Studio.Storage;
using EcomCraftStudio.Studio.Templates;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using ISImage = SixLabors.ImageSharp.Image;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
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
        // A layout task first waits for its product's fusion picture (T98) — outside the per-person slot, so it never blocks the account's other tasks.
        string layoutBase = null;
        if (job.Source == SetRules.SourceLayout)
        {
            try
            {
                (layoutBase, string baseProblem) = await WaitForFusionBaseAsync(batch, job, cancel);
                if (baseProblem is not null)
                {
                    SetState(batch, job, "failed", baseProblem, "generation_failed");
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                SetState(batch, job, "cancelled", null, null);
                return;
            }
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
            JObject result = job.Source switch
            {
                SetRules.SourceCutout => await GenerateCutoutAsync(session, batch, job),
                SetRules.SourceCrop => await GenerateCropAsync(session, batch, job),
                SetRules.SourceLayout => GenerateLayout(session, batch, job, layoutBase),
                _ when batch.ReferenceEdit || job.Reference >= 0 => await GenerateFusionAsync(session, batch, job),
                _ => await GenerateAsync(session, batch, job)
            };
            lock (batch.Lock)
            {
                job.Timing = null;
            }
            if (result.ContainsKey("error"))
            {
                SetState(batch, job, "failed", result["error"]?.ToString(), result["error_id"]?.ToString());
                NoteFusionOutcome(batch, job);
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
                NoteFusionOutcome(batch, job);
                return;
            }
            SetState(batch, job, "done", null, null);
            NoteFusionOutcome(batch, job);
        }
        catch (Exception ex)
        {
            Logs.Warning($"EcomCraft batch task failed: {ex.GetType().Name}");
            SetState(batch, job, "failed", "任务没有完成，请重试。", "generation_uncertain");
            NoteFusionOutcome(batch, job);
        }
        finally
        {
            slot.Release();
        }
    }

    /// <summary>Builds the template request for one non-reference task (set, single, storyboard) and runs it through the normal generation path
    /// (synchronously, so results come back here). Reference and fusion tasks go through <see cref="GenerateFusionAsync"/> instead (T98).</summary>
    private static async Task<JObject> GenerateAsync(Session session, Batch batch, Job job)
    {
        Product product = batch.Products[job.Product];
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
        JObject payload = new()
        {
            ["source"] = "local",
            ["template_id"] = template.Template.Id,
            ["template_params"] = parameters,
            ["prompt"] = job.Shot >= 0 && job.Shot < batch.ShotPrompts.Count && batch.ShotPrompts[job.Shot].Length > 0
                ? string.Join(", ", new[] { batch.Prompt, batch.ShotPrompts[job.Shot] }.Where(part => !string.IsNullOrEmpty(part)))
                : batch.Prompt,
            ["negativeprompt"] = batch.Negative,
            ["images"] = batch.Candidates,
            ["placement"] = new JObject() { ["x"] = product.Placement.CenterX, ["y"] = product.Placement.CenterY, ["size"] = product.Placement.Size },
            ["platform"] = batch.Platform
        };
        if (job.OutputSize is not null || batch.OutputSize is not null)
        {
            payload["output_size"] = (job.OutputSize ?? batch.OutputSize).DeepClone();
        }
        GenerateOptions options = new();
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

    /// <summary>Runs one fusion task — the reference flows and the slot batches' fusion slots (T98) — through <see cref="FusionRoutes"/>, the one seam
    /// both routes live behind. The direct-fusion trial (F84) keeps its server-written prompt, its angles and its pixel budget; the composition route
    /// keeps its per-reference prompt, placement and product canvas. Everything else (sizes, candidates, the pinned server) is the same for both.</summary>
    private static async Task<JObject> GenerateFusionAsync(Session session, Batch batch, Job job)
    {
        Product product = batch.Products[job.Product];
        FusionRequest request = new()
        {
            TemplateId = job.Template,
            Negative = batch.Negative,
            Images = job.ImageCount ?? SetRules.JobImageCount(job.Source, batch.Candidates),
            Platform = batch.Platform,
            OutputSize = job.OutputSize ?? batch.OutputSize,
            Server = batch.Server
        };
        if (batch.ReferenceEdit)
        {
            List<string> angles = await PrepareEditInputsAsync(session, product);
            if (angles is null)
            {
                return StudioApi.Error(product.EditError ?? "商品图没有准备好。", "cutout_failed");
            }
            request.ReferenceImage = batch.ReferenceImages[job.Reference];
            request.ProductAngles = angles;
            request.EditResolution = batch.EditResolution;
            request.Prompt = PromptRules.ReferenceEditPrompt(angles.Count, ModelRecipeRules.EffectivePromptSyntax(ModelRecipes.Current().Recipe?.PromptSyntax));
            return await FusionRoutes.RunAsync(FusionRoutes.Edit, session, request);
        }
        (EcomTemplate Template, string WorkflowId, string ImageField) template = TemplateApi.RunnableTemplates(session).FirstOrDefault(item => item.Template.Id == job.Template);
        if (template.ImageField is not null)
        {
            request.ImageField = template.ImageField;
            string canvas = product.Image;
            if (template.Template.PreserveProduct)
            {
                canvas = await CutoutFor(product);
                if (canvas is null)
                {
                    return StudioApi.Error(product.CutoutError ?? "抠图没有完成。", "cutout_failed");
                }
            }
            request.ProductCanvas = canvas;
        }
        request.Placement = job.Reference >= 0 && job.Reference < batch.ReferencePlacements.Count && batch.ReferencePlacements[job.Reference] is Placement forReference
            ? forReference : product.Placement;
        request.Prompt = job.Reference >= 0 && job.Reference < batch.ReferencePrompts.Count ? batch.ReferencePrompts[job.Reference] : batch.Prompt;
        return await FusionRoutes.RunAsync(FusionRoutes.Composition, session, request);
    }

    /// <summary>One white-background slot task (T98): the product's cut-out on pure white, made locally like the cut-out-only main images (F22) — no
    /// generation, one deterministic picture however many candidates were asked for.</summary>
    private static async Task<JObject> GenerateCutoutAsync(Session session, Batch batch, Job job)
    {
        Product product = batch.Products[job.Product];
        string cutout = await CutoutFor(product);
        if (cutout is null)
        {
            return StudioApi.Error(product.CutoutError ?? "抠图没有完成。", "cutout_failed");
        }
        (int width, int height) = JobSize(batch, job, "white_main");
        JObject result = await Task.Run(() => CutoutOnly.Create(session.User, cutout, width, height, job.SlotKind));
        if (!result.ContainsKey("error"))
        {
            // The direct cut-out is also a candidate (F39): unpicked copies are cleaned after the retention time.
            CandidateCleanup.Register(session.User, result["images"] as JArray);
        }
        return result;
    }

    /// <summary>One detail slot task (T98): a real crop of the product's own photo — no generation — at the slot's aspect, enlarged to the slot's size.
    /// The candidates are different crop positions, starting centered; the photo is the inline photo's original or the saved product's main angle.</summary>
    private static async Task<JObject> GenerateCropAsync(Session session, Batch batch, Job job)
    {
        Product product = batch.Products[job.Product];
        string original = OriginalFor(session, product);
        if (original is null)
        {
            return StudioApi.Error("商品原图无法读取，无法裁切细节图。", "invalid_reference");
        }
        (int width, int height) = JobSize(batch, job, "detail");
        int count = job.ImageCount ?? SetRules.JobImageCount(job.Source, batch.Candidates);
        // Later tasks of the same slot (retries aside, "one more") continue the crop positions instead of repeating the first ones.
        int start;
        lock (batch.Lock)
        {
            start = batch.Jobs.Where(item => item.Id < job.Id && item.Product == job.Product && item.SlotId == job.SlotId).Sum(item => item.Images.Count);
        }
        JObject result = await Task.Run(() =>
        {
            try
            {
                int comma = original.IndexOf(',');
                using Image<Rgba32> photo = ISImage.Load<Rgba32>(Convert.FromBase64String(original[(comma + 1)..]));
                if (!SetRules.DetailZoomCrop(photo.Width, photo.Height, width, height, out int cropWidth, out int cropHeight))
                {
                    return StudioApi.Error("商品原图无法读取，无法裁切细节图。", "invalid_reference");
                }
                JArray images = [];
                for (int index = 0; index < count; index++)
                {
                    (double fx, double fy) = SetRules.CropShift(start + index);
                    int x = (int)Math.Round(fx * (photo.Width - cropWidth));
                    int y = (int)Math.Round(fy * (photo.Height - cropHeight));
                    using Image<Rgba32> crop = photo.Clone(context => context.Crop(new Rectangle(x, y, cropWidth, cropHeight)).Resize(width, height));
                    string url = SaveLocalWork(session.User, crop, "ecomcraft/detail-crop", new JObject()
                    {
                        ["ecomcraft_photo_crop"] = "true",
                        ["ecomcraft_template"] = job.SlotKind,
                        ["ecomcraft_execution_server_name"] = "平台本机（真实裁切）"
                    });
                    if (url is null)
                    {
                        return StudioApi.Error("细节图没有生成，请稍后重试。", "generation_failed");
                    }
                    images.Add(url);
                }
                return new JObject() { ["images"] = images, ["execution_server"] = "平台本机（真实裁切）" };
            }
            catch (Exception ex) when (ex is FormatException or UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
            {
                return StudioApi.Error("商品原图无法读取，无法裁切细节图。", "invalid_reference");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logs.Warning($"EcomCraft could not save a detail crop: {ex.Message}");
                return StudioApi.Error("细节图没有生成，请稍后重试。", "generation_failed");
            }
        });
        if (!result.ContainsKey("error"))
        {
            CandidateCleanup.Register(session.User, result["images"] as JArray);
        }
        return result;
    }

    /// <summary>One selling slot task (T98): the product's own selling info (T99) drawn by the selling-image service (F47) onto the product's finished fusion
    /// picture. The candidates are the service's layouts, starting where this task's number falls so remakes differ.</summary>
    private static JObject GenerateLayout(Session session, Batch batch, Job job, string baseUrl)
    {
        Product product = batch.Products[job.Product];
        if (product.ProductId is null)
        {
            return StudioApi.Error("这个商品还没有填写卖点信息，无法排版卖点图。", "invalid_selling_text");
        }
        (Products.ProductSpecs specs, string name) = Products.ProductLibrary.SellingInfoFor(session.User.UserID, product.ProductId, batch.Language);
        (string title, List<string> points) = SellingImageRules.FromSpecs(specs, name);
        if (SellingImageRules.Problem(title, points) is string problem)
        {
            return StudioApi.Error(problem, "invalid_selling_text");
        }
        string path = WorkSync.LocalPathFor(session.User, baseUrl, out _);
        if (path is null || !File.Exists(path))
        {
            return StudioApi.Error("融合结果无法读取，无法排版卖点图。", "invalid_reference");
        }
        int count = job.ImageCount ?? SetRules.JobImageCount(job.Source, batch.Candidates);
        JArray images = [];
        for (int index = 0; index < count; index++)
        {
            string layout = SellingImageRules.Layouts[(job.Id + index) % SellingImageRules.Layouts.Count];
            JObject rendered = SellingImageService.RenderFromWork(session.User, path, layout, title, points, SellingImageRules.DefaultAccent);
            if (rendered.ContainsKey("error"))
            {
                return rendered;
            }
            foreach (JToken image in rendered["images"] as JArray ?? new JArray())
            {
                images.Add(image);
            }
        }
        CandidateCleanup.Register(session.User, images);
        return new JObject() { ["images"] = images, ["execution_server"] = "平台本机（服务端排版）" };
    }

    /// <summary>The size a slot task renders at (T98): its own slot size when the batch chose none, else the platform's size for the kind, else 2000 square.</summary>
    private static (int Width, int Height) JobSize(Batch batch, Job job, string fallbackKind)
    {
        if (job.OutputSize is JObject size)
        {
            return (size.Value<int>("width"), size.Value<int>("height"));
        }
        (int Width, int Height)? slotSize = Export.ExportService.SlotSize(batch.Platform, fallbackKind);
        return slotSize ?? (2000, 2000);
    }

    /// <summary>Saves a locally made picture as a new PNG work with the given metadata markers (T98); its viewable URL, or null when it could not be written.</summary>
    private static string SaveLocalWork(User user, Image<Rgba32> picture, string folder, JObject markers)
    {
        JObject metadata = new() { ["sui_extra_data"] = markers };
        picture.Metadata.GetPngMetadata().TextData.Add(new SixLabors.ImageSharp.Formats.Png.Chunks.PngTextData("parameters", metadata.ToString(Newtonsoft.Json.Formatting.None), "", ""));
        string relative = $"{folder}/{DateTime.Now:yyyy-MM-dd}/{DateTime.Now:HHmmss}-{Guid.NewGuid().ToString("N")[..8]}.png";
        string path = Path.GetFullPath(UserImageHistoryHelper.GetRealPathFor(user, $"{user.OutputDirectory}/{relative}"));
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using (FileStream stream = File.Create(path))
        {
            picture.Save(stream, new PngEncoder());
        }
        return (Program.ServerSettings.Paths.AppendUserNameToOutputPath ? $"View/{user.UserID}/" : "Output/") + relative;
    }

    /// <summary>Waits for the product's first finished fusion picture (T98). Returns the picture's URL, or the plain-language reason it will not come;
    /// throws <see cref="OperationCanceledException"/> when the batch is cancelled while waiting.</summary>
    private static async Task<(string Url, string Problem)> WaitForFusionBaseAsync(Batch batch, Job job, CancellationToken cancel)
    {
        TaskCompletionSource<string> source;
        lock (batch.Lock)
        {
            source = batch.FusionBase.GetValueOrDefault(job.Product);
        }
        if (source is null)
        {
            return (null, "这个任务缺少融合结果，无法排版。");
        }
        string url = await source.Task.WaitAsync(cancel);
        return url is null ? (null, "首图没有生成，无法排版卖点图；请先重试首图，再重试这个任务。") : (url, null);
    }

    /// <summary>Hands a finished fusion task's pictures to the product's layout waiter (T98): the first task with pictures wins; when every fusion task of
    /// the product has ended without pictures, the waiter is released with nothing so its layout task fails with a plain reason.</summary>
    private static void NoteFusionOutcome(Batch batch, Job job)
    {
        if (job.Source != SetRules.SourceFusion)
        {
            return;
        }
        lock (batch.Lock)
        {
            if (!batch.FusionBase.TryGetValue(job.Product, out TaskCompletionSource<string> source) || source.Task.IsCompleted)
            {
                return;
            }
            Job withPictures = batch.Jobs.FirstOrDefault(item => item.Source == SetRules.SourceFusion && item.Product == job.Product
                && item.State == "done" && item.Images.Count > 0);
            if (withPictures is not null)
            {
                source.TrySetResult(withPictures.Images[0]);
                return;
            }
            bool pending = batch.Jobs.Any(item => item.Source == SetRules.SourceFusion && item.Product == job.Product
                && item.State is not ("done" or "failed" or "cancelled"));
            if (!pending)
            {
                source.TrySetResult(null);
            }
        }
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
