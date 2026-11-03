'use strict';

/**
 * Simple mode: one-click set generation and its batch tracking. Part of simple.js since R15; static methods are copied onto SimpleMode by adopt(), so callers and this stay unchanged.
 */
class SimpleBatchPart {
    /** The set's templates for the chosen platform: composed by the platform's slot kinds when it defines slots (F58), matching the server's
     * composition; platforms without slots keep the old composition by set membership. */
    static setTemplates() {
        let visible = this.visibleTemplates();
        let platform = this.data.platforms.find(item => item.id == this.choice.platform);
        let slots = platform && platform.slots ? platform.slots : [];
        if (!slots.length) {
            return visible.filter(template => (template.sets || []).includes('standard'));
        }
        let kinds = [];
        for (let slot of slots) {
            if (slot.optional && !this.optionalSlots.has(slot.id)) {
                continue;
            }
            // Video covers only record specs and never join sets (T97, matching the server's SetRules.SetSlotKinds).
            if (slot.kind == 'video_cover') {
                continue;
            }
            if (!kinds.includes(slot.kind)) {
                kinds.push(slot.kind);
            }
        }
        let set = [];
        for (let kind of kinds) {
            for (let template of visible) {
                if ((template.sets || []).includes('standard') && template.slot_kind == kind) {
                    set.push(template);
                }
            }
        }
        return set;
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
            optional_slots: Array.from(this.optionalSlots),
            output_size: this.outputSizeField(),
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
            this.batchAt = Date.now();
            this.batchDone = 0;
            // Every path that starts a batch sets the trial flag: a set batch is never the direct-fusion trial, so a previous trial's
            // flag must not mark its results as trial output (T123fix).
            this.batchEdit = false;
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
                this.batchAt = Date.now();
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

    /** Which group a slot kind belongs to (T97, T98): the main-image kinds, the detail screens, or the slot-less tasks. */
    static slotGroup(kind) {
        let mainKinds = ['white_main', 'first_main', 'scene_main', 'selling', 'detail', 'model'];
        return mainKinds.includes(kind) ? 'main' : kind == 'detail_page' ? 'detail' : 'other';
    }

    /** A slot's display name, from the chosen platform's slot list (T98); falls back to the kind. */
    static slotLabel(slotId, kind) {
        let slot = this.platformSlots().find(item => item.id == slotId);
        return slot ? slot.name : (slotId || kind || '');
    }

    /** Shows each task's state with a retry button for failed ones, and adds finished images to the candidates. Slot batches (T98) group their
     * tasks by the slot groups (T97), show each group's uncovered slots with their reason, and offer "one more" per finished slot task. */
    static renderBatch() {
        let tr = this.api.tr;
        let el = this.api.el;
        let jobs = this.batch?.jobs || [];
        let uncovered = (this.batch && this.batch.uncovered) || [];
        this.api.$('simple-batch').hidden = !jobs.length;
        let items = [];
        let added = false;
        let slotBatch = jobs.some(job => job.slot_id) || uncovered.length > 0;
        // The groups of a slot batch (T98): main images first, detail screens, then slot-less tasks.
        let groups = [];
        let groupOf = key => {
            let found = groups.find(group => group.key == key);
            if (!found) {
                let titles = { main: '主图组', detail: '详情组', other: '按参考图' };
                found = { key, title: tr(titles[key] || '按参考图'), rows: [] };
                groups.push(found);
            }
            return found;
        };
        let order = ['main', 'detail', 'other'];
        for (let job of jobs) {
            let item = el('li', `simple-job${job.state == 'failed' ? ' is-failed' : ''}`);
            let name;
            if (job.slot_id) {
                name = this.products.length > 1
                    ? tr('商品 {0}｜{1}', job.product + 1, this.slotLabel(job.slot_id, job.slot_kind))
                    : this.slotLabel(job.slot_id, job.slot_kind);
            }
            else if (job.reference_index >= 0) {
                name = tr('商品 {0}｜参考图 {1}', job.product + 1, job.reference_index + 1);
                let thumb = el('img', 'simple-job-ref');
                let reference = this.references[job.reference_index];
                thumb.src = reference ? reference.image : '';
                thumb.alt = '';
                item.append(thumb);
            }
            else {
                name = tr('商品 {0}｜{1}', job.product + 1, job.purpose || job.template_name || job.template);
            }
            let state = this.stageText(job.state, job.state == 'done' ? job.execution_server : '');
            // Time estimates (F70): the batch prompt while it is written, and each task while it waits for its images.
            if (job.state == 'prompt') {
                state = this.stageWithEstimate(state, { elapsed_seconds: this.batch.prompt_elapsed_seconds, remaining_seconds: this.batch.prompt_remaining_seconds }, this.batchAt);
            }
            else if (job.state == 'running') {
                state = this.stageWithEstimate(state, job, this.batchAt);
            }
            item.append(el('span', '', name), el('span', 'simple-job-state', state));
            // How many pictures this slot task made so far (T98), so each group shows its progress.
            if (job.slot_id && (job.images || []).length) {
                item.append(el('span', 'simple-job-count', tr('已出 {0} 张', job.images.length)));
            }
            if (job.state == 'failed' || job.state == 'cancelled') {
                let retry = el('button', 'btn btn-sm', tr('重试'));
                retry.type = 'button';
                retry.addEventListener('click', () => this.retryJob(job.id));
                item.append(retry);
                // The reason stays visible on the card, so users need not hover to learn why a task failed.
                if (job.error) {
                    item.append(el('span', 'simple-job-error', job.error_id ? this.api.serverErrorText(job.error_id, job.error) : job.error));
                }
            }
            else if (job.slot_id && job.state == 'done') {
                // One more picture of only this slot (T98): other tasks are untouched; the server keeps the batch within its caps.
                let remake = el('button', 'btn btn-sm', tr('再出一张'));
                remake.type = 'button';
                remake.addEventListener('click', () => this.remakeJob(job.id));
                item.append(remake);
            }
            if (slotBatch) {
                groupOf(job.slot_id ? this.slotGroup(job.slot_kind) : 'other').rows.push(item);
            }
            else {
                items.push(item);
            }
            for (let url of job.images || []) {
                if (!this.results.includes(url)) {
                    this.results.push(url);
                    this.resultGroups.set(url, this.batchProducts[job.product] || tr('商品 {0}', job.product + 1));
                    // The trial's results carry the "model output, not pasted back" notice on their tiles (F84).
                    if (this.batchEdit) {
                        this.trialImages.add(url);
                    }
                    // Fusion-slot results carry the "route not yet accepted" notice on their tiles (T98).
                    if (job.source == 'fusion') {
                        this.fusionImages.add(url);
                    }
                    added = true;
                }
            }
        }
        if (slotBatch) {
            // Uncovered slots (T98) join their group with the plain reason; they are facts to fill in later, never invented.
            for (let entry of uncovered) {
                let row = el('li', 'simple-job simple-job-uncovered');
                let products = uncovered.filter(item => item.slot_id == entry.slot_id).length;
                row.append(el('span', '', this.slotLabel(entry.slot_id, entry.slot_kind)),
                    el('span', 'simple-job-state', tr('未覆盖：{0}', this.uncoveredReasonText(entry.reason))),
                    products > 1 ? el('span', 'simple-job-count', tr('{0} 个商品', products)) : el('span'));
                groupOf(this.slotGroup(entry.slot_kind)).rows.push(row);
            }
            for (let key of order) {
                let group = groups.find(item => item.key == key);
                if (!group) {
                    continue;
                }
                let header = el('li', 'simple-job-group', group.title);
                items.push(header, ...group.rows);
            }
        }
        this.api.$('simple-jobs').replaceChildren(...items);
        this.api.$('simple-batch-cancel').hidden = !jobs.some(job => job.state == 'queued' || job.state == 'prompt');
        if (added) {
            this.renderResults();
        }
    }

    /** Makes one more picture of one finished slot task (T98): the server adds a single-picture task for that slot alone. */
    static async remakeJob(id) {
        try {
            this.batch = await this.api.request('EcomCraftStudioBatchRemake', { batch_id: this.batch.batch_id, job_id: id });
            this.batchAt = Date.now();
            this.api.titleAdd();
            this.renderBatch();
            this.trackBatch();
        }
        catch (error) {
            this.message(error.message || this.api.tr('操作失败，请稍后重试。'), true);
        }
    }

    /** Retries one failed or cancelled task. */
    static async retryJob(id) {
        try {
            this.batch = await this.api.request('EcomCraftStudioBatchRetry', { batch_id: this.batch.batch_id, job_id: id });
            this.batchAt = Date.now();
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
            this.batchAt = Date.now();
            this.renderBatch();
        }
        catch (error) {
            this.message(error.message || this.api.tr('操作失败，请稍后重试。'), true);
        }
    }

    /** The main generate buttons of the single and set tasks. */
    static bindGenerate() {
        let $ = this.api.$;
        $('simple-generate').addEventListener('click', () => this.generate());
        $('simple-generate-set').addEventListener('click', () => this.generateSet());
    }

    /** Candidate count and batch cancellation. */
    static bindBatch() {
        let $ = this.api.$;
        $('simple-count').addEventListener('input', () => this.update());
        $('simple-count').addEventListener('change', () => this.update());
        $('simple-batch-cancel').addEventListener('click', () => this.cancelBatch());
    }
}
SimpleMode.adopt(SimpleBatchPart);
