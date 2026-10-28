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
    static loaded = false;
    static loading = false;
    static submitting = false;
    static pending = new Set();
    static timer = null;
    static results = [];
    static selected = new Set();
    static maxProductBytes = 5 * 1024 * 1024;
    static storageKey = 'ecomcraft.simple';

    /** Which views an account sees: everyone gets the simple mode; only administrators get the professional mode with sampler, LoRA and step settings. */
    static viewAccess(catalog) {
        return { simple: true, pro: Boolean(catalog && catalog.can_manage_settings) };
    }

    /** Receives the helpers of studio.js and binds the controls. */
    static attach(api) {
        this.api = api;
        let $ = api.$;
        let drop = $('simple-drop');
        $('simple-file').addEventListener('change', e => this.readFile(e.target.files[0]));
        drop.addEventListener('dragover', e => {
            e.preventDefault();
            drop.classList.add('is-over');
        });
        drop.addEventListener('dragleave', () => drop.classList.remove('is-over'));
        drop.addEventListener('drop', e => {
            e.preventDefault();
            drop.classList.remove('is-over');
            if (e.dataTransfer.files.length) {
                this.readFile(e.dataTransfer.files[0]);
            }
        });
        $('simple-clear').addEventListener('click', () => this.clearProduct());
        $('simple-platform').addEventListener('change', e => {
            this.choice.platform = e.target.value;
            this.remember();
            this.renderTemplates();
            this.update();
        });
        $('simple-generate').addEventListener('click', () => this.generate());
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
        reader.onload = () => {
            this.product = { name: file.name, data: String(reader.result) };
            let preview = this.api.$('simple-preview');
            preview.src = this.product.data;
            preview.hidden = false;
            this.api.$('simple-drop-text').textContent = file.name;
            this.api.$('simple-clear').hidden = false;
            this.message('', false);
            this.update();
        };
        reader.onerror = () => this.message(tr('无法读取文件，请重新选择。'), true);
        reader.readAsDataURL(file);
    }

    /** Forgets the product photo. */
    static clearProduct() {
        this.product = null;
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
        this.api.$('simple-hint').textContent = blocker ? this.api.tr(blocker) : '';
        this.api.$('simple-download').disabled = this.selected.size == 0;
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
        if (field && this.product) {
            params[field.id] = this.product.data;
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
        this.submitting = true;
        this.update();
        try {
            let result = await this.api.request('EcomCraftStudioGenerate', payload);
            if (result.task_id != null) {
                this.pending.add(result.task_id);
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
