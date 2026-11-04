'use strict';

/**
 * Simple mode: selling info per product (F79, T99) — display name, points, parameters and size, kept per language on the server. The dialog is
 * reached from a product card, never from the step-2 generation flow (A30). While typing, the advertising-claim check only notices wording with a
 * suggestion; it never blocks saving. The optional model suggestions only fill in after the user ticks them. Part of simple.js since R15; static
 * methods are copied onto SimpleMode by adopt(), so callers and this stay unchanged.
 */
class SimpleProductInfoPart {
    /** The dialog's editor state — { product, language, tabs, suggesting, suggestions, saving } — or null while closed. Every language keeps its
     * own tab of { name, points, params, size } so switching never mixes them. */
    static productInfo = null;

    /** The debounce timer of the claim check; typing only schedules one check. */
    static productInfoClaimTimer = null;

    /** The languages selling info is kept in, in display order; labels go through the translation table. */
    static productInfoLanguages() {
        return [['zh-CN', '中文'], ['en', 'English']];
    }

    /** Opens the selling-info dialog of one saved product (F79, T99); the language tab falls back to the page's language. Each language keeps its
     * own tab state for this visit, so switching tabs never mixes or loses what was typed in the other one. */
    static openProductInfo(product, opener) {
        if (!product || !product.id) {
            return;
        }
        let pageLanguage = I18n.current == 'en' ? 'en' : 'zh-CN';
        let info = product.info || {};
        let language = info[pageLanguage] ? pageLanguage : 'zh-CN';
        let tabs = {};
        for (let [id] of this.productInfoLanguages()) {
            tabs[id] = this.productInfoTab(info, id);
        }
        this.productInfo = {
            product,
            language,
            tabs,
            suggesting: false,
            suggestions: null,
            saving: false
        };
        this.api.$('product-info-modal').hidden = false;
        this.renderProductInfo();
        this.api.dialogEnter(this.api.$('product-info-modal'), opener, this.api.$('pinfo-name'));
    }

    /** One language's editable fields from its saved entry. */
    static productInfoTab(info, language) {
        let saved = info[language] || {};
        return {
            name: saved.display_name || '',
            points: [...(saved.points || [])],
            params: (saved.params || []).map(param => ({ name: param.name || '', value: param.value || '' })),
            size: saved.size || ''
        };
    }

    /** The tab state of the open language, with the current input values stored back in. */
    static productInfoCurrent() {
        let editor = this.productInfo;
        editor.tabs[editor.language] = {
            name: this.api.$('pinfo-name').value,
            points: editor.points.map(point => String(point)),
            params: editor.params.map(param => ({ name: param.name, value: param.value })),
            size: this.api.$('pinfo-size').value
        };
        return editor.tabs[editor.language];
    }

    /** Closes the selling-info dialog; unsaved changes are dropped. */
    static closeProductInfo() {
        if (this.api.$('product-info-modal').hidden) {
            return;
        }
        this.api.$('product-info-modal').hidden = true;
        this.productInfo = null;
        clearTimeout(this.productInfoClaimTimer);
        this.api.dialogLeave(this.api.$('product-info-modal'));
    }

    /** The dialog's current values as the server's info object of the open language. */
    static productInfoFields() {
        let current = this.productInfoCurrent();
        return {
            display_name: current.name,
            points: current.points.map(point => String(point)),
            params: current.params.map(param => ({ name: param.name, value: param.value })),
            size: current.size
        };
    }

    /** Whether anything is filled in. */
    static productInfoFilled(fields) {
        return Boolean(fields.display_name || (fields.points || []).some(point => point) || (fields.params || []).some(param => param.name || param.value) || fields.size);
    }

    /** Shows the dialog: language tabs, fields, suggestions and the save state. Structural changes re-render; typing only updates the state. */
    static renderProductInfo() {
        let tr = this.api.tr;
        let el = this.api.el;
        let modal = this.api.$('product-info-modal');
        if (modal.hidden || !this.productInfo) {
            return;
        }
        let editor = this.productInfo;
        let tab = editor.tabs[editor.language];
        if (!tab) {
            tab = editor.tabs[editor.language] = { name: '', points: [], params: [], size: '' };
        }
        editor.points = tab.points;
        editor.params = tab.params;
        let chips = [];
        for (let [id, label] of this.productInfoLanguages()) {
            let chip = el('button', 'simple-chip', tr(label));
            chip.type = 'button';
            chip.setAttribute('aria-pressed', id == editor.language ? 'true' : 'false');
            chip.addEventListener('click', () => this.switchProductInfoLanguage(id));
            chips.push(chip);
        }
        this.api.$('pinfo-languages').replaceChildren(...chips);
        // The state is updated on every keystroke, so a structural re-render can always take the inputs' values from it.
        this.api.$('pinfo-name').value = tab.name;
        this.api.$('pinfo-size').value = tab.size;
        let points = [];
        for (let [index] of editor.points.entries()) {
            let row = el('div', 'simple-point');
            let input = el('input');
            input.type = 'text';
            input.maxLength = 40;
            input.autocomplete = 'off';
            input.value = editor.points[index];
            input.placeholder = tr('例如：双层不锈钢');
            input.setAttribute('aria-label', tr('卖点 {0}', index + 1));
            input.addEventListener('input', e => {
                editor.points[index] = e.target.value;
                this.scheduleProductInfoClaims();
            });
            row.append(input, this.productInfoRemove(tr('删除卖点 {0}', index + 1), () => {
                editor.points.splice(index, 1);
                this.renderProductInfo();
            }));
            points.push(row);
        }
        this.api.$('pinfo-points').replaceChildren(...points);
        this.api.$('pinfo-point-add').disabled = editor.points.length >= 5;
        let params = [];
        for (let [index, param] of editor.params.entries()) {
            let row = el('div', 'pinfo-param');
            let name = el('input');
            name.type = 'text';
            name.maxLength = 30;
            name.autocomplete = 'off';
            name.value = param.name;
            name.placeholder = tr('例如：容量');
            name.setAttribute('aria-label', tr('参数 {0} 的名称', index + 1));
            name.addEventListener('input', e => {
                param.name = e.target.value;
                this.scheduleProductInfoClaims();
            });
            let value = el('input');
            value.type = 'text';
            value.maxLength = 60;
            value.autocomplete = 'off';
            value.value = param.value;
            value.placeholder = tr('例如：380 ml');
            value.setAttribute('aria-label', tr('参数 {0} 的值', index + 1));
            value.addEventListener('input', e => {
                param.value = e.target.value;
                this.scheduleProductInfoClaims();
            });
            row.append(name, value, this.productInfoRemove(tr('删除参数 {0}', index + 1), () => {
                editor.params.splice(index, 1);
                this.renderProductInfo();
            }));
            params.push(row);
        }
        this.api.$('pinfo-params').replaceChildren(...params);
        this.api.$('pinfo-param-add').disabled = editor.params.length >= 20;
        this.api.$('pinfo-save').disabled = editor.saving;
        this.api.$('pinfo-save').textContent = editor.saving ? tr('正在保存…') : tr('保存');
        let suggest = this.api.$('pinfo-suggest-block');
        suggest.hidden = !this.suggestReady;
        let run = this.api.$('pinfo-suggest-run');
        run.disabled = editor.suggesting;
        run.textContent = editor.suggesting ? tr('正在建议…') : tr('建议卖点');
        this.renderProductInfoSuggestions();
        this.renderProductInfoMessage();
    }

    /** One small remove button of a row. */
    static productInfoRemove(label, onRemove) {
        let button = this.api.el('button', 'icon-btn', '✕');
        button.type = 'button';
        button.setAttribute('aria-label', label);
        button.addEventListener('click', onRemove);
        return button;
    }

    /** The dialog's message line. */
    static renderProductInfoMessage() {
        let editor = this.productInfo;
        let note = this.api.$('pinfo-message');
        note.textContent = editor.message || '';
        note.classList.toggle('simple-hint-error', Boolean(editor.error));
        note.hidden = !editor.message;
    }

    /** Shows or replaces the dialog's message. */
    static productInfoMessage(text, error) {
        this.productInfo.message = text;
        this.productInfo.error = Boolean(error);
        this.renderProductInfoMessage();
    }

    /** Switches the language tab (F79, T99): what was typed stays on its own tab; the fields take the other language's tab state, and the saved
     * entry has not changed. */
    static switchProductInfoLanguage(language) {
        let editor = this.productInfo;
        if (!this.productInfoLanguages().some(([id]) => id == language) || language == editor.language) {
            return;
        }
        this.productInfoCurrent();
        editor.language = language;
        editor.suggestions = null;
        editor.message = '';
        editor.error = false;
        this.renderProductInfo();
        this.scheduleProductInfoClaims();
    }

    /** Schedules the advertising-claim check of the current fields (F79, T99): notices only, never blocks. */
    static scheduleProductInfoClaims() {
        clearTimeout(this.productInfoClaimTimer);
        this.productInfoClaimTimer = setTimeout(() => this.checkProductInfoClaims(), 600);
    }

    /** Runs the advertising-claim check of the dialog's fields and shows the hits, once per word. */
    static async checkProductInfoClaims() {
        let editor = this.productInfo;
        if (!editor) {
            return;
        }
        let fields = this.productInfoFields();
        if (!this.productInfoFilled(fields)) {
            this.renderClaims([], this.api.$('pinfo-claims'));
            return;
        }
        let text = [fields.display_name, ...fields.points, ...fields.params.flatMap(param => [param.name, param.value]), fields.size]
            .filter(part => part).join('\n');
        try {
            let result = await this.api.request('EcomCraftStudioClaimCheck', { text });
            if (this.productInfo == editor) {
                this.renderClaims(result.hits || [], this.api.$('pinfo-claims'));
            }
        }
        catch (error) {
            // A failed check never blocks editing; the area just stays as it was.
        }
    }

    /** Shows claim hits as plain notices: one line per word, with the suggestion and the basis. */
    static renderClaims(hits, box) {
        let tr = this.api.tr;
        let el = this.api.el;
        if (!hits.length) {
            box.replaceChildren();
            box.hidden = true;
            return;
        }
        let seen = new Set();
        let items = [];
        for (let hit of hits) {
            if (seen.has(hit.word)) {
                continue;
            }
            seen.add(hit.word);
            let line = el('p', '', tr('「{0}」{1}：{2}（依据：{3}）', hit.word, hit.category, hit.suggestion || tr('建议删掉或改写'), hit.basis || (hit.verified ? '' : tr('常见执法口径，待核对官方规定'))));
            items.push(line);
        }
        box.replaceChildren(el('p', 'pinfo-claims-title', tr('用语提示（只提醒，不拦截）：')), ...items);
        box.hidden = false;
    }

    /** Saves the current language's fields (F79, T99); an all-empty entry clears the language on the server. */
    static async saveProductInfo() {
        let tr = this.api.tr;
        let editor = this.productInfo;
        if (!editor || editor.saving) {
            return;
        }
        let fields = this.productInfoFields();
        editor.saving = true;
        editor.message = '';
        this.renderProductInfo();
        try {
            let result = await this.api.request('EcomCraftStudioProductSaveInfo', { product_id: editor.product.id, language: editor.language, info: fields });
            editor.product.info = result.product.info || {};
            editor.saving = false;
            this.renderProductInfo();
            this.productInfoMessage(this.productInfoFilled(fields) ? tr('已保存。') : tr('这个语言的内容已清空并保存。'), false);
            await this.loadLibrary();
        }
        catch (error) {
            editor.saving = false;
            this.renderProductInfo();
            this.productInfoMessage(error.message || tr('保存失败，请稍后重试。'), true);
        }
    }

    /** Asks the prompt model for selling-point suggestions from the user's own description (F79, T99). */
    static async suggestProductPoints() {
        let editor = this.productInfo;
        let tr = this.api.tr;
        if (!editor || editor.suggesting) {
            return;
        }
        let description = this.api.$('pinfo-suggest-desc').value.trim();
        if (!description) {
            this.productInfoMessage(tr('先写一段商品描述，再让模型建议卖点。'), true);
            return;
        }
        editor.suggesting = true;
        editor.message = '';
        this.renderProductInfo();
        try {
            let result = await this.api.request('EcomCraftStudioProductSuggestPoints', { description, language: editor.language });
            editor.suggesting = false;
            editor.suggestions = result.suggestions || [];
            if (!editor.suggestions.length) {
                this.productInfoMessage(tr('模型没有给出可用的建议，请自己填写卖点。'), true);
            }
        }
        catch (error) {
            editor.suggesting = false;
            editor.suggestions = null;
            this.productInfoMessage(error.message || tr('建议卖点暂时不可用，请稍后重试。'), true);
        }
        this.renderProductInfo();
    }

    /** Shows the model's suggestions as ticked lines with their own claim notices; nothing is saved here. */
    static renderProductInfoSuggestions() {
        let tr = this.api.tr;
        let el = this.api.el;
        let editor = this.productInfo;
        let list = this.api.$('pinfo-suggest-list');
        let actions = this.api.$('pinfo-suggest-actions');
        if (!editor || !editor.suggestions || !editor.suggestions.length) {
            list.replaceChildren();
            list.hidden = true;
            actions.hidden = true;
            return;
        }
        let rows = [];
        for (let [index, suggestion] of editor.suggestions.entries()) {
            let row = el('label', 'pinfo-suggest-item');
            let box = el('input');
            box.type = 'checkbox';
            box.checked = true;
            row.append(box, el('span', '', suggestion.text));
            if ((suggestion.claims || []).length) {
                let hit = suggestion.claims[0];
                row.append(el('span', 'pinfo-suggest-claim', tr('「{0}」{1}：{2}', hit.word, hit.category, hit.suggestion || tr('建议删掉或改写'))));
            }
            rows.push(row);
        }
        list.replaceChildren(...rows);
        list.hidden = false;
        actions.hidden = false;
    }

    /** Fills the ticked suggestions into the point inputs — the only way a suggestion reaches the fields (F79, T99). */
    static applySuggestedPoints() {
        let editor = this.productInfo;
        let tr = this.api.tr;
        if (!editor || !editor.suggestions) {
            return;
        }
        let boxes = Array.from(this.api.$('pinfo-suggest-list').querySelectorAll('input[type="checkbox"]'));
        let picked = editor.suggestions.filter((suggestion, index) => boxes[index] && boxes[index].checked);
        if (!picked.length) {
            this.productInfoMessage(tr('先勾选至少一条建议。'), true);
            return;
        }
        // The point list of the open tab (set by renderProductInfo, taken here again so the function also works right after opening).
        editor.points = editor.tabs[editor.language].points;
        let empty = editor.points.filter(point => !point).length;
        let added = 0;
        let skipped = 0;
        for (let suggestion of picked) {
            if (!suggestion.text) {
                continue;
            }
            let blank = editor.points.findIndex(point => !point);
            if (blank >= 0) {
                editor.points[blank] = suggestion.text;
                added++;
            }
            else if (editor.points.length < 5) {
                editor.points.push(suggestion.text);
                added++;
            }
            else {
                skipped++;
            }
        }
        editor.suggestions = null;
        this.renderProductInfo();
        this.scheduleProductInfoClaims();
        this.productInfoMessage(skipped
            ? tr('已填入 {0} 条；还有 {1} 条没填入（卖点最多 5 条）。', added, skipped)
            : tr('已填入 {0} 条，确认内容后记得保存。', added), false);
    }

    /** Selling-info dialog controls (F79, T99). */
    static bindProductInfo() {
        let $ = this.api.$;
        $('product-info-close').addEventListener('click', () => this.closeProductInfo());
        $('product-info-backdrop').addEventListener('click', () => this.closeProductInfo());
        $('pinfo-name').addEventListener('input', e => {
            if (this.productInfo) {
                this.productInfo.tabs[this.productInfo.language].name = e.target.value;
                this.scheduleProductInfoClaims();
            }
        });
        $('pinfo-size').addEventListener('input', e => {
            if (this.productInfo) {
                this.productInfo.tabs[this.productInfo.language].size = e.target.value;
                this.scheduleProductInfoClaims();
            }
        });
        $('pinfo-point-add').addEventListener('click', () => {
            if (!this.productInfo) {
                return;
            }
            this.productInfo.points.push('');
            this.renderProductInfo();
            let inputs = this.api.$('pinfo-points').querySelectorAll('input');
            if (inputs.length) {
                inputs[inputs.length - 1].focus();
            }
        });
        $('pinfo-param-add').addEventListener('click', () => {
            if (!this.productInfo) {
                return;
            }
            this.productInfo.params.push({ name: '', value: '' });
            this.renderProductInfo();
            let rows = this.api.$('pinfo-params').querySelectorAll('.pinfo-param input');
            if (rows.length) {
                rows[rows.length - 2].focus();
            }
        });
        $('pinfo-save').addEventListener('click', () => this.saveProductInfo());
        $('pinfo-suggest-run').addEventListener('click', () => this.suggestProductPoints());
        $('pinfo-suggest-apply').addEventListener('click', () => this.applySuggestedPoints());
    }

    /** Re-renders the dialog after a language switch of the page; typed text of the open tab stays. */
    static renderSellingInfo() {
        this.renderProductInfo();
    }
}
SimpleMode.adopt(SimpleProductInfoPart);
