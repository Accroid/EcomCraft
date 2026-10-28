'use strict';

/**
 * Three-step simple mode (F07), the default view: 1. upload a product photo, 2. pick platform, image type (template), product type, an optional
 * visual style and whether a model is needed (F53), 3. pick and download candidates. The only prompt text is the optional bilingual prompt panel (F54):
 * "AI expand" fills both languages, edited Chinese is translated by the server and edited English is sent as is; the server always adds the product type,
 * model and style fragments and fills the template's product image field. Professional parameters stay in the administrators' professional mode.
 * studio.js hands over its helpers through SimpleMode.attach(); all text goes through the translation table.
 */
class SimpleMode {
    static api = null;
    static data = { platforms: [], templates: [], styles: [], categories: [] };
    static choice = { platform: '', template: '', style: '', category: '', subtype: '', brandKit: '' };
    static needModel = false;
    static prompt = { zh: '', en: '', base: { zh: '', en: '' }, last: '', tab: 'zh', expanding: false };
    static promptUsed = null;
    static storyboard = { points: [''], shots: [], running: false };
    static brandKits = [];
    static brandEditor = null;
    static brandSaving = false;
    static product = null;
    static products = [];
    static library = [];
    static picker = '';
    static galleryItems = null;
    static galleryError = '';
    static exportOptions = { platforms: [], folder_available: false };
    static resultGroups = new Map();
    static taskProducts = new Map();
    static batchProducts = [];
    static exporting = false;
    static placementTimer = null;
    static batch = null;
    static batchTimer = null;
    static loaded = false;
    static loading = false;
    static submitting = false;
    static pending = new Set();
    static preparing = new Map();
    static tasks = [];
    static polling = false;
    static batchSent = null;
    static batchPromptShown = false;
    static timer = null;
    static results = [];
    static selected = new Set();
    static maxProductBytes = 5 * 1024 * 1024;
    static placement = { x: 0.5, y: 0.5, size: 0.7 };
    static cutting = false;
    static drag = null;
    static storageKey = 'ecomcraft.simple';
    static preferredKey = 'ecomcraft.preferred-platforms';
    static quality = { min_side: 512, blur_variance: 30, min_cutout_ratio: 0.08 };

    /** The product rectangle on a canvas, the same math as Placement.Rect on the server: centered at (x, y) as fractions of the canvas,
     * longer side = size × the canvas's shorter side, aspect ratio kept. */
    static placementRect(canvasWidth, canvasHeight, productWidth, productHeight, placement) {
        let longest = Math.max(1, productWidth, productHeight);
        let target = placement.size * Math.min(canvasWidth, canvasHeight);
        let width = Math.max(1, Math.round(productWidth / longest * target));
        let height = Math.max(1, Math.round(productHeight / longest * target));
        return { x: Math.round(placement.x * canvasWidth - width / 2), y: Math.round(placement.y * canvasHeight - height / 2), width, height };
    }

    /** Keeps a placement inside the ranges the server accepts: center 0–1, size 0.1–1.5. */
    static clampPlacement(placement) {
        let clamp = (value, min, max) => Math.min(max, Math.max(min, Number.isFinite(value) ? value : min));
        return { x: clamp(placement.x, 0, 1), y: clamp(placement.y, 0, 1), size: clamp(placement.size, 0.1, 1.5) };
    }

    /** Whether at least 2% of an image's pixels are transparent, as the server checks a cut-out. */
    static hasTransparency(imageData) {
        let transparent = 0;
        let data = imageData.data;
        for (let i = 3; i < data.length; i += 4) {
            if (data[i] < 16) {
                transparent++;
            }
        }
        return transparent * 50 >= data.length / 4;
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

    /** Which views an account sees: everyone gets the simple mode; only administrators get the professional mode with sampler, LoRA and step settings. */
    static viewAccess(catalog) {
        return { simple: true, pro: Boolean(catalog && catalog.can_manage_settings) };
    }

    /** Receives the helpers of studio.js and binds the controls. */
    static attach(api) {
        this.api = api;
        let $ = api.$;
        let drop = $('simple-drop');
        // Four equal ways to add a product photo (F52): this computer, the camera, the product library and the gallery of generated works.
        $('simple-file-btn').addEventListener('click', () => $('simple-file').click());
        $('simple-library-btn').addEventListener('click', e => this.openPicker('library', e.currentTarget));
        $('simple-gallery-btn').addEventListener('click', e => this.openPicker('gallery', e.currentTarget));
        $('simple-picker-close').addEventListener('click', () => this.closePicker());
        $('simple-picker-backdrop').addEventListener('click', () => this.closePicker());
        $('simple-file').addEventListener('change', e => {
            for (let file of Array.from(e.target.files || [])) {
                this.readFile(file, 'local');
            }
            e.target.value = '';
        });
        drop.addEventListener('dragover', e => {
            e.preventDefault();
            drop.classList.add('is-over');
        });
        drop.addEventListener('dragleave', () => drop.classList.remove('is-over'));
        drop.addEventListener('drop', e => {
            e.preventDefault();
            drop.classList.remove('is-over');
            for (let file of Array.from(e.dataTransfer.files || [])) {
                this.readFile(file, 'local');
            }
        });
        $('simple-clear').addEventListener('click', () => this.clearProduct());
        // Camera capture (F42): the capture attribute opens the camera on phones and works over plain http, unlike secure-context APIs.
        $('simple-camera-btn').addEventListener('click', () => $('simple-camera').click());
        $('simple-camera').addEventListener('change', e => {
            for (let file of Array.from(e.target.files || [])) {
                this.readFile(file, 'camera');
            }
            e.target.value = '';
        });
        $('simple-cutout-run').addEventListener('click', () => this.runCutout());
        $('simple-placement-reset').addEventListener('click', () => {
            this.placement = { x: 0.5, y: 0.5, size: 0.7 };
            $('simple-size').value = '70';
            this.renderStage();
        });
        $('simple-size').addEventListener('input', e => {
            this.placement = this.clampPlacement({ ...this.placement, size: Number(e.target.value) / 100 });
            this.renderStage();
        });
        let product = $('simple-stage-product');
        product.addEventListener('pointerdown', e => this.startDrag(e));
        product.addEventListener('pointermove', e => this.moveDrag(e));
        product.addEventListener('pointerup', e => this.endDrag(e));
        product.addEventListener('pointercancel', e => this.endDrag(e));
        window.addEventListener('resize', () => this.renderStage());
        $('simple-platform').addEventListener('change', e => {
            this.choice.platform = e.target.value;
            this.remember();
            this.renderTemplates();
            this.renderStoryboard();
            this.update();
        });
        $('simple-model').addEventListener('change', e => {
            this.needModel = e.target.checked;
            this.update();
        });
        $('simple-expand').addEventListener('click', () => this.expandPrompt());
        $('simple-prompt-tab-zh').addEventListener('click', () => this.switchPromptTab('zh'));
        $('simple-prompt-tab-en').addEventListener('click', () => this.switchPromptTab('en'));
        for (let lang of ['zh', 'en']) {
            $(`simple-prompt-${lang}`).addEventListener('input', e => {
                this.prompt[lang] = e.target.value;
                this.prompt.last = lang;
                this.renderPromptState();
            });
        }
        // Brand kits (F12): pick one for generation, or manage them in a dialog.
        $('simple-brand').addEventListener('change', e => {
            this.choice.brandKit = e.target.value;
            this.remember();
        });
        $('simple-brand-manage').addEventListener('click', e => this.openBrand(e.currentTarget));
        $('brand-close').addEventListener('click', () => this.closeBrand());
        $('brand-backdrop').addEventListener('click', () => this.closeBrand());
        $('brand-new').addEventListener('click', () => this.editBrand(null));
        $('brand-color-add').addEventListener('click', () => {
            if (this.brandEditor && this.brandEditor.colors.length < 5) {
                this.brandEditor.colors.push('#1F2937');
                this.renderBrandEditor();
            }
        });
        $('brand-name').addEventListener('input', e => {
            if (this.brandEditor) {
                this.brandEditor.name = e.target.value;
            }
        });
        $('brand-background').addEventListener('input', e => {
            if (this.brandEditor) {
                this.brandEditor.background = e.target.value;
            }
        });
        $('brand-logo-pick').addEventListener('click', () => $('brand-logo-file').click());
        $('brand-reference-pick').addEventListener('click', () => $('brand-reference-file').click());
        $('brand-logo-file').addEventListener('change', async e => {
            let file = (e.target.files || [])[0];
            e.target.value = '';
            let data = file ? await this.brandImageData(file) : null;
            if (data && this.brandEditor) {
                this.brandEditor.logoData = data;
                this.brandEditor.removeLogo = false;
                this.renderBrandEditor();
            }
        });
        $('brand-reference-file').addEventListener('change', async e => {
            let files = Array.from(e.target.files || []);
            e.target.value = '';
            for (let file of files) {
                if (!this.brandEditor || this.brandReferenceCount() >= 4) {
                    break;
                }
                let data = await this.brandImageData(file);
                if (data && this.brandEditor) {
                    this.brandEditor.added.push(data);
                }
            }
            this.renderBrandEditor();
        });
        $('brand-save').addEventListener('click', () => this.saveBrand());
        $('brand-delete').addEventListener('click', () => this.deleteBrand());
        $('simple-point-add').addEventListener('click', () => {
            if (this.storyboard.points.length < 5) {
                this.storyboard.points.push('');
                this.renderStoryboard();
            }
        });
        $('simple-storyboard-run').addEventListener('click', () => this.runStoryboard());
        $('simple-shot-add').addEventListener('click', () => this.addShot());
        $('simple-storyboard-clear').addEventListener('click', () => {
            this.storyboard.shots = [];
            this.renderStoryboard();
            this.update();
        });
        $('simple-generate').addEventListener('click', () => this.generate());
        $('simple-generate-set').addEventListener('click', () => this.generateSet());
        $('simple-count').addEventListener('input', () => this.update());
        $('simple-count').addEventListener('change', () => this.update());
        $('simple-batch-cancel').addEventListener('click', () => this.cancelBatch());
        $('simple-product-name').addEventListener('change', e => this.renameProduct(e.target.value));
        $('simple-export-zip').addEventListener('click', () => this.exportSelected('zip'));
        $('simple-export-folder').addEventListener('click', () => this.exportSelected('folder'));
        $('simple-export-platform').addEventListener('change', () => this.renderExport());
        $('simple-product-delete').addEventListener('click', () => this.deleteProduct());
        $('simple-size').addEventListener('change', () => this.savePlacement());
        $('simple-download').addEventListener('click', () => this.download());
        let saved = api.readStorage(this.storageKey);
        if (saved) {
            try {
                let parsed = JSON.parse(saved);
                this.choice.platform = String(parsed.platform || '');
                this.choice.style = String(parsed.style || '');
                this.choice.category = String(parsed.category || '');
                this.choice.subtype = String(parsed.subtype || '');
                this.choice.brandKit = String(parsed.brand_kit || '');
            }
            catch (error) {
                // A damaged preference only resets the remembered choice.
            }
        }
    }

    /** Remembers the platform, product type and style in this browser (never the photo or description). */
    static remember() {
        this.api.writeStorage(this.storageKey, JSON.stringify({ platform: this.choice.platform, style: this.choice.style, category: this.choice.category, subtype: this.choice.subtype, brand_kit: this.choice.brandKit }));
    }

    /** Called when the view opens; loads platforms, templates and styles once per language. */
    static async enter() {
        if (!this.loaded) {
            await this.load();
        }
        this.update();
    }

    /** Reloads the lists in the current language, keeping the choices that still exist. */
    static async refresh() {
        this.loaded = false;
        if (this.api.state.view == 'simple') {
            await this.load();
        }
        this.renderResults();
        this.renderPromptUsed();
        this.renderTasks();
        this.renderProducts();
        this.renderStoryboard();
        this.update();
    }

    /** Which panel text the server should use (F54): an edited language ('zh' is translated by the server, 'en' is sent as is; when both were edited the last one wins),
     * the English text when the panel still matches the last expansion or translation, or '' when the panel is empty and the one-line description applies. */
    static promptSource(prompt) {
        let p = prompt || this.prompt;
        let text = { zh: String(p.zh || '').trim(), en: String(p.en || '').trim() };
        let zhDirty = text.zh != String(p.base.zh || '').trim();
        let enDirty = text.en != String(p.base.en || '').trim();
        let order = zhDirty && (!enDirty || p.last == 'zh') ? ['zh', 'en'] : ['en', 'zh'];
        for (let lang of order) {
            if (text[lang]) {
                return lang;
            }
        }
        return '';
    }

    /** Whether the panel text of a language differs from the last expansion or translation. */
    static promptDirty(lang) {
        return String(this.prompt[lang] || '').trim() != String(this.prompt.base[lang] || '').trim();
    }

    /** The prompt fields of a generation request and what to show afterwards: only the text the server should use is sent. */
    static promptSnapshot() {
        let source = this.promptSource();
        let zh = String(this.prompt.zh || '').trim();
        let en = String(this.prompt.en || '').trim();
        if (source == 'zh') {
            return { source, zh, fields: { prompt_zh: zh } };
        }
        if (source == 'en') {
            return { source, zh: this.promptDirty('en') ? '' : zh, fields: { prompt_en: en } };
        }
        return { source, zh: '', fields: {} };
    }

    /** Fills both languages of the panel and marks them as in sync. */
    static setPromptTexts(zh, en) {
        this.prompt.zh = zh;
        this.prompt.en = en;
        this.prompt.base = { zh, en };
        this.prompt.last = '';
        this.api.$('simple-prompt-zh').value = zh;
        this.api.$('simple-prompt-en').value = en;
        this.renderPromptState();
    }

    /** Shows the Chinese or English text of the panel. */
    static switchPromptTab(lang) {
        this.prompt.tab = lang;
        for (let item of ['zh', 'en']) {
            this.api.$(`simple-prompt-tab-${item}`).setAttribute('aria-selected', item == lang ? 'true' : 'false');
            this.api.$(`simple-prompt-${item}`).hidden = item != lang;
        }
    }

    /** Shows whether the panel is empty, in sync, or which edited language will be used. */
    static renderPromptState() {
        let tr = this.api.tr;
        let chip = this.api.$('simple-prompt-state');
        let source = this.promptSource();
        let dirty = source != '' && this.promptDirty(source);
        chip.textContent = source == '' ? tr('未填写')
            : !dirty ? tr('已同步')
            : source == 'zh' ? tr('中文已修改，提交时自动翻译') : tr('英文已修改，将直接发送');
        chip.classList.toggle('is-synced', source != '' && !dirty);
        chip.classList.toggle('is-dirty', dirty);
    }

    /** AI expand (F54): asks the server to write the one-line description (or the product type) into a full prompt in both languages. */
    static async expandPrompt() {
        let tr = this.api.tr;
        if (this.prompt.expanding || !this.loaded) {
            return;
        }
        this.prompt.expanding = true;
        this.update();
        try {
            let result = await this.api.request('EcomCraftStudioExpandPrompt', {
                description: this.api.$('simple-description').value.trim(),
                product_type_id: this.choice.subtype,
                style_id: this.choice.style,
                need_model: this.needModel
            });
            this.setPromptTexts(String(result.prompt_zh || ''), String(result.prompt_en || ''));
            if (result.prompt_zh) {
                this.message(tr('已生成提示词，可直接修改中文或英文。'), false);
            }
            else {
                this.switchPromptTab('en');
                this.message(tr('中文版本暂时没有生成，已填入英文提示词，可直接修改。'), false);
            }
        }
        catch (error) {
            this.message(error.message || tr('提示词服务暂时不可用，可直接用一句话出图。'), true);
        }
        finally {
            this.prompt.expanding = false;
            this.update();
        }
    }

    /** After a submission: remembers the prompt the engine received for the result card and puts the server's translation of edited Chinese on the English tab. */
    static applyPromptResult(result, sent) {
        if (!result.prompt_used) {
            return;
        }
        let zh = sent.zh;
        if (result.prompt_translated && sent.source == 'zh' && String(this.prompt.zh || '').trim() == sent.zh) {
            this.setPromptTexts(this.prompt.zh, String(result.prompt_translated));
        }
        this.promptUsed = { zh, en: String(result.prompt_used) };
        this.renderPromptUsed();
    }

    /** Shows the prompt of the latest submission (Chinese shown, English actually sent), folded under the status line. */
    static renderPromptUsed() {
        let box = this.api.$('simple-prompt-used');
        let used = this.promptUsed;
        box.hidden = !used || !used.en;
        if (box.hidden) {
            return;
        }
        let lines = [];
        if (used.zh) {
            lines.push(this.api.el('p', '', this.api.tr('中文：{0}', used.zh)));
        }
        lines.push(this.api.el('p', '', this.api.tr('英文（实际发送）：{0}', used.en)));
        this.api.$('simple-prompt-used-body').replaceChildren(...lines);
    }

    /** Reads platforms, templates and styles from the server. */
    static async load() {
        if (this.loading || !this.api.state.session) {
            return;
        }
        this.loading = true;
        let language = I18n.current;
        try {
            let [platforms, templates, styles, productTypes] = await Promise.all([
                this.api.request('EcomCraftStudioPlatforms', { language }),
                this.api.request('EcomCraftStudioTemplates', { language }),
                this.api.request('EcomCraftStudioStyles', { language }),
                this.api.request('EcomCraftStudioProductTypes', { language })
            ]);
            this.data.platforms = platforms.platforms || [];
            this.quality = platforms.quality || this.quality;
            this.data.templates = templates.templates || [];
            this.data.styles = styles.styles || [];
            this.data.categories = productTypes.categories || [];
            // The product type is the primary choice (F53): keep the remembered one when it still exists, else fall back to the general category.
            if (!this.data.categories.some(category => category.id == this.choice.category && category.subtypes.some(subtype => subtype.id == this.choice.subtype))) {
                let general = this.data.categories.find(category => category.id == 'general') || this.data.categories[0];
                this.choice.category = general ? general.id : '';
                this.choice.subtype = general && general.subtypes.length ? general.subtypes[0].id : '';
            }
            // The visual style is optional (F53): an empty choice means "auto"; only a stale remembered style is reset.
            if (this.choice.style && !this.data.styles.some(style => style.id == this.choice.style)) {
                this.choice.style = '';
            }
            this.loaded = true;
            this.message('', false);
        }
        catch (error) {
            this.message(error.message || this.api.tr('无法读取模板、产品类型和风格，请刷新页面重试。'), true);
        }
        finally {
            this.loading = false;
        }
        this.renderPlatforms();
        this.renderTemplates();
        this.renderCategories();
        this.renderStyles();
        this.renderStoryboard();
        await this.loadBrandKits();
        await this.loadLibrary();
        await this.loadExportOptions();
    }

    /** Reads the platforms that have export presets and whether the shared export folder is available. */
    static async loadExportOptions() {
        try {
            this.exportOptions = await this.api.request('EcomCraftStudioExportOptions', { language: I18n.current });
        }
        catch (error) {
            this.exportOptions = { platforms: [], folder_available: false };
        }
        let select = this.api.$('simple-export-platform');
        let current = select.value || this.choice.platform;
        let options = (this.exportOptions.platforms || []).map(platform => new Option(this.api.tr('{0}（{1}×{2}）', platform.name, platform.width, platform.height), platform.id));
        select.replaceChildren(...options);
        if (options.some(option => option.value == current)) {
            select.value = current;
        }
        this.renderExport();
    }

    /** Shows the export panel once there are candidates, with a notice when the chosen preset is not verified yet. */
    static renderExport() {
        let tr = this.api.tr;
        let platform = (this.exportOptions.platforms || []).find(item => item.id == this.api.$('simple-export-platform').value);
        this.api.$('simple-export').hidden = !this.results.length || !(this.exportOptions.platforms || []).length;
        this.api.$('simple-export-folder').hidden = !this.exportOptions.folder_available;
        let ready = Boolean(platform) && this.selected.size > 0 && !this.exporting;
        this.api.$('simple-export-zip').disabled = !ready;
        this.api.$('simple-export-folder').disabled = !ready;
        let note = this.selected.size ? tr('将导出已选的 {0} 张，每个商品一个文件夹。', this.selected.size) : tr('先点选要导出的图片。');
        if (platform && !platform.verified) {
            note += ' ' + tr('这个平台的尺寸规格还没有经过管理员核对，上传前请对照平台最新要求。');
        }
        this.api.$('simple-export-note').textContent = note;
    }

    /** Exports the selected candidates for the chosen platform, grouped by product. */
    static async exportSelected(target) {
        let tr = this.api.tr;
        let platform = this.api.$('simple-export-platform').value;
        if (!platform || !this.selected.size || this.exporting) {
            return;
        }
        let groups = new Map();
        for (let url of this.selected) {
            let name = this.resultGroups.get(url) || tr('商品');
            if (!groups.has(name)) {
                groups.set(name, []);
            }
            groups.get(name).push(url);
        }
        this.exporting = true;
        this.renderExport();
        try {
            let result = await this.api.request('EcomCraftStudioExport', { platform, target, groups: Array.from(groups, ([name, images]) => ({ name, images })) });
            if (result.download) {
                let link = document.createElement('a');
                link.href = result.download;
                link.download = result.file_name || 'ecomcraft-export.zip';
                document.body.append(link);
                link.click();
                link.remove();
                this.message(tr('已打包 {0} 张图片，开始下载。', result.count), false);
            }
            else {
                this.message(tr('已导出 {0} 张图片到共享文件夹：{1}', result.exported, result.folder), false);
            }
            if ((result.not_white || []).length) {
                this.message(tr('注意：{0} 张图片的背景不是纯白（RGB 255,255,255），这个平台的主图可能被拒：{1}', result.not_white.length, result.not_white.join(tr('、'))), true);
            }
        }
        catch (error) {
            this.message(error.message || tr('导出失败，请稍后重试。'), true);
        }
        finally {
            this.exporting = false;
            this.renderExport();
        }
    }

    /** Reads the account's saved products for the "from the product library" list. */
    static async loadLibrary() {
        try {
            let result = await this.api.request('EcomCraftStudioProducts');
            this.library = result.products || [];
        }
        catch (error) {
            this.library = [];
        }
        this.renderLibrary();
    }

    /** Refreshes the product-library picker when it is open; choosing a saved product skips uploading and cutting out again. */
    static renderLibrary() {
        if (this.picker == 'library') {
            this.renderPicker();
        }
    }

    /** Opens the picker for the product library or the gallery of generated works (F52). */
    static async openPicker(kind, opener) {
        let tr = this.api.tr;
        this.picker = kind;
        this.galleryItems = kind == 'gallery' ? null : this.galleryItems;
        this.api.$('simple-picker-title').textContent = kind == 'library' ? tr('从商品库选择') : tr('从画廊选择');
        this.api.$('simple-picker-hint').textContent = kind == 'library'
            ? tr('已入库的商品会带上确认过的抠图和位置。')
            : tr('这里是你已生成的作品，选一张作为商品图；作品本身不会被改动，超过 5 MB 的图会先压缩。');
        this.api.$('simple-picker-modal').hidden = false;
        this.renderPicker();
        this.api.dialogEnter(this.api.$('simple-picker-modal'), opener, this.api.$('simple-picker-close'));
        if (kind == 'gallery') {
            await this.loadGalleryItems();
        }
        else {
            await this.loadLibrary();
        }
    }

    /** Closes the picker. */
    static closePicker() {
        if (!this.picker) {
            return;
        }
        this.picker = '';
        this.api.$('simple-picker-modal').hidden = true;
        this.api.dialogLeave(this.api.$('simple-picker-modal'));
    }

    /** Reads this account's recent generated images for the gallery picker (newest first, images only, starred copies skipped). */
    static async loadGalleryItems() {
        try {
            let result = await this.api.request('ListImages', { path: '', depth: 4, sortBy: 'Date', sortReverse: false, filter: null });
            this.galleryItems = (result.files || []).map(file => String(file.src || ''))
                .filter(src => src && !src.startsWith('Starred/') && /\.(png|jpe?g|webp)$/i.test(src)).slice(0, 60);
        }
        catch (error) {
            this.galleryItems = [];
            this.galleryError = error.message || this.api.tr('画廊暂时无法读取，请稍后重试。');
        }
        if (this.picker == 'gallery') {
            this.renderPicker();
        }
        this.galleryError = '';
    }

    /** Fills the picker with saved products or generated works as selectable tiles. */
    static renderPicker() {
        let tr = this.api.tr;
        let tiles = [];
        let empty = '';
        if (this.picker == 'library') {
            for (let saved of this.library) {
                tiles.push(this.pickerTile(saved.cutout_image || saved.image, saved.name, tr('选择商品 {0}', saved.name), async () => {
                    this.closePicker();
                    await this.useSaved(saved);
                }));
            }
            empty = tiles.length ? '' : tr('商品库还是空的。上传的商品图会自动存入商品库。');
        }
        else if (this.picker == 'gallery') {
            if (this.galleryItems == null) {
                empty = tr('正在读取画廊…');
            }
            else {
                for (let src of this.galleryItems) {
                    let name = src.split('/').pop();
                    tiles.push(this.pickerTile(this.api.viewUrl(src), name, tr('选择已生成作品 {0}', name), button => this.useGalleryImage(src, name, button)));
                }
                empty = this.galleryError || (tiles.length ? '' : tr('画廊里还没有已生成的图片。'));
            }
        }
        this.api.$('simple-picker-list').replaceChildren(...tiles);
        let note = this.api.$('simple-picker-empty');
        note.textContent = empty;
        note.hidden = !empty;
    }

    /** One picker tile: a thumbnail with its name. */
    static pickerTile(source, name, label, onPick) {
        let el = this.api.el;
        let button = el('button', 'simple-picker-item');
        button.type = 'button';
        button.title = name;
        button.setAttribute('aria-label', label);
        let image = el('img');
        image.src = source;
        image.alt = '';
        image.loading = 'lazy';
        button.append(image, el('span', '', name));
        button.addEventListener('click', () => onPick(button));
        return button;
    }

    /** Uses a generated work from the gallery as a product photo (F52): read as is when it fits, otherwise compressed in the browser to at most 5 MB.
     * The work itself is not changed; the copy goes through the same quality check and product library as an upload. */
    static async useGalleryImage(src, name, button) {
        let tr = this.api.tr;
        if (this.products.length >= 20) {
            this.message(tr('一次最多 20 张商品图。'), true);
            return;
        }
        button.disabled = true;
        try {
            let response = await fetch(this.api.viewUrl(src), { credentials: 'same-origin' });
            if (!response.ok) {
                throw new Error('fetch');
            }
            let blob = await response.blob();
            let data = await this.blobToDataUrl(blob);
            if (!['image/png', 'image/jpeg', 'image/webp'].includes(blob.type) || blob.size > this.maxProductBytes) {
                data = await this.compressImage(data, this.maxProductBytes);
            }
            this.closePicker();
            await this.addPhoto(data, name, 'gallery');
        }
        catch (error) {
            this.message(tr('这张作品暂时无法读取，请换一张或稍后重试。'), true);
        }
        finally {
            button.disabled = false;
        }
    }

    /** Reads a blob as a data URL. */
    static blobToDataUrl(blob) {
        return new Promise((resolve, reject) => {
            let reader = new FileReader();
            reader.onload = () => resolve(String(reader.result));
            reader.onerror = () => reject(new Error('read'));
            reader.readAsDataURL(blob);
        });
    }

    /** The first scale for compressing an image: at most 16 megapixels, which every mobile browser can draw on a canvas. */
    static compressionScale(width, height) {
        let pixels = Math.max(1, width) * Math.max(1, height);
        return Math.min(1, Math.sqrt(16000000 / pixels));
    }

    /** Re-encodes an image as JPEG on a white background, lowering the quality and then the size until it fits in maxBytes. */
    static async compressImage(dataUrl, maxBytes) {
        let image = await this.loadImage(dataUrl);
        let scale = this.compressionScale(image.naturalWidth, image.naturalHeight);
        for (let attempt = 0; attempt < 8; attempt++) {
            let canvas = document.createElement('canvas');
            canvas.width = Math.max(1, Math.round(image.naturalWidth * scale));
            canvas.height = Math.max(1, Math.round(image.naturalHeight * scale));
            let context = canvas.getContext('2d');
            context.fillStyle = '#FFFFFF';
            context.fillRect(0, 0, canvas.width, canvas.height);
            context.drawImage(image, 0, 0, canvas.width, canvas.height);
            for (let quality of [0.92, 0.82, 0.7]) {
                let blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', quality));
                if (blob && blob.size <= maxBytes) {
                    return await this.blobToDataUrl(blob);
                }
            }
            scale *= 0.75;
        }
        throw new Error('compress');
    }

    /** Makes a saved product the current one, with its confirmed cut-out and placement. */
    static async useSaved(saved) {
        let existing = this.products.find(product => product.id == saved.id);
        if (existing) {
            this.selectProduct(existing);
            return;
        }
        if (this.products.length >= 20) {
            this.message(this.api.tr('一次最多 20 张商品图。'), true);
            return;
        }
        let product = { id: saved.id, name: saved.name, origin: 'library', data: null, source: saved.image, cutout: saved.cutout_image || null, width: 0, height: 0 };
        try {
            let image = await this.loadImage(product.cutout || product.source);
            product.width = image.naturalWidth;
            product.height = image.naturalHeight;
        }
        catch (error) {
            this.message(this.api.tr('商品图暂时无法读取：作品库位置可能离线，请稍后重试。'), true);
            return;
        }
        if (saved.placement) {
            this.placement = this.clampPlacement(saved.placement);
            this.api.$('simple-size').value = String(Math.round(this.placement.size * 100));
        }
        this.products.push(product);
        this.selectProduct(product);
    }

    /** Saves a newly uploaded photo to the product library; generation keeps working when saving fails. */
    static async saveToLibrary(product) {
        try {
            let name = String(product.name || '').replace(/\.[^.]+$/, '').slice(0, 60);
            let result = await this.api.request('EcomCraftStudioProductSave', { image: product.data, name, cutout: product.cutout && product.cutout.startsWith('data:') ? product.cutout : '' });
            product.id = result.product.id;
            product.name = result.product.name;
            await this.loadLibrary();
            if (product == this.product) {
                this.renderProductMeta();
            }
        }
        catch (error) {
            this.message(error.message || this.api.tr('商品没有存入商品库，但仍可以继续出图。'), true);
        }
    }

    /** Shows the name field and delete button for a saved current product. */
    static renderProductMeta() {
        let saved = Boolean(this.product && this.product.id);
        this.api.$('simple-product-meta').hidden = !saved;
        if (saved) {
            this.api.$('simple-product-name').value = this.product.name || '';
        }
    }

    /** Renames the current saved product. */
    static async renameProduct(name) {
        if (!this.product || !this.product.id) {
            return;
        }
        try {
            let result = await this.api.request('EcomCraftStudioProductUpdate', { product_id: this.product.id, name });
            this.product.name = result.product.name;
            await this.loadLibrary();
        }
        catch (error) {
            this.message(error.message || this.api.tr('操作失败，请稍后重试。'), true);
        }
    }

    /** Deletes the current product from the library and from this page. */
    static async deleteProduct() {
        if (!this.product || !this.product.id || !window.confirm(this.api.tr('从商品库删除“{0}”？已生成的作品不受影响。', this.product.name))) {
            return;
        }
        try {
            await this.api.request('EcomCraftStudioProductDelete', { product_id: this.product.id });
            this.clearProduct();
            await this.loadLibrary();
        }
        catch (error) {
            this.message(error.message || this.api.tr('操作失败，请稍后重试。'), true);
        }
    }

    /** Remembers the confirmed placement of the current saved product, shortly after the user stops adjusting it. */
    static savePlacement() {
        if (!this.product || !this.product.id) {
            return;
        }
        clearTimeout(this.placementTimer);
        let id = this.product.id;
        let placement = this.clampPlacement(this.placement);
        this.placementTimer = setTimeout(() => {
            this.api.request('EcomCraftStudioProductUpdate', { product_id: id, placement }).catch(() => {
                // The placement is still sent with each generation; saving it is only a convenience.
            });
        }, 800);
    }

    /** A data URL for a product image that may only be available as a server URL. */
    static async toDataUrl(source) {
        if (String(source).startsWith('data:')) {
            return source;
        }
        let response = await fetch(source, { credentials: 'same-origin' });
        if (!response.ok) {
            throw new Error(this.api.tr('商品图暂时无法读取：作品库位置可能离线，请稍后重试。'));
        }
        let blob = await response.blob();
        return await new Promise((resolve, reject) => {
            let reader = new FileReader();
            reader.onload = () => resolve(String(reader.result));
            reader.onerror = () => reject(new Error('read'));
            reader.readAsDataURL(blob);
        });
    }

    /** Shows or hides the message banner. */
    static message(text, error) {
        let box = this.api.$('simple-message');
        box.textContent = text;
        box.classList.toggle('error', Boolean(error));
        box.hidden = !text;
    }

    /** The platform IDs this browser marked as common (first-setup wizard, F40), limited to still-known platforms. */
    static preferredPlatforms() {
        if (!this.api) {
            return [];
        }
        try {
            let saved = JSON.parse(this.api.readStorage(this.preferredKey) || '[]');
            let ids = Array.isArray(saved) ? saved.map(id => String(id)).slice(0, 10) : [];
            return ids.filter(id => this.data.platforms.some(platform => platform.id == id));
        }
        catch (error) {
            return [];
        }
    }

    /** Fills the platform list; "all platforms" shows every template. Common platforms come first and the first one is preselected. */
    static renderPlatforms() {
        let select = this.api.$('simple-platform');
        let preferred = this.preferredPlatforms();
        let ordered = [];
        for (let id of preferred) {
            let platform = this.data.platforms.find(item => item.id == id);
            if (platform) {
                ordered.push(platform);
            }
        }
        for (let platform of this.data.platforms) {
            if (!ordered.includes(platform)) {
                ordered.push(platform);
            }
        }
        let all = new Option(this.api.tr('全部平台'), '');
        let options = [all];
        for (let platform of ordered) {
            options.push(new Option(platform.name, platform.id));
        }
        select.replaceChildren(...options);
        if (!this.data.platforms.some(platform => platform.id == this.choice.platform)) {
            this.choice.platform = preferred.length ? preferred[0] : '';
        }
        select.value = this.choice.platform;
    }

    /** Templates that suit the chosen platform. */
    static visibleTemplates() {
        let platform = this.choice.platform;
        return this.data.templates.filter(template => !platform || (template.platforms || []).includes('*') || (template.platforms || []).includes(platform));
    }

    /** Builds one selectable card with an optional example image. */
    static card(item, image, selected, onPick) {
        let el = this.api.el;
        let button = el('button', 'simple-card');
        button.type = 'button';
        button.setAttribute('aria-pressed', selected ? 'true' : 'false');
        if (image) {
            let img = el('img');
            img.src = image;
            img.alt = '';
            img.loading = 'lazy';
            button.append(img);
        }
        button.append(el('span', 'simple-card-name', item.name || item.id));
        if (item.description) {
            button.append(el('span', 'simple-card-desc', item.description));
        }
        button.addEventListener('click', onPick);
        return button;
    }

    /** Shows the image types (templates) for the platform; keeps the choice when it is still listed. */
    static renderTemplates() {
        let list = this.visibleTemplates();
        if (!list.some(template => template.id == this.choice.template)) {
            this.choice.template = list.length ? list[0].id : '';
        }
        let cards = [];
        for (let template of list) {
            cards.push(this.card(template, template.example_image, template.id == this.choice.template, () => {
                this.choice.template = template.id;
                this.renderTemplates();
                this.renderCutout();
                this.update();
            }));
        }
        this.api.$('simple-templates').replaceChildren(...cards);
        this.api.$('simple-templates-empty').hidden = cards.length > 0 || !this.loaded;
    }

    /** Shows the product-type categories as chips and their subtypes as cards (F53); the choice is remembered in this browser. */
    static renderCategories() {
        let el = this.api.el;
        let chips = [];
        for (let category of this.data.categories) {
            let chip = el('button', 'simple-chip');
            chip.type = 'button';
            chip.textContent = category.name || category.id;
            chip.setAttribute('aria-pressed', category.id == this.choice.category ? 'true' : 'false');
            chip.addEventListener('click', () => {
                this.choice.category = category.id;
                this.choice.subtype = category.subtypes.length ? category.subtypes[0].id : '';
                this.remember();
                this.renderCategories();
                this.update();
            });
            chips.push(chip);
        }
        this.api.$('simple-categories').replaceChildren(...chips);
        this.renderSubtypes();
    }

    /** Shows the subtypes of the chosen category as cards. */
    static renderSubtypes() {
        let category = this.data.categories.find(item => item.id == this.choice.category);
        let subtypes = category ? category.subtypes : [];
        if (!subtypes.some(subtype => subtype.id == this.choice.subtype)) {
            this.choice.subtype = subtypes.length ? subtypes[0].id : '';
        }
        let cards = [];
        for (let subtype of subtypes) {
            cards.push(this.card(subtype, null, subtype.id == this.choice.subtype, () => {
                this.choice.subtype = subtype.id;
                this.remember();
                this.renderSubtypes();
                this.update();
            }));
        }
        this.api.$('simple-subtypes').replaceChildren(...cards);
    }

    /** Shows the optional visual styles (F53); the first card is "auto" and an empty choice means no style fragment. */
    static renderStyles() {
        let cards = [];
        cards.push(this.card({ name: this.api.tr('自动'), description: this.api.tr('不指定，由模型发挥') }, null, this.choice.style == '', () => {
            this.choice.style = '';
            this.remember();
            this.renderStyles();
            this.update();
        }));
        for (let style of this.data.styles) {
            cards.push(this.card(style, style.example_image, style.id == this.choice.style, () => {
                this.choice.style = style.id;
                this.remember();
                this.renderStyles();
                this.update();
            }));
        }
        this.api.$('simple-styles').replaceChildren(...cards);
    }

    /** The selected template, or null. */
    static currentTemplate() {
        return this.data.templates.find(template => template.id == this.choice.template) || null;
    }

    /** The template's product image field, or null when the template does not take a photo. */
    static imageField(template) {
        return (template?.fields || []).find(field => field.type == 'image') || null;
    }

    /** Reads a product photo from this computer or the camera into memory (origin 'local' or 'camera'). */
    static readFile(file, origin) {
        let tr = this.api.tr;
        if (!file) {
            return;
        }
        if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type) || file.size > this.maxProductBytes) {
            this.message(tr('请选择不超过 5 MB 的 PNG、JPEG 或 WebP 图片。文件尚未上传。'), true);
            return;
        }
        let reader = new FileReader();
        reader.onload = () => this.addPhoto(String(reader.result), file.name, origin || 'local');
        reader.onerror = () => this.message(tr('无法读取文件，请重新选择。'), true);
        reader.readAsDataURL(file);
    }

    /** Adds a new product photo (a data URL of at most 5 MB) from any entry (F52): checks its quality (F38), makes it the current photo and saves it to the product library.
     * <origin> is 'local', 'camera' or 'gallery' and is shown on the thumbnail. */
    static async addPhoto(data, name, origin) {
        if (this.products.length >= 20) {
            this.message(this.api.tr('一次最多 20 张商品图。'), true);
            return;
        }
        let product = { id: null, name, origin, data, source: data, cutout: null, width: 0, height: 0 };
        this.product = product;
        this.products.push(product);
        try {
            let image = await this.loadImage(product.data);
            product.width = image.naturalWidth;
            product.height = image.naturalHeight;
            let canvas = document.createElement('canvas');
            canvas.width = Math.min(512, image.naturalWidth);
            canvas.height = Math.max(1, Math.round(image.naturalHeight * canvas.width / Math.max(1, image.naturalWidth)));
            let context = canvas.getContext('2d');
            context.drawImage(image, 0, 0, canvas.width, canvas.height);
            let imageData = context.getImageData(0, 0, canvas.width, canvas.height);
            // Quality checks (F38): warnings with suggestions, never a block; the photo is usable anyway.
            product.qualityIssues = this.uploadIssues(image.naturalWidth, image.naturalHeight, imageData, this.quality);
            if (this.hasTransparency(imageData)) {
                product.cutout = product.data;
                product.qualityIssues = product.qualityIssues.concat(this.cutoutIssues(imageData, this.quality));
            }
        }
        catch (error) {
            // Without a readable preview the photo can still be used by templates that do not preserve the product.
            product.qualityIssues = [];
        }
        if (this.product == product) {
            this.selectProduct(product);
        }
        else {
            this.renderProducts();
        }
        this.message('', false);
        this.saveToLibrary(product);
    }

    /** Loads an image element from a data URL. */
    static loadImage(source) {
        return new Promise((resolve, reject) => {
            let image = new Image();
            image.onload = () => resolve(image);
            image.onerror = () => reject(new Error('image'));
            image.src = source;
        });
    }

    /** Shows the cut-out panel only for templates that keep the original product pixels, and explains what is missing. */
    static renderCutout() {
        let tr = this.api.tr;
        let template = this.currentTemplate();
        let needed = Boolean(template && template.preserve_product && this.product);
        this.api.$('simple-cutout').hidden = !needed;
        if (!needed) {
            return;
        }
        let status = this.cutting ? tr('正在抠图…')
            : this.product.cutout ? tr('确认抠图结果，可拖动商品调整位置，或用滑块调整大小。不满意可以重新上传。')
            : tr('这个图片类型会保留原商品的像素，需要先去掉商品图的背景。点“自动抠图”，或上传透明背景的 PNG。');
        this.api.$('simple-cutout-status').textContent = status;
        this.api.$('simple-cutout-run').hidden = Boolean(this.product.cutout);
        this.api.$('simple-cutout-run').disabled = this.cutting;
        this.renderStage();
    }

    /** Draws the cut-out on the square preview at the current placement. */
    static renderStage() {
        let image = this.api.$('simple-stage-product');
        let stage = this.api.$('simple-stage');
        if (!this.product || !this.product.cutout || this.api.$('simple-cutout').hidden) {
            image.hidden = true;
            return;
        }
        if (image.getAttribute('src') != this.product.cutout) {
            image.src = this.product.cutout;
        }
        let size = stage.clientWidth || 300;
        let rect = this.placementRect(size, size, this.product.width || 1, this.product.height || 1, this.placement);
        image.style.left = `${rect.x}px`;
        image.style.top = `${rect.y}px`;
        image.style.width = `${rect.width}px`;
        image.style.height = `${rect.height}px`;
        image.hidden = false;
    }

    /** Starts dragging the product on the preview. */
    static startDrag(event) {
        event.preventDefault();
        event.target.setPointerCapture(event.pointerId);
        event.target.classList.add('is-dragging');
        this.drag = { startX: event.clientX, startY: event.clientY, origin: { ...this.placement } };
    }

    /** Moves the product with the pointer; the center stays inside the picture. */
    static moveDrag(event) {
        if (!this.drag) {
            return;
        }
        let size = this.api.$('simple-stage').clientWidth || 300;
        this.placement = this.clampPlacement({
            x: this.drag.origin.x + (event.clientX - this.drag.startX) / size,
            y: this.drag.origin.y + (event.clientY - this.drag.startY) / size,
            size: this.placement.size
        });
        this.renderStage();
    }

    /** Ends dragging. */
    static endDrag(event) {
        if (!this.drag) {
            return;
        }
        event.target.classList.remove('is-dragging');
        this.drag = null;
        this.savePlacement();
    }

    /** Asks the server to remove the background; the result keeps the original product pixels. */
    static async runCutout() {
        let tr = this.api.tr;
        if (!this.product || this.cutting) {
            return;
        }
        this.cutting = true;
        this.renderCutout();
        this.update();
        try {
            let product = this.product;
            let source = product.data || await this.toDataUrl(product.source);
            let result = await this.api.request('EcomCraftStudioCutout', { product_image: source });
            product.cutout = result.cutout;
            // A tiny cut-out means the photo was taken too far away (F38): suggest getting closer, but let the user continue.
            try {
                let image = await this.loadImage(product.cutout);
                let canvas = document.createElement('canvas');
                canvas.width = Math.min(512, image.naturalWidth);
                canvas.height = Math.max(1, Math.round(image.naturalHeight * canvas.width / Math.max(1, image.naturalWidth)));
                let context = canvas.getContext('2d');
                context.drawImage(image, 0, 0, canvas.width, canvas.height);
                let ratioIssues = this.cutoutIssues(context.getImageData(0, 0, canvas.width, canvas.height), this.quality);
                product.qualityIssues = (product.qualityIssues || []).filter(issue => issue.code != 'ratio').concat(ratioIssues);
            }
            catch (error) {
                // The cut-out is still usable when its preview cannot be read back.
            }
            if (product.id) {
                this.api.request('EcomCraftStudioProductUpdate', { product_id: product.id, cutout: result.cutout }).then(() => this.loadLibrary()).catch(() => {
                    // The cut-out is still used on this page; it just is not remembered for next time.
                });
            }
            this.message('', false);
        }
        catch (error) {
            this.message(error.message || tr('抠图没有完成，请稍后重试，或上传已经去掉背景的透明 PNG。'), true);
        }
        finally {
            this.cutting = false;
            this.renderCutout();
            this.update();
        }
    }

    /** Forgets the product photo. */
    static clearProduct() {
        this.products = this.products.filter(product => product != this.product);
        if (this.products.length) {
            this.selectProduct(this.products[this.products.length - 1]);
            return;
        }
        this.product = null;
        this.renderProducts();
        this.api.$('simple-cutout').hidden = true;
        this.api.$('simple-file').value = '';
        this.api.$('simple-preview').hidden = true;
        this.api.$('simple-preview').removeAttribute('src');
        this.api.$('simple-drop-text').textContent = this.api.tr('点击或拖入商品图');
        this.api.$('simple-clear').hidden = true;
        this.update();
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

    /** Why the generate button is disabled, or an empty string when it can be pressed. */
    static blocker() {
        let template = this.currentTemplate();
        if (!this.api.state.session || !this.loaded) {
            return '正在读取模板和风格…';
        }
        if (!template) {
            return '请选择图片类型。';
        }
        if (this.imageField(template) && !this.product) {
            return '请先上传商品图。';
        }
        if (template.preserve_product && !this.product.cutout) {
            return this.cutting ? '正在抠图…' : '请先确认抠图结果。';
        }
        if (!this.choice.subtype) {
            return '请选择产品类型。';
        }
        let countProblem = this.countProblem();
        if (countProblem) {
            return countProblem;
        }
        if (this.submitting) {
            return '提交中…';
        }
        return '';
    }

    /** The plain-language warning for one quality issue, with a suggestion; the user can always continue. */
    static qualityText(issue) {
        let tr = this.api.tr;
        if (issue.code == 'resolution') {
            return tr('这张图只有 {0}×{1} 像素，建议上传短边至少 {2} 像素的图，否则细节会模糊。也可以继续使用。', issue.width, issue.height, issue.min_side);
        }
        if (issue.code == 'blur') {
            return tr('这张图看起来有些模糊，建议对准商品重新拍一张。也可以继续使用。');
        }
        return tr('抠图后商品只占画面约 {0}%，建议拍得更近一些，让商品占满画面。也可以继续使用。', Math.round((issue.ratio || 0) * 100));
    }

    /** Shows the current product's quality warnings under the upload step; empty when there is nothing to complain about. */
    static renderQuality() {
        if (!this.api) {
            return;
        }
        let box = this.api.$('simple-quality');
        let issues = this.product && this.product.qualityIssues ? this.product.qualityIssues : [];
        box.replaceChildren(...issues.map(issue => this.api.el('p', '', this.qualityText(issue))));
        box.hidden = !issues.length;
    }

    /** Enables the buttons and shows what is missing. */
    static update() {
        if (!this.api) {
            return;
        }
        this.renderQuality();
        // Cut-out-only platforms (white main images) never generate, so the model flag does not apply to them (F53).
        let template = this.currentTemplate();
        let cutoutOnly = Boolean(template && this.choice.platform && (template.cutout_only_platforms || []).includes(this.choice.platform));
        if (cutoutOnly && this.needModel) {
            this.needModel = false;
        }
        let modelBox = this.api.$('simple-model');
        modelBox.disabled = cutoutOnly;
        modelBox.checked = this.needModel;
        this.api.$('simple-model-hint').textContent = cutoutOnly
            ? this.api.tr('白底主图为纯抠图，不支持模特。')
            : this.api.tr('勾选后提示词会加入模特描述（初版仅此一项，后续再扩展性别、手模等属性）。');
        let expand = this.api.$('simple-expand');
        expand.disabled = this.prompt.expanding || !this.loaded || !this.api.state.session;
        expand.textContent = this.prompt.expanding ? this.api.tr('AI 扩写中…') : this.api.tr('AI 扩写');
        this.renderPromptState();
        let blocker = this.blocker();
        this.api.$('simple-generate').disabled = Boolean(blocker);
        let countProblem = this.countProblem();
        let countHint = this.api.$('simple-count-hint');
        countHint.textContent = countProblem || this.api.tr('可输入 1–20 的正整数；单次出图和一键出整套最多共 20 张。');
        countHint.classList.toggle('simple-hint-error', Boolean(countProblem));
        let count = countProblem ? 0 : this.countValue();
        this.api.$('simple-hint').textContent = blocker
            ? this.api.tr(blocker)
            : (cutoutOnly
                ? this.api.tr('为当前商品生成白底主图 1 张（不调用生成模型）。')
                : this.api.tr('为当前商品 × {0}（{1}）生成 {2} 张候选。', template ? template.name : '', this.needModel ? this.api.tr('含模特') : this.api.tr('无模特'), count));
        let set = this.setTemplates();
        let shots = this.storyboard.shots.length;
        let types = shots || set.length;
        let storyboardProblem = this.storyboardProblem();
        // One shared cap of 20 for single generations and whole sets (F55): products × set types (or storyboard shots, F25) × candidates.
        let total = this.products.length * types * count;
        let setReady = this.loaded && this.products.length > 0 && types > 0 && !storyboardProblem && Boolean(this.choice.subtype) && !countProblem && total > 0 && total <= 20 && !this.submitting;
        let setButton = this.api.$('simple-generate-set');
        setButton.disabled = !setReady;
        setButton.textContent = shots ? this.api.tr('按分镜出整套') : this.api.tr('一键出整套');
        this.api.$('simple-set-hint').textContent = storyboardProblem
            ? this.api.tr(storyboardProblem)
            : !types
            ? this.api.tr('这个平台还没有可用的套图模板。')
            : (total > 20
                ? this.api.tr('预计 {0} 张，超过 20 张总上限，请减少商品图、类型或候选张数。', total)
                : shots
                ? this.api.tr('按分镜为全部 {0} 张商品图各出 {1} 张图 × 每张 {2} 个候选，预计共 {3} 张（上限 20）。', this.products.length, shots, count, total)
                : this.api.tr('为全部 {0} 张商品图 × 套图 {1} 种类型（{2}）× 每种 {3} 张，预计共 {4} 张（上限 20）。',
                    this.products.length, set.length, set.map(template => template.name).join(this.api.tr('、')), count, total));
        this.api.$('simple-storyboard-run').disabled = this.storyboard.running || !this.loaded || !this.api.state.session;
        this.api.$('simple-storyboard-run').textContent = this.storyboard.running ? this.api.tr('正在生成分镜…') : this.api.tr('生成分镜');
        this.api.$('simple-download').disabled = this.selected.size == 0;
    }

    /** Reads the account's brand kits (F12) and refreshes the step 2 list; a stale remembered choice falls back to "none". */
    static async loadBrandKits() {
        try {
            let result = await this.api.request('EcomCraftStudioBrandKits');
            this.brandKits = result.brand_kits || [];
        }
        catch (error) {
            this.brandKits = [];
        }
        this.renderBrandSelect();
        if (!this.api.$('brand-modal').hidden) {
            this.renderBrandList();
        }
    }

    /** Fills the brand kit list of step 2: "none" first, then the account's kits. */
    static renderBrandSelect() {
        let select = this.api.$('simple-brand');
        let options = [new Option(this.api.tr('不使用'), '')];
        for (let kit of this.brandKits) {
            options.push(new Option(kit.name, kit.id));
        }
        select.replaceChildren(...options);
        if (!this.brandKits.some(kit => kit.id == this.choice.brandKit)) {
            this.choice.brandKit = '';
        }
        select.value = this.choice.brandKit;
    }

    /** The brand kit fields of a generation request: only the kit's ID (F12). */
    static brandFields() {
        return this.choice.brandKit ? { brand_kit_id: this.choice.brandKit } : {};
    }

    /** Opens the brand kit dialog with the account's kits. */
    static async openBrand(opener) {
        this.brandEditor = null;
        this.api.$('brand-modal').hidden = false;
        this.renderBrandList();
        this.renderBrandEditor();
        this.api.dialogEnter(this.api.$('brand-modal'), opener, this.api.$('brand-new'));
        await this.loadBrandKits();
        this.renderBrandList();
    }

    /** Closes the brand kit dialog; unsaved changes are dropped. */
    static closeBrand() {
        if (this.api.$('brand-modal').hidden) {
            return;
        }
        this.api.$('brand-modal').hidden = true;
        this.brandEditor = null;
        this.api.dialogLeave(this.api.$('brand-modal'));
    }

    /** Shows the account's kits as chips; choosing one opens it in the editor. */
    static renderBrandList() {
        let el = this.api.el;
        let chips = [];
        for (let kit of this.brandKits) {
            let chip = el('button', 'simple-chip', kit.name);
            chip.type = 'button';
            chip.setAttribute('aria-pressed', this.brandEditor && this.brandEditor.id == kit.id ? 'true' : 'false');
            chip.addEventListener('click', () => this.editBrand(kit));
            chips.push(chip);
        }
        if (!chips.length) {
            chips.push(el('p', 'hint', this.api.tr('还没有品牌资料包。点“新建资料包”创建一个。')));
        }
        this.api.$('brand-list').replaceChildren(...chips);
        this.api.$('brand-new').disabled = this.brandKits.length >= 20;
    }

    /** Opens a kit in the editor, or an empty one for a new kit. */
    static editBrand(kit) {
        this.brandEditor = {
            id: kit ? kit.id : null,
            name: kit ? kit.name : '',
            colors: kit ? [...(kit.colors || [])] : [],
            background: kit ? kit.background || '' : '',
            logo: kit ? kit.logo : null,
            logoData: null,
            removeLogo: false,
            references: kit ? [...(kit.references || [])] : [],
            removed: [],
            added: []
        };
        this.brandMessage(kit && kit.background_ready == false ? this.api.tr('偏好背景描述上次没能翻译成英文，出图时暂不使用；保存一次会重新翻译。') : '', false);
        this.renderBrandList();
        this.renderBrandEditor();
        this.api.$('brand-name').focus();
    }

    /** How many reference images the kit will have after saving. */
    static brandReferenceCount() {
        let editor = this.brandEditor;
        return editor ? editor.references.filter(item => !editor.removed.includes(item.id)).length + editor.added.length : 0;
    }

    /** One thumbnail with a remove button. */
    static brandThumb(source, label, onRemove) {
        let el = this.api.el;
        let box = el('div', 'brand-image');
        let image = el('img');
        image.src = source;
        image.alt = '';
        let remove = el('button', 'icon-btn', '✕');
        remove.type = 'button';
        remove.setAttribute('aria-label', label);
        remove.addEventListener('click', onRemove);
        box.append(image, remove);
        return box;
    }

    /** Shows the editor for the open kit: name, colors, background, logo and references. */
    static renderBrandEditor() {
        let tr = this.api.tr;
        let el = this.api.el;
        let editor = this.brandEditor;
        this.api.$('brand-editor').hidden = !editor;
        if (!editor) {
            return;
        }
        this.api.$('brand-name').value = editor.name;
        this.api.$('brand-background').value = editor.background;
        let colors = [];
        for (let [index, color] of editor.colors.entries()) {
            let item = el('span', 'brand-color');
            let input = el('input');
            input.type = 'color';
            input.value = /^#[0-9a-fA-F]{6}$/.test(color) ? color.toLowerCase() : '#1f2937';
            input.setAttribute('aria-label', tr('品牌色 {0}', index + 1));
            input.addEventListener('input', e => {
                editor.colors[index] = e.target.value;
            });
            let remove = el('button', 'icon-btn', '✕');
            remove.type = 'button';
            remove.setAttribute('aria-label', tr('删除品牌色 {0}', index + 1));
            remove.addEventListener('click', () => {
                editor.colors.splice(index, 1);
                this.renderBrandEditor();
            });
            item.append(input, remove);
            colors.push(item);
        }
        this.api.$('brand-colors').replaceChildren(...colors);
        this.api.$('brand-color-add').disabled = editor.colors.length >= 5;
        let logo = editor.logoData || (editor.removeLogo ? null : editor.logo);
        this.api.$('brand-logo').replaceChildren(...(logo ? [this.brandThumb(logo, tr('移除 Logo'), () => {
            editor.logoData = null;
            editor.removeLogo = true;
            this.renderBrandEditor();
        })] : []));
        let references = [];
        for (let item of editor.references.filter(reference => !editor.removed.includes(reference.id))) {
            references.push(this.brandThumb(item.image, tr('移除参考图'), () => {
                editor.removed.push(item.id);
                this.renderBrandEditor();
            }));
        }
        for (let [index, data] of editor.added.entries()) {
            references.push(this.brandThumb(data, tr('移除参考图'), () => {
                editor.added.splice(index, 1);
                this.renderBrandEditor();
            }));
        }
        this.api.$('brand-references').replaceChildren(...references);
        this.api.$('brand-reference-pick').disabled = this.brandReferenceCount() >= 4;
        this.api.$('brand-save').disabled = this.brandSaving;
        this.api.$('brand-save').textContent = this.brandSaving ? tr('保存中…') : tr('保存');
        this.api.$('brand-delete').hidden = !editor.id;
    }

    /** Shows a message inside the brand kit dialog. */
    static brandMessage(text, error) {
        let box = this.api.$('brand-message');
        box.textContent = text;
        box.classList.toggle('simple-hint-error', Boolean(error));
        box.hidden = !text;
    }

    /** Reads a chosen image as a data URL of at most 5 MB (compressed in the browser when larger), or null with a message. */
    static async brandImageData(file) {
        try {
            let data = await this.blobToDataUrl(file);
            if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type) || file.size > this.maxProductBytes) {
                data = await this.compressImage(data, this.maxProductBytes);
            }
            return data;
        }
        catch (error) {
            this.brandMessage(this.api.tr('这张图片无法读取，请换一张 PNG、JPEG 或 WebP 图片。'), true);
            return null;
        }
    }

    /** Saves the open kit; only new images are sent, existing ones are referred to by ID. */
    static async saveBrand() {
        let tr = this.api.tr;
        let editor = this.brandEditor;
        if (!editor || this.brandSaving) {
            return;
        }
        if (!editor.name.trim()) {
            this.brandMessage(tr('请填写品牌资料包名称。'), true);
            return;
        }
        this.brandSaving = true;
        this.renderBrandEditor();
        try {
            let payload = {
                name: editor.name.trim(),
                colors: editor.colors.map(color => String(color).toUpperCase()),
                background: editor.background.trim(),
                remove_logo: editor.removeLogo,
                add_references: editor.added,
                remove_references: editor.removed
            };
            if (editor.id) {
                payload.brand_kit_id = editor.id;
            }
            if (editor.logoData) {
                payload.logo = editor.logoData;
            }
            let result = await this.api.request('EcomCraftStudioBrandKitSave', payload);
            await this.loadBrandKits();
            let saved = this.brandKits.find(kit => kit.id == result.brand_kit.id) || result.brand_kit;
            this.editBrand(saved);
            this.brandMessage(result.notice_code == 'background_untranslated'
                ? tr('已保存。提示词服务暂时不可用，偏好背景描述没能翻译成英文，出图时暂不使用；稍后再保存一次即可。')
                : tr('已保存。'), result.notice_code == 'background_untranslated');
        }
        catch (error) {
            this.brandMessage(error.message || tr('保存失败，请稍后重试。'), true);
        }
        finally {
            this.brandSaving = false;
            this.renderBrandEditor();
        }
    }

    /** Deletes the open kit after confirmation. */
    static async deleteBrand() {
        let editor = this.brandEditor;
        if (!editor || !editor.id || !window.confirm(this.api.tr('删除品牌资料包“{0}”？已生成的作品不受影响。', editor.name))) {
            return;
        }
        try {
            await this.api.request('EcomCraftStudioBrandKitDelete', { brand_kit_id: editor.id });
            this.brandEditor = null;
            await this.loadBrandKits();
            this.renderBrandList();
            this.renderBrandEditor();
        }
        catch (error) {
            this.brandMessage(error.message || this.api.tr('操作失败，请稍后重试。'), true);
        }
    }

    /** Why the storyboard cannot be used on the chosen platform, or an empty string (F25). */
    static storyboardProblem() {
        let visible = this.visibleTemplates();
        return this.storyboard.shots.some(shot => !visible.some(template => template.id == shot.template))
            ? '分镜里有当前平台不可用的图片类型，请重新选择。' : '';
    }

    /** Shows the selling points and storyboard shots as editable rows (F25). Only structural changes re-render; typing just updates the state. */
    static renderStoryboard() {
        if (!this.api) {
            return;
        }
        let tr = this.api.tr;
        let el = this.api.el;
        let points = [];
        for (let [index, text] of this.storyboard.points.entries()) {
            let row = el('div', 'simple-point');
            let input = el('input');
            input.type = 'text';
            input.maxLength = 60;
            input.autocomplete = 'off';
            input.value = text;
            input.placeholder = tr('例如：深层保湿 24 小时');
            input.setAttribute('aria-label', tr('卖点 {0}', index + 1));
            input.addEventListener('input', e => {
                this.storyboard.points[index] = e.target.value;
            });
            let remove = el('button', 'icon-btn', '✕');
            remove.type = 'button';
            remove.setAttribute('aria-label', tr('删除卖点 {0}', index + 1));
            remove.addEventListener('click', () => {
                this.storyboard.points.splice(index, 1);
                if (!this.storyboard.points.length) {
                    this.storyboard.points.push('');
                }
                this.renderStoryboard();
            });
            row.append(input, remove);
            points.push(row);
        }
        this.api.$('simple-points').replaceChildren(...points);
        this.api.$('simple-point-add').disabled = this.storyboard.points.length >= 5;
        let visible = this.visibleTemplates();
        let shots = [];
        for (let [index, shot] of this.storyboard.shots.entries()) {
            let item = el('li', 'simple-shot');
            let head = el('div', 'simple-shot-head');
            let remove = el('button', 'link-btn', tr('删除'));
            remove.type = 'button';
            remove.setAttribute('aria-label', tr('删除第 {0} 张', index + 1));
            remove.addEventListener('click', () => {
                this.storyboard.shots.splice(index, 1);
                this.renderStoryboard();
                this.update();
            });
            head.append(el('span', '', tr('第 {0} 张', index + 1)), remove);
            let row = el('div', 'simple-shot-row');
            let select = el('select');
            select.setAttribute('aria-label', tr('第 {0} 张的图片类型', index + 1));
            let known = visible.some(template => template.id == shot.template);
            let options = [];
            if (!known) {
                options.push(new Option(tr('请选择图片类型'), ''));
            }
            for (let template of visible) {
                options.push(new Option(template.name || template.id, template.id));
            }
            select.replaceChildren(...options);
            select.value = known ? shot.template : '';
            select.addEventListener('change', e => {
                shot.template = e.target.value;
                this.renderStoryboard();
                this.update();
            });
            let purpose = el('input');
            purpose.type = 'text';
            purpose.maxLength = 60;
            purpose.autocomplete = 'off';
            purpose.value = shot.purpose;
            purpose.placeholder = tr('用途，例如：突出保湿成分');
            purpose.setAttribute('aria-label', tr('第 {0} 张的用途', index + 1));
            purpose.addEventListener('input', e => {
                shot.purpose = e.target.value;
            });
            row.append(select, purpose);
            let prompt = el('input');
            prompt.type = 'text';
            prompt.maxLength = 300;
            prompt.autocomplete = 'off';
            prompt.spellcheck = false;
            prompt.value = shot.prompt;
            prompt.placeholder = tr('画面描述（英文或中文，可留空）');
            prompt.setAttribute('aria-label', tr('第 {0} 张的画面描述', index + 1));
            prompt.addEventListener('input', e => {
                shot.prompt = e.target.value;
            });
            item.append(head, row, prompt);
            shots.push(item);
        }
        this.api.$('simple-shots').replaceChildren(...shots);
        this.api.$('simple-shot-actions').hidden = !shots.length;
        this.api.$('simple-shot-add').disabled = this.storyboard.shots.length >= 8;
    }

    /** Adds an empty shot with the first image type of the platform's set (or of the platform). */
    static addShot() {
        if (this.storyboard.shots.length >= 8) {
            return;
        }
        let first = this.setTemplates()[0] || this.visibleTemplates()[0];
        this.storyboard.shots.push({ template: first ? first.id : '', purpose: '', prompt: '' });
        this.renderStoryboard();
        this.update();
    }

    /** Asks the server for selling points and a storyboard (F25) from the description or prompt panel, the user's points and the choices of step 2. */
    static async runStoryboard() {
        let tr = this.api.tr;
        if (this.storyboard.running || !this.loaded) {
            return;
        }
        this.storyboard.running = true;
        this.update();
        try {
            let result = await this.api.request('EcomCraftStudioStoryboard', {
                description: this.api.$('simple-description').value.trim(),
                selling_points: this.storyboard.points.map(point => String(point).trim()).filter(point => point).join('\n'),
                product_type_id: this.choice.subtype,
                style_id: this.choice.style,
                need_model: this.needModel,
                platform: this.choice.platform,
                language: I18n.current,
                ...this.promptSnapshot().fields
            });
            let points = (result.selling_points || []).map(point => String(point)).slice(0, 5);
            this.storyboard.points = points.length ? points : [''];
            this.storyboard.shots = (result.shots || []).slice(0, 8).map(shot => ({ template: String(shot.template_id || ''), purpose: String(shot.purpose || ''), prompt: String(shot.prompt || '') }));
            this.message(result.notice_code == 'storyboard_fallback'
                ? tr('提示词服务暂时不可用，已按平台套图的图片类型列出分镜，可以手动修改。')
                : tr('已生成 {0} 张分镜，可以修改后点“按分镜出整套”。', this.storyboard.shots.length), false);
        }
        catch (error) {
            this.message(error.message || tr('分镜没有生成，请稍后重试。'), true);
        }
        finally {
            this.storyboard.running = false;
            this.renderStoryboard();
            this.update();
        }
    }

    /** The plain-language source of a product photo (F52): this computer, the camera, the product library or the gallery. */
    static originText(origin) {
        let names = { local: '本机', camera: '拍照', library: '商品库', gallery: '画廊' };
        return this.api.tr(names[origin] || '本机');
    }

    /** Shows the product photos as thumbnails with their source (F52); clicking one makes it the current photo. */
    static renderProducts() {
        let el = this.api.el;
        let list = this.api.$('simple-products');
        let items = [];
        for (let [index, product] of this.products.entries()) {
            let button = el('button', 'simple-product');
            button.type = 'button';
            button.setAttribute('aria-pressed', product == this.product ? 'true' : 'false');
            let origin = this.originText(product.origin);
            button.setAttribute('aria-label', this.api.tr('商品图 {0}（来自{1}）', index + 1, origin));
            button.title = product.name || '';
            let image = el('img');
            image.src = product.cutout || product.source || product.data;
            image.alt = '';
            button.append(image, el('span', 'simple-product-source', origin));
            button.addEventListener('click', () => this.selectProduct(product));
            items.push(button);
        }
        list.replaceChildren(...items);
        list.hidden = this.products.length == 0;
    }

    /** Makes a product photo the current one for preview, cut-out and single generation. */
    static selectProduct(product) {
        this.product = product;
        let preview = this.api.$('simple-preview');
        preview.src = product.source || product.data;
        preview.hidden = false;
        this.api.$('simple-drop-text').textContent = product.name;
        this.api.$('simple-clear').hidden = false;
        this.renderProducts();
        this.renderProductMeta();
        this.renderCutout();
        this.update();
    }

    /** Templates of the standard set for the chosen platform, as the server will expand them. */
    static setTemplates() {
        return this.visibleTemplates().filter(template => (template.sets || []).includes('standard'));
    }

    /** Starts a one-click set for every uploaded product: each product × each image type of the set, with the chosen number of candidates. */
    static async generateSet() {
        let tr = this.api.tr;
        let set = this.setTemplates();
        let shots = this.storyboard.shots;
        if (!this.products.length || this.submitting || this.countProblem() || this.storyboardProblem()
            || this.products.length * (shots.length || set.length) * this.countValue() > 20) {
            return;
        }
        let sent = this.promptSnapshot();
        let payload = {
            language: I18n.current,
            platform: this.choice.platform,
            set_id: 'standard',
            style_id: this.choice.style,
            product_type_id: this.choice.subtype,
            need_model: this.needModel,
            description: this.api.$('simple-description').value.trim(),
            ...sent.fields,
            ...this.brandFields(),
            candidates: this.countValue(),
            products: this.products.map(product => product.id
                ? { product_id: product.id, placement: this.clampPlacement(this.placement) }
                : { image: product.cutout || product.data, placement: this.clampPlacement(this.placement) })
        };
        if (shots.length) {
            // The confirmed storyboard (F25): only template IDs and text; the server checks every shot again.
            payload.shots = shots.map(shot => ({ template_id: shot.template, prompt: String(shot.prompt || '').trim(), purpose: String(shot.purpose || '').trim() }));
        }
        this.submitting = true;
        this.update();
        this.batchProducts = this.products.map(product => product.name || '');
        try {
            let batch = await this.api.request('EcomCraftStudioGenerateSet', payload);
            this.batch = batch;
            this.batchDone = 0;
            this.api.titleAdd((batch.jobs || []).length);
            this.renderBatch();
            this.trackBatch();
            this.api.$('simple-status').textContent = tr('已开始出整套图：共 {0} 个任务，会分配到空闲的执行服务器上并行完成。', (batch.jobs || []).length);
            this.batchSent = sent;
            this.batchPromptShown = false;
            this.message('', false);
            this.showBatchPrompt();
        }
        catch (error) {
            this.message(error.message || tr('提交失败，请稍后重试。'), true);
        }
        finally {
            this.submitting = false;
            this.update();
        }
    }

    /** Polls the batch until every task has ended, adding finished candidates to the results. Keeps polling in the background
     * so the tab title shows the progress (F43). */
    static trackBatch() {
        if (this.batchTimer) {
            return;
        }
        this.batchTimer = setInterval(async () => {
            if (!this.batch) {
                return;
            }
            try {
                this.batch = await this.api.request('EcomCraftStudioBatchStatus', { batch_id: this.batch.batch_id });
            }
            catch (error) {
                return;
            }
            let jobs = this.batch.jobs || [];
            this.showBatchPrompt();
            let finished = jobs.filter(job => job.state != 'prompt' && job.state != 'queued' && job.state != 'running').length;
            if (finished > (this.batchDone || 0)) {
                this.api.titleDone(finished - (this.batchDone || 0));
                this.batchDone = finished;
            }
            this.renderBatch();
            if (this.batchFinished(jobs)) {
                clearInterval(this.batchTimer);
                this.batchTimer = null;
                let failed = jobs.filter(job => job.state == 'failed' || job.state == 'cancelled').length;
                if (failed) {
                    this.message(this.api.tr('整套出图结束：{0} 个任务没有完成，可以点“重试”单独重提。', failed), true);
                }
                else {
                    this.message(this.api.tr('整套出图已完成，点选满意的图片后下载。'), false);
                }
                this.api.$('simple-status').textContent = this.api.tr('点选满意的图片，再点“下载所选”。所有结果也保存在作品画廊。');
            }
        }, 3000);
    }

    /** Once the batch prompt is written (F56), shows the prompt used and any fallback notice, only once per batch. */
    static showBatchPrompt() {
        if (!this.batch || !this.batch.prompt_ready || this.batchPromptShown) {
            return;
        }
        this.batchPromptShown = true;
        this.applyPromptResult(this.batch, this.batchSent || { source: '', zh: '' });
        if (this.batch.prompt_notice_code) {
            this.promptNotice(this.batch.prompt_notice_code);
        }
        if (this.batch.shots_untranslated) {
            this.message(this.api.tr('提示词服务暂时不可用，部分分镜的中文画面描述没能翻译，这些图只按图片类型和整体提示词出图。'), true);
        }
    }

    /** Whether every task of a batch has ended (done, failed or cancelled). */
    static batchFinished(jobs) {
        return !(jobs || []).some(job => job.state == 'prompt' || job.state == 'queued' || job.state == 'running');
    }

    /** Shows each task's state with a retry button for failed ones, and adds finished images to the candidates. */
    static renderBatch() {
        let tr = this.api.tr;
        let el = this.api.el;
        let jobs = this.batch?.jobs || [];
        this.api.$('simple-batch').hidden = !jobs.length;
        let items = [];
        let added = false;
        for (let job of jobs) {
            let item = el('li', `simple-job${job.state == 'failed' ? ' is-failed' : ''}`);
            let name = tr('商品 {0}｜{1}', job.product + 1, job.purpose || job.template_name || job.template);
            let state = this.stageText(job.state, job.state == 'done' ? job.execution_server : '');
            item.append(el('span', '', name), el('span', 'simple-job-state', state));
            if (job.state == 'failed' || job.state == 'cancelled') {
                if (job.error) {
                    item.title = job.error_id ? this.api.serverErrorText(job.error_id, job.error) : job.error;
                }
                let retry = el('button', 'btn btn-sm', tr('重试'));
                retry.type = 'button';
                retry.addEventListener('click', () => this.retryJob(job.id));
                item.append(retry);
            }
            items.push(item);
            for (let url of job.images || []) {
                if (!this.results.includes(url)) {
                    this.results.push(url);
                    this.resultGroups.set(url, this.batchProducts[job.product] || tr('商品 {0}', job.product + 1));
                    added = true;
                }
            }
        }
        this.api.$('simple-jobs').replaceChildren(...items);
        this.api.$('simple-batch-cancel').hidden = !jobs.some(job => job.state == 'queued' || job.state == 'prompt');
        if (added) {
            this.renderResults();
        }
    }

    /** Retries one failed or cancelled task. */
    static async retryJob(id) {
        try {
            this.batch = await this.api.request('EcomCraftStudioBatchRetry', { batch_id: this.batch.batch_id, job_id: id });
            this.api.titleAdd();
            this.renderBatch();
            this.trackBatch();
        }
        catch (error) {
            this.message(error.message || this.api.tr('操作失败，请稍后重试。'), true);
        }
    }

    /** Cancels the tasks still waiting; running tasks finish. */
    static async cancelBatch() {
        if (!this.batch) {
            return;
        }
        try {
            this.batch = await this.api.request('EcomCraftStudioBatchCancel', { batch_id: this.batch.batch_id });
            this.renderBatch();
        }
        catch (error) {
            this.message(error.message || this.api.tr('操作失败，请稍后重试。'), true);
        }
    }

    /** Submits one template generation; the server composes the prompt from the style and description. */
    static async generate() {
        let tr = this.api.tr;
        let template = this.currentTemplate();
        if (this.blocker() || !template) {
            return;
        }
        let params = {};
        let sent = this.promptSnapshot();
        let field = this.imageField(template);
        if (field && this.product && !this.product.id) {
            params[field.id] = template.preserve_product ? this.product.cutout : this.product.data;
        }
        let payload = {
            source: 'local',
            kind: template.kind || 'image',
            template_id: template.id,
            template_params: params,
            style_id: this.choice.style,
            product_type_id: this.choice.subtype,
            need_model: this.needModel,
            description: this.api.$('simple-description').value.trim(),
            ...sent.fields,
            ...this.brandFields(),
            images: this.countValue(),
            platform: this.choice.platform,
            async: true
        };
        if (field && this.product && this.product.id) {
            payload.product_id = this.product.id;
        }
        if (template.preserve_product) {
            payload.placement = this.clampPlacement(this.placement);
        }
        this.submitting = true;
        this.update();
        let entry = { product: this.product ? this.product.name : '', template: template.name || template.id, stage: 'prompt', server: '', taskId: null };
        try {
            let result = await this.api.request('EcomCraftStudioGenerate', payload);
            if (result.prepare_id) {
                // The language model is writing the prompt (F56): the task is shown at once and starts by itself when the prompt is ready.
                this.preparing.set(result.prepare_id, { entry, sent });
                this.addTask(entry);
                this.api.titleAdd();
                this.track();
                this.api.$('simple-status').textContent = tr('已提交，正在生成提示词，完成后会自动开始出图。');
                this.message('', false);
            }
            else {
                this.addTask(entry);
                this.acceptSubmitted(result, entry, sent, false);
            }
        }
        catch (error) {
            this.message(error.message || tr('提交失败，请稍后重试。'), true);
        }
        finally {
            this.submitting = false;
            this.update();
        }
    }

    /** Takes a submit answer, either straight from the submit call or once the prompt was written in the background (F56): shows inline images,
     * starts tracking the task and shows the prompt used. <counted> says whether the tab-title progress already counts this task. */
    static acceptSubmitted(result, entry, sent, counted) {
        let tr = this.api.tr;
        if (result.images && result.images.length) {
            // Fully cached generations can finish during the submit call and return their images inline,
            // the same way the cut-out-only branch does; slower tasks deliver images through the queue poll.
            for (let url of result.images) {
                this.results.unshift(url);
                this.resultGroups.set(url, entry.product);
            }
            this.renderResults();
        }
        entry.server = result.cutout_only ? '' : String(result.execution_server || '');
        if (result.task_id != null) {
            entry.taskId = result.task_id;
            entry.stage = 'queued';
            this.pending.add(result.task_id);
            this.taskProducts.set(result.task_id, entry.product);
            if (!counted) {
                this.api.titleAdd();
            }
            this.track();
        }
        else {
            entry.stage = 'done';
            if (counted) {
                this.api.titleDone();
            }
        }
        let status = result.cutout_only ? tr('已按平台要求把商品抠图放到纯白背景上（不调用生成模型），结果在下方。') : tr('已开始出图，完成后候选图会出现在下方。');
        if (entry.server) {
            status = tr('已开始出图（由“{0}”执行），完成后候选图会出现在下方。', entry.server);
        }
        this.api.$('simple-status').textContent = status;
        this.applyPromptResult(result, sent);
        this.promptNotice(result.prompt_notice_code);
        this.renderTasks();
    }

    /** Adds a single submission to the task list (newest first, the last ten are shown). */
    static addTask(entry) {
        this.tasks.unshift(entry);
        this.tasks = this.tasks.slice(0, 10);
        this.renderTasks();
    }

    /** The plain-language stage of a task (F56): writing the prompt, queued or running on a named server, done or failed. */
    static stageText(stage, server) {
        let tr = this.api.tr;
        if (stage == 'prompt') {
            return tr('正在生成提示词…');
        }
        if (stage == 'queued') {
            return server ? tr('排队中（{0}）', server) : tr('排队中');
        }
        if (stage == 'running') {
            return server ? tr('执行中（{0}）', server) : tr('执行中');
        }
        if (stage == 'done') {
            return server ? tr('已完成（{0}）', server) : tr('已完成');
        }
        return stage == 'cancelled' ? tr('已取消') : tr('失败');
    }

    /** Shows the single submissions of this page with their stage. */
    static renderTasks() {
        let el = this.api.el;
        let items = [];
        for (let entry of this.tasks) {
            let item = el('li', `simple-job${entry.stage == 'failed' ? ' is-failed' : ''}`);
            item.append(el('span', '', this.api.tr('{0}｜{1}', entry.product || this.api.tr('商品'), entry.template)), el('span', 'simple-job-state', this.stageText(entry.stage, entry.server)));
            if (entry.error) {
                item.title = entry.error;
            }
            items.push(item);
        }
        let list = this.api.$('simple-tasks');
        list.replaceChildren(...items);
        list.hidden = !items.length;
    }

    /** Asks how the submissions still waiting for their prompt are doing (F56) and takes over the ones that were submitted or failed. */
    static async pollPreparing() {
        let tr = this.api.tr;
        for (let [id, item] of Array.from(this.preparing)) {
            let result;
            try {
                result = await this.api.request('EcomCraftStudioPrepareStatus', { prepare_id: id });
            }
            catch (error) {
                if (error.code != 'task_not_found') {
                    continue;
                }
                result = { stage: 'failed', reason: tr('任务已过期，请重新出图。') };
            }
            if (result.stage == 'prompt') {
                continue;
            }
            this.preparing.delete(id);
            if (result.stage == 'failed') {
                let reason = result.reason_id ? this.api.serverErrorText(result.reason_id, result.reason) : (result.reason || tr('执行服务器没有返回结果。'));
                item.entry.stage = 'failed';
                item.entry.error = reason;
                this.api.titleDone();
                this.message(tr('出图失败：{0}', reason), true);
                this.renderTasks();
                continue;
            }
            this.acceptSubmitted(result, item.entry, item.sent, true);
        }
    }

    /** Explains a prompt fallback the server used (F37/F54); clears the banner when there was none. */
    static promptNotice(code) {
        let tr = this.api.tr;
        this.message(code == 'description_unused' ? tr('提示词优化服务暂时不可用，你的中文描述这次没有用上，已按所选风格出图。')
            : code == 'description_used' ? tr('提示词优化服务暂时不可用，已直接使用你的英文描述。')
            : code == 'prompt_untranslated' ? tr('提示词服务暂时不可用，你修改的中文提示词这次没能翻译成英文，已按所选产品类型和风格出图。') : '', false);
    }

    /** Polls the queue for this page's tasks and collects their results; keeps polling in the background (F43). */
    static track() {
        if (this.timer) {
            return;
        }
        this.timer = setInterval(async () => {
            if (this.polling) {
                return;
            }
            this.polling = true;
            try {
                if (this.preparing.size) {
                    await this.pollPreparing();
                }
                if (this.pending.size) {
                    await this.pollQueue();
                }
            }
            finally {
                this.polling = false;
            }
            if (!this.pending.size && !this.preparing.size && this.timer) {
                clearInterval(this.timer);
                this.timer = null;
                this.api.$('simple-status').textContent = this.results.length
                    ? this.api.tr('点选满意的图片，再点“下载所选”。所有结果也保存在作品画廊。')
                    : this.api.tr('出图完成后，候选图会出现在这里。点选满意的图片后下载。');
            }
        }, 3000);
    }

    /** Reads the queue for this page's submitted tasks: marks running ones and collects the results of finished ones. */
    static async pollQueue() {
        let queue;
        try {
            queue = await this.api.request('EcomCraftStudioQueue');
        }
        catch (error) {
            return;
        }
        let mine = (queue.tasks || []).filter(task => task.mine && task.id != null);
        let running = new Set(mine.map(task => task.id));
        let executing = new Set(mine.filter(task => task.state == 'running').map(task => task.id));
        let finished = new Map((queue.finished_tasks || []).map(task => [task.id, task]));
        for (let id of Array.from(this.pending)) {
            let entry = this.tasks.find(task => task.taskId == id);
            let outcome = finished.get(id);
            if (running.has(id) || !outcome) {
                if (entry && executing.has(id)) {
                    entry.stage = 'running';
                }
                continue;
            }
            this.pending.delete(id);
            this.api.titleDone();
            if (entry) {
                entry.server = outcome.execution_server || entry.server;
                entry.stage = outcome.state == 'failed' ? 'failed' : 'done';
            }
            if (outcome.state == 'failed') {
                let reason = outcome.error_id ? this.api.serverErrorText(outcome.error_id, outcome.error) : (outcome.error || this.api.tr('执行服务器没有返回结果。'));
                if (entry) {
                    entry.error = reason;
                }
                this.message(this.api.tr('出图失败：{0}', reason), true);
            }
            else {
                for (let url of outcome.images || []) {
                    this.results.unshift(url);
                    this.resultGroups.set(url, this.taskProducts.get(id) || '');
                }
                this.renderResults();
            }
        }
        this.renderTasks();
    }

    /** Address of a result as returned by the server ("View/user/..." or "Output/..."). */
    static resultUrl(url) {
        return '/' + String(url).split('/').map(part => encodeURIComponent(part)).join('/');
    }

    /** Shows the candidates as selectable tiles. */
    static renderResults() {
        let el = this.api.el;
        let tiles = [];
        for (let url of this.results) {
            let button = el('button', 'simple-result');
            button.type = 'button';
            let picked = this.selected.has(url);
            button.setAttribute('aria-pressed', picked ? 'true' : 'false');
            button.setAttribute('aria-label', this.api.tr('选择 {0}', url.split('/').pop()));
            let video = /\.(mp4|webm)$/i.test(url);
            let media = el(video ? 'video' : 'img');
            media.src = this.resultUrl(url);
            if (video) {
                media.muted = true;
                media.preload = 'metadata';
            }
            else {
                media.alt = '';
                media.loading = 'lazy';
            }
            button.append(media, el('span', 'simple-result-check', picked ? '✓' : ''));
            button.addEventListener('click', () => {
                if (this.selected.has(url)) {
                    this.selected.delete(url);
                }
                else {
                    this.selected.add(url);
                    // Picked candidates are kept forever (F39); the server drops them from the cleanup registry.
                    this.api.request('EcomCraftStudioMarkKept', { images: [url] }).catch(() => {
                        // The server also marks them when downloading or exporting; a lost notice here is harmless.
                    });
                }
                this.renderResults();
                this.update();
            });
            tiles.push(button);
        }
        this.api.$('simple-results').replaceChildren(...tiles);
        this.renderExport();
    }

    /** Downloads the selected candidates one by one through the browser; downloaded works are kept forever (F39). */
    static download() {
        if (this.selected.size) {
            this.api.request('EcomCraftStudioMarkKept', { images: Array.from(this.selected) }).catch(() => {
                // Exporting or picking again still marks them kept.
            });
        }
        for (let url of this.selected) {
            let link = document.createElement('a');
            link.href = this.resultUrl(url);
            link.download = url.split('/').pop();
            document.body.append(link);
            link.click();
            link.remove();
        }
    }
}
