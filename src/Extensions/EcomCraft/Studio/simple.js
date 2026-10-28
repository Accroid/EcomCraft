'use strict';

/**
 * Three-step simple mode (F07), the default view: 1. upload a product photo, 2. pick platform, image type (template) and style with an optional
 * one-line description, 3. pick and download candidates. There is no prompt box: the server turns the style and description into the prompt
 * and fills the template's product image field. Professional parameters stay in the administrators' professional mode.
 * studio.js hands over its helpers through SimpleMode.attach(); all text goes through the translation table.
 */
class SimpleMode {
    static api = null;
    static data = { platforms: [], templates: [], styles: [] };
    static choice = { platform: '', template: '', style: '' };
    static product = null;
    static products = [];
    static library = [];
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
    static timer = null;
    static results = [];
    static selected = new Set();
    static maxProductBytes = 5 * 1024 * 1024;
    static placement = { x: 0.5, y: 0.5, size: 0.7 };
    static cutting = false;
    static drag = null;
    static storageKey = 'ecomcraft.simple';

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

    /** Which views an account sees: everyone gets the simple mode; only administrators get the professional mode with sampler, LoRA and step settings. */
    static viewAccess(catalog) {
        return { simple: true, pro: Boolean(catalog && catalog.can_manage_settings) };
    }

    /** Receives the helpers of studio.js and binds the controls. */
    static attach(api) {
        this.api = api;
        let $ = api.$;
        let drop = $('simple-drop');
        $('simple-file').addEventListener('change', e => {
            for (let file of Array.from(e.target.files || [])) {
                this.readFile(file);
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
                this.readFile(file);
            }
        });
        $('simple-clear').addEventListener('click', () => this.clearProduct());
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
            this.update();
        });
        $('simple-generate').addEventListener('click', () => this.generate());
        $('simple-generate-set').addEventListener('click', () => this.generateSet());
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
            }
            catch (error) {
                // A damaged preference only resets the remembered choice.
            }
        }
    }

    /** Remembers the platform and style in this browser (never the photo or description). */
    static remember() {
        this.api.writeStorage(this.storageKey, JSON.stringify({ platform: this.choice.platform, style: this.choice.style }));
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
        this.update();
    }

    /** Reads platforms, templates and styles from the server. */
    static async load() {
        if (this.loading || !this.api.state.session) {
            return;
        }
        this.loading = true;
        let language = I18n.current;
        try {
            let [platforms, templates, styles] = await Promise.all([
                this.api.request('EcomCraftStudioPlatforms', { language }),
                this.api.request('EcomCraftStudioTemplates', { language }),
                this.api.request('EcomCraftStudioStyles', { language })
            ]);
            this.data.platforms = platforms.platforms || [];
            this.data.templates = templates.templates || [];
            this.data.styles = styles.styles || [];
            if (!this.data.styles.some(style => style.id == this.choice.style)) {
                this.choice.style = this.data.styles.length ? this.data.styles[0].id : '';
            }
            this.loaded = true;
            this.message('', false);
        }
        catch (error) {
            this.message(error.message || this.api.tr('无法读取模板和风格，请刷新页面重试。'), true);
        }
        finally {
            this.loading = false;
        }
        this.renderPlatforms();
        this.renderTemplates();
        this.renderStyles();
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

    /** Shows saved products as thumbnails; choosing one skips uploading and cutting out again. */
    static renderLibrary() {
        let el = this.api.el;
        let items = [];
        for (let saved of this.library) {
            let button = el('button', 'simple-product');
            button.type = 'button';
            button.title = saved.name;
            button.setAttribute('aria-label', this.api.tr('选择商品 {0}', saved.name));
            let image = el('img');
            image.src = saved.cutout_image || saved.image;
            image.alt = '';
            image.loading = 'lazy';
            button.append(image);
            button.addEventListener('click', () => this.useSaved(saved));
            items.push(button);
        }
        this.api.$('simple-library-list').replaceChildren(...items);
        this.api.$('simple-library').hidden = items.length == 0;
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
        let product = { id: saved.id, name: saved.name, data: null, source: saved.image, cutout: saved.cutout_image || null, width: 0, height: 0 };
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

    /** Fills the platform list; "all platforms" shows every template. */
    static renderPlatforms() {
        let select = this.api.$('simple-platform');
        let all = new Option(this.api.tr('全部平台'), '');
        let options = [all];
        for (let platform of this.data.platforms) {
            options.push(new Option(platform.name, platform.id));
        }
        select.replaceChildren(...options);
        if (!this.data.platforms.some(platform => platform.id == this.choice.platform)) {
            this.choice.platform = '';
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

    /** Shows the styles; the choice is remembered in this browser. */
    static renderStyles() {
        let cards = [];
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

    /** Reads the product photo into memory; it is sent only with the generation request. */
    static readFile(file) {
        let tr = this.api.tr;
        if (!file) {
            return;
        }
        if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type) || file.size > this.maxProductBytes) {
            this.message(tr('请选择不超过 5 MB 的 PNG、JPEG 或 WebP 图片。文件尚未上传。'), true);
            return;
        }
        let reader = new FileReader();
        reader.onload = async () => {
            if (this.products.length >= 20) {
                this.message(this.api.tr('一次最多 20 张商品图。'), true);
                return;
            }
            this.product = { id: null, name: file.name, data: String(reader.result), source: String(reader.result), cutout: null, width: 0, height: 0 };
            this.products.push(this.product);
            try {
                let image = await this.loadImage(this.product.data);
                this.product.width = image.naturalWidth;
                this.product.height = image.naturalHeight;
                let canvas = document.createElement('canvas');
                canvas.width = Math.min(512, image.naturalWidth);
                canvas.height = Math.max(1, Math.round(image.naturalHeight * canvas.width / Math.max(1, image.naturalWidth)));
                let context = canvas.getContext('2d');
                context.drawImage(image, 0, 0, canvas.width, canvas.height);
                if (this.hasTransparency(context.getImageData(0, 0, canvas.width, canvas.height))) {
                    this.product.cutout = this.product.data;
                }
            }
            catch (error) {
                // Without a readable preview the photo can still be used by templates that do not preserve the product.
            }
            this.renderCutout();
            let preview = this.api.$('simple-preview');
            preview.src = this.product.data;
            preview.hidden = false;
            this.api.$('simple-drop-text').textContent = file.name;
            this.api.$('simple-clear').hidden = false;
            this.message('', false);
            this.renderProducts();
            this.renderProductMeta();
            this.update();
            this.saveToLibrary(this.product);
        };
        reader.onerror = () => this.message(tr('无法读取文件，请重新选择。'), true);
        reader.readAsDataURL(file);
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
        if (!this.choice.style) {
            return '请选择风格。';
        }
        if (this.submitting) {
            return '提交中…';
        }
        return '';
    }

    /** Enables the buttons and shows what is missing. */
    static update() {
        if (!this.api) {
            return;
        }
        let blocker = this.blocker();
        this.api.$('simple-generate').disabled = Boolean(blocker);
        let set = this.setTemplates();
        let setReady = this.loaded && this.products.length > 0 && set.length > 0 && Boolean(this.choice.style) && !this.submitting;
        this.api.$('simple-generate-set').disabled = !setReady;
        this.api.$('simple-set-hint').textContent = set.length
            ? this.api.tr('整套包含：{0}；{1} 张商品图 × {2} 种 × 每种 {3} 张。', set.map(template => template.name).join(this.api.tr('、')), this.products.length, set.length, Number(this.api.$('simple-count').value) || 1)
            : this.api.tr('这个平台还没有可用的套图模板。');
        this.api.$('simple-hint').textContent = blocker ? this.api.tr(blocker) : '';
        this.api.$('simple-download').disabled = this.selected.size == 0;
    }

    /** Shows the uploaded product photos as thumbnails when there is more than one; clicking one makes it the current photo. */
    static renderProducts() {
        let el = this.api.el;
        let list = this.api.$('simple-products');
        let items = [];
        for (let [index, product] of this.products.entries()) {
            let button = el('button', 'simple-product');
            button.type = 'button';
            button.setAttribute('aria-pressed', product == this.product ? 'true' : 'false');
            button.setAttribute('aria-label', this.api.tr('商品图 {0}', index + 1));
            let image = el('img');
            image.src = product.cutout || product.source || product.data;
            image.alt = '';
            button.append(image);
            button.addEventListener('click', () => this.selectProduct(product));
            items.push(button);
        }
        list.replaceChildren(...items);
        list.hidden = this.products.length < 2;
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
        if (!this.products.length || this.submitting) {
            return;
        }
        let payload = {
            language: I18n.current,
            platform: this.choice.platform,
            set_id: 'standard',
            style_id: this.choice.style,
            description: this.api.$('simple-description').value.trim(),
            candidates: Number(this.api.$('simple-count').value) || 1,
            products: this.products.map(product => product.id
                ? { product_id: product.id, placement: this.clampPlacement(this.placement) }
                : { image: product.cutout || product.data, placement: this.clampPlacement(this.placement) })
        };
        this.submitting = true;
        this.update();
        this.batchProducts = this.products.map(product => product.name || '');
        try {
            let batch = await this.api.request('EcomCraftStudioGenerateSet', payload);
            this.batch = batch;
            this.renderBatch();
            this.trackBatch();
            this.api.$('simple-status').textContent = tr('已开始出整套图：共 {0} 个任务，会分配到空闲的执行服务器上并行完成。', (batch.jobs || []).length);
        }
        catch (error) {
            this.message(error.message || tr('提交失败，请稍后重试。'), true);
        }
        finally {
            this.submitting = false;
            this.update();
        }
    }

    /** Polls the batch until every task has ended, adding finished candidates to the results. */
    static trackBatch() {
        if (this.batchTimer) {
            return;
        }
        this.batchTimer = setInterval(async () => {
            if (!this.batch || document.hidden) {
                return;
            }
            try {
                this.batch = await this.api.request('EcomCraftStudioBatchStatus', { batch_id: this.batch.batch_id });
            }
            catch (error) {
                return;
            }
            this.renderBatch();
            if (this.batchFinished(this.batch.jobs)) {
                clearInterval(this.batchTimer);
                this.batchTimer = null;
                this.api.$('simple-status').textContent = this.api.tr('点选满意的图片，再点“下载所选”。所有结果也保存在作品画廊。');
            }
        }, 3000);
    }

    /** Whether every task of a batch has ended (done, failed or cancelled). */
    static batchFinished(jobs) {
        return !(jobs || []).some(job => job.state == 'queued' || job.state == 'running');
    }

    /** Shows each task's state with a retry button for failed ones, and adds finished images to the candidates. */
    static renderBatch() {
        let tr = this.api.tr;
        let el = this.api.el;
        let jobs = this.batch?.jobs || [];
        this.api.$('simple-batch').hidden = !jobs.length;
        let labels = { queued: '排队中', running: '执行中', done: '已完成', failed: '失败', cancelled: '已取消' };
        let items = [];
        let added = false;
        for (let job of jobs) {
            let item = el('li', `simple-job${job.state == 'failed' ? ' is-failed' : ''}`);
            let name = tr('商品 {0}｜{1}', job.product + 1, job.template_name || job.template);
            let state = tr(labels[job.state] || job.state);
            if (job.state == 'done' && job.execution_server) {
                state = tr('已完成（{0}）', job.execution_server);
            }
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
        this.api.$('simple-batch-cancel').hidden = !jobs.some(job => job.state == 'queued');
        if (added) {
            this.renderResults();
        }
    }

    /** Retries one failed or cancelled task. */
    static async retryJob(id) {
        try {
            this.batch = await this.api.request('EcomCraftStudioBatchRetry', { batch_id: this.batch.batch_id, job_id: id });
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
            description: this.api.$('simple-description').value.trim(),
            images: Number(this.api.$('simple-count').value) || 1,
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
        try {
            let result = await this.api.request('EcomCraftStudioGenerate', payload);
            if (result.task_id != null) {
                this.pending.add(result.task_id);
                this.taskProducts.set(result.task_id, this.product ? this.product.name : '');
                this.track();
            }
            let status = tr('已开始出图，完成后候选图会出现在下方。');
            if (result.execution_server) {
                status = tr('已开始出图（由“{0}”执行），完成后候选图会出现在下方。', result.execution_server);
            }
            this.api.$('simple-status').textContent = status;
            this.message(result.prompt_notice_code == 'description_unused' ? tr('提示词优化服务暂时不可用，你的中文描述这次没有用上，已按所选风格出图。')
                : result.prompt_notice_code == 'description_used' ? tr('提示词优化服务暂时不可用，已直接使用你的英文描述。') : '', false);
        }
        catch (error) {
            this.message(error.message || tr('提交失败，请稍后重试。'), true);
        }
        finally {
            this.submitting = false;
            this.update();
        }
    }

    /** Polls the queue for this page's tasks and collects their results. */
    static track() {
        if (this.timer) {
            return;
        }
        this.timer = setInterval(async () => {
            if (!this.pending.size || document.hidden) {
                return;
            }
            let queue;
            try {
                queue = await this.api.request('EcomCraftStudioQueue');
            }
            catch (error) {
                return;
            }
            let running = new Set((queue.tasks || []).filter(task => task.mine && task.id != null).map(task => task.id));
            let finished = new Map((queue.finished_tasks || []).map(task => [task.id, task]));
            for (let id of Array.from(this.pending)) {
                let outcome = finished.get(id);
                if (running.has(id) || !outcome) {
                    continue;
                }
                this.pending.delete(id);
                if (outcome.state == 'failed') {
                    let reason = outcome.error_id ? this.api.serverErrorText(outcome.error_id, outcome.error) : (outcome.error || this.api.tr('执行服务器没有返回结果。'));
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
            if (!this.pending.size) {
                clearInterval(this.timer);
                this.timer = null;
                this.api.$('simple-status').textContent = this.results.length
                    ? this.api.tr('点选满意的图片，再点“下载所选”。所有结果也保存在作品画廊。')
                    : this.api.tr('出图完成后，候选图会出现在这里。点选满意的图片后下载。');
            }
        }, 4000);
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
                }
                this.renderResults();
                this.update();
            });
            tiles.push(button);
        }
        this.api.$('simple-results').replaceChildren(...tiles);
        this.renderExport();
    }

    /** Downloads the selected candidates one by one through the browser. */
    static download() {
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
