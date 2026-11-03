using EcomCraftStudio.Studio.Export;
using EcomCraftStudio.Studio.Generation;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio;

/// <summary>Planning how one picture reaches a platform image slot (T107). Part of the StudioApi partial class (R07); read-only, so the client submits the
/// picture's own numbers and platform / slot IDs only, and the generation itself keeps going through the existing outpaint and upscale validation.</summary>
public static partial class StudioApi
{
    [API.APIDescription("Plan how a picture reaches a platform image slot (T107): send the picture's width and height and the platform and slot IDs. Returns the outpaint amount per direction with the canvas size (null when the ratios already match), the upscale factor within 1.5-4 (0 when the export scales instead) with the size it yields, the slot's export size, the recipe's size step and longest-edge cap, and note codes. Read-only; generation still goes through EcomCraftStudioGenerate.",
        "platform: \"taobao\", slot: \"white_main\", slot_kind: \"white_main\", source: { width: 1024, height: 1024 }, outpaint: { left: 0, top: 256, right: 0, bottom: 256, width: 1024, height: 1536 }, upscale: { needed: 0.78, factor: 0, remaining: 0, width: 1024, height: 1536 }, export_size: { width: 800, height: 1200 }, step: 16, max_edge: 2048, notes: []")]
    public static Task<JObject> EcomCraftStudioSlotPlan(Session session, int width, int height, string platform, string slot)
    {
        if (!CanView(session))
        {
            return Task.FromResult(Error("无权访问创作工作台。", "bad_permissions"));
        }
        // Input pictures are not bound by the output cap (A24): they may be any size the reference upload accepts.
        if (width < 16 || width > 8192 || height < 16 || height > 8192)
        {
            return Task.FromResult(Error("原图宽高须为 16–8192 像素的整数。", "invalid_parameters"));
        }
        ExportService.PlatformExport match = ExportService.PlatformPresets().FirstOrDefault(item => item.Platform["id"]?.ToString() == platform);
        ExportSlot target = match?.Find(slot ?? "");
        if (target is null)
        {
            return Task.FromResult(Error("所选平台或图位不存在，请刷新后重新选择。", "invalid_export"));
        }
        OutputSizeRules.SlotFitPlan plan = OutputSizeRules.PlanSlotFit(width, height, target.Preset.Width, target.Preset.Height, OutputSizeRules.Step, OutputSizeRules.MaxSide);
        return Task.FromResult(new JObject()
        {
            ["platform"] = platform,
            ["slot"] = target.Id,
            ["slot_kind"] = target.Kind,
            ["source"] = new JObject() { ["width"] = plan.SourceWidth, ["height"] = plan.SourceHeight },
            ["outpaint"] = plan.Outpaints ? new JObject()
            {
                ["left"] = plan.Left, ["top"] = plan.Top, ["right"] = plan.Right, ["bottom"] = plan.Bottom,
                ["width"] = plan.CanvasWidth, ["height"] = plan.CanvasHeight
            } : null,
            ["upscale"] = new JObject()
            {
                ["needed"] = plan.NeededFactor, ["factor"] = plan.UpscaleFactor, ["remaining"] = plan.RemainingFactor,
                ["width"] = plan.UpscaleWidth, ["height"] = plan.UpscaleHeight
            },
            ["export_size"] = new JObject() { ["width"] = plan.TargetWidth, ["height"] = plan.TargetHeight },
            ["step"] = plan.Step,
            ["max_edge"] = plan.MaxEdge,
            ["notes"] = new JArray(plan.Notes)
        });
    }
}
