using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using EcomCraftStudio.MagicPrompt.WebAPI;
using EcomCraftStudio.Studio.Generation;
using EcomCraftStudio.Studio.Storage;
using EcomCraftStudio.Studio.Templates;
using EcomCraftStudio.Studio.Prompting;
using EcomCraftStudio.Studio.Setup;
using FreneticUtilities.FreneticExtensions;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Backends;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Core;
using SwarmUI.Text2Image;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio;


/// <summary>Published-workflow catalog, exposed inputs and lookups. Part of the StudioApi partial class (R07).</summary>
public static partial class StudioApi
{
    /// <summary>Allows the stored-workflow selector only for a published workflow in an EcomCraft-local request.</summary>
    public static bool PublishedWorkflowPermissionOverride(string parameterName, string value, T2IParamInput input)
    {
        if (!string.Equals(parameterName, "comfyuicustomworkflow", StringComparison.OrdinalIgnoreCase)
            || input?.SourceSession?.User is not User user
            || !user.HasPermission(EcomCraft.PermUsePublishedWorkflows)
            || !input.ExtraMeta.TryGetValue(ExecutionScopeKey, out object scope) || scope?.ToString() != ExecutionScopeValue
            || !input.ExtraMeta.TryGetValue(PublishedWorkflowScopeKey, out object requested) || requested?.ToString() != value
            || !WorkflowReview.NotOthersOwn(value, user.UserID))
        {
            return false;
        }
        return TryGetPublishedWorkflow(value, out _, out _, out _);
    }

    /// <summary>Provides the saved parameter schema only for an approved workflow; arbitrary client-supplied schemas are ignored.</summary>
    public static T2IParamType PublishedWorkflowDynamicParamProvider(string name, T2IParamInput context)
    {
        if (context?.SourceSession?.User is not User user
            || user.HasPermission(ComfyUIBackendExtension.PermDynamicCustomWorkflows)
            || !user.HasPermission(EcomCraft.PermUsePublishedWorkflows)
            || !context.ExtraMeta.TryGetValue(ExecutionScopeKey, out object scope) || scope?.ToString() != ExecutionScopeValue
            || !context.ExtraMeta.TryGetValue(PublishedWorkflowScopeKey, out object requested)
            || !WorkflowReview.NotOthersOwn(requested?.ToString(), user.UserID)
            || !TryGetPublishedWorkflow(requested?.ToString(), out _, out JObject metadata, out JObject savedValues))
        {
            return null;
        }
        if (string.Equals(name, "comfyworkflowparammetadata", StringComparison.OrdinalIgnoreCase))
        {
            return ComfyUIBackendExtension.FakeParameterMetadata;
        }
        if (WorkflowDeniedParameterIds.Contains(name) || WorkflowFormParameterIds.Contains(name))
        {
            return null;
        }
        JArray exposedFields = PublishedWorkflowInputs(context.SourceSession, metadata, savedValues, out bool supported);
        if (!supported || !exposedFields.OfType<JObject>().Any(field =>
            string.Equals(field["id"]?.ToString(), name, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }
        if (metadata.TryGetValue(name, out JToken parameter) && parameter is JObject parameterObject)
        {
            return T2IParamType.FromNet(parameterObject);
        }
        return null;
    }

    /// <summary>Lists templates that at least one ready execution server can run, and collapses identical native graphs without removing stored workflows.</summary>
    /// <summary>The published workflows the account can run on an online server now, as the workspace catalog lists them.</summary>
    internal static JArray PublishedWorkflowsFor(Session session) => PublishedWorkflowCatalog(session);

    private static JArray PublishedWorkflowCatalog(Session session)
    {
        JArray published = new();
        if (session?.User is null || !session.User.HasPermission(EcomCraft.PermUsePublishedWorkflows))
        {
            return published;
        }
        List<BackendHandler.T2IBackendData> servers = [.. ExecutionServers.All.Where(ExecutionServers.IsReady)];
        if (servers.Count == 0)
        {
            return published;
        }
        WorkflowCatalogFilter filter = new(servers.Select(ExecutionServers.Schema));
        HashSet<string> modelKinds = new(LocalModels(session).Select(Kind).Where(kind => kind is not null));
        bool adminSeesChanges = session.User.HasPermission(Permissions.EditServerSettings);
        foreach (string name in ComfyUIBackendExtension.CustomWorkflows.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            // Another account's own upload (F60) never appears here.
            if (!WorkflowReview.NotOthersOwn(name, session.User.UserID)
                || !TryGetPublishedWorkflow(name, out ComfyUIBackendExtension.ComfyCustomWorkflow workflow, out JObject metadata, out JObject savedValues))
            {
                continue;
            }
            JArray fields = PublishedWorkflowInputs(session, metadata, savedValues, out bool supported);
            if (!supported)
            {
                continue;
            }
            bool own = WorkflowReview.IsOwnBy(workflow.Name, session.User.UserID);
            bool native = own || WorkflowReview.IsPublished(workflow.Name);
            string kind = InferWorkflowKind(workflow);
            if (kind == "video" && !session.User.HasPermission(Permissions.ParamVideo))
            {
                continue;
            }
            if (native)
            {
                // The standard scene and detail workflows share one blueprint, so the duplicate check would hide one of them.
                if (!filter.Include(ParseWorkflowObject(workflow.Prompt), metadata, savedValues, !StandardWorkflows.IsStandard(workflow.Name)))
                {
                    continue;
                }
            }
            else if (!modelKinds.Contains(kind))
            {
                continue;
            }
            published.Add(new JObject()
            {
                ["id"] = workflow.Name,
                ["name"] = own ? WorkflowReview.Get(workflow.Name)?.Name ?? workflow.Name : workflow.Name,
                ["own"] = own,
                ["kind"] = kind,
                ["description"] = workflow.Description ?? "",
                ["inputs"] = fields,
                ["native"] = native,
                // Administrators only (F62/A13): whether the source on its server changed after this snapshot was published,
                // so the page can point at the review instead of silently showing the old parameters.
                ["source_changed"] = adminSeesChanges && SourceChanged(workflow.Name),
                ["defaults"] = native ? NativeWorkflowParameters.FormDefaults(metadata, savedValues) : null
            });
        }
        return published;
    }

    /// <summary>Whether the discovered source of a published workflow changed since its snapshot (mirrors the review's "rereview" check).
    /// Own uploads and system-generated workflows have no editable source and never count as changed.</summary>
    private static bool SourceChanged(string name)
    {
        WorkflowDiscovery.Entry entry = WorkflowDiscovery.Find(name);
        if (entry?.Workflow is null)
        {
            return false;
        }
        WorkflowReview.Record record = WorkflowReview.Get(name);
        return record?.Snapshot is not null && entry.Fingerprint != record.Snapshot.Fingerprint
            && entry.Fingerprint != record.RejectedFingerprint;
    }

    private static JArray PublishedWorkflowInputs(Session session, JObject metadata, JObject savedValues, out bool supported)
    {
        JArray fields = new();
        supported = true;
        List<JObject> onlineSchemas = [.. ExecutionServers.All.Where(ExecutionServers.IsReady).Select(ExecutionServers.Schema).Where(schema => schema is not null)];
        foreach (JProperty property in metadata.Properties())
        {
            if (property.Value is not JObject definition || WorkflowDeniedParameterIds.Contains(property.Name)
                || WorkflowFormParameterIds.Contains(property.Name))
            {
                continue;
            }
            if (definition["visible"] is not null && bool.TryParse(definition["visible"].ToString(), out bool visible) && !visible)
            {
                // A hidden model stays fixed, so an account that may not use it cannot run the workflow at all.
                if (definition.Value<bool?>("native_model") == true
                    && !session.User.IsAllowedModel((savedValues[property.Name] ?? definition["default"])?.ToString() ?? ""))
                {
                    supported = false;
                    return new JArray();
                }
                continue;
            }
            string type = definition["type"]?.ToString()?.ToLowerInvariant() ?? "";
            if (!SupportedWorkflowInputTypes.Contains(type))
            {
                continue; // Keep fixed/hidden values from the saved template, but never expose unknown input types to the browser.
            }
            if (type == "dropdown")
            {
                if (definition["values"] is not JArray dropdownValues || dropdownValues.Count == 0)
                {
                    supported = false;
                    return fields;
                }
            }
            JToken defaultValue = savedValues[property.Name]?.DeepClone() ?? definition["default"]?.DeepClone();
            if (type == "image")
            {
                // Never disclose a workflow owner's default image data or their private output path to other users.
                defaultValue = new JValue("");
            }
            JObject field = new()
            {
                ["id"] = property.Name,
                ["name"] = definition["name"]?.ToString() ?? property.Name,
                ["type"] = type,
                ["description"] = definition["description"]?.ToString() ?? "",
                ["default"] = defaultValue,
                ["min"] = definition["min"]?.DeepClone() ?? new JValue(0),
                ["max"] = definition["max"]?.DeepClone() ?? new JValue(0),
                ["step"] = definition["step"]?.DeepClone() ?? new JValue(1),
                ["form"] = definition["form"]?.DeepClone(),
                ["subtype"] = definition["subtype"]?.DeepClone()
            };
            if (type == "model")
            {
                string subtype = definition["subtype"]?.ToString() ?? "Stable-Diffusion";
                if (Program.T2IModelSets.TryGetValue(subtype, out T2IModelHandler handler))
                {
                    List<string> allowedModels = handler.ListModelNamesFor(session).Where(model => model != "(None)").ToList();
                    if (allowedModels.Count == 0)
                    {
                        supported = false;
                        return fields;
                    }
                    field["values"] = new JArray(allowedModels);
                }
                else
                {
                    supported = false;
                    return fields;
                }
            }
            else if (definition["values"] is not null)
            {
                if (definition.Value<bool?>("native_model") == true)
                {
                    // R4.3: only models the account may use and at least one online execution server has.
                    HashSet<string> offered = NativeWorkflowParameters.OfferedChoices(definition, onlineSchemas);
                    field["values"] = new JArray(((JArray)definition["values"]).Where(value => session.User.IsAllowedModel(value.ToString())
                        && (offered is null || offered.Contains(value.ToString()))));
                }
                else
                {
                    field["values"] = definition["values"].DeepClone();
                }
                if (definition.Value<bool?>("native_model") == true && !session.User.IsAllowedModel(defaultValue?.ToString() ?? ""))
                {
                    supported = false;
                    return new JArray();
                }
            }
            if (type == "dropdown" && definition.Value<bool?>("native_model") == true
                && (definition["native_input"]?.ToString() ?? "") is "unet_name" or "ckpt_name"
                && field["values"] is JArray finalValues)
            {
                // Steps and cfg follow the model's recipe when the account switches models (F86); numbers only, nothing else leaves.
                JObject recipeSampling = RecipeSamplingFor(finalValues);
                if (recipeSampling.Count > 0)
                {
                    field["recipe_sampling"] = recipeSampling;
                }
            }
            fields.Add(field);
        }
        return fields;
    }

    /// <summary>The sampling each recipe's main-model file declares, keyed by file name and restricted to the dropdown's own values: the page
    /// follows these numbers when the account switches models (F86). Numbers and nothing else — no sources, addresses or checksums leave.</summary>
    private static JObject RecipeSamplingFor(JArray modelValues)
    {
        JObject map = new();
        HashSet<string> offered = new(modelValues.Select(value => value?.ToString() ?? ""), StringComparer.OrdinalIgnoreCase);
        foreach (ModelRecipeRules.Recipe recipe in ModelRecipes.Load())
        {
            foreach (ModelRecipeRules.Variant variant in recipe.Variants)
            {
                ModelRecipeRules.FileSpec unet = ModelRecipeRules.FileFor(variant, ModelRecipeRules.RoleUnet);
                if (unet is null || !offered.Contains(unet.Filename) || map.ContainsKey(unet.Filename))
                {
                    continue;
                }
                // A recipe without a step count (the fixed sigma-table models, T118b) only links cfg; the page leaves steps alone.
                JObject entry = new() { ["cfg"] = recipe.Sampling.Cfg.Default };
                if (recipe.Sampling.Steps is int steps)
                {
                    entry["steps"] = steps;
                }
                map[unet.Filename] = entry;
            }
        }
        return map;
    }

    private static string InferWorkflowKind(ComfyUIBackendExtension.ComfyCustomWorkflow workflow) => InferWorkflowKind(workflow?.Prompt);

    /// <summary>"video" when the executable graph contains a video node, otherwise "image".</summary>
    internal static string InferWorkflowKind(string prompt)
    {
        JObject graph = ParseWorkflowObject(prompt);
        bool containsVideoNode = graph?.Properties().Any(property =>
        {
            string classType = property.Value is JObject node ? node["class_type"]?.ToString() ?? "" : "";
            return classType.Contains("video", StringComparison.OrdinalIgnoreCase)
                || classType.Contains("VHS_", StringComparison.OrdinalIgnoreCase)
                || classType.Contains("AnimateDiff", StringComparison.OrdinalIgnoreCase);
        }) == true;
        return containsVideoNode ? "video" : "image";
    }

    private static bool TryGetPublishedWorkflow(string name, out ComfyUIBackendExtension.ComfyCustomWorkflow workflow,
        out JObject metadata, out JObject savedValues)
    {
        workflow = null;
        metadata = null;
        savedValues = new JObject();
        if (string.IsNullOrWhiteSpace(name) || !ComfyUIBackendExtension.CustomWorkflows.ContainsKey(name))
        {
            return false;
        }
        workflow = ComfyUIBackendExtension.GetWorkflowByName(name);
        if (workflow is null || !workflow.EnableInSimple || string.IsNullOrWhiteSpace(workflow.Prompt))
        {
            workflow = null;
            return false;
        }
        metadata = ParseWorkflowObject(workflow.CustomParams);
        savedValues = ParseWorkflowObject(workflow.ParamValues) ?? new JObject();
        if (metadata is null || ParseWorkflowObject(workflow.Prompt) is null)
        {
            workflow = null;
            return false;
        }
        return true;
    }

    /// <summary>Parses stored workflow JSON; null for empty or invalid text.</summary>
    internal static JObject ParseWorkflowObject(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            return JToken.Parse(json) as JObject;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
