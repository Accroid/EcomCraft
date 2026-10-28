# EcomCraft Platform Preset Slots

> **Language:** English · [简体中文](EcomCraft%20平台预设槽位设计.zh-CN.md)
>
> **Status:** Confirmed (F58 / T60, not yet developed), ready to implement as T60; every platform rule value in this document must still be checked against the platform's current official rules under N06 · **Version:** v1.1 · **Date:** 2026-10-28
>
> Related files: [EcomCraft Architecture and Roadmap](EcomCraft%20Architecture.md) (6.3 platform presets, F58) and the [EcomCraft development tracker](EcomCraft%20开发总表.xlsx) (F05, F22, F58, T60; N06; Chinese spreadsheet). When this design changes, update this document (Chinese and English together) first, then the code.

## 1. Background and Problem

Current state (F05 / F22 implemented): each platform in `Templates/platforms.json` has a single export preset — width, height, padding colour, format (jpg / png), JPEG quality, whether a pure white background is required, a `verified` flag and a source note. Administrators can override the built-in presets with `Data/EcomCraft/platforms.json`.

The problem: a platform's requirements for product images are not "one size" but **a set of image slots**, each with different rules:

- Taobao / Tmall: 1:1 main images (zoom from 800×800) + 3:4 wireless main images + a white-background image in slot 5 (pure white, ~300KB) + 1:1 / 3:4 main-image video
- JD.com: slot 1 must be a pure white image (no shadows or reflections), roughly a 1MB per-file cap, 1:1 / 16:9 main-image video
- Douyin Shop: 1:1 main images (≥600×600), watermarks and promotional text strictly forbidden, 9:16 / 3:4 video
- Amazon: the main image must be pure white, the product must fill ≥85%, files up to 10MB

A single preset cannot express these differences: image sets cannot produce the right combination per platform, exports cannot respect per-platform file size caps, and forbidden-content or AI labelling rules have nowhere to live.

## 2. Platform Differences Beyond Size

| # | Dimension | Examples | Affected side |
|---|---|---|---|
| 1 | Slot structure | One platform = white main + scene main + 3:4 wireless + video cover and other slots | Generation (set composition) |
| 2 | White image position and count | JD slot 1, Taobao slot 5, Amazon main image only, Douyin none required | Generation (how many cut-out-only images, and where) |
| 3 | Minimum subject fill | ~60% (Douyin) → 2/3 (JD) → 80% (Taobao white) → 85% (Amazon) | Generation (composition defaults), export (hints) |
| 4 | Shadows / reflections / props | JD white images allow no shadows or reflections; Amazon main images allow no props or text | Generation |
| 5 | Per-file size cap | ~1MB (JD), ~3MB (Taobao), ~5MB (Douyin), 10MB (Amazon) | Export (automatic compression) |
| 6 | Forbidden content ("clutter") | Watermarks, promo text, prices, QR codes, borders, collages; JD and Douyin enforce hardest | Export (selling-point images only in allowed slots) |
| 7 | AI content labels | China's measures on labelling AI-generated content require explicit labels and platform declarations; the EU and Amazon require disclosure of AI people | Export (visible labels are the later F49) |
| 8 | Formats | Besides JPG / PNG, Douyin accepts WebP | Export |
| 9 | Main-image count caps | Taobao 5, JD 5–6, Douyin 3–5, Amazon 1 + 8 | Generation (candidate count defaults) |
| 10 | Category-specific rules | Disclosure of AI models for apparel, real photos of ingredient lists for food, certification marks for electronics | Generation / export (detailed after N09 picks the category) |

The values above are commonly published guidelines; **N06 must verify them against current official rules before any platform's `verified` flag is set**.

## 3. Design: Preset Schema v3 (Slots)

### 3.1 Data structure

`platforms.json` moves from "one `export` object per platform" to "a set of `slots` per platform". Example (Taobao, values pending the N06 check):

```json
{
  "id": "taobao",
  "name": { "zh-CN": "淘宝 / 天猫", "en": "Taobao / Tmall" },
  "region": "cn",
  "slots": [
    {
      "id": "white_main",
      "kind": "white_main",
      "name": { "zh-CN": "白底主图", "en": "White main image" },
      "width": 800, "height": 800, "background": "#FFFFFF",
      "format": "jpg", "quality": 92, "max_bytes": 307200,
      "subject_ratio_min": 0.8, "position": 5
    },
    {
      "id": "scene_main",
      "kind": "scene_main",
      "width": 800, "height": 800, "background": "#FFFFFF",
      "format": "jpg", "quality": 92, "max_bytes": 3145728
    },
    {
      "id": "wireless_3_4",
      "kind": "scene_main",
      "width": 800, "height": 1200,
      "format": "jpg", "quality": 92, "max_bytes": 3145728,
      "optional": true
    }
  ],
  "main_image_count": 5,
  "video": { "ratios": ["1:1", "3:4"], "max_seconds": 60, "note": "video is F48; only the spec is recorded here" },
  "forbidden": ["watermark", "promo_text", "price", "qr_code", "border", "collage"],
  "ai_label": { "implicit_metadata": true, "platform_declaration": true, "visible_label": "F49 (not developed)" },
  "verified": false,
  "source": "pending N06"
}
```

Field reference:

| Field | Level | Meaning |
|---|---|---|
| `slots[].kind` | Slot | Enum: `white_main` (white main image, cut-out-only branch), `scene_main`, `detail`, `model`, `video_cover` (spec only this phase) |
| `slots[].position` | Slot | Which slot the white image should occupy (JD 1, Taobao 5); shown as a hint, not enforced |
| `slots[].width` / `height` | Slot | Export canvas size; keeps the existing validation range |
| `slots[].subject_ratio_min` | Slot | Minimum subject fill (0–1); a hint when below, never a block |
| `slots[].max_bytes` | Slot | Per-file size cap; export re-encodes with binary-searched quality, and reports if still over |
| `slots[].optional` | Slot | Optional slots (e.g. the 3:4 wireless image): off by default, added when the user ticks them |
| `main_image_count` | Platform | Main-image count cap, used for candidate defaults and over-limit hints |
| `forbidden` | Platform | Forbidden-content enum; checked when exporting images with text (e.g. F47 selling-point images) |
| `ai_label` | Platform | AI labelling policy: `implicit_metadata` (existing IPTC metadata), `platform_declaration` (declare on the platform's seller portal), `visible_label` (later F49) |
| `verified` / `source` | Platform | Same as v2: set to true after N06 verifies the official rules |

### 3.2 Mapping slots to templates

`slots[].kind` maps to the built-in templates: `white_main` → white main image (cut-out-only branch, no generative model), `scene_main` → scene, `detail` → detail, `model` → model or hand-held. **One-click image set = the chosen platform's slot list expanded into a template combination**, for example:

- Taobao = white main (slot-5 hint) + scene + detail + (when ticked) 3:4 wireless scene
- JD = white main (slot-1 hint) + scene + detail
- Amazon = white main + scene gallery image (non-white slot)
- Douyin = scene main + detail (no mandatory white slot)

The existing F55 cap of 20 images in total is unchanged; `main_image_count` only affects default suggestions and over-limit hints.

### 3.3 Export behaviour

- Each work is exported to the target slot's size, format and quality; when `max_bytes` is exceeded the image is re-encoded with binary-searched quality, twice at most, and the result says so if still over.
- `subject_ratio_min` is estimated from the cut-out's share of the canvas; below the minimum the UI hints "the product is rather small" without blocking export.
- `forbidden`: exporting an image with text (F47 selling-point images) to a slot that forbids `promo_text` shows a hint; white slots reject non-white images (the existing `require_white_background` check moves into the slot).
- AI labelling keeps the F22 IPTC metadata; platforms with `platform_declaration: true` get an export hint "declare AI generation when uploading on the platform's portal"; visible labels are F49 and out of scope here.

### 3.4 Compatibility and migration

- Read compatibility: v2 files with a single `export` object (including the `Data/EcomCraft/platforms.json` override) keep working, treated as one `scene_main` slot, with `require_white_background` becoming a `white_main` slot marker.
- The built-in `platforms.json` bumps `version` to 3; v2-only fields are no longer accepted when v3 is detected (slots win, and it is logged).
- No data migration: platform presets have no stored history; republishing takes effect.

## 4. N06 Verification Checklist

Check each field against the platform's current official rules (help centres / seller rule centres); record the outcome and set the platform's `verified` to `true`:

| Field | Taobao / Tmall | JD | Pinduoduo | Douyin | Xiaohongshu | Amazon | Shopify | TikTok Shop | eBay |
|---|---|---|---|---|---|---|---|---|---|
| Main image ratio and minimum size | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ |
| Zoom threshold | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ |
| White image: required, which slot, colour / shadows | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ |
| Subject fill requirement | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ |
| Per-file size cap and formats | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ |
| Main-image count and video slot | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ |
| Forbidden content and enforcement | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ |
| AI-generated content labels and declarations | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ |
| Other ratios (3:4 / 9:16 etc.) | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ |

## 5. Involved Changes (implementation outline, T60)

| File | Change |
|---|---|
| `Templates/platforms.json` | Upgrade to the v3 slot structure; top-level fields such as `upload_quality` unchanged |
| `Studio/Export/ExportRules.cs` | `ExportPreset` becomes a slot collection; parse and validate `max_bytes` and `subject_ratio_min`; v2-compatible reading |
| `Studio/Export/ExportService.cs` | Export per slot; automatic compression to size caps; forbidden-content and subject-fill hints |
| `Studio/Templates/EcomTemplate.cs`, `TemplateStore.cs`, `TemplateApi.cs` | Image sets combine templates by platform slots; optional-slot checkboxes |
| `Studio/simple.js`, `index.html`, `lang/en.js` | Platform picker shows slot badges (white image slot 5 etc.); optional-slot checkboxes; export-result hints |
| `Tests/ExportTests.cs` and others | Unit tests for slot parsing, v2 compatibility, size-cap compression and set composition |

Security boundaries unchanged: clients still submit only platform IDs, template IDs and allowlisted parameters; preset files are read server-side.

## 6. Confirmed Design Decisions

The four questions below were open until 2026-10-28, when they were decided (the user delegated the decision to the agent):

| # | Question | Decision | Rationale |
|---|---|---|---|
| 1 | Should `subject_ratio_min` also shape generation? | **Export hint only**; no generation-side coupling in the first version | Generation-side coupling changes product placement defaults for every template and is hard to verify in isolation; hint at export first, and reconsider coupling only if real generations regularly fall below the minimum |
| 2 | Behaviour when still over `max_bytes` after two re-encodes | **Export anyway and say so in the result** | Refusing wastes the operator's time; platforms compress or reject on upload anyway, and the notice lets the operator act knowingly |
| 3 | Should `video_cover` join image sets this phase? | **Spec only; not part of image sets** | Main-image video is F48 (not developed); recording the 9:16 / 3:4 specs now means the data is ready when F48 lands |
| 4 | Where do category-specific rules (#10 in section 2) live: platform presets or template categories? | **Decide after N09 picks the main category**; no category-rule fields in the first version | Whether such rules follow the platform or the category depends on their actual shape; both F19 category presets and F58 platform presets will exist by then, so place each rule where it belongs instead of guessing now |

## 7. Change Log

| Date | Version | Description |
|---|---|---|
| 2026-10-28 | v1.0 | First draft: background and problem, ten difference dimensions, the v3 slot schema, slot-to-template mapping, export behaviour, compatibility, the N06 checklist, involved changes and open questions |
| 2026-10-28 | v1.1 | The four open questions in section 6 became confirmed design decisions (the user delegated them to the agent): subject fill hints at export only, export anyway when still over the size cap with a notice, video covers record their spec only, category rules wait for N09; status changed to confirmed |
