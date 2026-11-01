'use strict';

/**
 * Simple mode: brand kits (F12). Part of simple.js since R15; static methods are copied onto SimpleMode by adopt(), so callers and this stay unchanged.
 */
class SimpleBrandPart {
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

    /** The brand kit fields of a generation request: only the kit's ID (F12). */
    static brandFields() {
        return this.choice.brandKit ? { brand_kit_id: this.choice.brandKit } : {};
    }

    /** Opens the brand kit dialog with the account's kits. */

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

    /** How many reference images the kit will have after saving. */
    static brandReferenceCount() {
        let editor = this.brandEditor;
        return editor ? editor.references.filter(item => !editor.removed.includes(item.id)).length + editor.added.length : 0;
    }

    /** One thumbnail with a remove button. */

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

    /** Shows a message inside the brand kit dialog. */
    static brandMessage(text, error) {
        let box = this.api.$('brand-message');
        box.textContent = text;
        box.classList.toggle('simple-hint-error', Boolean(error));
        box.hidden = !text;
    }

    /** Reads a chosen image as a data URL of at most 5 MB (compressed in the browser when larger), or null with a message. */

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

    /** Brand kit selection and the brand dialog's controls (F12). */
    static bindBrand() {
        let $ = this.api.$;
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
    }
}
SimpleMode.adopt(SimpleBrandPart);
