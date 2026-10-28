using System.IO;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Backends;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio.Templates;

/// <summary>Template management for reviewers: list every template with the reasons it cannot run, and add, replace or remove administrator templates.
/// Built-in templates are read-only here. Templates may only reference published snapshots and their whitelisted parameters.</summary>
[API.APIClass("EcomCraft template management: list templates with validation results, save or delete administrator templates.")]
public static class TemplateAdminApi
{
    /// <summary>Largest example image accepted.</summary>
    private const int MaxImageBytes = 2 * 1024 * 1024;

    /// <summary>Whether the session may manage templates.</summary>
    private static bool CanManage(Session session) => StudioApi.CanView(session) && session.User.HasPermission(EcomCraft.PermReviewWorkflows);

    [API.APIDescription("List every template with its source, whether it can run now and why not. Requires the review permission.", "templates: [{ id: \"white-main\", valid: true, problems: [] }]")]
    public static Task<JObject> EcomCraftTemplateAdminList(Session session)
    {
        if (!CanManage(session))
        {
            return Task.FromResult(StudioApi.Error("此账号没有管理模板的权限。", "bad_permissions"));
        }
        JArray items = new();
        foreach (TemplateStore.Entry entry in TemplateStore.All())
        {
            List<string> problems = TemplateStore.Problems(entry);
            items.Add(new JObject()
            {
                ["id"] = entry.Id,
                ["source"] = entry.Source,
                ["name"] = entry.Template is null ? entry.Id : EcomTemplate.Pick(entry.Template.Name, "zh-CN"),
                ["enabled"] = entry.Template?.Enabled ?? false,
                ["valid"] = problems.Count == 0,
                ["problems"] = new JArray(problems),
                ["template"] = entry.Raw?.DeepClone()
            });
        }
        return Task.FromResult(new JObject() { ["templates"] = items });
    }

    [API.APIDescription("List the models and nodes each template needs, each model's commercial-use license status, and which execution servers lack them. Servers are given by ID and name only. Requires the review permission.", "servers: [{ id: 0, name: \"本机 #0\", checked: true }], templates: [{ id: \"scene\", models: [{ name, folder, commercial, missing_on: [0] }], nodes: [{ name, missing_on: [] }] }]")]
    public static Task<JObject> EcomCraftTemplateRequirements(Session session)
    {
        if (!CanManage(session))
        {
            return Task.FromResult(StudioApi.Error("此账号没有管理模板的权限。", "bad_permissions"));
        }
        JArray servers = new();
        List<(int Id, HashSet<string> Models, HashSet<string> Nodes)> inventories = [];
        foreach (BackendHandler.T2IBackendData data in ExecutionServers.All)
        {
            JObject schema = ExecutionServers.Schema(data);
            servers.Add(new JObject() { ["id"] = data.ID, ["name"] = ExecutionServers.DisplayName(data), ["checked"] = schema is not null });
            if (schema is not null)
            {
                inventories.Add((data.ID, RequirementRules.OfferedModels(schema), (data.Backend as ComfyUIAPIAbstractBackend)?.NodeTypes ?? []));
            }
        }
        JArray templates = new();
        foreach (TemplateStore.Entry entry in TemplateStore.All().Where(entry => entry.Template is not null))
        {
            JArray models = new();
            foreach (ModelRequirement model in entry.Template.RequiredModels)
            {
                JArray missing = new(inventories.Where(server => RequirementRules.MissingModels([model], server.Models).Count > 0).Select(server => server.Id));
                models.Add(new JObject()
                {
                    ["name"] = model.Name,
                    ["folder"] = model.Folder,
                    ["commercial"] = model.Commercial,
                    ["license_url"] = model.LicenseUrl,
                    ["download_url"] = model.DownloadUrl,
                    ["missing_on"] = missing
                });
            }
            JArray nodes = new();
            foreach (string node in entry.Template.RequiredNodes)
            {
                nodes.Add(new JObject() { ["name"] = node, ["missing_on"] = new JArray(inventories.Where(server => !server.Nodes.Contains(node)).Select(server => server.Id)) });
            }
            templates.Add(new JObject()
            {
                ["id"] = entry.Id,
                ["source"] = entry.Source,
                ["name"] = EcomTemplate.Pick(entry.Template.Name, "zh-CN"),
                ["models"] = models,
                ["nodes"] = nodes
            });
        }
        return Task.FromResult(new JObject() { ["servers"] = servers, ["templates"] = templates });
    }

    [API.APIDescription("Add or replace an administrator template. The template must pass structure checks and reference a published snapshot with whitelisted parameters; example_image_data may carry a PNG, JPEG or WebP data URL. Requires the review permission. rawInput is the root request JSON.", "success: true")]
    public static Task<JObject> EcomCraftTemplateSave(Session session, JObject rawInput)
    {
        if (!CanManage(session))
        {
            return Task.FromResult(StudioApi.Error("此账号没有管理模板的权限。", "bad_permissions"));
        }
        if (rawInput["template"] is not JObject template)
        {
            return Task.FromResult(StudioApi.Error("缺少模板内容。", "invalid_template"));
        }
        template = (JObject)template.DeepClone();
        string imageData = rawInput["example_image_data"]?.Type == JTokenType.String ? rawInput.Value<string>("example_image_data") : null;
        string imageFile = null;
        byte[] imageBytes = null;
        if (!string.IsNullOrEmpty(imageData))
        {
            string id = template["id"]?.ToString() ?? "";
            if (!TryDecodeImage(imageData, out imageBytes, out string extension) || !System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z0-9][a-z0-9-]{0,47}$"))
            {
                return Task.FromResult(StudioApi.Error("示例图须为不超过 2 MB 的 PNG、JPEG 或 WebP 图片。", "invalid_template"));
            }
            imageFile = $"{id}.{extension}";
            template["example_image"] = imageFile;
        }
        string error = TemplateStore.SaveCustom(template);
        if (error is not null)
        {
            return Task.FromResult(StudioApi.Error(error, "invalid_template"));
        }
        if (imageBytes is not null)
        {
            try
            {
                Directory.CreateDirectory(TemplateStore.CustomImageDir);
                File.WriteAllBytes(Path.Combine(TemplateStore.CustomImageDir, imageFile), imageBytes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logs.Error($"EcomCraft could not save a template example image: {ex.Message}");
                return Task.FromResult(StudioApi.Error("模板已保存，但示例图无法写入服务器数据目录。", "template_image_failed"));
            }
        }
        return Task.FromResult(new JObject() { ["success"] = true });
    }

    [API.APIDescription("Remove an administrator template. Built-in templates cannot be removed. Requires the review permission.", "success: true")]
    public static Task<JObject> EcomCraftTemplateDelete(Session session, string template_id)
    {
        if (!CanManage(session))
        {
            return Task.FromResult(StudioApi.Error("此账号没有管理模板的权限。", "bad_permissions"));
        }
        string error = TemplateStore.DeleteCustom(template_id);
        return Task.FromResult(error is null ? new JObject() { ["success"] = true } : StudioApi.Error(error, "invalid_template"));
    }

    /// <summary>Decodes a PNG, JPEG or WebP data URL of at most <see cref="MaxImageBytes"/>.</summary>
    private static bool TryDecodeImage(string data, out byte[] bytes, out string extension)
    {
        bytes = null;
        extension = null;
        int comma = data.IndexOf(',');
        if (comma < 0 || data.Length > (MaxImageBytes + 2) / 3 * 4 + 40)
        {
            return false;
        }
        extension = data[..comma] switch
        {
            "data:image/png;base64" => "png",
            "data:image/jpeg;base64" => "jpg",
            "data:image/webp;base64" => "webp",
            _ => null
        };
        if (extension is null)
        {
            return false;
        }
        try
        {
            bytes = Convert.FromBase64String(data[(comma + 1)..]);
        }
        catch (FormatException)
        {
            return false;
        }
        return bytes.Length > 0 && bytes.Length <= MaxImageBytes;
    }
}
