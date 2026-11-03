'use strict';

/**
 * Simple mode: "match the tone" for a set of results (F78, T101). Off by default; when switched on, the user picks one result as the base and the other
 * results of the batch get a local colour transfer toward it on the server (EcomCraftStudioToneMatch) — no new generation, the product's area untouched.
 * Matched copies replace the originals in the result list and can be switched back. The panel is built here, inside the results step, so the shared page
 * markup only gains the script tag. Static methods are copied onto SimpleMode by adopt().
 */
class SimpleTonePart {
    /** Tone state, kept on the part class (adopt() copies methods only): whether the switch is on, a request is running, and each matched copy's original. */
    static tone = { on: false, running: false, originals: new Map() };

    /** Builds the panel once, next to the selling-image panel, and keeps it in step with the results and the selection. */
    static renderTone() {
        let tr = this.api.tr;
        let anchor = this.api.$('simple-selling');
        if (!anchor) {
            return;
        }
        let panel = document.getElementById('simple-tone');
        if (!panel) {
            panel = document.createElement('details');
            panel.id = 'simple-tone';
            panel.className = 'simple-storyboard simple-tone';
            let summary = document.createElement('summary');
            summary.id = 'simple-tone-summary';
            let toggle = document.createElement('label');
            toggle.className = 'simple-edit-toggle';
            let box = document.createElement('input');
            box.type = 'checkbox';
            box.id = 'simple-tone-on';
            box.addEventListener('change', () => {
                SimpleTonePart.tone.on = box.checked;
                this.renderTone();
            });
            let toggleText = document.createElement('span');
            toggleText.id = 'simple-tone-on-text';
            toggle.append(box, toggleText);
            let hint = document.createElement('p');
            hint.className = 'hint';
            hint.id = 'simple-tone-hint';
            let actions = document.createElement('div');
            actions.className = 'recover-actions';
            let run = document.createElement('button');
            run.type = 'button';
            run.className = 'btn';
            run.id = 'simple-tone-run';
            run.addEventListener('click', () => this.matchTone());
            let revert = document.createElement('button');
            revert.type = 'button';
            revert.className = 'btn';
            revert.id = 'simple-tone-revert';
            revert.addEventListener('click', () => this.revertTone());
            actions.append(run, revert);
            panel.append(summary, toggle, hint, actions);
            anchor.parentNode.insertBefore(panel, anchor);
        }
        let state = SimpleTonePart.tone;
        panel.hidden = this.results.length < 2;
        this.api.$('simple-tone-summary').textContent = tr('统一色调');
        this.api.$('simple-tone-on').checked = state.on;
        this.api.$('simple-tone-on-text').textContent = tr('开启统一色调');
        this.api.$('simple-tone-hint').textContent = !state.on
            ? tr('风格差异大的整套图可以统一色调：以一张为基准，在本地调整其余图片的颜色，不重新出图，商品区域不改动。默认关闭。')
            : this.selected.size == 1
            ? tr('将以所选的这张为基准，统一本批其余图片的色调；结果另存为新作品，原图不变。')
            : tr('先点选一张作为基准。');
        let run = this.api.$('simple-tone-run');
        run.hidden = !state.on;
        run.disabled = state.running || this.selected.size != 1;
        run.textContent = state.running ? tr('正在统一色调…') : tr('以所选这张为基准统一色调');
        let revert = this.api.$('simple-tone-revert');
        revert.hidden = !this.results.some(url => state.originals.has(url));
        revert.disabled = state.running;
        revert.textContent = tr('恢复原图');
    }

    /** The plain-language reason a picture was not matched; the server sends only the code. */
    static toneSkipText(code) {
        let texts = {
            is_base: '这张就是基准图',
            no_generated_background: '不是生成的背景（白底图、真实裁切或纯文字屏）',
            drawn_text: '带排版文字的卖点图或详情屏',
            no_product_position: '无法确定商品位置，未统一色调',
            no_background: '背景太少，无法取色',
            unreadable: '图片无法读取'
        };
        return this.api.tr(texts[code] || '图片无法读取');
    }

    /** Matches every other result to the picked base, replacing each matched picture with its copy in place; skipped ones stay and say why. */
    static async matchTone() {
        let tr = this.api.tr;
        let state = SimpleTonePart.tone;
        if (state.running || this.selected.size != 1) {
            return;
        }
        let picked = Array.from(this.selected)[0];
        let base = state.originals.get(picked) || picked;
        let images = this.results.map(url => state.originals.get(url) || url).filter(url => url != base);
        if (!images.length) {
            return;
        }
        state.running = true;
        this.renderTone();
        try {
            let result = await this.api.request('EcomCraftStudioToneMatch', { base, images });
            let matched = 0;
            let skipped = new Map();
            for (let item of result.results || []) {
                if (item.url) {
                    let index = this.results.findIndex(url => (state.originals.get(url) || url) == item.image);
                    if (index >= 0) {
                        let old = this.results[index];
                        this.results[index] = item.url;
                        state.originals.set(item.url, item.image);
                        this.carryResultMarks(old, item.url);
                        if (this.selected.delete(old)) {
                            this.selected.add(item.url);
                        }
                    }
                    matched++;
                }
                else if (item.skipped) {
                    skipped.set(item.skipped, (skipped.get(item.skipped) || 0) + 1);
                }
            }
            this.renderResults();
            let reasons = Array.from(skipped, ([code, count]) => tr('{0} 张{1}', count, this.toneSkipText(code)));
            this.message(reasons.length
                ? tr('已统一 {0} 张的色调；没有处理：{1}。', matched, reasons.join(tr('；')))
                : tr('已统一 {0} 张的色调，结果另存为新作品，原图不变。', matched), reasons.length > 0);
        }
        catch (error) {
            this.message(error.message || tr('统一色调没有完成，请稍后重试。'), true);
        }
        finally {
            state.running = false;
            this.renderTone();
        }
    }

    /** Switches every matched copy back to its original picture in the result list (the copies stay in the gallery). */
    static revertTone() {
        let state = SimpleTonePart.tone;
        for (let index = 0; index < this.results.length; index++) {
            let original = state.originals.get(this.results[index]);
            if (original) {
                let copy = this.results[index];
                this.results[index] = original;
                if (this.selected.delete(copy)) {
                    this.selected.add(original);
                }
            }
        }
        state.originals.clear();
        this.renderResults();
        this.renderTone();
    }

    /** Copies a result's group, slot and notice marks to its matched copy, so exports and tiles treat the copy like the original. */
    static carryResultMarks(from, to) {
        if (this.resultGroups.has(from)) {
            this.resultGroups.set(to, this.resultGroups.get(from));
        }
        if (this.resultSlots.has(from)) {
            this.resultSlots.set(to, this.resultSlots.get(from));
        }
        for (let marks of [this.trialImages, this.fusionImages]) {
            if (marks.has(from)) {
                marks.add(to);
            }
        }
    }
}
SimpleMode.adopt(SimpleTonePart);
