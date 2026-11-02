'use strict';

/**
 * Simple mode: the placement preview and its drag handling (T79). Part of simple.js since R15; static methods are copied onto SimpleMode by adopt(), so callers and this stay unchanged.
 */
class SimplePlacementPart {
    /** Shows the cut-out panel only for templates that keep the original product pixels, and explains what is missing.
     * Reference mode always runs the scene template, which keeps the product, so the panel (with "place for reference n", T79) follows the product alone. */
    static renderCutout() {
        let tr = this.api.tr;
        let template = this.currentTemplate();
        let needed = Boolean(this.product && (this.referenceMode() || (template && template.preserve_product)));
        this.api.$('simple-cutout').hidden = !needed;
        if (!needed) {
            return;
        }
        let status = this.cutting ? tr('正在抠图…')
            : this.product.cutout ? tr('确认抠图结果，可拖动商品调整位置，或用滑块调整大小。不满意可以重新上传。')
            // Reference mode cuts the product out by itself when generating; the preview needs the cut-out only for placing it.
            : this.referenceMode() ? tr('出图时会自动抠图。想对照参考图调整商品的位置和大小，请先点“自动抠图”。')
            : tr('这个图片类型会保留原商品的像素，需要先去掉商品图的背景。点“自动抠图”，或上传透明背景的 PNG。');
        this.api.$('simple-cutout-status').textContent = status;
        this.api.$('simple-cutout-run').hidden = Boolean(this.product.cutout);
        this.api.$('simple-cutout-run').disabled = this.cutting;
        this.renderStage();
    }

    /** Draws the cut-out on the square preview at the current placement. */
    static renderStage() {
        let image = this.api.$('simple-stage-product');
        let stage = this.api.$('simple-stage');
        if (!this.product || !this.product.cutout || this.api.$('simple-cutout').hidden) {
            image.hidden = true;
            return;
        }
        let preview = this.product.preview || this.product.cutout;
        if (image.getAttribute('src') != preview) {
            image.src = preview;
        }
        // The preview has the output's aspect ratio, so the placement looks the way it will be generated (F73).
        let aspect = this.stageAspect();
        let ratio = aspect.width + ' / ' + aspect.height;
        if (stage.style.aspectRatio != ratio) {
            stage.style.aspectRatio = ratio;
        }
        let width = stage.clientWidth || 300;
        let height = stage.clientHeight || Math.round(width * aspect.height / aspect.width);
        let rect = this.placementRect(width, height, this.product.width || 1, this.product.height || 1, this.activePlacement());
        image.style.left = `${rect.x}px`;
        image.style.top = `${rect.y}px`;
        image.style.width = `${rect.width}px`;
        image.style.height = `${rect.height}px`;
        image.hidden = false;
    }

    /** The output size the preview stands for: the chosen size, or with "by platform" the slot size of the current image type (the scene slot in
     * reference mode); square when it is unknown, as for whole sets whose image types differ. */
    static stageAspect() {
        if (this.outputSize.mode != 'auto') {
            return this.outputSizeProblem() ? { width: 1, height: 1 } : { width: this.outputSize.width, height: this.outputSize.height };
        }
        let template = this.currentTemplate();
        let kind = this.referenceMode() ? 'scene_main' : this.chosenTask == 'single' && template ? template.slot_kind : '';
        let slot = kind ? this.slotSize(kind) : null;
        return slot || { width: 1, height: 1 };
    }

    /** The placement the preview edits: the chosen reference's (T79) once adjusted, otherwise the product's own. */
    static activePlacement() {
        let reference = this.referenceMode() ? this.references[this.placementRef] : null;
        return reference && reference.placement ? reference.placement : this.placement;
    }

    /** Stores a placement edited on the preview: on the chosen reference in reference mode (T79), otherwise as the product's own. */
    static setActivePlacement(placement) {
        let reference = this.referenceMode() ? this.references[this.placementRef] : null;
        if (reference) {
            let first = !reference.placement;
            reference.placement = placement;
            // Only the first change alters the button label ("adjusted"); dragging calls this on every move.
            if (first) {
                this.renderPlacementPicker();
            }
        }
        else {
            this.placement = placement;
        }
    }

    /** Lists "place for reference n" above the preview in reference mode (T79) and shows the chosen reference behind the product. */
    static renderPlacementPicker() {
        let tr = this.api.tr;
        let el = this.api.el;
        if (this.placementRef >= this.references.length) {
            this.placementRef = -1;
        }
        let box = this.api.$('simple-ref-place');
        // The direct-fusion trial places nothing (F84): the model takes the reference and the product photos as they are.
        box.hidden = !this.references.length || this.referenceEditOn();
        let buttons = [];
        for (let index = -1; index < this.references.length; index++) {
            let reference = this.references[index];
            let label = index < 0 ? tr('商品自己的摆放') : reference.placement ? tr('参考图 {0}（已调整）', index + 1) : tr('参考图 {0}', index + 1);
            let button = el('button', 'btn btn-sm', label);
            button.type = 'button';
            button.setAttribute('aria-pressed', index == this.placementRef ? 'true' : 'false');
            button.addEventListener('click', () => this.choosePlacementRef(index));
            buttons.push(button);
        }
        this.api.$('simple-ref-place-btns').replaceChildren(...buttons);
        let backdrop = this.api.$('simple-stage-ref');
        let chosen = this.referenceMode() ? this.references[this.placementRef] : null;
        backdrop.hidden = !chosen;
        if (chosen && backdrop.getAttribute('src') != chosen.image) {
            backdrop.src = chosen.image;
        }
    }

    /** Makes the preview edit one reference's placement (T79), starting from the product's own until it is adjusted; -1 goes back to the product's. */
    static choosePlacementRef(index) {
        this.placementRef = index;
        this.renderPlacementPicker();
        this.api.$('simple-size').value = String(Math.round(this.activePlacement().size * 100));
        this.renderStage();
    }

    /** Starts dragging the product on the preview. */
    static startDrag(event) {
        event.preventDefault();
        event.target.setPointerCapture(event.pointerId);
        event.target.classList.add('is-dragging');
        this.drag = { startX: event.clientX, startY: event.clientY, origin: { ...this.activePlacement() } };
    }

    /** Moves the product with the pointer; the center stays inside the picture. */
    static moveDrag(event) {
        if (!this.drag) {
            return;
        }
        let stage = this.api.$('simple-stage');
        let width = stage.clientWidth || 300;
        let height = stage.clientHeight || width;
        this.setActivePlacement(this.clampPlacement({
            x: this.drag.origin.x + (event.clientX - this.drag.startX) / width,
            y: this.drag.origin.y + (event.clientY - this.drag.startY) / height,
            size: this.activePlacement().size
        }));
        this.renderStage();
    }

    /** Ends dragging. */
    static endDrag(event) {
        if (!this.drag) {
            return;
        }
        event.target.classList.remove('is-dragging');
        this.drag = null;
        // A reference's placement lives only in this page (T79); only the product's own one is remembered in the library.
        if (this.placementRef < 0) {
            this.savePlacement();
        }
    }

    /** Placement controls of step 1: reset, the size slider and the drag handling (T79). */
    static bindPlacement() {
        let $ = this.api.$;
        $('simple-placement-reset').addEventListener('click', () => {
            this.setActivePlacement({ x: 0.5, y: 0.56, size: 0.55 });
            $('simple-size').value = '55';
            this.renderStage();
        });
        $('simple-size').addEventListener('input', e => {
            this.setActivePlacement(this.clampPlacement({ ...this.activePlacement(), size: Number(e.target.value) / 100 }));
            this.renderStage();
        });
        let product = $('simple-stage-product');
        product.addEventListener('pointerdown', e => this.startDrag(e));
        product.addEventListener('pointermove', e => this.moveDrag(e));
        product.addEventListener('pointerup', e => this.endDrag(e));
        product.addEventListener('pointercancel', e => this.endDrag(e));
        window.addEventListener('resize', () => this.renderStage());
    }
}
SimpleMode.adopt(SimplePlacementPart);
