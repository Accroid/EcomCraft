'use strict';

/**
 * Three-step simple mode (F07), the default view: 1. upload a product photo, 2. pick platform, image type (template), product type, an optional
 * visual style and whether a model is needed (F53), 3. pick and download candidates. The only prompt text is the optional bilingual prompt panel (F54):
 * "AI expand" fills both languages, edited Chinese is translated by the server and edited English is sent as is; the server always adds the product type,
 * model and style fragments and fills the template's product image field. Professional parameters stay in the administrators' professional mode.
 * studio.js hands over its helpers through SimpleMode.attach(); all text goes through the translation table.
 */
class SimpleMode {
    /** Copies the static methods of a part class onto SimpleMode, so simple mode can live in several files while callers and this stay the same (R15). */
    static adopt(part) {
        for (let name of Object.getOwnPropertyNames(part)) {
            if (typeof part[name] == 'function' && !['length', 'name', 'prototype'].includes(name)) {
                this[name] = part[name];
            }
        }
    }

    static api = null;
    static data = { platforms: [], templates: [], styles: [], categories: [] };
    static choice = { platform: '', template: '', style: '', category: '', subtype: '', brandKit: '' };
    static needModel = false;
    static prompt = { zh: '', en: '', base: { zh: '', en: '' }, last: '', tab: 'zh', expanding: false };
    static promptUsed = null;
    static storyboard = { points: [''], shots: [], running: false };
    static selling = { layout: 'banner', running: false, accentTouched: false, prefilled: false };
    static brandKits = [];
    static brandEditor = null;
    static brandSaving = false;
    static product = null;
    static products = [];
    /** "Make again with the same settings" (F04): the template, seed and prompt of a filed work, applied to the next single generation with that template. */
    static redo = null;
    static library = [];
    static picker = '';
    static galleryItems = null;
    static galleryError = '';
    /** The optional image slots ticked for the chosen platform (F58); same-kind optional slots only add export targets, so only kinds not already in the default set are offered. */
    static optionalSlots = new Set();

    /** The task chosen at the top of the page (F61): 'reference' (follow a reference, the default), 'single' (one image type) or 'set' (the platform's set). */
    static chosenTask = 'reference';

    /** The tasks of the task picker, in display order (F61). */
    static taskKinds = ['reference', 'single', 'set'];

    /** Whether "More options" (style, model, brand kit, description and prompt) is unfolded; remembered in this browser (F61). */
    static moreOpen = false;

    /** Reference images of reference mode (F59): each { image, promptEn, promptZh, edited, queued, describing, failed }; one image is made per reference. */
    static references = [];

    /** References waiting to be described, in order: a local vision model reads one image at a time, so they are sent one by one rather than all at once. */
    static describeWaiting = [];

    /** Whether the description queue is being worked through. */
    static describeRunning = false;

    /** Whether image description works (F68): { state, provider, model, can_manage } from the server, state 'unknown' when it could not be asked, null before the first answer. */
    static vision = null;

    /** The vision models an administrator can switch between on this page (F68): { models, loading, saving, message, failed }; null before the first listing. */
    static visionModels = null;

    static exportOptions = { platforms: [], folder_available: false };
    /** The output size chosen before generating: 'auto' (the platform's slot size for each image kind), a preset 'WxH' or 'custom' with width and height. */
    static outputSize = { mode: 'auto', width: 1000, height: 1000 };
    /** Common output sizes offered besides the platform's own: width, height and the aspect label. */
    static sizePresets = [[800, 800, '1:1'], [1000, 1000, '1:1'], [1200, 1200, '1:1'], [750, 1000, '3:4'], [1080, 1440, '3:4'], [1080, 1920, '9:16'], [1920, 1080, '16:9']];
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

    /** Candidates starred on this page (F65); the star is the gallery's own, so they also show under "Starred" there. */
    static starred = new Set();

    /** Candidates whose star request is still running, so a second click waits. */
    static starring = new Set();
    static maxProductBytes = 5 * 1024 * 1024;
    static placement = { x: 0.5, y: 0.56, size: 0.55 };

    /** Which reference's placement the preview edits in reference mode (T79), or -1 for the product's own placement. */
    static placementRef = -1;

    /** The task the placement preview was last shown for, so it is only redrawn when the task changes. */
    static placementTask = null;
    static cutting = false;
    static drag = null;
    static storageKey = 'ecomcraft.simple';
    static preferredKey = 'ecomcraft.preferred-platforms';
    static quality = { min_side: 512, blur_variance: 30, min_cutout_ratio: 0.08 };

    static viewAccess(catalog) {
        return { simple: true, pro: Boolean(catalog) };
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
            this.setActivePlacement({ x: 0.5, y: 0.56, size: 0.55 });
            $('simple-size').value = '55';
            this.renderStage();
        });
        $('simple-size').addEventListener('input', e => {
            this.setActivePlacement(this.clampPlacement({ ...this.activePlacement(), size: Number(e.target.value) / 100 }));
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
            this.optionalSlots.clear();
            this.renderPlatformSlots();
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
        // Selling-point images (F47): the user's own text in a fixed layout, drawn by the server.
        for (let chip of Array.from($('simple-selling-layouts').querySelectorAll('button[data-layout]'))) {
            chip.addEventListener('click', () => {
                this.selling.layout = chip.dataset.layout;
                this.renderSelling();
            });
        }
        $('simple-selling-accent').addEventListener('input', () => {
            this.selling.accentTouched = true;
        });
        $('simple-selling-run').addEventListener('click', () => this.makeSellingImage());
        // Brand kits (F12): pick one for generation, or manage them in a dialog.
        $('simple-brand').addEventListener('change', e => {
            this.choice.brandKit = e.target.value;
            this.remember();
        });
        $('simple-brand-manage').addEventListener('click', e => this.openBrand(e.currentTarget));
        $('simple-redo-clear').addEventListener('click', () => this.clearRedo());
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
        $('simple-reference-add').addEventListener('click', () => this.addReferenceClick());
        $('simple-reference-file').addEventListener('change', e => {
            this.addReferenceFiles(Array.from(e.target.files || []));
            e.target.value = '';
        });
        $('simple-generate-reference').addEventListener('click', () => this.generateReference());
        // Task-first simple mode (F61): the chosen task decides which step-2 fields and which main button are shown.
        for (let button of Array.from($('simple-task-picker').querySelectorAll('button[data-task]'))) {
            button.addEventListener('click', () => this.setTask(button.dataset.task));
        }
        $('simple-more').addEventListener('toggle', () => {
            this.moreOpen = $('simple-more').open;
            this.remember();
        });
        $('simple-count').addEventListener('input', () => this.update());
        $('simple-count').addEventListener('change', () => this.update());
        $('simple-batch-cancel').addEventListener('click', () => this.cancelBatch());
        $('simple-product-name').addEventListener('change', e => this.renameProduct(e.target.value));
        $('simple-export-zip').addEventListener('click', () => this.exportSelected('zip'));
        $('simple-export-folder').addEventListener('click', () => this.exportSelected('folder'));
        $('simple-output-size').addEventListener('change', e => {
            this.outputSize.mode = e.target.value;
            let preset = /^(\d+)x(\d+)$/.exec(e.target.value);
            if (preset) {
                this.outputSize.width = Number(preset[1]);
                this.outputSize.height = Number(preset[2]);
            }
            this.remember();
            this.update();
        });
        for (let side of ['width', 'height']) {
            $('simple-output-' + side).addEventListener('input', e => {
                this.outputSize[side] = Number(String(e.target.value || '').trim());
                this.remember();
                this.update();
            });
        }
        $('simple-product-delete').addEventListener('click', () => this.deleteProduct());
        $('simple-size').addEventListener('change', () => this.savePlacement());
        $('simple-download').addEventListener('click', () => this.download());
        // One-way hand-off to the professional mode (F64): exactly one picked image.
        $('simple-refine').addEventListener('click', () => {
            let url = Array.from(this.selected)[0];
            if (this.selected.size == 1 && url) {
                this.api.refineInPro(url);
            }
        });
        let saved = api.readStorage(this.storageKey);
        if (saved) {
            try {
                let parsed = JSON.parse(saved);
                this.choice.platform = String(parsed.platform || '');
                this.choice.style = String(parsed.style || '');
                this.choice.category = String(parsed.category || '');
                this.choice.subtype = String(parsed.subtype || '');
                this.choice.brandKit = String(parsed.brand_kit || '');
                if (this.taskKinds.includes(parsed.task)) {
                    this.chosenTask = parsed.task;
                }
                this.moreOpen = parsed.more_open == true;
                let size = parsed.output_size;
                if (size && typeof size.mode == 'string' && Number.isInteger(size.width) && Number.isInteger(size.height)) {
                    this.outputSize = { mode: size.mode, width: size.width, height: size.height };
                }
            }
            catch (error) {
                // A damaged preference only resets the remembered choice.
            }
        }
        $('simple-more').open = this.moreOpen;
        this.renderTask();
    }

    /** Switches the task of the page (F61); uploaded product photos and references are kept, references only apply to "follow a reference". */
    static setTask(task) {
        if (!this.taskKinds.includes(task) || task == this.chosenTask) {
            return;
        }
        this.chosenTask = task;
        this.remember();
        this.update();
    }

    /** Marks the chosen task card and shows the step-2 fields of that task; CSS hides fields whose data-tasks does not name it (F61). */
    static renderTask() {
        let $ = this.api.$;
        $('view-simple').dataset.simpleTask = this.chosenTask;
        for (let button of Array.from($('simple-task-picker').querySelectorAll('button[data-task]'))) {
            button.setAttribute('aria-pressed', String(button.dataset.task == this.chosenTask));
        }
        let titles = { reference: '选平台和参考图', single: '选平台和图片类型', set: '选平台和产品类型' };
        $('simple-step2-text').textContent = this.api.tr(titles[this.chosenTask]);
        // Per-reference placements (T79) apply only to "follow a reference": the preview follows the task when it changes.
        if (this.placementTask != this.chosenTask) {
            this.placementTask = this.chosenTask;
            this.renderCutout();
            this.choosePlacementRef(this.placementRef);
        }
    }

    /** Remembers the platform, product type, style, task and "More options" state in this browser (never the photo or description). */
    static remember() {
        this.api.writeStorage(this.storageKey, JSON.stringify({ platform: this.choice.platform, style: this.choice.style, category: this.choice.category, subtype: this.choice.subtype, brand_kit: this.choice.brandKit,
            task: this.chosenTask, more_open: this.moreOpen, output_size: this.outputSize }));
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
        this.renderVision();
        this.update();
    }

    static async load() {
        if (this.loading || !this.api.state.session) {
            return;
        }
        this.loading = true;
        this.loadVision();
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
        this.renderPlatformSlots();
    }

    /** Shows the chosen platform's slot hints (F58): where the white image goes, how many main images fit, and checkboxes for optional slots
     * whose kind is not already in the default set (same-kind optional slots only add export targets). */
    static renderPlatformSlots() {
        let tr = this.api.tr;
        let note = this.api.$('simple-platform-note');
        let box = this.api.$('simple-optional-slots');
        let platform = this.data.platforms.find(item => item.id == this.choice.platform);
        let slots = platform && platform.slots ? platform.slots : [];
        if (!slots.length) {
            note.hidden = true;
            box.hidden = true;
            box.replaceChildren();
            return;
        }
        let texts = [];
        let white = slots.find(slot => slot.kind == 'white_main');
        if (white && white.position && (!white.optional || this.optionalSlots.has(white.id))) {
            texts.push(tr('白底图建议放第 {0} 张。', white.position));
        }
        if (platform.main_image_count) {
            texts.push(tr('主图最多 {0} 张。', platform.main_image_count));
        }
        note.textContent = texts.join(' ');
        note.hidden = !texts.length;
        let defaultKinds = new Set(slots.filter(slot => !slot.optional).map(slot => slot.kind));
        let optional = slots.filter(slot => slot.optional && !defaultKinds.has(slot.kind));
        let rows = [];
        for (let slot of optional) {
            let input = document.createElement('input');
            input.type = 'checkbox';
            input.checked = this.optionalSlots.has(slot.id);
            input.addEventListener('change', () => {
                if (input.checked) {
                    this.optionalSlots.add(slot.id);
                }
                else {
                    this.optionalSlots.delete(slot.id);
                }
                this.renderPlatformSlots();
                this.update();
            });
            let row = this.api.el('label', 'simple-slot-opt');
            row.append(input, document.createTextNode(slot.name));
            rows.push(row);
        }
        box.replaceChildren(...rows);
        box.hidden = !rows.length;
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
                if (this.redo && this.redo.template != template.id) {
                    this.clearRedo();
                }
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
        let sizeProblem = this.outputSizeProblem();
        if (sizeProblem) {
            return sizeProblem;
        }
        if (this.submitting) {
            return '提交中…';
        }
        return '';
    }

    static update() {
        if (!this.api) {
            return;
        }
        this.renderQuality();
        this.renderTask();
        this.renderRedo();
        // Reference mode (F59): the other step-2 options stop applying and a single "generate by reference" button takes over.
        this.setReferenceOptions();
        let refMode = this.referenceMode();
        let refProblem = this.referenceProblem();
        this.api.$('simple-generate').hidden = this.chosenTask != 'single';
        this.api.$('simple-generate-set').hidden = this.chosenTask != 'set';
        this.api.$('simple-generate-reference').hidden = !refMode;
        this.api.$('simple-generate-reference').disabled = Boolean(refProblem || this.outputSizeProblem());
        this.renderOutputSize();
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
        expand.textContent = this.prompt.expanding ? this.waitingLabel(this.api.tr('AI 扩写中…'), 'expand', this.prompt.started) : this.api.tr('AI 扩写');
        this.renderPromptState();
        let blocker = this.blocker();
        this.api.$('simple-generate').disabled = Boolean(blocker);
        let countProblem = this.countProblem();
        let countHint = this.api.$('simple-count-hint');
        countHint.textContent = countProblem || this.api.tr('可输入 1–20 的正整数；单次出图和一键出整套最多共 20 张。');
        countHint.classList.toggle('simple-hint-error', Boolean(countProblem));
        let count = countProblem ? 0 : this.countValue();
        // Reference mode shows its status only on simple-set-hint right under the button; repeating it on simple-hint would render the same message twice.
        this.api.$('simple-hint').textContent = refMode
            ? ''
            : this.chosenTask == 'set'
            ? this.api.tr(this.setBlocker())
            : blocker
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
        let setReady = this.loaded && this.products.length > 0 && types > 0 && !storyboardProblem && Boolean(this.choice.subtype) && !countProblem && !this.outputSizeProblem() && total > 0 && total <= 20 && !this.submitting;
        let setButton = this.api.$('simple-generate-set');
        setButton.disabled = !setReady;
        setButton.textContent = shots ? this.api.tr('按分镜出整套') : this.api.tr('一键出整套');
        this.api.$('simple-set-hint').textContent = refMode
            ? this.api.tr(refProblem || '为 {0} 张商品图 × {1} 张参考图各出 1 张，预计共 {2} 张（上限 20）。', this.products.length, this.references.length, this.products.length * this.references.length)
            : storyboardProblem
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
        this.api.$('simple-storyboard-run').textContent = this.storyboard.running
            ? this.waitingLabel(this.api.tr('正在生成分镜…'), 'storyboard', this.storyboard.started) : this.api.tr('生成分镜');
        this.api.$('simple-download').disabled = this.selected.size == 0;
        let picked = Array.from(this.selected)[0] || '';
        this.api.$('simple-refine').hidden = this.selected.size != 1 || /\.(mp4|webm)$/i.test(picked) || !this.api.$('nav-create') || this.api.$('nav-create').hidden;
    }

    /** What a whole set still needs before it can start (F61): product photos and a product type; empty when only the count or the cap is left, which the set hint explains. */
    static setBlocker() {
        if (!this.api.state.session || !this.loaded) {
            return '正在读取模板和风格…';
        }
        if (!this.products.length) {
            return '请先上传商品图。';
        }
        if (!this.choice.subtype) {
            return '请选择产品类型。';
        }
        return '';
    }



}
