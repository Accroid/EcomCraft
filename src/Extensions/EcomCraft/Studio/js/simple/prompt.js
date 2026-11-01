'use strict';

/**
 * Simple mode: the bilingual prompt panel (F54). Part of simple.js since R15; static methods are copied onto SimpleMode by adopt(), so callers and this stay unchanged.
 */
class SimplePromptPart {
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
        this.prompt.started = Date.now();
        this.update();
        // The button counts down with the usual expansion time (F70).
        let stopTicker = this.startTicker(() => {
            this.api.$('simple-expand').textContent = this.waitingLabel(tr('AI 扩写中…'), 'expand', this.prompt.started);
        });
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
            stopTicker();
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

    /** The bilingual prompt panel's buttons and inputs (F54). */
    static bindPrompt() {
        let $ = this.api.$;
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
    }
}
SimpleMode.adopt(SimplePromptPart);
