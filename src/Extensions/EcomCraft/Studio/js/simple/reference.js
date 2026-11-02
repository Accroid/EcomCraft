'use strict';

/**
 * Simple mode: reference mode: uploads, vision describing and generation (F59). Part of simple.js since R15; static methods are copied onto SimpleMode by adopt(), so callers and this stay unchanged.
 */
class SimpleReferencePart {
    /** Whether the direct-fusion trial's switch is on (F84): only meaningful in the "follow a reference" task. */
    static referenceEditOn() {
        return this.referenceMode() && this.editFusion;
    }

    /** Asks the server whether the direct-fusion trial can run (F84) and shows the reason when it cannot. */
    static async loadEditStatus() {
        if (this.editStatus || !this.api.state.session) {
            return;
        }
        try {
            let status = await this.api.request('EcomCraftStudioReferenceEditStatus', {});
            this.editStatus = { available: status.available == true, reason: String(status.reason || '') };
        }
        catch (error) {
            // Without an answer the switch stays usable; submitting then reports the server's own error.
            this.editStatus = { available: true, reason: '' };
        }
        this.renderReferences();
        this.update();
    }

    /** Opens the reference picker only when image description can work (F59/F68): without a usable vision model the guidance
     * below shows instead — its settings steps open the configuration directly — so references never sit undescribable.
     * The direct-fusion trial (F84) needs no vision model: its prompt is written by the server, so the picker opens at once. */
    static async addReferenceClick() {
        if (this.referenceEditOn()) {
            this.api.$('simple-reference-file').click();
            return;
        }
        if (!this.vision || this.vision.state == 'unknown') {
            await this.loadVision();
        }
        if (this.visionGuide()) {
            this.message(this.api.tr('还没有可用的视觉模型。请按下方提示配置（点“打开提示词优化设置”可直接进入），配好后回来添加参考图。'), true);
            this.renderVision();
            this.api.$('simple-vision').scrollIntoView({ block: 'nearest', behavior: 'smooth' });
            return;
        }
        this.api.$('simple-reference-file').click();
    }

    /** Adds reference image files (F59): at most 8 in total, each compressed like a product photo; every new one is described in the background.
     * References only apply to the "follow a reference" task and storyboards only to "make a full set" (F61), so neither clears the other. */
    static async addReferenceFiles(files) {
        let tr = this.api.tr;
        for (let file of files) {
            if (this.references.length >= 8) {
                this.message(tr('参考图最多 8 张，后面的没有添加。'), true);
                break;
            }
            if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type)) {
                this.message(tr('参考图请选择 PNG、JPEG 或 WebP 图片。'), true);
                continue;
            }
            try {
                let data = await this.blobToDataUrl(file);
                if (file.size > this.maxProductBytes) {
                    data = await this.compressImage(data, this.maxProductBytes);
                }
                this.references.push({ image: data, promptEn: '', promptZh: '', edited: false, queued: false, describing: false, failed: false });
            }
            catch (error) {
                this.message(tr('有参考图无法读取，请重新选择。'), true);
            }
        }
        this.renderReferences();
        this.update();
        // The trial (F84) never describes references: its prompt comes from the server.
        if (this.referenceEditOn()) {
            return;
        }
        this.queueMissingDescriptions();
    }

    /** Queues the description of every reference that still has no prompt and is not on its way to one (F59): the ones just added, and the ones
     * uploaded while the direct-fusion trial (F84) was on, whose descriptions the trial skipped — the same condition addReferenceFiles uses. */
    static queueMissingDescriptions() {
        for (let item of this.references) {
            if (!item.queued && !item.describing && !item.promptEn && !item.failed) {
                this.queueDescribe(item);
            }
        }
    }

    /** Puts one reference at the end of the description queue (also used by "Retry") and starts working through the queue if it is idle. */
    static queueDescribe(item) {
        if (item.queued || item.describing) {
            return;
        }
        item.queued = true;
        item.failed = false;
        this.describeWaiting.push(item);
        this.renderReferences();
        this.update();
        this.runDescribeQueue();
    }

    /** Describes the queued references one at a time, skipping any the user removed meanwhile. */
    static async runDescribeQueue() {
        if (this.describeRunning) {
            return;
        }
        this.describeRunning = true;
        try {
            while (this.describeWaiting.length) {
                let item = this.describeWaiting.shift();
                item.queued = false;
                if (!this.references.includes(item)) {
                    continue;
                }
                await this.describeReference(item);
            }
        }
        finally {
            this.describeRunning = false;
        }
    }

    /** Asks the server to describe one reference image (F59); fills the item's prompt unless the user edited it first. */
    static async describeReference(item) {
        item.describing = true;
        item.started = Date.now();
        item.failed = false;
        this.renderReferences();
        this.ensureDescribeTicker();
        try {
            let result = await this.api.request('EcomCraftStudioReferenceDescribe', { reference_images: [item.image] });
            let answer = (result.items || [])[0] || {};
            if (answer.prompt_en) {
                if (!item.edited) {
                    item.promptEn = String(answer.prompt_en);
                    item.promptZh = String(answer.prompt_zh || '');
                }
            }
            else {
                item.failed = true;
            }
        }
        catch (error) {
            item.failed = true;
            // The vision service may have gone away or been turned off; refresh the guidance (F68).
            this.loadVision();
        }
        finally {
            item.describing = false;
            this.renderReferences();
            this.update();
        }
    }

    /** Counts down the reference descriptions in progress every second (F70), updating only their status lines so prompts being edited stay untouched;
     * stops once none is left. */
    static ensureDescribeTicker() {
        if (this.stopDescribeTicker) {
            return;
        }
        this.stopDescribeTicker = this.startTicker(() => {
            let describing = this.references.filter(item => item.describing);
            if (!describing.length) {
                let stop = this.stopDescribeTicker;
                this.stopDescribeTicker = null;
                if (stop) {
                    stop();
                }
                return;
            }
            for (let item of describing) {
                if (item.statusEl) {
                    item.statusEl.textContent = this.waitingLabel(this.api.tr('描述中…'), 'describe', item.started);
                }
            }
        });
    }

    /** Asks the server whether image description can work (F68) and shows what is missing for "follow a reference". */
    static async loadVision() {
        try {
            let status = await this.api.request('EcomCraftStudioVisionStatus', {});
            this.vision = { state: String(status.state || 'unknown'), provider: String(status.provider || ''), model: String(status.model || ''), can_manage: status.can_manage == true };
        }
        catch (error) {
            // Without an answer nothing is blocked; describing a reference reports its own error.
            this.vision = { state: 'unknown', provider: '', model: '', can_manage: false };
        }
        this.renderVision();
        this.update();
        if (this.visionModelShown && this.vision.can_manage && !this.visionModels) {
            this.loadVisionModels();
        }
    }

    /** Lists the models the vision service offers, so an administrator can switch the vision model here (F68); the same setting as in the prompt enhancement settings. */
    static async loadVisionModels() {
        if (!this.vision?.can_manage || this.visionModels?.loading) {
            return;
        }
        this.visionModels = { models: this.visionModels?.models || [], loading: true, saving: false, message: '', failed: false };
        this.renderVisionModel();
        try {
            let result = await this.api.request('EcomCraftStudioVisionModels', {});
            let models = [];
            for (let name of result.models || []) {
                models.push(String(name));
            }
            this.visionModels.models = models;
            if (!models.length) {
                this.visionModels.message = this.api.tr('视觉服务里还没有能看图的模型。');
                this.visionModels.failed = true;
            }
        }
        catch (error) {
            this.visionModels.message = error.message;
            this.visionModels.failed = true;
        }
        finally {
            this.visionModels.loading = false;
            this.renderVisionModel();
        }
    }

    /** Switches the vision model used for the next descriptions (F68); a failure keeps the previous model and says why. */
    static async switchVisionModel(model) {
        let models = this.visionModels;
        if (!models || models.saving || !model || model == this.vision?.model) {
            return;
        }
        models.saving = true;
        models.failed = false;
        models.message = this.api.tr('正在切换看图模型…');
        this.renderVisionModel();
        let switched = false;
        try {
            let result = await this.api.request('EcomCraftStudioSetVisionModel', { model });
            this.vision.model = String(result.model || model);
            models.message = this.api.tr('已切换为 {0}，之后描述的参考图都用它。', this.vision.model);
            switched = true;
        }
        catch (error) {
            models.message = error.message;
            models.failed = true;
        }
        finally {
            models.saving = false;
            this.renderVisionModel();
        }
        if (switched) {
            // The new model may not be installed on the vision service yet; the guidance below says so.
            this.loadVision();
        }
    }

    /** 出图页是否仍显示看图模型。2026-11-02（P1b）起从界面隐藏：运营和老板不在这页接触模型，管理员改在“提示词优化设置”里
     * 查看和切换。切换代码与接口保留（隐藏不删除，A30），需要恢复时把这面旗设回 true 即可。 */
    static visionModelShown = false;

    /** Shows the vision model under the references (F68): administrators get a switcher, other accounts only see the model name. Hidden since P1b. */
    static renderVisionModel() {
        if (!this.api) {
            return;
        }
        let tr = this.api.tr;
        let el = this.api.el;
        let box = this.api.$('simple-vision-model');
        if (!this.visionModelShown) {
            box.hidden = true;
            box.replaceChildren();
            return;
        }
        let vision = this.vision;
        if (!vision || (!vision.model && !vision.can_manage)) {
            box.hidden = true;
            box.replaceChildren();
            return;
        }
        box.hidden = false;
        if (!vision.can_manage) {
            let text = el('span', 'hint', tr('看图模型：{0}', vision.model));
            text.dataset.i18nSkip = '';
            box.replaceChildren(text);
            return;
        }
        let state = this.visionModels || { models: [], loading: false, saving: false, message: '', failed: false };
        let label = el('label', '', tr('看图模型'));
        label.htmlFor = 'simple-vision-model-select';
        let select = el('select');
        select.id = 'simple-vision-model-select';
        select.dataset.i18nSkip = '';
        let names = [...state.models];
        // A saved model the service does not list stays visible, so the switcher never pretends another one is in use.
        if (vision.model && !names.includes(vision.model)) {
            names.unshift(vision.model);
        }
        for (let name of names) {
            select.append(new Option(name, name));
        }
        select.value = vision.model;
        select.disabled = state.loading || state.saving || names.length < 2;
        select.addEventListener('change', () => this.switchVisionModel(select.value));
        let note = el('span', 'hint simple-vision-model-note', state.loading ? tr('正在读取看图模型列表…') : state.message);
        note.classList.toggle('failed', state.failed);
        let parts = [label, select, note];
        if (!state.loading && !state.saving) {
            let reload = el('button', 'link-btn', tr('刷新列表'));
            reload.type = 'button';
            reload.addEventListener('click', () => this.loadVisionModels());
            parts.push(reload);
        }
        box.replaceChildren(...parts);
    }

    /** The guidance for an image description problem (F68): a title and the steps; administrators get the steps to fix it, other accounts are asked to contact one. */
    static visionGuide() {
        let tr = this.api.tr;
        let vision = this.vision;
        if (!vision || vision.state == 'ready' || vision.state == 'unknown') {
            return null;
        }
        let ollama = vision.provider == 'ollama';
        // The one-click scripts (F69) install Ollama when needed and pull the model; the manual steps follow.
        let script = tr('最省事：在服务器的 EcomCraft 目录运行一键安装脚本。Windows：powershell -ExecutionPolicy Bypass -File launchtools\\install-vision-model-windows.ps1 -Model {0}；Linux：bash launchtools/install-vision-model-linux.sh {0}', vision.model);
        if (!vision.can_manage) {
            return {
                title: vision.state == 'off'
                    ? tr('照参考图做需要看图描述，管理员已关闭这项功能。')
                    : tr('照参考图做需要视觉模型，服务器还没有配置好。'),
                steps: [tr('请联系管理员按页面提示配置；期间可以先用“做一张图”或“出一整套”。')]
            };
        }
        if (vision.state == 'off') {
            return { title: tr('照参考图做需要看图描述，它已在“提示词优化设置”中关闭。'), steps: [tr('打开“提示词优化设置”，勾选“允许看图描述”并保存。'), tr('回到本页点“重新检查”。')], settings_steps: [0] };
        }
        if (vision.state == 'no_model') {
            return { title: tr('还没有为看图描述设置视觉模型。'), steps: [tr('打开“提示词优化设置”，为看图描述选择一个视觉模型并保存。'), tr('回到本页点“重新检查”。')], settings_steps: [0] };
        }
        if (vision.state == 'unreachable') {
            return ollama
                ? { title: tr('连不上 Ollama 视觉服务。'), steps: [script, tr('或者手动：在这台服务器上安装 Ollama：打开 https://ollama.com/download 下载并安装，安装后它会在后台自动运行。'), tr('打开命令行运行：ollama pull {0}', vision.model), tr('下载完成后回到本页点“重新检查”。')] }
                : { title: tr('连不上视觉服务 {0}。', vision.provider), steps: [tr('打开“提示词优化设置”，检查视觉服务的 Key 和模型是否可用。'), tr('也可以改用本机 Ollama：免费，图片不出本机。')], settings_steps: [0] };
        }
        return ollama
            ? { title: tr('Ollama 已在运行，但还没有视觉模型 {0}。', vision.model), steps: [script, tr('或者在服务器上打开命令行运行：ollama pull {0}', vision.model), tr('下载完成后回到本页点“重新检查”；也可以在“提示词优化设置”中改选已安装的视觉模型。')] }
            : { title: tr('视觉服务里没有模型 {0}。', vision.model), steps: [tr('打开“提示词优化设置”，改选可用的视觉模型。')], settings_steps: [0] };
    }

    /** Shows the image description guidance under the references (F68); hidden when description works. */
    static renderVision() {
        if (!this.api) {
            return;
        }
        let tr = this.api.tr;
        let el = this.api.el;
        this.renderVisionModel();
        let box = this.api.$('simple-vision');
        let guide = this.visionGuide();
        box.hidden = !guide;
        if (!guide) {
            box.replaceChildren();
            return;
        }
        let list = el('ol', 'simple-vision-steps');
        // Steps that open the prompt enhancement settings link exactly those words to the dialog (F68);
        // a translation that rephrased the name falls back to the whole step being the button.
        let settingsSteps = new Set(guide.settings_steps || []);
        let linkName = tr('提示词优化设置');
        for (let [index, step] of guide.steps.entries()) {
            let item = el('li');
            let at = settingsSteps.has(index) && this.vision.can_manage && typeof this.api.openEnhancerSettings == 'function'
                ? step.toLowerCase().indexOf(linkName.toLowerCase()) : -1;
            if (at >= 0) {
                if (at > 0) {
                    item.append(document.createTextNode(step.slice(0, at)));
                }
                let open = el('button', 'link-btn', step.slice(at, at + linkName.length));
                open.type = 'button';
                open.setAttribute('aria-label', tr('打开提示词优化设置'));
                open.addEventListener('click', () => this.api.openEnhancerSettings());
                item.append(open, document.createTextNode(step.slice(at + linkName.length)));
            }
            else if (settingsSteps.has(index) && this.vision.can_manage && typeof this.api.openEnhancerSettings == 'function') {
                let open = el('button', 'link-btn', step);
                open.type = 'button';
                open.addEventListener('click', () => this.api.openEnhancerSettings());
                item.append(open);
            }
            else {
                item.textContent = step;
            }
            list.append(item);
        }
        let parts = [el('p', 'simple-vision-title', guide.title), list];
        if (this.vision.can_manage) {
            let again = el('button', 'btn btn-sm', tr('重新检查'));
            again.type = 'button';
            again.addEventListener('click', () => this.loadVision());
            parts.push(again);
        }
        box.replaceChildren(...parts);
    }

    /** Whether reference mode is on: the "follow a reference" task is chosen (F61); its references are required (F59). */
    static referenceMode() {
        return this.chosenTask == 'reference';
    }

    /** Opens the references full size in the gallery viewer, starting at one of them. */
    static openReference(index, opener) {
        let items = [];
        for (let [at, item] of this.references.entries()) {
            items.push({ url: item.image, name: this.api.tr('参考图 {0}', at + 1) });
        }
        this.api.openImages(items, index, opener);
    }

    /** Shows the reference thumbnails and the per-reference prompt list (F59): the English prompt is editable, the Chinese translation read-only.
     * With the direct-fusion trial on (F84) the prompts do not apply, so only the thumbnails show and the placement picker and its note hide. */
    static renderReferences() {
        if (!this.api) {
            return;
        }
        let tr = this.api.tr;
        let el = this.api.el;
        let edit = this.referenceEditOn();
        let thumbs = [];
        for (let [index, item] of this.references.entries()) {
            let wrap = el('span', 'simple-ref-thumb');
            // Clicking a reference opens it full size in the same viewer as the gallery, with previous / next across the references.
            let open = el('button', 'simple-ref-open');
            open.type = 'button';
            open.setAttribute('aria-label', tr('放大参考图 {0}', index + 1));
            open.addEventListener('click', () => this.openReference(index, open));
            let img = el('img');
            img.src = item.image;
            img.alt = tr('参考图 {0}', index + 1);
            open.append(img);
            let remove = el('button', 'link-btn');
            remove.type = 'button';
            remove.textContent = tr('删除');
            remove.setAttribute('aria-label', tr('删除参考图 {0}', index + 1));
            remove.addEventListener('click', () => {
                this.references.splice(index, 1);
                this.placementRef = -1;
                this.renderReferences();
                this.renderStage();
                this.update();
            });
            wrap.append(open, remove);
            thumbs.push(wrap);
        }
        this.api.$('simple-references').replaceChildren(...thumbs);
        let block = this.api.$('simple-reference-list-block');
        block.hidden = edit || !this.references.length;
        let rows = [];
        for (let [index, item] of this.references.entries()) {
            let row = el('div', 'simple-ref-item');
            let img = el('img', 'simple-ref-item-img');
            img.src = item.image;
            img.alt = '';
            let mid = el('div', 'simple-ref-item-main');
            let input = document.createElement('textarea');
            input.className = 'simple-ref-prompt';
            input.rows = 4;
            input.maxLength = 1500;
            input.spellcheck = false;
            input.value = item.promptEn;
            input.setAttribute('aria-label', tr('参考图 {0} 的提示词', index + 1));
            input.addEventListener('input', () => {
                item.promptEn = input.value;
                item.edited = true;
                this.update();
            });
            let zh = el('p', 'hint simple-ref-zh');
            if (item.describing) {
                zh.textContent = this.waitingLabel(tr('描述中…'), 'describe', item.started);
            }
            else if (item.queued) {
                zh.textContent = tr('排队等待描述…');
            }
            else {
                zh.textContent = item.failed ? tr('没有描述成功，可点“重试”。')
                    : item.promptZh || (item.promptEn && !item.edited ? tr('中文翻译没有完成，不影响出图；英文提示词可以直接使用。') : '');
            }
            item.statusEl = zh;
            mid.append(input, zh);
            let retry = el('button', 'link-btn');
            retry.type = 'button';
            retry.textContent = tr('重试');
            retry.hidden = !(item.failed && !item.describing && !item.queued);
            retry.addEventListener('click', () => this.queueDescribe(item));
            row.append(img, mid, retry);
            rows.push(row);
        }
        this.api.$('simple-reference-list').replaceChildren(...rows);
        this.api.$('simple-reference-add').hidden = this.references.length >= 8;
        this.renderPlacementPicker();
        this.api.$('simple-reference-edit-note').hidden = !edit;
        this.api.$('simple-ref-place').hidden = edit || !this.references.length;
        this.api.$('simple-reference-place-note').hidden = edit || !this.references.length;
        this.api.$('simple-reference-hint').textContent = edit
            ? tr('试用：结果由模型直接输出，未贴回原商品；参考图仅用于本次生成，不会保存到商品库或项目。请使用有权使用的参考图。')
            : tr('参考图只用于借鉴构图与光线，请使用有权使用的图片（自己拍摄、已授权或无版权限制）；生成结果请勿与参考图雷同。');
        if (edit && this.editStatus && !this.editStatus.available) {
            this.api.$('simple-reference-edit-note').hidden = false;
            this.api.$('simple-reference-edit-note').textContent = tr('直接融合试用暂不可用：{0}', this.editStatus.reason);
        }
    }

    /** Turns the other step-2 options on or off (F59): in reference mode they do not apply (and are hidden by the task, F61).
     * The optional-slot box is left out: it already hides itself via data-tasks, and disabling it here would grey out the whole
     * platform field (its closest .fld), which every task still needs for choosing a platform. */
    static setReferenceOptions() {
        let off = this.referenceMode();
        let ids = ['simple-templates', 'simple-categories', 'simple-subtypes', 'simple-styles', 'simple-model',
            'simple-brand', 'simple-brand-manage', 'simple-description', 'simple-expand', 'simple-prompt-zh', 'simple-prompt-en',
            'simple-count', 'simple-storyboard', 'simple-storyboard-run', 'simple-shot-add'];
        for (let id of ids) {
            let node = this.api.$(id);
            if (!node) {
                continue;
            }
            if (node.tagName != 'DETAILS') {
                node.disabled = off;
            }
            else if (off) {
                node.open = false;
            }
            for (let control of Array.from(node.querySelectorAll('button, input, select, textarea'))) {
                control.disabled = off;
            }
            let field = node.closest('.fld') || node;
            field.classList.toggle('simple-mode-off', off);
        }
    }

    /** Why "generate by reference" cannot run yet (F59); empty when it can. With the trial on (F84) no prompts or vision model are needed:
     * the references and the workflow's availability are all that matter. */
    static referenceProblem() {
        let tr = this.api.tr;
        if (!this.referenceMode()) {
            return '';
        }
        if (this.referenceEditOn()) {
            if (!this.references.length) {
                return '请添加参考图（1–8 张），每张参考图出 1 张。';
            }
            if (!this.products.length) {
                return '请先在第 1 步上传商品图。';
            }
            if (this.editStatus && !this.editStatus.available) {
                return tr('直接融合试用暂不可用：{0}', this.editStatus.reason);
            }
            let total = this.products.length * this.references.length;
            if (total > 20) {
                return tr('预计 {0} 张，超过 20 张总上限，请减少商品图或参考图。', total);
            }
            if (this.submitting) {
                return '提交中…';
            }
            return '';
        }
        if (this.visionGuide()) {
            return '照参考图做暂不可用，请按上方提示处理。';
        }
        if (!this.references.length) {
            return '请添加参考图（1–8 张），每张参考图出 1 张。';
        }
        if (!this.products.length) {
            return '请先在第 1 步上传商品图。';
        }
        if (this.references.some(item => item.describing || item.queued)) {
            return '还在描述参考图…';
        }
        if (this.references.some(item => !item.promptEn.trim())) {
            return '还有参考图没有提示词，请等待描述完成、重试或手动填写。';
        }
        let total = this.products.length * this.references.length;
        if (total > 20) {
            return tr('预计 {0} 张，超过 20 张总上限，请减少商品图或参考图。', total);
        }
        if (this.submitting) {
            return '提交中…';
        }
        return '';
    }

    /** Starts reference mode: one image per reference for every product, each with the prompt shown to the user (F59).
     * With the trial on (F84) the images go straight to the edit model: no per-reference prompts, no placements, one batch flag marks
     * the finished candidates as the trial's direct output. */
    static async generateReference() {
        let tr = this.api.tr;
        if (!this.products.length || this.submitting || this.referenceProblem()) {
            return;
        }
        let edit = this.referenceEditOn();
        let payload = {
            language: I18n.current,
            platform: this.choice.platform,
            output_size: this.outputSizeField(),
            products: this.products.map(product => product.id
                ? { product_id: product.id, placement: this.clampPlacement(this.placement) }
                : { image: product.cutout || product.data, placement: this.clampPlacement(this.placement) })
        };
        if (edit) {
            payload.reference_mode = 'edit';
            payload.reference_images = this.references.map(item => item.image);
        }
        else {
            payload.reference_prompts = this.references.map(item => item.promptEn.trim());
            payload.reference_placements = this.references.map(item => item.placement ? this.clampPlacement(item.placement) : null);
        }
        this.submitting = true;
        this.update();
        this.batchProducts = this.products.map(product => product.name || '');
        try {
            let batch = await this.api.request('EcomCraftStudioGenerateSet', payload);
            this.batch = batch;
            this.batchAt = Date.now();
            this.batchDone = 0;
            this.batchEdit = edit;
            this.api.titleAdd((batch.jobs || []).length);
            this.renderBatch();
            this.trackBatch();
            this.api.$('simple-status').textContent = edit
                ? tr('已开始直接融合（试用）：共 {0} 个任务，结果由模型直接输出，不会贴回原商品。', (batch.jobs || []).length)
                : tr('已按参考图开始出图：共 {0} 个任务，会分配到空闲的执行服务器上并行完成。', (batch.jobs || []).length);
            this.batchSent = this.promptSnapshot();
            this.batchPromptShown = true;
            this.message('', false);
        }
        catch (error) {
            this.message(error.message || tr('提交失败，请稍后重试。'), true);
        }
        finally {
            this.submitting = false;
            this.update();
        }
    }

    /** Reference uploads, the direct-fusion switch (F84) and the reference generation button (F59). */
    static bindReference() {
        let $ = this.api.$;
        $('simple-reference-add').addEventListener('click', () => this.addReferenceClick());
        $('simple-reference-file').addEventListener('change', e => {
            this.addReferenceFiles(Array.from(e.target.files || []));
            e.target.value = '';
        });
        $('simple-reference-edit').addEventListener('change', e => {
            this.editFusion = e.target.checked;
            this.editStatus = null;
            if (this.referenceEditOn()) {
                this.loadEditStatus();
            }
            else {
                // References uploaded while the trial was on were never described: closing the switch catches them up (F84, T123fix).
                this.queueMissingDescriptions();
            }
            this.renderReferences();
            this.update();
        });
        $('simple-generate-reference').addEventListener('click', () => this.generateReference());
    }
}
SimpleMode.adopt(SimpleReferencePart);
