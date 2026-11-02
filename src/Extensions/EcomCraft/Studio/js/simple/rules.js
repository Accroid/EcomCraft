'use strict';

/**
 * Simple mode: pure value and quality helpers (placement math, transparency and sharpness checks). Part of simple.js since R15; static methods are copied onto SimpleMode by adopt(), so callers and this stay unchanged.
 */
class SimpleRulesPart {
    /** The product rectangle on a canvas, the same math as Placement.Rect on the server: centered at (x, y) as fractions of the canvas,
     * longer side = size × the canvas's shorter side, aspect ratio kept. */
    static placementRect(canvasWidth, canvasHeight, productWidth, productHeight, placement) {
        let longest = Math.max(1, productWidth, productHeight);
        let target = placement.size * Math.min(canvasWidth, canvasHeight);
        let width = Math.max(1, Math.round(productWidth / longest * target));
        let height = Math.max(1, Math.round(productHeight / longest * target));
        return { x: Math.round(placement.x * canvasWidth - width / 2), y: Math.round(placement.y * canvasHeight - height / 2), width, height };
    }

    /** The bounds of a cut-out's product plus a small transparent margin, the same math as ProductComposite.Trim on the server, so a placement's size
     * is the product's own size and not that of the photo's empty border: { x, y, width, height }, or null when nothing is visible. */
    static trimBounds(imageData) {
        let width = imageData.width, height = imageData.height, data = imageData.data;
        let left = width, top = height, right = -1, bottom = -1;
        for (let y = 0; y < height; y++) {
            for (let x = 0; x < width; x++) {
                if (data[(y * width + x) * 4 + 3] >= 16) {
                    left = Math.min(left, x);
                    right = Math.max(right, x);
                    top = Math.min(top, y);
                    bottom = Math.max(bottom, y);
                }
            }
        }
        if (right < 0) {
            return null;
        }
        let margin = Math.max(2, Math.round(Math.max(right - left + 1, bottom - top + 1) * 0.02));
        left = Math.max(0, left - margin);
        top = Math.max(0, top - margin);
        right = Math.min(width - 1, right + margin);
        bottom = Math.min(height - 1, bottom + margin);
        return { x: left, y: top, width: right - left + 1, height: bottom - top + 1 };
    }

    /** Makes the preview image of a product's cut-out: trimmed like the server does, so the preview matches the result. The cut-out itself is sent unchanged.
     * Without a readable cut-out the preview is the cut-out as it is. */
    static async makePreview(product) {
        product.preview = product.cutout;
        try {
            let image = await this.loadImage(product.cutout);
            let canvas = document.createElement('canvas');
            canvas.width = image.naturalWidth;
            canvas.height = image.naturalHeight;
            let context = canvas.getContext('2d');
            context.drawImage(image, 0, 0);
            let bounds = this.trimBounds(context.getImageData(0, 0, canvas.width, canvas.height));
            product.width = image.naturalWidth;
            product.height = image.naturalHeight;
            if (bounds && (bounds.width < canvas.width || bounds.height < canvas.height)) {
                let trimmed = document.createElement('canvas');
                trimmed.width = bounds.width;
                trimmed.height = bounds.height;
                trimmed.getContext('2d').drawImage(canvas, bounds.x, bounds.y, bounds.width, bounds.height, 0, 0, bounds.width, bounds.height);
                product.preview = trimmed.toDataURL('image/png');
                product.width = bounds.width;
                product.height = bounds.height;
            }
        }
        catch (error) {
            // The untrimmed cut-out still shows; only its size on the preview is less exact.
        }
    }

    /** Keeps a placement inside the ranges the server accepts: center 0–1, size 0.1–1.5. */
    static clampPlacement(placement) {
        let clamp = (value, min, max) => Math.min(max, Math.max(min, Number.isFinite(value) ? value : min));
        return { x: clamp(placement.x, 0, 1), y: clamp(placement.y, 0, 1), size: clamp(placement.size, 0.1, 1.5) };
    }

    /** Whether an image is a cut-out, the same rule as the server: at least 2% of its pixels are transparent, or at least 5% of the border ring is,
     * which also catches a rounded product that fills almost the whole canvas and whose few transparent pixels lie along the border (F83). */
    static hasTransparency(imageData) {
        let width = imageData.width || 0, height = imageData.height || 0, data = imageData.data;
        let transparent = 0, ring = 0, ringTransparent = 0;
        for (let y = 0; y < height; y++) {
            for (let x = 0; x < width; x++) {
                let clear = data[(y * width + x) * 4 + 3] < 16;
                let onRing = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                if (clear) {
                    transparent++;
                    if (onRing) {
                        ringTransparent++;
                    }
                }
                if (onRing) {
                    ring++;
                }
            }
        }
        if (transparent * 50 >= width * height) {
            return true;
        }
        return ringTransparent * 20 >= ring;
    }

    /** Server edge warnings (F83) as quality issues: the codes arrive with the cut-out or the product save, the text comes from the translation table. */
    static edgeIssues(codes) {
        let issues = [];
        for (let code of codes || []) {
            if (code) {
                issues.push({ code: 'edge_' + code });
            }
        }
        return issues;
    }

    /** Appends issues that are not shown yet; warnings can arrive twice, with the cut-out and again when the product is saved. */
    static mergeIssues(issues, extra) {
        let shown = new Set((issues || []).map(issue => issue.code));
        return (issues || []).concat((extra || []).filter(issue => !shown.has(issue.code)));
    }

    /** Variance of the 4-neighbour Laplacian over a grayscale copy, the classic sharpness score: blurry photos score low. */
    static laplacianVariance(imageData) {
        let width = imageData.width, height = imageData.height, data = imageData.data;
        if (width < 3 || height < 3) {
            return 0;
        }
        let gray = new Float64Array(width * height);
        for (let i = 0; i < gray.length; i++) {
            gray[i] = 0.299 * data[i * 4] + 0.587 * data[i * 4 + 1] + 0.114 * data[i * 4 + 2];
        }
        let sum = 0, sumSq = 0, count = 0;
        for (let y = 1; y < height - 1; y++) {
            for (let x = 1; x < width - 1; x++) {
                let i = y * width + x;
                let lap = 4 * gray[i] - gray[i - 1] - gray[i + 1] - gray[i - width] - gray[i + width];
                sum += lap;
                sumSq += lap * lap;
                count++;
            }
        }
        if (!count) {
            return 0;
        }
        let mean = sum / count;
        return sumSq / count - mean * mean;
    }

    /** Share of pixels that are at least half opaque, as the product's share of a cut-out (0–1). */
    static opaqueRatio(imageData) {
        let data = imageData.data;
        let opaque = 0;
        for (let i = 3; i < data.length; i += 4) {
            if (data[i] >= 128) {
                opaque++;
            }
        }
        return data.length ? opaque / (data.length / 4) : 1;
    }

    /** Upload quality issues of an original photo (F38): resolution below the configured minimum or a blurry picture.
     * Each issue is { code, ... } and only leads to a warning; the user can always continue. */
    static uploadIssues(width, height, imageData, quality) {
        let q = quality || this.quality;
        let issues = [];
        let min = Number(q.min_side) || 512;
        if (Math.min(width || 0, height || 0) < min) {
            issues.push({ code: 'resolution', width, height, min_side: min });
        }
        let blur = q.blur_variance == null ? 30 : Number(q.blur_variance);
        if (imageData && this.laplacianVariance(imageData) < blur) {
            issues.push({ code: 'blur', threshold: blur });
        }
        return issues;
    }

    /** The cut-out issue when the product covers too little of the picture: [{ code: 'ratio', ratio }], or none. */
    static cutoutIssues(imageData, quality) {
        let q = quality || this.quality;
        let min = q.min_cutout_ratio == null ? 0.08 : Number(q.min_cutout_ratio);
        if (!imageData || !(min > 0)) {
            return [];
        }
        let ratio = this.opaqueRatio(imageData);
        return ratio < min ? [{ code: 'ratio', ratio, min }] : [];
    }

    /** The first scale for compressing an image: at most 16 megapixels, which every mobile browser can draw on a canvas. */
    static compressionScale(width, height) {
        let pixels = Math.max(1, width) * Math.max(1, height);
        return Math.min(1, Math.sqrt(16000000 / pixels));
    }

    /** The candidate count the user typed, as a number (NaN when not numeric). The same 1–20 cap applies to single generations and whole sets (F55). */
    static countValue() {
        return Number(String(this.api.$('simple-count').value || '').trim());
    }

    /** Why the typed count cannot be used, or an empty string when it is a positive integer from 1 to 20 (F55). */
    static countProblem() {
        let raw = String(this.api.$('simple-count').value || '').trim();
        if (!/^[1-9][0-9]*$/.test(raw)) {
            return '请输入正整数。';
        }
        let n = Number(raw);
        if (n < 1 || n > 20) {
            return '候选张数须为 1–20 的正整数。';
        }
        return '';
    }

    /** The plain-language warning for one quality issue, with a suggestion; the user can always continue.
     * Edge issues (F83) only describe what was found in the picture; they never block a generation. */
    static qualityText(issue) {
        let tr = this.api.tr;
        if (issue.code == 'resolution') {
            return tr('这张图只有 {0}×{1} 像素，建议上传短边至少 {2} 像素的图，否则细节会模糊。也可以继续使用。', issue.width, issue.height, issue.min_side);
        }
        if (issue.code == 'blur') {
            return tr('这张图看起来有些模糊，建议对准商品重新拍一张。也可以继续使用。');
        }
        if (issue.code == 'oversize') {
            return tr('这张透明图太大，自动压缩后会超过 5 MB，已保留原图使用。也可以继续使用，或换一张更简单的透明 PNG。');
        }
        if (issue.code == 'edge_fringe_white') {
            return tr('图片边缘有一圈白色残留，放在深色背景上会显出来；出图时会自动淡化，换一张边缘干净的透明图效果更好。也可以继续使用。');
        }
        if (issue.code == 'edge_fringe_black') {
            return tr('图片边缘有一圈深色残留，放在浅色背景上会显出来；出图时会自动淡化，换一张边缘干净的透明图效果更好。也可以继续使用。');
        }
        if (issue.code == 'edge_islands') {
            return tr('商品旁边有零碎的小碎片，可能是没删干净的背景；建议重新抠图或擦掉它们。也可以继续使用。');
        }
        if (issue.code == 'edge_canvas_cut') {
            return tr('商品贴着图片边缘，可能被裁掉了一部分；建议重新上传四周留些空白的图。也可以继续使用。');
        }
        if (issue.code == 'edge_jagged') {
            return tr('商品边缘是硬锯齿，放进场景会有毛边；建议上传边缘平滑的透明图。也可以继续使用。');
        }
        if (issue.code == 'edge_semi_transparent') {
            return tr('这张图有大量半透明边缘（玻璃、毛发类商品）；这类边缘只能按图片自带的透明度呈现，效果不保证。也可以继续使用。');
        }
        if (issue.code == 'angle_cutout_failed') {
            return tr('这张角度图没有自动抠图成功，出图时会自动再抠一次。');
        }
        return tr('抠图后商品只占画面约 {0}%，建议拍得更近一些，让商品占满画面。也可以继续使用。', Math.round((issue.ratio || 0) * 100));
    }
}
SimpleMode.adopt(SimpleRulesPart);
