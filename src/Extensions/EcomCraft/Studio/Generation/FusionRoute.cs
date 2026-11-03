using EcomCraftStudio.Studio.PostProcess;
using EcomCraftStudio.Studio.Templates;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio.Generation;

/// <summary>Everything one fusion image needs (T98): the caller (the batch) prepares the product's inputs and the final prompt, the route turns them into
/// a published-workflow request. The two routes today are the composition route (the reference's layout borrowed, the product pasted back, F59) and the
/// direct-fusion trial (the edit model, F84); when a new model route lands in P4 only a new implementation of <see cref="IFusionRoute"/> is added, the
/// batch and the slot expansion stay untouched.</summary>
internal sealed class FusionRequest
{
    /// <summary>The composition route's template ID (the platform's scene template).</summary>
    public string TemplateId;

    /// <summary>The template's product image parameter, or null when the template takes no photo.</summary>
    public string ImageField;

    /// <summary>The composition route's product input: its cut-out when the template preserves the product, else the photo.</summary>
    public string ProductCanvas;

    /// <summary>Where the product sits for the composition route.</summary>
    public Placement Placement;

    /// <summary>The final prompt: the reference's own prompt (composition) or the server-written edit prompt.</summary>
    public string Prompt;

    /// <summary>The user's excluded content.</summary>
    public string Negative;

    /// <summary>Images to make.</summary>
    public int Images;

    /// <summary>Target platform, so the request keeps the platform's rules.</summary>
    public string Platform;

    /// <summary>The output size chosen before generating, or null for the platform's slot size.</summary>
    public JObject OutputSize;

    /// <summary>Optional pinned execution server.</summary>
    public int? Server;

    /// <summary>The edit route's reference image.</summary>
    public string ReferenceImage;

    /// <summary>The edit route's white-flattened product angles.</summary>
    public List<string> ProductAngles;

    /// <summary>The edit trial's pixel budget resolved for this server.</summary>
    public int EditResolution;
}

/// <summary>One way a fusion image is made from a reference and a product (T98). Implementations live in this file; the batch only ever calls
/// <see cref="FusionRoutes.RunAsync"/>.</summary>
internal interface IFusionRoute
{
    /// <summary>The route's ID, as the batch names it: "composition" or "edit".</summary>
    string Id { get; }

    /// <summary>Runs the request and returns the generation-like result (images, execution_server) or the error.</summary>
    Task<JObject> GenerateAsync(Session session, FusionRequest request);
}

/// <summary>The composition route (F59, kept as the reference flow's default): the reference's described layout drives the platform's scene template,
/// the product's cut-out goes in as the product canvas and the finished picture has the original product pasted back.</summary>
internal sealed class CompositionFusionRoute : IFusionRoute
{
    /// <summary>The single instance; the route keeps no state.</summary>
    public static readonly CompositionFusionRoute Instance = new();

    /// <summary>The route's ID.</summary>
    public string Id => FusionRoutes.Composition;

    /// <summary>Builds the template request and runs it through the normal generation path, so it passes the same snapshot allowlist and scheduling as ever.</summary>
    public async Task<JObject> GenerateAsync(Session session, FusionRequest request)
    {
        (EcomTemplate Template, string WorkflowId, string ImageField) template = TemplateApi.RunnableTemplates(session)
            .FirstOrDefault(item => item.Template.Id == request.TemplateId);
        if (template.Template is null)
        {
            return StudioApi.Error("这个模板暂时不可用：执行服务器可能离线，或模板已被下架。", "no_execution_server");
        }
        JObject parameters = new();
        if (template.ImageField is not null)
        {
            if (string.IsNullOrEmpty(request.ProductCanvas))
            {
                return StudioApi.Error("抠图没有完成。", "cutout_failed");
            }
            parameters[template.ImageField] = request.ProductCanvas;
        }
        JObject payload = new()
        {
            ["source"] = "local",
            ["template_id"] = template.Template.Id,
            ["template_params"] = parameters,
            ["prompt"] = request.Prompt,
            ["negativeprompt"] = request.Negative,
            ["images"] = request.Images,
            ["placement"] = new JObject() { ["x"] = request.Placement.CenterX, ["y"] = request.Placement.CenterY, ["size"] = request.Placement.Size },
            ["platform"] = request.Platform
        };
        if (request.OutputSize is not null)
        {
            payload["output_size"] = request.OutputSize.DeepClone();
        }
        if (request.Server is int server)
        {
            payload["execution_server_id"] = server;
        }
        // A reference task's prompt is final and user-edited (F59): it is sent as is, without the template's own fragments.
        GenerateOptions options = new() { RawPrompt = true };
        return await StudioApi.GenerateInternal(session, payload, options);
    }
}

/// <summary>The direct-fusion trial (F84, T123): the reference and the product's white-flattened angles go to the reference-edit workflow of the product's
/// angle count, the prompt is written by the server with the recipe's image tags, and the result is the model's own output (not pasted back).</summary>
internal sealed class EditFusionRoute : IFusionRoute
{
    /// <summary>The single instance; the route keeps no state.</summary>
    public static readonly EditFusionRoute Instance = new();

    /// <summary>The route's ID.</summary>
    public string Id => FusionRoutes.Edit;

    /// <summary>Builds the edit request; the images and the pixel budget ride in the server-side options, so no request field can carry them (A34).</summary>
    public async Task<JObject> GenerateAsync(Session session, FusionRequest request)
    {
        (EcomTemplate Template, string WorkflowId, string ImageField) template = TemplateApi.RunnableTemplates(session)
            .FirstOrDefault(item => item.Template.Id == request.TemplateId);
        if (template.Template is null)
        {
            return StudioApi.Error("直接融合试用版现在不可用：执行服务器可能离线，或工作流已被下架。", "no_execution_server");
        }
        List<string> angles = request.ProductAngles ?? [];
        if (angles.Count == 0 || string.IsNullOrEmpty(request.ReferenceImage))
        {
            return StudioApi.Error("商品图没有准备好。", "cutout_failed");
        }
        JObject serverParams = new() { [ReferenceEditRules.ReferenceParam] = request.ReferenceImage };
        for (int index = 0; index < angles.Count; index++)
        {
            serverParams[ReferenceEditRules.ProductParam(index)] = angles[index];
        }
        serverParams[ReferenceEditRules.ResolutionParam] = request.EditResolution;
        GenerateOptions options = new()
        {
            RawPrompt = true,
            AllowImageTagPrompt = true,
            WorkflowOverride = StandardWorkflows.ReferenceEditId(angles.Count),
            ServerParams = serverParams,
            // T98: the reference is center-cropped to the output aspect instead of stretched, and the product's angles keep their own size, so the
            // product is not distorted on its way into the edit model.
            ImageResizeCrops = [ReferenceEditRules.ReferenceParam],
            ImageResizeSkips = [.. Enumerable.Range(0, angles.Count).Select(ReferenceEditRules.ProductParam)]
        };
        JObject payload = new()
        {
            ["source"] = "local",
            ["template_id"] = template.Template.Id,
            ["template_params"] = new JObject(),
            ["prompt"] = request.Prompt,
            ["negativeprompt"] = request.Negative,
            ["images"] = request.Images,
            ["platform"] = request.Platform
        };
        if (request.OutputSize is not null)
        {
            payload["output_size"] = request.OutputSize.DeepClone();
        }
        if (request.Server is int server)
        {
            payload["execution_server_id"] = server;
        }
        return await StudioApi.GenerateInternal(session, payload, options);
    }
}

/// <summary>The seam every fusion image goes through (T98): the batch picks a route by ID and nothing else knows which model path runs.</summary>
internal static class FusionRoutes
{
    /// <summary>The composition route's ID (borrowing the reference's layout, F59).</summary>
    public const string Composition = "composition";

    /// <summary>The direct-fusion trial's ID (F84).</summary>
    public const string Edit = "edit";

    /// <summary>The route with the given ID, or null when the ID names none.</summary>
    public static IFusionRoute Find(string id) => id == Edit ? EditFusionRoute.Instance : id == Composition ? CompositionFusionRoute.Instance : null;

    /// <summary>Runs the request on the named route.</summary>
    public static Task<JObject> RunAsync(string route, Session session, FusionRequest request)
    {
        IFusionRoute found = Find(route);
        return found is null ? Task.FromResult(StudioApi.Error("未知的参考图模式。", "invalid_parameters")) : found.GenerateAsync(session, request);
    }
}
