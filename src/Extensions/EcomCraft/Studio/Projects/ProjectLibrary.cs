using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using EcomCraftStudio.Studio.Common;
using EcomCraftStudio.Studio.Storage;
using EcomCraftStudio.Studio.Templates;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using ISImage = SixLabors.ImageSharp.Image;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio.Projects;

/// <summary>Project archive (F04): an account's works filed by customer or product batch, each with a recipe (template, seed, prompt subject, execution server)
/// read by the server from the work's own metadata, so a work can be made again with the same settings. This is a record the user saves on purpose:
/// it is only ever readable by its owner, can be deleted at any time, holds no image content, workflow graph or server address, and never appears in queue
/// answers or diagnostics. Records live in Data/EcomCraft/projects.json; the works themselves stay in the gallery.</summary>
[API.APIClass("EcomCraft project archive: projects per account, works filed with their recipe.")]
public static class ProjectLibrary
{
    /// <summary>Most works filed or removed in one call.</summary>
    private const int MaxPerCall = 100;

    /// <summary>The loaded index.</summary>
    private static ProjectIndex Index;

    /// <summary>Protects the index and its file.</summary>
    private static readonly object Lock = new();

    /// <summary>Index file.</summary>
    private static string IndexFile => System.IO.Path.Combine(Program.DataDir, "EcomCraft", "projects.json");

    /// <summary>Loads the index once. Callers hold <see cref="Lock"/>. An unreadable file starts empty and is not overwritten until a change is made.</summary>
    private static ProjectIndex Loaded()
    {
        if (Index is not null)
        {
            return Index;
        }
        Index = new ProjectIndex();
        JsonFileState state = JsonFile.TryRead<List<ProjectRecord>>(IndexFile, root => root["projects"]?.ToObject<List<ProjectRecord>>()?.Where(item => item is not null).ToList() ?? [], out List<ProjectRecord> items, out string error);
        if (state == JsonFileState.Unreadable)
        {
            Logs.Error($"EcomCraft could not read the project index: {error}");
            Unreadable = true;
        }
        else if (state == JsonFileState.Ok)
        {
            Index.Items = items;
        }
        return Index;
    }

    /// <summary>Whether the index file existed but could not be read; it is then never overwritten, so nothing the user saved is lost.</summary>
    private static bool Unreadable;

    /// <summary>Writes the index through a temporary file. Callers hold <see cref="Lock"/>. Refuses while the original file is unreadable.</summary>
    private static bool Save()
    {
        if (Unreadable)
        {
            return false;
        }
        if (!JsonFile.TryWriteAtomic(IndexFile, new JObject() { ["version"] = 1, ["projects"] = JArray.FromObject(Index.Items) }.ToString(Formatting.Indented), out string error))
        {
            Logs.Error($"EcomCraft could not save the project index: {error}");
            return false;
        }
        return true;
    }

    /// <summary>The result URL prefix of the account's works.</summary>
    private static string UrlPrefix(User user) => Program.ServerSettings.Paths.AppendUserNameToOutputPath ? $"View/{user.UserID}/" : "Output/";

    /// <summary>The local file and output-relative path of one of the account's works given as a result URL or gallery path, or null.</summary>
    private static string WorkFile(User user, string image, out string relative)
    {
        relative = null;
        if (string.IsNullOrEmpty(image))
        {
            return null;
        }
        string url = image.StartsWith("View/", StringComparison.Ordinal) || image.StartsWith("Output/", StringComparison.Ordinal) ? image : $"View/{user.UserID}/{image}";
        string path = WorkSync.LocalPathFor(user, url, out relative);
        return path is not null && File.Exists(path) ? path : null;
    }

    /// <summary>The SwarmUI metadata text of a saved work (PNG text chunks or the JPEG user comment) without decoding its pixels, plus its size.</summary>
    private static (string Text, int Width, int Height) ReadMetadata(string path)
    {
        ImageInfo info = ISImage.Identify(path);
        StringBuilder text = new();
        foreach (SixLabors.ImageSharp.Formats.Png.Chunks.PngTextData item in info.Metadata.GetPngMetadata().TextData)
        {
            text.Append(item.Value);
        }
        if (text.Length == 0 && info.Metadata.ExifProfile?.TryGetValue(ExifTag.UserComment, out IExifValue<EncodedString> comment) == true)
        {
            text.Append(comment.Value.Text);
        }
        return (text.ToString(), info.Width, info.Height);
    }

    /// <summary>A project as its owner sees it, without its works.</summary>
    private static JObject Json(ProjectRecord record) => new()
    {
        ["id"] = record.Id,
        ["name"] = record.Name,
        ["note"] = record.Note,
        ["created"] = record.Created,
        ["updated"] = record.Updated,
        ["works"] = record.Works.Count
    };

    [API.APIDescription("List the current account's projects (most recently changed first) with their work counts.", "projects: [{ id: \"…\", name: \"\", works: 3 }]")]
    public static Task<JObject> EcomCraftStudioProjects(Session session)
    {
        if (!StudioApi.CanView(session))
        {
            return Task.FromResult(StudioApi.Error("无权访问创作工作台。", "bad_permissions"));
        }
        lock (Lock)
        {
            return Task.FromResult(new JObject() { ["projects"] = new JArray(Loaded().ForUser(session.User.UserID).Select(Json)) });
        }
    }

    [API.APIDescription("Create one of the current account's projects, or rename it and change its note: project_id (omit to create), name (up to 40 characters), note (up to 200). rawInput is the root request JSON.", "project: { id: \"…\" }")]
    public static Task<JObject> EcomCraftStudioProjectSave(Session session, JObject rawInput)
    {
        if (!StudioApi.CanView(session))
        {
            return Task.FromResult(StudioApi.Error("无权访问创作工作台。", "bad_permissions"));
        }
        string id = rawInput["project_id"]?.Type == JTokenType.String ? rawInput.Value<string>("project_id") : null;
        string name = ProjectIndex.CleanText(rawInput["name"]?.Type == JTokenType.String ? rawInput.Value<string>("name") : null, ProjectIndex.MaxNameLength);
        string note = ProjectIndex.CleanText(rawInput["note"]?.Type == JTokenType.String ? rawInput.Value<string>("note") : null, ProjectIndex.MaxNoteLength) ?? "";
        if (name is null)
        {
            return Task.FromResult(StudioApi.Error("请填写项目名称。", "invalid_parameters"));
        }
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        lock (Lock)
        {
            ProjectIndex index = Loaded();
            ProjectRecord record = id is null ? null : index.Find(session.User.UserID, id);
            if (id is not null && record is null)
            {
                return Task.FromResult(StudioApi.Error("项目不存在，请刷新后重试。", "task_not_found"));
            }
            bool created = record is null;
            (string Name, string Note, long Updated) before = created ? default : (record.Name, record.Note, record.Updated);
            record ??= new ProjectRecord() { Id = Guid.NewGuid().ToString("N")[..16], UserId = session.User.UserID, Created = now };
            record.Name = name;
            record.Note = note;
            record.Updated = now;
            string error = created ? index.Add(record) : null;
            if (error is null && !Save())
            {
                error = "项目记录无法保存到服务器数据目录。";
                if (created)
                {
                    index.Items.Remove(record);
                }
                else
                {
                    (record.Name, record.Note, record.Updated) = before;
                }
            }
            if (error is not null)
            {
                return Task.FromResult(StudioApi.Error(error, "project_save_failed"));
            }
            return Task.FromResult(new JObject() { ["project"] = Json(record) });
        }
    }

    [API.APIDescription("Delete one of the current account's projects and its records; the works stay in the gallery.", "success: true")]
    public static Task<JObject> EcomCraftStudioProjectDelete(Session session, string project_id)
    {
        if (!StudioApi.CanView(session))
        {
            return Task.FromResult(StudioApi.Error("无权访问创作工作台。", "bad_permissions"));
        }
        lock (Lock)
        {
            ProjectRecord record = Loaded().Remove(session.User.UserID, project_id);
            if (record is null)
            {
                return Task.FromResult(StudioApi.Error("项目不存在，请刷新后重试。", "task_not_found"));
            }
            if (!Save())
            {
                Index.Items.Add(record);
                return Task.FromResult(StudioApi.Error("项目记录无法保存到服务器数据目录。", "project_save_failed"));
            }
        }
        return Task.FromResult(new JObject() { ["success"] = true });
    }

    [API.APIDescription("File works of the current account into one of its projects: project_id, images (gallery paths or result URLs, up to 100) and optional product_id, brand_kit_id and platform. The recipe is read from each work's own metadata; filed works are kept forever. rawInput is the root request JSON.", "added: 2, project: { id: \"…\" }")]
    public static async Task<JObject> EcomCraftStudioProjectAddWorks(Session session, JObject rawInput)
    {
        if (!StudioApi.CanView(session))
        {
            return StudioApi.Error("无权访问创作工作台。", "bad_permissions");
        }
        string projectId = rawInput["project_id"]?.Type == JTokenType.String ? rawInput.Value<string>("project_id") : null;
        List<string> images = [.. (rawInput["images"] as JArray ?? new JArray()).Where(item => item.Type == JTokenType.String).Select(item => item.ToString())];
        if (images.Count == 0 || images.Count > MaxPerCall)
        {
            return StudioApi.Error($"一次可以归档 1–{MaxPerCall} 件作品。", "invalid_parameters");
        }
        lock (Lock)
        {
            if (Loaded().Find(session.User.UserID, projectId) is null)
            {
                return StudioApi.Error("项目不存在，请刷新后重试。", "task_not_found");
            }
        }
        // Optional IDs are only kept when they belong to the account (or are a well-formed platform ID).
        string productId = rawInput["product_id"]?.Type == JTokenType.String ? rawInput.Value<string>("product_id") : null;
        if (productId is not null && !Products.ProductLibrary.Exists(session.User.UserID, productId))
        {
            productId = null;
        }
        string brandKitId = rawInput["brand_kit_id"]?.Type == JTokenType.String ? rawInput.Value<string>("brand_kit_id") : null;
        if (brandKitId is not null && Brand.BrandKitLibrary.FragmentFor(session.User.UserID, brandKitId).Error is not null)
        {
            brandKitId = null;
        }
        string platform = rawInput["platform"]?.Type == JTokenType.String ? rawInput.Value<string>("platform") : null;
        if (platform is not null && !Regex.IsMatch(platform, "^[a-z0-9][a-z0-9-]{0,47}$", RegexOptions.CultureInvariant))
        {
            platform = null;
        }
        List<(string Url, string Path)> files = [];
        foreach (string image in images)
        {
            string path = WorkFile(session.User, image, out string relative);
            if (path is null)
            {
                return StudioApi.Error("有作品不存在或不属于当前账号，请刷新后重新选择。", "invalid_reference");
            }
            files.Add((relative, path));
        }
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        List<ProjectWork> works = await Task.Run(() => files.Select(file =>
        {
            ProjectRecipe recipe;
            try
            {
                (string text, int width, int height) = ReadMetadata(file.Path);
                recipe = ProjectIndex.ParseMetadata(text, TemplatePrompt);
                recipe.Width = recipe.Width > 0 ? recipe.Width : width;
                recipe.Height = recipe.Height > 0 ? recipe.Height : height;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
            {
                recipe = new ProjectRecipe();
            }
            recipe.ProductId = productId;
            recipe.BrandKitId = brandKitId;
            recipe.Platform = platform;
            return new ProjectWork() { Path = file.Url, Added = now, Recipe = recipe };
        }).ToList());
        JObject answer;
        lock (Lock)
        {
            ProjectRecord record = Loaded().Find(session.User.UserID, projectId);
            if (record is null)
            {
                return StudioApi.Error("项目不存在，请刷新后重试。", "task_not_found");
            }
            List<ProjectWork> previous = [.. record.Works];
            long previousUpdated = record.Updated;
            int added = ProjectIndex.AddWorks(record, works);
            if (added < 0)
            {
                return StudioApi.Error($"一个项目最多 {ProjectIndex.MaxWorks} 件作品。", "invalid_parameters");
            }
            record.Updated = now;
            if (!Save())
            {
                record.Works = previous;
                record.Updated = previousUpdated;
                return StudioApi.Error("项目记录无法保存到服务器数据目录。", "project_save_failed");
            }
            answer = new JObject() { ["added"] = added, ["project"] = Json(record) };
        }
        // Filed works are clearly wanted, so they are kept forever (F39).
        CandidateCleanup.MarkKept(session.User, new JArray(files.Select(file => UrlPrefix(session.User) + file.Url)));
        return answer;
    }

    [API.APIDescription("Read the prompt and size of one of the current account's works from its own metadata (F64), so the professional mode can refine it. "
        + "image is a gallery path or result URL of the account's own work; the prompt is the full prompt sent to the engine, or empty when the work carries none.",
        "prompt: \"\", width: 1024, height: 1024")]
    public static async Task<JObject> EcomCraftStudioWorkRecipe(Session session, string image)
    {
        if (!StudioApi.CanView(session))
        {
            return StudioApi.Error("无权访问创作工作台。", "bad_permissions");
        }
        string path = WorkFile(session.User, image, out _);
        if (path is null)
        {
            return StudioApi.Error("作品不存在或不属于当前账号，请刷新后重新选择。", "invalid_reference");
        }
        return await Task.Run(() =>
        {
            try
            {
                (string text, int width, int height) = ReadMetadata(path);
                // No template trimming: the professional mode gets the whole prompt the engine received.
                ProjectRecipe recipe = ProjectIndex.ParseMetadata(text, _ => ("", ""));
                return new JObject()
                {
                    ["prompt"] = recipe.Prompt ?? "",
                    ["width"] = recipe.Width > 0 ? recipe.Width : width,
                    ["height"] = recipe.Height > 0 ? recipe.Height : height
                };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
            {
                return new JObject() { ["prompt"] = "", ["width"] = 0, ["height"] = 0 };
            }
        });
    }

    /// <summary>A template's own prompt prefix and suffix, so recipes keep only the subject.</summary>
    private static (string Prefix, string Suffix) TemplatePrompt(string templateId)
    {
        EcomTemplate template = TemplateStore.Find(templateId)?.Template;
        return (template?.PromptPrefix ?? "", template?.PromptSuffix ?? "");
    }

    [API.APIDescription("Remove works from one of the current account's projects: project_id and images (gallery paths or result URLs as listed). The works stay in the gallery. rawInput is the root request JSON.", "removed: 1, project: { id: \"…\" }")]
    public static Task<JObject> EcomCraftStudioProjectRemoveWorks(Session session, JObject rawInput)
    {
        if (!StudioApi.CanView(session))
        {
            return Task.FromResult(StudioApi.Error("无权访问创作工作台。", "bad_permissions"));
        }
        string projectId = rawInput["project_id"]?.Type == JTokenType.String ? rawInput.Value<string>("project_id") : null;
        string prefix = UrlPrefix(session.User);
        List<string> paths = [.. (rawInput["images"] as JArray ?? new JArray()).Where(item => item.Type == JTokenType.String)
            .Select(item => item.ToString()).Select(image => image.StartsWith(prefix, StringComparison.Ordinal) ? image[prefix.Length..] : image)];
        if (paths.Count == 0 || paths.Count > MaxPerCall)
        {
            return Task.FromResult(StudioApi.Error($"一次可以移出 1–{MaxPerCall} 件作品。", "invalid_parameters"));
        }
        lock (Lock)
        {
            ProjectRecord record = Loaded().Find(session.User.UserID, projectId);
            if (record is null)
            {
                return Task.FromResult(StudioApi.Error("项目不存在，请刷新后重试。", "task_not_found"));
            }
            List<ProjectWork> previous = [.. record.Works];
            long previousUpdated = record.Updated;
            int removed = ProjectIndex.RemoveWorks(record, paths);
            record.Updated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (!Save())
            {
                record.Works = previous;
                record.Updated = previousUpdated;
                return Task.FromResult(StudioApi.Error("项目记录无法保存到服务器数据目录。", "project_save_failed"));
            }
            return Task.FromResult(new JObject() { ["removed"] = removed, ["project"] = Json(record) });
        }
    }

    [API.APIDescription("List the works of one of the current account's projects (newest first) with their recipes; works deleted from the gallery are marked exists: false. Only the owner can read it.", "works: [{ image: \"View/…\", exists: true, recipe: { template: \"scene\", seed: 1, prompt: \"\" } }]")]
    public static Task<JObject> EcomCraftStudioProjectWorks(Session session, string project_id)
    {
        if (!StudioApi.CanView(session))
        {
            return Task.FromResult(StudioApi.Error("无权访问创作工作台。", "bad_permissions"));
        }
        List<ProjectWork> works;
        JObject project;
        lock (Lock)
        {
            ProjectRecord record = Loaded().Find(session.User.UserID, project_id);
            if (record is null)
            {
                return Task.FromResult(StudioApi.Error("项目不存在，请刷新后重试。", "task_not_found"));
            }
            works = [.. record.Works];
            project = Json(record);
        }
        string prefix = UrlPrefix(session.User);
        JArray list = new();
        foreach (ProjectWork work in Enumerable.Reverse(works))
        {
            string url = prefix + work.Path;
            list.Add(new JObject()
            {
                ["image"] = url,
                ["added"] = work.Added,
                ["exists"] = WorkFile(session.User, url, out _) is not null,
                ["recipe"] = new JObject()
                {
                    ["template"] = work.Recipe.Template,
                    ["seed"] = work.Recipe.Seed,
                    ["prompt"] = work.Recipe.Prompt,
                    ["execution_server"] = work.Recipe.Server,
                    ["width"] = work.Recipe.Width,
                    ["height"] = work.Recipe.Height,
                    ["product_id"] = work.Recipe.ProductId,
                    ["brand_kit_id"] = work.Recipe.BrandKitId,
                    ["platform"] = work.Recipe.Platform
                }
            });
        }
        return Task.FromResult(new JObject() { ["project"] = project, ["works"] = list });
    }
}
