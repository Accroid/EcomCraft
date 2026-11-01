'use strict';

/**
 * Simple mode: the storyboard generator (F25). Part of simple.js since R15; static methods are copied onto SimpleMode by adopt(), so callers and this stay unchanged.
 */
class SimpleStoryboardPart {
    static storyboardProblem() {
        let visible = this.visibleTemplates();
        return this.storyboard.shots.some(shot => !visible.some(template => template.id == shot.template))
            ? '分镜里有当前平台不可用的图片类型，请重新选择。' : '';
    }

    /** Shows the selling points and storyboard shots as editable rows (F25). Only structural changes re-render; typing just updates the state. */

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

    /** Asks the server for selling points and a storyboard (F25) from the description or prompt panel, the user's points and the choices of step 2. */
    static async runStoryboard() {
        let tr = this.api.tr;
        if (this.storyboard.running || !this.loaded) {
            return;
        }
        this.storyboard.running = true;
        this.storyboard.started = Date.now();
        this.update();
        // The button counts down with the usual storyboard time (F70).
        let stopTicker = this.startTicker(() => {
            this.api.$('simple-storyboard-run').textContent = this.waitingLabel(tr('正在生成分镜…'), 'storyboard', this.storyboard.started);
        });
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
            stopTicker();
            this.storyboard.running = false;
            this.renderStoryboard();
            this.update();
        }
    }

    /** Opens the reference picker only when image description can work (F59/F68): without a usable vision model the guidance
     * below shows instead — its settings steps open the configuration directly — so references never sit undescribable. */
}
SimpleMode.adopt(SimpleStoryboardPart);
