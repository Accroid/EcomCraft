'use strict';

/**
 * Simple mode: export options, output size (F73) and selling-image export. Part of simple.js since R15; static methods are copied onto SimpleMode by adopt(), so callers and this stay unchanged.
 */
class SimpleExportPart {
    /** Reads the platforms that have export presets and whether the shared export folder is available. */
    static async loadExportOptions() {
        try {
            this.exportOptions = await this.api.request('EcomCraftStudioExportOptions', { language: I18n.current });
        }
        catch (error) {
            this.exportOptions = { platforms: [], folder_available: false };
        }
        this.renderExport();
    }

    /** The export preset of the platform chosen in step 2, or null when it has none: works keep the size chosen before generating, and the
     * platform only decides format and size caps. */

    /** The export preset of the platform chosen in step 2, or null when it has none: works keep the size chosen before generating, and the
     * platform only decides format and size caps. */
    static exportPlatform() {
        return (this.exportOptions.platforms || []).find(item => item.id == this.choice.platform) || null;
    }

    /** The platform's slot size for an image kind (F58), the non-optional slot first, or null. */

    /** The platform's slot size for an image kind (F58), the non-optional slot first, or null. */
    static slotSize(kind) {
        let platform = this.data.platforms.find(item => item.id == this.choice.platform);
        let slots = (platform && platform.slots ? platform.slots : []).filter(slot => slot.kind == kind && slot.width && slot.height);
        slots.sort((a, b) => Number(a.optional) - Number(b.optional));
        return slots.length ? { width: slots[0].width, height: slots[0].height, name: slots[0].name } : null;
    }

    /** Why the chosen output size cannot be used, or an empty string. */

    /** Why the chosen output size cannot be used, or an empty string. */
    static outputSizeProblem() {
        if (this.outputSize.mode != 'custom') {
            return '';
        }
        let ok = value => Number.isInteger(value) && value >= 64 && value <= 8192;
        return ok(this.outputSize.width) && ok(this.outputSize.height) ? '' : '出图尺寸的宽和高须为 64–8192 的整数。';
    }

    /** The output_size to send: null for "by platform" (the server takes each image kind's slot size), otherwise { width, height }. */

    /** The output_size to send: null for "by platform" (the server takes each image kind's slot size), otherwise { width, height }. */
    static outputSizeField() {
        return this.outputSize.mode == 'auto' ? null : { width: this.outputSize.width, height: this.outputSize.height };
    }

    /** Fills the output size picker (the platform's size for the current image type, common sizes, custom) and explains what happens with it. */

    /** Fills the output size picker (the platform's size for the current image type, common sizes, custom) and explains what happens with it. */
    static renderOutputSize() {
        let tr = this.api.tr;
        let select = this.api.$('simple-output-size');
        let template = this.currentTemplate();
        let kind = this.referenceMode() ? 'scene_main' : this.chosenTask == 'single' && template ? template.slot_kind : '';
        let auto = kind ? this.slotSize(kind) : null;
        let autoLabel = this.chosenTask == 'set' && !this.referenceMode()
            ? tr('按平台自动（每种图片用平台规定的尺寸）')
            : auto ? tr('按平台自动（{0} {1}×{2}）', auto.name, auto.width, auto.height) : tr('按平台自动（平台没有规定时为 1024×1024）');
        let options = [new Option(autoLabel, 'auto')];
        for (let [width, height, ratio] of this.sizePresets) {
            options.push(new Option(width + '×' + height + ' (' + ratio + ')', width + 'x' + height));
        }
        options.push(new Option(tr('自定义尺寸'), 'custom'));
        select.replaceChildren(...options);
        select.value = this.outputSize.mode;
        if (select.value != this.outputSize.mode) {
            this.outputSize.mode = 'auto';
            select.value = 'auto';
        }
        let custom = this.outputSize.mode == 'custom';
        this.api.$('simple-output-custom').hidden = !custom;
        if (custom) {
            for (let side of ['width', 'height']) {
                let input = this.api.$('simple-output-' + side);
                if (document.activeElement != input) {
                    input.value = Number.isFinite(this.outputSize[side]) && this.outputSize[side] > 0 ? String(this.outputSize[side]) : '';
                }
            }
        }
        let problem = this.outputSizeProblem();
        let note = this.api.$('simple-output-note');
        let size = this.outputSize.mode == 'auto' ? auto : this.outputSize;
        note.textContent = problem ? tr(problem)
            : size ? tr('按 {0}×{1} 的比例出图，出图后自动缩放到这个尺寸，可直接下载使用。', size.width, size.height)
            : tr('每种图片按平台规定的尺寸出图，出图后自动缩放，可直接下载使用。');
        note.classList.toggle('simple-hint-error', Boolean(problem));
        this.renderStage();
    }

    /** Shows the export panel once there are candidates, with a notice when the chosen preset is not verified yet. */

    /** Shows the export panel once there are candidates, with a notice when the chosen preset is not verified yet. */
    static renderExport() {
        let tr = this.api.tr;
        let platform = this.exportPlatform();
        this.api.$('simple-export').hidden = !this.results.length || !platform;
        this.api.$('simple-export-folder').hidden = !this.exportOptions.folder_available;
        let ready = Boolean(platform) && this.selected.size > 0 && !this.exporting;
        this.api.$('simple-export-zip').disabled = !ready;
        this.api.$('simple-export-folder').disabled = !ready;
        let note = this.selected.size ? tr('将导出已选的 {0} 张，每个商品一个文件夹。', this.selected.size) : tr('先点选要导出的图片。');
        if (platform && !platform.verified) {
            note += ' ' + tr('这个平台的尺寸规格还没有经过管理员核对，上传前请对照平台最新要求。');
        }
        if (platform && platform.ai_declaration) {
            note += ' ' + tr('这个平台要求上传时声明图片由 AI 生成，请在商家后台按平台要求声明。');
        }
        this.api.$('simple-export-note').textContent = note;
        this.renderSelling();
    }

    /** Shows the selling-image panel once there are candidates (F47); it works on exactly one picked image. Points planned in the storyboard
     * and the brand kit's first color are offered once as a start. */

    /** Shows the selling-image panel once there are candidates (F47); it works on exactly one picked image. Points planned in the storyboard
     * and the brand kit's first color are offered once as a start. */
    static renderSelling() {
        let tr = this.api.tr;
        let panel = this.api.$('simple-selling');
        panel.hidden = !this.results.length;
        if (panel.hidden) {
            return;
        }
        if (!this.selling.prefilled) {
            this.selling.prefilled = true;
            let points = this.storyboard.points.map(point => String(point).trim()).filter(point => point).slice(0, 3);
            for (let [index, point] of points.entries()) {
                let input = this.api.$(`simple-selling-point-${index + 1}`);
                if (!input.value) {
                    input.value = point.slice(0, 16);
                }
            }
        }
        if (!this.selling.accentTouched) {
            let kit = this.brandKits.find(item => item.id == this.choice.brandKit);
            let color = kit && (kit.colors || []).length ? String(kit.colors[0]) : '#1F2937';
            this.api.$('simple-selling-accent').value = color.toLowerCase();
        }
        for (let chip of Array.from(this.api.$('simple-selling-layouts').querySelectorAll('button[data-layout]'))) {
            chip.setAttribute('aria-pressed', chip.dataset.layout == this.selling.layout ? 'true' : 'false');
        }
        let ready = this.selected.size == 1 && !this.selling.running;
        let run = this.api.$('simple-selling-run');
        run.disabled = !ready;
        run.textContent = this.selling.running ? tr('正在生成卖点图…') : tr('生成卖点图');
        this.api.$('simple-selling-note').textContent = this.selected.size == 1
            ? tr('将用所选的这张图做卖点图。文字由程序按固定版式排版，不由 AI 生成；会另存为新作品，原图不变。')
            : tr('先点选一张候选图。文字由程序按固定版式排版，不由 AI 生成；会另存为新作品，原图不变。');
    }

    /** Makes a selling-point image from the one picked candidate (F47) and adds it to the front of the candidates. */

    /** Makes a selling-point image from the one picked candidate (F47) and adds it to the front of the candidates. */
    static async makeSellingImage() {
        let tr = this.api.tr;
        if (this.selected.size != 1 || this.selling.running) {
            return;
        }
        let source = Array.from(this.selected)[0];
        let title = this.api.$('simple-selling-title').value.trim();
        if (!title) {
            this.message(tr('请填写卖点图标题。'), true);
            return;
        }
        this.selling.running = true;
        this.renderSelling();
        try {
            let result = await this.api.request('EcomCraftStudioSellingImage', {
                image: source,
                layout: this.selling.layout,
                title,
                points: [1, 2, 3].map(index => this.api.$(`simple-selling-point-${index}`).value.trim()).filter(point => point),
                accent: this.api.$('simple-selling-accent').value
            });
            for (let url of result.images || []) {
                this.results.unshift(url);
                this.resultGroups.set(url, this.resultGroups.get(source) || '');
            }
            this.renderResults();
            this.message(tr('卖点图已生成，排在候选图最前面，也保存在作品画廊。'), false);
        }
        catch (error) {
            this.message(error.message || tr('卖点图没有生成，请稍后重试。'), true);
        }
        finally {
            this.selling.running = false;
            this.renderSelling();
        }
    }

    /** Exports the selected candidates for the chosen platform, grouped by product, to the chosen slot (F58). */

    /** Exports the selected candidates for the chosen platform, grouped by product, to the chosen slot (F58). */
    static async exportSelected(target) {
        let tr = this.api.tr;
        let platform = this.choice.platform;
        if (!this.exportPlatform() || !this.selected.size || this.exporting) {
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
            // Works already have the size chosen before generating; the platform only decides each image's format and size cap.
            let result = await this.api.request('EcomCraftStudioExport', { platform, keep_size: true, target, groups: Array.from(groups, ([name, images]) => ({ name, images })) });
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
            if ((result.not_white || []).length) {
                this.message(tr('注意：{0} 张图片的背景不是纯白（RGB 255,255,255），这个平台的主图可能被拒：{1}', result.not_white.length, result.not_white.join(tr('、'))), true);
            }
            for (let warning of result.warnings || []) {
                this.message(this.warningText(warning), true);
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

    /** The plain-language text for one export warning code (F58): still over the slot's size cap, subject fill too small, or drawn text on a platform that forbids it. */

    /** The plain-language text for one export warning code (F58): still over the slot's size cap, subject fill too small, or drawn text on a platform that forbids it. */
    static warningText(warning) {
        let tr = this.api.tr;
        let count = warning.count || 1;
        if (warning.code == 'size_over') {
            return tr('有 {0} 张图压到最低质量后仍超过“{1}”的大小上限，已照常导出，上传前请自行压缩。', count, this.slotName(warning.slot));
        }
        if (warning.code == 'subject_small') {
            return tr('有 {0} 张白底图的商品占比低于“{1}”的要求，建议重新构图后再导出。', count, this.slotName(warning.slot));
        }
        return tr('有 {0} 张图带促销文字，平台可能按“牛皮癣”拒收，建议只放在允许的位置。', count);
    }

    /** The display name of an export slot ID, from the export platform's slot list. */

    /** The display name of an export slot ID, from the export platform's slot list. */
    static slotName(id) {
        let platform = this.exportPlatform();
        let slot = platform && (platform.slots || []).find(slot => slot.id == id);
        return slot ? slot.name : id;
    }

    /** Reads the account's saved products for the "from the product library" list. */
}
SimpleMode.adopt(SimpleExportPart);
