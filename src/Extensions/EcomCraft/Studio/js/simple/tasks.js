'use strict';

/**
 * Simple mode: the result list: redo, tracking and downloads. Part of simple.js since R15; static methods are copied onto SimpleMode by adopt(), so callers and this stay unchanged.
 */
class SimpleTasksPart {
    static async applyRecipe(recipe) {
        let tr = this.api.tr;
        // Opening the view starts loading the lists too; wait for that load instead of racing it.
        for (let i = 0; i < 100 && this.loading; i++) {
            await new Promise(resolve => setTimeout(resolve, 100));
        }
        if (!this.loaded) {
            await this.load();
        }
        let template = this.data.templates.find(item => item.id == recipe.template);
        if (!template) {
            this.message(tr('这件作品用的图片类型已停用或不存在，无法用相同参数重做。'), true);
            return false;
        }
        this.chosenTask = 'single';
        if (recipe.platform && this.data.platforms.some(platform => platform.id == recipe.platform)) {
            this.choice.platform = recipe.platform;
        }
        if (!this.visibleTemplates().includes(template)) {
            this.choice.platform = '';
        }
        this.api.$('simple-platform').value = this.choice.platform;
        this.optionalSlots.clear();
        this.renderPlatformSlots();
        this.choice.template = template.id;
        let prompt = String(recipe.prompt || '').trim();
        this.setPromptTexts('', prompt);
        this.switchPromptTab('en');
        this.moreOpen = true;
        this.api.$('simple-more').open = true;
        this.remember();
        let seed = Number.isSafeInteger(recipe.seed) && recipe.seed >= 0 ? recipe.seed : null;
        this.redo = { template: template.id, name: template.name || template.id, seed, prompt };
        let notice = '';
        if (recipe.product_id && this.imageField(template)) {
            let saved = this.library.find(product => product.id == recipe.product_id);
            if (!saved) {
                await this.loadLibrary();
                saved = this.library.find(product => product.id == recipe.product_id);
            }
            if (saved) {
                await this.useSaved(saved);
            }
            else {
                notice = tr('原商品已不在商品库，请重新上传或选择商品图。');
            }
        }
        this.renderTemplates();
        this.renderStoryboard();
        this.renderCutout();
        this.update();
        this.message(notice, Boolean(notice));
        return true;
    }

    /** Stops applying the filed work's seed and prompt; the page keeps the filled-in choices. */

    /** Stops applying the filed work's seed and prompt; the page keeps the filled-in choices. */
    static clearRedo() {
        this.redo = null;
        this.renderRedo();
    }

    /** Shows which work's settings the next generation repeats, with a button to stop. */

    /** Shows which work's settings the next generation repeats, with a button to stop. */
    static renderRedo() {
        let tr = this.api.tr;
        let box = this.api.$('simple-redo');
        let redo = this.redo && this.redo.template == this.choice.template ? this.redo : null;
        box.hidden = !redo;
        if (redo) {
            this.api.$('simple-redo-text').textContent = redo.seed != null
                ? tr('下一次出图沿用原作品的种子 {0} 和提示词（图片类型：{1}）。', redo.seed, redo.name)
                : tr('下一次出图沿用原作品的提示词（图片类型：{0}）。', redo.name);
        }
    }

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
            output_size: this.outputSizeField(),
            async: true
        };
        if (field && this.product && this.product.id) {
            payload.product_id = this.product.id;
        }
        if (template.preserve_product) {
            payload.placement = this.clampPlacement(this.placement);
        }
        let redo = this.redo && this.redo.template == template.id ? this.redo : null;
        if (redo) {
            // An untouched redo prompt goes out as the work's own prompt, so the template, brand and style words are not added a second time.
            if (redo.prompt && sent.source == 'en' && !this.promptDirty('en') && String(this.prompt.en || '').trim() == redo.prompt) {
                delete payload.prompt_en;
                delete payload.brand_kit_id;
                payload.prompt = redo.prompt;
            }
            if (redo.seed != null) {
                payload.seed = redo.seed;
            }
        }
        this.submitting = true;
        this.update();
        let entry = { product: this.product ? this.product.name : '', template: template.name || template.id, stage: 'prompt', server: '', taskId: null };
        try {
            let result = await this.api.request('EcomCraftStudioGenerate', payload);
            if (redo) {
                this.clearRedo();
            }
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

    /** Adds a single submission to the task list (newest first, the last ten are shown). */
    static addTask(entry) {
        this.tasks.unshift(entry);
        this.tasks = this.tasks.slice(0, 10);
        this.renderTasks();
    }

    /** A waiting label with the estimate of an operation of <kind> started at <started> (F70), such as "Writing… (about 20 s left)". */

    /** A waiting label with the estimate of an operation of <kind> started at <started> (F70), such as "Writing… (about 20 s left)". */
    static waitingLabel(base, kind, started) {
        let estimate = this.api.estimate;
        return estimate && started ? estimate.label(base, estimate.since(kind, started)) : base;
    }

    /** Calls <update> every second until the returned function is called (F70); without the estimate helper nothing runs. */

    /** Calls <update> every second until the returned function is called (F70); without the estimate helper nothing runs. */
    static startTicker(update) {
        return this.api.estimate ? this.api.estimate.tick(update) : () => {};
    }

    /** A task stage with its time estimate (F70): <fields> holds the server's elapsed, remaining and start-in seconds, received at <receivedAt>. */

    /** A task stage with its time estimate (F70): <fields> holds the server's elapsed, remaining and start-in seconds, received at <receivedAt>. */
    static stageWithEstimate(stageText, fields, receivedAt) {
        let estimate = this.api.estimate;
        return estimate && fields ? estimate.label(stageText, estimate.text(fields, receivedAt)) : stageText;
    }

    /** The plain-language stage of a task (F56): writing the prompt, queued or running on a named server, done or failed. */

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

    /** Shows the single submissions of this page with their stage. */
    static renderTasks() {
        let el = this.api.el;
        let items = [];
        for (let entry of this.tasks) {
            let item = el('li', `simple-job${entry.stage == 'failed' ? ' is-failed' : ''}`);
            let waiting = entry.stage == 'prompt' || entry.stage == 'queued' || entry.stage == 'running';
            let state = this.stageText(entry.stage, entry.server);
            item.append(el('span', '', this.api.tr('{0}｜{1}', entry.product || this.api.tr('商品'), entry.template)),
                el('span', 'simple-job-state', waiting ? this.stageWithEstimate(state, entry.eta, entry.etaAt) : state));
            // The reason stays visible on the card, as for set tasks.
            if (entry.error) {
                item.append(el('span', 'simple-job-error', entry.error));
            }
            items.push(item);
        }
        let list = this.api.$('simple-tasks');
        list.replaceChildren(...items);
        list.hidden = !items.length;
    }

    /** Asks how the submissions still waiting for their prompt are doing (F56) and takes over the ones that were submitted or failed. */

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
                // How long the prompt usually takes (F70).
                item.entry.eta = result;
                item.entry.etaAt = Date.now();
                this.renderTasks();
                continue;
            }
            item.entry.eta = null;
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

    /** Explains a prompt fallback the server used (F37/F54); clears the banner when there was none. */
    static promptNotice(code) {
        let tr = this.api.tr;
        this.message(code == 'description_unused' ? tr('提示词优化服务暂时不可用，你的中文描述这次没有用上，已按所选风格出图。')
            : code == 'description_used' ? tr('提示词优化服务暂时不可用，已直接使用你的英文描述。')
            : code == 'prompt_untranslated' ? tr('提示词服务暂时不可用，你修改的中文提示词这次没能翻译成英文，已按所选产品类型和风格出图。') : '', false);
    }

    /** Polls the queue for this page's tasks and collects their results; keeps polling in the background (F43). */

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
        let active = new Map(mine.map(task => [task.id, task]));
        let finished = new Map((queue.finished_tasks || []).map(task => [task.id, task]));
        let received = Date.now();
        for (let id of Array.from(this.pending)) {
            let entry = this.tasks.find(task => task.taskId == id);
            let outcome = finished.get(id);
            if (active.has(id) || !outcome) {
                let task = active.get(id);
                if (entry && task) {
                    if (task.state == 'running') {
                        entry.stage = 'running';
                    }
                    // The queue's time estimate for this account's own task (F70).
                    entry.eta = task;
                    entry.etaAt = received;
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

    /** Address of a result as returned by the server ("View/user/..." or "Output/..."). */
    static resultUrl(url) {
        return '/' + String(url).split('/').map(part => encodeURIComponent(part)).join('/');
    }

    /** The gallery path of a result URL ("View/<user>/a/b.png" or "Output/a/b.png" → "a/b.png"), as the star API expects. */

    /** The gallery path of a result URL ("View/<user>/a/b.png" or "Output/a/b.png" → "a/b.png"), as the star API expects. */
    static galleryPath(url) {
        return String(url).replace(/^\/?View\/[^/]+\//, '').replace(/^\/?Output\//, '');
    }

    /** Stars or unstars one candidate with the gallery's star (F65). A starred candidate is also marked kept, so cleanup never removes its original. */

    /** Stars or unstars one candidate with the gallery's star (F65). A starred candidate is also marked kept, so cleanup never removes its original. */
    static async toggleStar(url) {
        let tr = this.api.tr;
        if (this.starring.has(url)) {
            return;
        }
        this.starring.add(url);
        this.renderResults();
        try {
            let result = await this.api.request('ToggleImageStarred', { path: this.galleryPath(url) });
            if (result.error) {
                throw new Error(result.error);
            }
            if (result.new_state) {
                this.starred.add(url);
                this.api.request('EcomCraftStudioMarkKept', { images: [url] }).catch(() => {
                    // The starred copy stays either way; picking or downloading marks it again.
                });
            }
            else {
                this.starred.delete(url);
            }
        }
        catch (error) {
            this.message(tr('收藏没有成功：{0}', error.message || tr('请刷新后重试')), true);
        }
        finally {
            this.starring.delete(url);
            this.renderResults();
        }
    }

    /** Shows the candidates as selectable tiles, each with a star to weed out weak results (F65). */

    /** Shows the candidates as selectable tiles, each with a star to weed out weak results (F65). */
    static renderResults() {
        let el = this.api.el;
        let tr = this.api.tr;
        let tiles = [];
        for (let url of this.results) {
            let tile = el('div', 'simple-result-tile');
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
            let name = url.split('/').pop();
            let starred = this.starred.has(url);
            let star = el('button', 'simple-result-star', starred ? '★' : '☆');
            star.type = 'button';
            star.disabled = this.starring.has(url);
            star.setAttribute('aria-pressed', String(starred));
            star.setAttribute('aria-label', starred ? tr('取消收藏 {0}', name) : tr('收藏 {0}', name));
            star.addEventListener('click', () => this.toggleStar(url));
            tile.append(button, star);
            tiles.push(tile);
        }
        this.api.$('simple-results').replaceChildren(...tiles);
        this.renderExport();
    }

    /** Downloads the selected candidates one by one through the browser; downloaded works are kept forever (F39). */

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
SimpleMode.adopt(SimpleTasksPart);
