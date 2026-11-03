'use strict';

/**
 * Simple mode: the product library, the photo picker and the cut-out. Part of simple.js since R15; static methods are copied onto SimpleMode by adopt(), so callers and this stay unchanged.
 */
class SimpleProductsPart {
    /** Reads the account's saved products for the "from the product library" list, and whether a prompt model is configured so the page knows
     * whether to offer model-suggested selling points (F79, T99). */
    static async loadLibrary() {
        try {
            let result = await this.api.request('EcomCraftStudioProducts');
            this.library = result.products || [];
            this.suggestReady = result.suggest_ready == true;
        }
        catch (error) {
            this.library = [];
        }
        this.renderLibrary();
    }

    /** Refreshes the product-library picker when it is open; choosing a saved product skips uploading and cutting out again. */
    static renderLibrary() {
        if (this.picker == 'library') {
            this.renderPicker();
        }
    }

    /** Opens the picker for the product library or the gallery of generated works (F52). */
    static async openPicker(kind, opener) {
        let tr = this.api.tr;
        this.picker = kind;
        this.galleryItems = kind == 'gallery' ? null : this.galleryItems;
        this.api.$('simple-picker-title').textContent = kind == 'library' ? tr('从商品库选择') : tr('从画廊选择');
        this.api.$('simple-picker-hint').textContent = kind == 'library'
            ? tr('已入库的商品会带上确认过的抠图和位置。')
            : tr('这里是你已生成的作品，选一张作为商品图；作品本身不会被改动，超过 5 MB 的图会先压缩。');
        this.api.$('simple-picker-modal').hidden = false;
        this.renderPicker();
        this.api.dialogEnter(this.api.$('simple-picker-modal'), opener, this.api.$('simple-picker-close'));
        if (kind == 'gallery') {
            await this.loadGalleryItems();
        }
        else {
            await this.loadLibrary();
        }
    }

    /** Closes the picker. */
    static closePicker() {
        if (!this.picker) {
            return;
        }
        this.picker = '';
        this.api.$('simple-picker-modal').hidden = true;
        this.api.dialogLeave(this.api.$('simple-picker-modal'));
    }

    /** Reads this account's recent generated images for the gallery picker (newest first, images only, starred copies skipped). */
    static async loadGalleryItems() {
        try {
            let result = await this.api.request('ListImages', { path: '', depth: 4, sortBy: 'Date', sortReverse: false, filter: null });
            this.galleryItems = (result.files || []).map(file => String(file.src || ''))
                .filter(src => src && !src.startsWith('Starred/') && /\.(png|jpe?g|webp)$/i.test(src)).slice(0, 60);
        }
        catch (error) {
            this.galleryItems = [];
            this.galleryError = error.message || this.api.tr('画廊暂时无法读取，请稍后重试。');
        }
        if (this.picker == 'gallery') {
            this.renderPicker();
        }
        this.galleryError = '';
    }

    /** Fills the picker with saved products or generated works as selectable tiles. */
    static renderPicker() {
        let tr = this.api.tr;
        let tiles = [];
        let empty = '';
        if (this.picker == 'library') {
            for (let saved of this.library) {
                tiles.push(this.pickerTile(saved.cutout_image || saved.image, saved.name, tr('选择商品 {0}', saved.name), async () => {
                    this.closePicker();
                    await this.useSaved(saved);
                }));
            }
            empty = tiles.length ? '' : tr('商品库还是空的。上传的商品图会自动存入商品库。');
        }
        else if (this.picker == 'gallery') {
            if (this.galleryItems == null) {
                empty = tr('正在读取画廊…');
            }
            else {
                for (let src of this.galleryItems) {
                    let name = src.split('/').pop();
                    tiles.push(this.pickerTile(this.api.viewUrl(src), name, tr('选择已生成作品 {0}', name), button => this.useGalleryImage(src, name, button)));
                }
                empty = this.galleryError || (tiles.length ? '' : tr('画廊里还没有已生成的图片。'));
            }
        }
        this.api.$('simple-picker-list').replaceChildren(...tiles);
        let note = this.api.$('simple-picker-empty');
        note.textContent = empty;
        note.hidden = !empty;
    }

    /** One picker tile: a thumbnail with its name. */
    static pickerTile(source, name, label, onPick) {
        let el = this.api.el;
        let button = el('button', 'simple-picker-item');
        button.type = 'button';
        button.title = name;
        button.setAttribute('aria-label', label);
        let image = el('img');
        image.src = source;
        image.alt = '';
        image.loading = 'lazy';
        button.append(image, el('span', '', name));
        button.addEventListener('click', () => onPick(button));
        return button;
    }

    /** Uses a generated work from the gallery as a product photo (F52): read as is when it fits, otherwise made small enough for the 5 MB limit
     * in the browser. Transparency decides the way down (F83): a transparent work is scaled down and stays a PNG, so its alpha is never thrown
     * away onto a white JPEG; only an opaque one becomes JPEG. The work itself is not changed; the copy goes through the same quality check and
     * product library as an upload. */
    static async useGalleryImage(src, name, button) {
        let tr = this.api.tr;
        if (this.products.length >= 20) {
            this.message(tr('一次最多 20 张商品图。'), true);
            return;
        }
        button.disabled = true;
        try {
            let response = await fetch(this.api.viewUrl(src), { credentials: 'same-origin' });
            if (!response.ok) {
                throw new Error('fetch');
            }
            let blob = await response.blob();
            let data = await this.blobToDataUrl(blob);
            if (!['image/png', 'image/jpeg', 'image/webp'].includes(blob.type) || blob.size > this.maxProductBytes) {
                let image = await this.loadImage(data);
                data = await this.shrinkImage(image, await this.imageIsTransparent(image));
            }
            this.closePicker();
            await this.addPhoto(data, name, 'gallery');
        }
        catch (error) {
            this.message(error.message == 'png_oversize'
                ? tr('这张透明图压缩后仍超过 5 MB，无法上传。请换一张更简单的透明图，或先用系统工具压小。')
                : tr('这张作品暂时无法读取，请换一张或稍后重试。'), true);
        }
        finally {
            button.disabled = false;
        }
    }

    /** Reads a blob as a data URL. */
    static blobToDataUrl(blob) {
        return new Promise((resolve, reject) => {
            let reader = new FileReader();
            reader.onload = () => resolve(String(reader.result));
            reader.onerror = () => reject(new Error('read'));
            reader.readAsDataURL(blob);
        });
    }

    /** Scales a loaded image down to at most 16 megapixels. A transparent one stays a PNG at any size (F83): the scale drops until it fits in 5 MB,
     * and when even below the recommended side it cannot, the user's own PNG bytes are kept when they fit the limit — never a white JPEG that throws
     * the alpha away. When they do not fit either, there is nothing acceptable to send, so 'png_oversize' tells the caller to explain and give up. */
    static async shrinkImage(image, transparent) {
        if (transparent) {
            let min = Number(this.quality && this.quality.min_side) || 512;
            let scale = this.compressionScale(image.naturalWidth, image.naturalHeight);
            for (let attempt = 0; attempt < 8; attempt++) {
                let canvas = document.createElement('canvas');
                canvas.width = Math.max(1, Math.round(image.naturalWidth * scale));
                canvas.height = Math.max(1, Math.round(image.naturalHeight * scale));
                canvas.getContext('2d').drawImage(image, 0, 0, canvas.width, canvas.height);
                let blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/png'));
                if (blob && blob.size <= this.maxProductBytes) {
                    return await this.blobToDataUrl(blob);
                }
                if (Math.min(canvas.width, canvas.height) <= min) {
                    break;
                }
                scale *= 0.75;
            }
            if (this.dataUrlBytes(image.src) <= this.maxProductBytes) {
                return image.src;
            }
            throw new Error('png_oversize');
        }
        return await this.compressImage(image.src, this.maxProductBytes);
    }

    /** Re-encodes an image as JPEG on a white background, lowering the quality and then the size until it fits in maxBytes. */
    static async compressImage(dataUrl, maxBytes) {
        let image = await this.loadImage(dataUrl);
        let scale = this.compressionScale(image.naturalWidth, image.naturalHeight);
        for (let attempt = 0; attempt < 8; attempt++) {
            let canvas = document.createElement('canvas');
            canvas.width = Math.max(1, Math.round(image.naturalWidth * scale));
            canvas.height = Math.max(1, Math.round(image.naturalHeight * scale));
            let context = canvas.getContext('2d');
            context.fillStyle = '#FFFFFF';
            context.fillRect(0, 0, canvas.width, canvas.height);
            context.drawImage(image, 0, 0, canvas.width, canvas.height);
            for (let quality of [0.92, 0.82, 0.7]) {
                let blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', quality));
                if (blob && blob.size <= maxBytes) {
                    return await this.blobToDataUrl(blob);
                }
            }
            scale *= 0.75;
        }
        throw new Error('compress');
    }

    /** Makes a saved product the current one, with its confirmed cut-out and placement. */
    static async useSaved(saved) {
        let existing = this.products.find(product => product.id == saved.id);
        if (existing) {
            this.selectProduct(existing);
            return;
        }
        if (this.products.length >= 20) {
            this.message(this.api.tr('一次最多 20 张商品图。'), true);
            return;
        }
        let product = { id: saved.id, name: saved.name, origin: 'library', data: null, source: saved.image, cutout: saved.cutout_image || null, width: 0, height: 0, info: saved.info || {} };
        this.applyAngles(product, saved);
        try {
            let image = await this.loadImage(product.cutout || product.source);
            product.width = image.naturalWidth;
            product.height = image.naturalHeight;
            if (product.cutout) {
                await this.makePreview(product);
            }
        }
        catch (error) {
            this.message(this.api.tr('商品图暂时无法读取：作品库位置可能离线，请稍后重试。'), true);
            return;
        }
        if (saved.placement) {
            this.placement = this.clampPlacement(saved.placement);
            this.api.$('simple-size').value = String(Math.round(this.activePlacement().size * 100));
        }
        this.products.push(product);
        this.selectProduct(product);
    }

    /** Saves a newly uploaded photo to the product library; generation keeps working when saving fails. The save is also the first time the server
     * sees an uploaded transparent PNG, so its edge warnings (F83) arrive here and join the quality notices. */
    static async saveToLibrary(product) {
        try {
            let name = String(product.name || '').replace(/\.[^.]+$/, '').slice(0, 60);
            let result = await this.api.request('EcomCraftStudioProductSave', { image: product.data, name, cutout: product.cutout && product.cutout.startsWith('data:') ? product.cutout : '' });
            product.id = result.product.id;
            product.name = result.product.name;
            this.applyAngles(product, result.product);
            let withEdges = this.mergeIssues(product.qualityIssues || [], this.edgeIssues(result.edge_warnings));
            if (withEdges.length != (product.qualityIssues || []).length) {
                product.qualityIssues = withEdges;
                if (product == this.product) {
                    this.renderQuality();
                }
            }
            await this.loadLibrary();
            if (product == this.product) {
                this.renderProductMeta();
            }
        }
        catch (error) {
            this.message(error.message || this.api.tr('商品没有存入商品库，但仍可以继续出图。'), true);
        }
    }

    /** Takes the server's view of a saved product's angles into the page's copy (F84); angle-tagged quality notices of angles that were removed
     * or renumbered are dropped. The product's own photo fields are refreshed separately by refreshMain. */
    static applyAngles(product, saved) {
        product.id = saved.id;
        product.name = saved.name;
        product.info = saved.info || {};
        let angles = (saved.angles || []).map(angle => ({ id: angle.id, image: angle.image, cutout_image: angle.cutout_image || null, placement: angle.placement || null }));
        if (!angles.length) {
            angles = [{ id: 'main', image: saved.image, cutout_image: saved.cutout_image || null, placement: saved.placement || null }];
        }
        product.angles = angles;
        product.qualityIssues = (product.qualityIssues || []).filter(issue => !issue.angle);
    }

    /** Refreshes a product's own photo fields after its angles changed on the server (F84): the main angle's photo, cut-out and placement lead the
     * page again. */
    static async refreshMain(product) {
        let main = product.angles[0];
        product.source = main.image;
        product.cutout = main.cutout_image;
        try {
            if (product.cutout) {
                await this.makePreview(product);
            }
            else {
                let image = await this.loadImage(product.source);
                product.width = image.naturalWidth;
                product.height = image.naturalHeight;
                product.preview = null;
            }
        }
        catch (error) {
            // The photo cannot be re-read (for example an offline work library); the saved angles still lead the next generation.
        }
        if (product == this.product) {
            this.placement = this.clampPlacement(main.placement || this.placement);
            this.api.$('simple-size').value = String(Math.round(this.activePlacement().size * 100));
            this.selectProduct(product);
        }
        this.renderProducts();
    }

    /** Shows the name field and delete button for a saved current product. */
    static renderProductMeta() {
        let saved = Boolean(this.product && this.product.id);
        this.api.$('simple-product-meta').hidden = !saved;
        if (saved) {
            this.api.$('simple-product-name').value = this.product.name || '';
        }
    }

    /** Renames the current saved product. */
    static async renameProduct(name) {
        if (!this.product || !this.product.id) {
            return;
        }
        try {
            let result = await this.api.request('EcomCraftStudioProductUpdate', { product_id: this.product.id, name });
            this.product.name = result.product.name;
            await this.loadLibrary();
        }
        catch (error) {
            this.message(error.message || this.api.tr('操作失败，请稍后重试。'), true);
        }
    }

    /** Deletes the current product from the library and from this page. */
    static async deleteProduct() {
        if (!this.product || !this.product.id || !window.confirm(this.api.tr('从商品库删除“{0}”？已生成的作品不受影响。', this.product.name))) {
            return;
        }
        try {
            await this.api.request('EcomCraftStudioProductDelete', { product_id: this.product.id });
            this.clearProduct();
            await this.loadLibrary();
        }
        catch (error) {
            this.message(error.message || this.api.tr('操作失败，请稍后重试。'), true);
        }
    }

    /** Opens the file picker for another angle of one saved product (F84); on a phone the picker also offers the camera, so angles can be shot
     * one after another. */
    static openAnglePicker(product) {
        this.angleTarget = product;
        this.api.$('simple-angle-file').click();
    }

    /** Adds angle photos picked on a product card (F84): each goes through the same shrink, transparency and quality checks as a first photo,
     * and at most 3 angles per product — the main image included. */
    static async addAngleFiles(product, files) {
        let tr = this.api.tr;
        let angles = product.angles || [];
        let room = 3 - angles.length;
        let list = Array.from(files || []).filter(file => file);
        if (room <= 0) {
            this.message(tr('一个商品最多 3 张角度图，请先删除不用的角度图。'), true);
            return;
        }
        let overflow = Math.max(0, list.length - room);
        if (overflow > 0) {
            list = list.slice(0, room);
        }
        let added = 0;
        for (let file of list) {
            let data = await this.readLocalFile(file);
            if (data && await this.addAngle(product, data)) {
                added++;
            }
        }
        if (added || overflow > 0) {
            this.message(overflow > 0
                ? tr('已添加 {0} 张其他角度；一个商品最多 3 张（含主图），多出的 {1} 张没有添加。', added, overflow)
                : tr('已添加 {0} 张其他角度。', added), overflow > 0);
        }
    }

    /** Sends one angle photo to a saved product after the browser-side checks (F84); resolves true when it was added. An opaque photo then goes
     * through the same automatic cut-out as the main photo's button and the result is stored on that angle (cutoutAngle). */
    static async addAngle(product, data) {
        let tr = this.api.tr;
        let cutout = '';
        let issues = [];
        try {
            let image = await this.loadImage(data);
            let canvas = document.createElement('canvas');
            canvas.width = Math.min(512, image.naturalWidth);
            canvas.height = Math.max(1, Math.round(image.naturalHeight * canvas.width / Math.max(1, image.naturalWidth)));
            let context = canvas.getContext('2d');
            context.drawImage(image, 0, 0, canvas.width, canvas.height);
            let imageData = context.getImageData(0, 0, canvas.width, canvas.height);
            issues = this.uploadIssues(image.naturalWidth, image.naturalHeight, imageData, this.quality);
            let transparent = this.hasTransparency(imageData);
            if (this.dataUrlBytes(data) > this.maxProductBytes || this.compressionScale(image.naturalWidth, image.naturalHeight) < 1) {
                data = await this.shrinkImage(image, transparent);
                transparent = transparent && data.startsWith('data:image/png');
            }
            if (transparent) {
                cutout = data;
                issues = issues.concat(this.cutoutIssues(imageData, this.quality));
            }
        }
        catch (error) {
            if (error && error.message == 'png_oversize') {
                this.message(tr('这张透明图压缩后仍超过 5 MB，无法上传。请换一张更简单的透明图，或先用系统工具压小。'), true);
                return false;
            }
            // Without a readable preview the angle can still be used by templates that do not preserve the product.
            issues = [];
        }
        try {
            let result = await this.api.request('EcomCraftStudioProductAngleAdd', { product_id: product.id, image: data, cutout });
            this.applyAngles(product, result.product);
            let index = (product.angles || []).findIndex(angle => angle.id == result.angle_id);
            if (index > 0) {
                let tagged = issues.concat(this.edgeIssues(result.edge_warnings)).map(issue => ({ ...issue, angle: index + 1 }));
                product.qualityIssues = this.mergeIssues(product.qualityIssues || [], tagged);
                if (product == this.product) {
                    this.renderQuality();
                }
            }
            this.renderProducts();
            // An opaque angle photo gets the same automatic cut-out as the main photo's button (F84), stored on that angle; a transparent
            // photo already carries its own cut-out. The cut-out never blocks the add: a failure only notices below.
            if (!cutout) {
                await this.cutoutAngle(product, result.angle_id, data);
            }
            return true;
        }
        catch (error) {
            this.message(error.message || tr('角度图没有添加，请稍后重试。'), true);
            return false;
        }
    }

    /** Runs the same automatic cut-out as the main photo's button for one just-added opaque angle photo (F84), and stores the result on that angle.
     * A failure only shows a notice — the angle stays added, and generation cuts out again when it needs to. */
    static async cutoutAngle(product, angleId, data) {
        let tr = this.api.tr;
        try {
            let result = await this.api.request('EcomCraftStudioCutout', { product_image: data });
            // The angle notices of the add are re-tagged after the server's answer, so they survive applyAngles dropping tagged notices.
            let kept = (product.qualityIssues || []).filter(issue => issue.angle);
            let updated = await this.api.request('EcomCraftStudioProductUpdate', { product_id: product.id, angle_id: angleId, cutout: result.cutout });
            this.applyAngles(product, updated.product);
            let index = (product.angles || []).findIndex(angle => angle.id == angleId);
            if (index > 0) {
                let tagged = kept.concat(this.edgeIssues(result.edge_warnings)).map(issue => ({ ...issue, angle: index + 1 }));
                product.qualityIssues = this.mergeIssues(product.qualityIssues || [], tagged);
                if (product == this.product) {
                    this.renderQuality();
                }
            }
            this.renderProducts();
        }
        catch (error) {
            // The failure notice also becomes a quality notice of that angle, so it stays visible after the add flow's summary banner.
            let index = (product.angles || []).findIndex(item => item.id == angleId);
            if (index > 0) {
                product.qualityIssues = this.mergeIssues(product.qualityIssues || [], [{ code: 'angle_cutout_failed', angle: index + 1 }]);
                if (product == this.product) {
                    this.renderQuality();
                }
            }
            this.message(tr('这张角度图没有自动抠图成功，已按原图添加：{0}', error.message || tr('请稍后重试，或换一张透明背景的 PNG。')), true);
        }
    }

    /** Makes another angle the product's main image (F84); the photo, cut-out and placement of the page follow the new main angle. */
    static async setAngleMain(product, angleId) {
        try {
            let result = await this.api.request('EcomCraftStudioProductAngleMain', { product_id: product.id, angle_id: angleId });
            this.applyAngles(product, result.product);
            await this.refreshMain(product);
            if (product == this.product) {
                this.renderQuality();
            }
            await this.loadLibrary();
        }
        catch (error) {
            this.message(error.message || this.api.tr('操作失败，请稍后重试。'), true);
        }
    }

    /** Deletes one angle (F84); the last one cannot be deleted — the product itself has to go. */
    static async deleteAngle(product, angleId) {
        if ((product.angles || []).length <= 1 || !window.confirm(this.api.tr('删除这张角度图？商品和已生成的作品不受影响。'))) {
            return;
        }
        try {
            let result = await this.api.request('EcomCraftStudioProductAngleDelete', { product_id: product.id, angle_id: angleId });
            this.applyAngles(product, result.product);
            await this.refreshMain(product);
            if (product == this.product) {
                this.renderQuality();
            }
            await this.loadLibrary();
        }
        catch (error) {
            this.message(error.message || this.api.tr('操作失败，请稍后重试。'), true);
        }
    }

    /** Remembers the confirmed placement of the current saved product, shortly after the user stops adjusting it. */
    static savePlacement() {
        if (!this.product || !this.product.id) {
            return;
        }
        clearTimeout(this.placementTimer);
        let id = this.product.id;
        let placement = this.clampPlacement(this.placement);
        this.placementTimer = setTimeout(() => {
            this.api.request('EcomCraftStudioProductUpdate', { product_id: id, placement }).catch(() => {
                // The placement is still sent with each generation; saving it is only a convenience.
            });
        }, 800);
    }

    /** A data URL for a product image that may only be available as a server URL. */
    static async toDataUrl(source) {
        if (String(source).startsWith('data:')) {
            return source;
        }
        let response = await fetch(source, { credentials: 'same-origin' });
        if (!response.ok) {
            throw new Error(this.api.tr('商品图暂时无法读取：作品库位置可能离线，请稍后重试。'));
        }
        let blob = await response.blob();
        return await new Promise((resolve, reject) => {
            let reader = new FileReader();
            reader.onload = () => resolve(String(reader.result));
            reader.onerror = () => reject(new Error('read'));
            reader.readAsDataURL(blob);
        });
    }

    /** Reads one file into a data URL after the upload checks (format, size, HEIC), showing the plain-language message and resolving null when the
     * file cannot be used (F85). */
    static readLocalFile(file) {
        return new Promise(resolve => {
            let tr = this.api.tr;
            let name = String(file.name || '').toLowerCase();
            if (file.type == 'image/heic' || file.type == 'image/heif' || name.endsWith('.heic') || name.endsWith('.heif')) {
                this.message(tr('这张 HEIC 照片浏览器打不开，请在手机或电脑上另存为 JPEG 后再上传，或用“拍照上传”现场拍一张。'), true);
                resolve(null);
                return;
            }
            if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type)) {
                this.message(tr('请选择 PNG、JPEG 或 WebP 图片。文件尚未上传。'), true);
                resolve(null);
                return;
            }
            if (file.size > this.maxLocalReadBytes) {
                this.message(tr('这张图片超过 64 MB，请先用系统工具压缩或裁小后再上传。文件尚未上传。'), true);
                resolve(null);
                return;
            }
            let reader = new FileReader();
            reader.onload = () => resolve(String(reader.result));
            reader.onerror = () => {
                this.message(tr('无法读取文件，请重新选择。'), true);
                resolve(null);
            };
            reader.readAsDataURL(file);
        });
    }

    /** Reads a product photo from this computer or the camera into memory (origin 'local' or 'camera'). Any photo up to 64 MB is read (F85):
     * a phone photo over 5 MB is compressed in the browser by addPhoto instead of being refused, whatever its format. HEIC/HEIF, which
     * browsers cannot draw, gets its own plain-language hint instead of the generic format message. */
    static async readFile(file, origin) {
        if (!file) {
            return null;
        }
        let data = await this.readLocalFile(file);
        if (!data) {
            return null;
        }
        return await this.addPhoto(data, file.name, origin || 'local');
    }

    /** Entry for every multi-file pick (file input, drop, camera): one photo goes straight in; several first ask whether they are angles of one
     * product or separate products (F84), and nothing is added until the user answers. */
    static readFiles(files, origin, opener) {
        let list = Array.from(files || []).filter(file => file);
        if (!list.length) {
            return;
        }
        if (list.length == 1) {
            this.readFile(list[0], origin);
            return;
        }
        this.pendingFiles = { files: list, origin: origin || 'local' };
        let modal = this.api.$('simple-angles-ask-modal');
        modal.hidden = false;
        this.api.dialogEnter(modal, opener, this.api.$('simple-angles-ask-same'));
    }

    /** Answers the "angles of one product or separate products?" question (F84); null cancels and nothing is added. */
    static resolveAngleAsk(sameProduct) {
        let modal = this.api.$('simple-angles-ask-modal');
        if (modal.hidden) {
            return;
        }
        modal.hidden = true;
        this.api.dialogLeave(modal);
        let pending = this.pendingFiles;
        this.pendingFiles = null;
        if (!pending) {
            return;
        }
        if (sameProduct === true) {
            this.addAsAngles(pending.files, pending.origin);
        }
        else if (sameProduct === false) {
            for (let file of pending.files) {
                this.readFile(file, pending.origin);
            }
        }
    }

    /** Adds the picked photos as one product (F84): the first becomes the main image, the rest its other angles — at most 3 per product. */
    static async addAsAngles(files, origin) {
        let tr = this.api.tr;
        let list = files.slice(0, 3);
        let overflow = files.length - 3;
        let first = await this.readLocalFile(list[0]);
        if (!first) {
            return;
        }
        let product = await this.addPhoto(first, list[0].name, origin);
        if (!product) {
            return;
        }
        await product.savePromise;
        if (!product.id) {
            this.message(tr('商品还没有存入商品库，暂时不能添加其他角度，请稍后重试。'), true);
            return;
        }
        let added = 0;
        for (let file of list.slice(1)) {
            let data = await this.readLocalFile(file);
            if (data && await this.addAngle(product, data)) {
                added++;
            }
        }
        if (added || overflow > 0) {
            this.message(overflow > 0
                ? tr('已添加 {0} 张其他角度；一个商品最多 3 张（含主图），多出的 {1} 张没有添加。', added, overflow)
                : tr('已添加 {0} 张其他角度。', added), overflow > 0);
        }
    }

    /** Adds a new product photo (a data URL read from a file up to 64 MB, or a smaller one from the library or gallery pickers) from any entry (F52):
     * checks its quality (F38), makes it the current photo and saves it to the product library. <origin> is 'local', 'camera' or 'gallery' and is shown
     * on the thumbnail. A photo over 5 MB or over 16 megapixels is shrunk here (F85): a phone photo becomes a JPEG within the upload limit, a transparent
     * one is scaled down as a PNG (F83), and a transparent one that not even the smallest recommended size fits is refused with its own message —
     * the server would reject anything larger. The preview and the placement match what the user saw: the browser already shows a photo turned
     * upright by its EXIF, and the server turns the stored bytes upright too before cutting out. */
    static async addPhoto(data, name, origin) {
        if (this.products.length >= 20) {
            this.message(this.api.tr('一次最多 20 张商品图。'), true);
            return null;
        }
        let tr = this.api.tr;
        let product = { id: null, name, origin, data, source: data, cutout: null, width: 0, height: 0, angles: [] };
        this.product = product;
        this.products.push(product);
        try {
            let image = await this.loadImage(product.data);
            product.width = image.naturalWidth;
            product.height = image.naturalHeight;
            let canvas = document.createElement('canvas');
            canvas.width = Math.min(512, image.naturalWidth);
            canvas.height = Math.max(1, Math.round(image.naturalHeight * canvas.width / Math.max(1, image.naturalWidth)));
            let context = canvas.getContext('2d');
            context.drawImage(image, 0, 0, canvas.width, canvas.height);
            let imageData = context.getImageData(0, 0, canvas.width, canvas.height);
            // Quality checks (F38): warnings with suggestions, never a block; the photo is usable anyway.
            product.qualityIssues = this.uploadIssues(image.naturalWidth, image.naturalHeight, imageData, this.quality);
            let transparent = this.hasTransparency(imageData);
            let overBytes = this.dataUrlBytes(product.data) > this.maxProductBytes;
            if (overBytes || this.compressionScale(image.naturalWidth, image.naturalHeight) < 1) {
                let before = product.data;
                try {
                    product.data = await this.shrinkImage(image, transparent);
                }
                catch (error) {
                    if (error && error.message == 'png_oversize') {
                        this.dropProduct(product);
                        this.message(tr('这张透明图压缩后仍超过 5 MB，无法上传。请换一张更简单的透明图，或先用系统工具压小。'), true);
                        return null;
                    }
                    throw error;
                }
                product.source = product.data;
                let shrunk = await this.loadImage(product.data);
                product.width = shrunk.naturalWidth;
                product.height = shrunk.naturalHeight;
                // A transparent PNG that cannot be re-encoded within 5 MB keeps its own bytes (F83); the notice explains why the original stays.
                if (transparent && product.data == before) {
                    product.qualityIssues = product.qualityIssues.concat([{ code: 'oversize' }]);
                }
                transparent = transparent && product.data.startsWith('data:image/png');
            }
            if (transparent) {
                product.cutout = product.data;
                await this.makePreview(product);
                product.qualityIssues = product.qualityIssues.concat(this.cutoutIssues(imageData, this.quality));
            }
        }
        catch (error) {
            // Without a readable preview the photo can still be used by templates that do not preserve the product.
            product.qualityIssues = [];
        }
        if (this.product == product) {
            this.selectProduct(product);
        }
        else {
            this.renderProducts();
        }
        this.message('', false);
        product.savePromise = this.saveToLibrary(product);
        return product;
    }

    /** The bytes a data URL carries, from its base64 length: the closest a browser can tell without re-encoding. */
    static dataUrlBytes(data) {
        let base64 = String(data).slice(String(data).indexOf(',') + 1);
        let padding = base64.endsWith('==') ? 2 : base64.endsWith('=') ? 1 : 0;
        return Math.max(0, Math.floor(base64.length * 3 / 4) - padding);
    }

    /** Whether a loaded image is a cut-out, the same rule as an upload (F83): drawn small and read back, so large works are not decoded twice more. */
    static async imageIsTransparent(image) {
        try {
            let canvas = document.createElement('canvas');
            canvas.width = Math.min(512, image.naturalWidth);
            canvas.height = Math.max(1, Math.round(image.naturalHeight * canvas.width / Math.max(1, image.naturalWidth)));
            let context = canvas.getContext('2d');
            context.drawImage(image, 0, 0, canvas.width, canvas.height);
            return this.hasTransparency(context.getImageData(0, 0, canvas.width, canvas.height));
        }
        catch (error) {
            return false;
        }
    }

    /** Takes a just-added photo back out of the list after it was refused (too large to send at all), and shows the picker state again. */
    static dropProduct(product) {
        let restore = this.product;
        this.product = product;
        this.clearProduct();
        if (restore != product && restore) {
            this.selectProduct(restore);
        }
    }

    /** Loads an image element from a data URL. */
    static loadImage(source) {
        return new Promise((resolve, reject) => {
            let image = new Image();
            image.onload = () => resolve(image);
            image.onerror = () => reject(new Error('image'));
            image.src = source;
        });
    }

    /** Asks the server to remove the background; the result keeps the original product pixels. */
    static async runCutout() {
        let tr = this.api.tr;
        if (!this.product || this.cutting) {
            return;
        }
        this.cutting = true;
        this.renderCutout();
        this.update();
        try {
            let product = this.product;
            let source = product.data || await this.toDataUrl(product.source);
            let result = await this.api.request('EcomCraftStudioCutout', { product_image: source });
            product.cutout = result.cutout;
            await this.makePreview(product);
            // A tiny cut-out means the photo was taken too far away (F38): suggest getting closer, but let the user continue.
            try {
                let image = await this.loadImage(product.cutout);
                let canvas = document.createElement('canvas');
                canvas.width = Math.min(512, image.naturalWidth);
                canvas.height = Math.max(1, Math.round(image.naturalHeight * canvas.width / Math.max(1, image.naturalWidth)));
                let context = canvas.getContext('2d');
                context.drawImage(image, 0, 0, canvas.width, canvas.height);
                let ratioIssues = this.cutoutIssues(context.getImageData(0, 0, canvas.width, canvas.height), this.quality);
                product.qualityIssues = (product.qualityIssues || []).filter(issue => issue.code != 'ratio').concat(ratioIssues);
            }
            catch (error) {
                // The cut-out is still usable when its preview cannot be read back.
            }
            // Edge warnings of the server's cut-out (F83): plain-language notices, never a block.
            product.qualityIssues = this.mergeIssues(product.qualityIssues || [], this.edgeIssues(result.edge_warnings));
            if (product.id) {
                this.api.request('EcomCraftStudioProductUpdate', { product_id: product.id, cutout: result.cutout }).then(() => this.loadLibrary()).catch(() => {
                    // The cut-out is still used on this page; it just is not remembered for next time.
                });
            }
            this.message('', false);
        }
        catch (error) {
            this.message(error.message || tr('抠图没有完成，请稍后重试，或上传已经去掉背景的透明 PNG。'), true);
        }
        finally {
            this.cutting = false;
            this.renderCutout();
            this.update();
        }
    }

    /** Forgets the product photo. */
    static clearProduct() {
        this.products = this.products.filter(product => product != this.product);
        if (this.products.length) {
            this.selectProduct(this.products[this.products.length - 1]);
            return;
        }
        this.product = null;
        this.renderProducts();
        this.api.$('simple-cutout').hidden = true;
        this.api.$('simple-file').value = '';
        this.api.$('simple-preview').hidden = true;
        this.api.$('simple-preview').removeAttribute('src');
        this.api.$('simple-drop-text').textContent = this.api.tr('点击或拖入商品图');
        this.api.$('simple-clear').hidden = true;
        this.update();
    }

    /** Shows the current product's quality warnings under the upload step; empty when there is nothing to complain about.
     * The preview-background toggle (F83) is shown exactly while a photo is there to look at. */
    static renderQuality() {
        if (!this.api) {
            return;
        }
        let box = this.api.$('simple-quality');
        let issues = this.product && this.product.qualityIssues ? this.product.qualityIssues : [];
        box.replaceChildren(...issues.map(issue => this.api.el('p', '', issue.angle
            ? this.api.tr('角度 {0}：{1}', issue.angle, this.qualityText(issue))
            : this.qualityText(issue))));
        box.hidden = !issues.length;
        this.api.$('simple-bg-row').hidden = !this.product;
    }

    /** The plain-language source of a product photo (F52): this computer, the camera, the product library or the gallery. */
    static originText(origin) {
        let names = { local: '本机', camera: '拍照', library: '商品库', gallery: '画廊' };
        return this.api.tr(names[origin] || '本机');
    }

    /** Shows the product photos as thumbnails with their source (F52) and, once saved, their angles (F84); clicking one makes it the current photo. */
    static renderProducts() {
        let el = this.api.el;
        let list = this.api.$('simple-products');
        let items = [];
        for (let [index, product] of this.products.entries()) {
            let cell = el('div', 'simple-product-cell');
            let button = el('button', 'simple-product');
            button.type = 'button';
            button.setAttribute('aria-pressed', product == this.product ? 'true' : 'false');
            let origin = this.originText(product.origin);
            button.setAttribute('aria-label', this.api.tr('商品图 {0}（来自{1}）', index + 1, origin));
            button.title = product.name || '';
            let image = el('img');
            image.src = product.cutout || product.source || product.data;
            image.alt = '';
            button.append(image, el('span', 'simple-product-source', origin));
            button.addEventListener('click', () => this.selectProduct(product));
            cell.append(button, this.renderAngles(product));
            items.push(cell);
        }
        list.replaceChildren(...items);
        list.hidden = this.products.length == 0;
    }

    /** The angle strip of a saved product's card (F84): every angle's thumbnail — the first one marked as the main image — with a delete button per
     * angle, "make main" for the others and "add another angle" while there is room. Nothing is shown before the product has been saved. */
    static renderAngles(product) {
        let el = this.api.el;
        let tr = this.api.tr;
        let box = el('div', 'simple-angles');
        let angles = product.angles || [];
        if (!product.id || !angles.length) {
            box.hidden = true;
            return box;
        }
        for (let [index, angle] of angles.entries()) {
            let item = el('span', 'simple-angle');
            let image = el('img');
            image.src = angle.cutout_image || angle.image;
            image.alt = '';
            image.loading = 'lazy';
            item.append(image, el('span', 'simple-angle-badge', index == 0 ? tr('主图') : tr('角度 {0}', index + 1)));
            if (index > 0) {
                let mainButton = el('button', 'link-btn simple-angle-btn', tr('设为主图'));
                mainButton.type = 'button';
                mainButton.addEventListener('click', () => this.setAngleMain(product, angle.id));
                item.append(mainButton);
            }
            if (angles.length > 1) {
                let deleteButton = el('button', 'link-btn simple-angle-btn', tr('删除'));
                deleteButton.type = 'button';
                deleteButton.addEventListener('click', () => this.deleteAngle(product, angle.id));
                item.append(deleteButton);
            }
            box.append(item);
        }
        if (angles.length < 3) {
            let add = el('button', 'link-btn simple-angle-btn', tr('添加其他角度'));
            add.type = 'button';
            add.addEventListener('click', () => this.openAnglePicker(product));
            box.append(add);
        }
        // Selling info per product (F79, T99): filled in a dialog from the card, never in the step-2 generation flow (A30).
        let info = el('button', 'link-btn simple-angle-btn', tr('卖点信息'));
        info.type = 'button';
        info.setAttribute('aria-label', tr('打开 {0} 的卖点信息', product.name || tr('商品')));
        info.addEventListener('click', e => this.openProductInfo(product, e.currentTarget));
        box.append(info);
        return box;
    }

    /** Makes a product photo the current one for preview, cut-out and single generation. */
    static selectProduct(product) {
        this.product = product;
        let preview = this.api.$('simple-preview');
        preview.src = product.source || product.data;
        preview.hidden = false;
        this.api.$('simple-drop-text').textContent = product.name;
        this.api.$('simple-clear').hidden = false;
        this.renderProducts();
        this.renderProductMeta();
        this.renderCutout();
        this.update();
    }

    /** Upload controls of step 1 (F52): this computer, camera, library and gallery pickers. */
    static bindUpload() {
        let $ = this.api.$;
        let drop = $('simple-drop');
        this.bindBackgrounds();
        // Four equal ways to add a product photo (F52): this computer, the camera, the product library and the gallery of generated works.
        $('simple-file-btn').addEventListener('click', () => $('simple-file').click());
        $('simple-library-btn').addEventListener('click', e => this.openPicker('library', e.currentTarget));
        $('simple-gallery-btn').addEventListener('click', e => this.openPicker('gallery', e.currentTarget));
        $('simple-picker-close').addEventListener('click', () => this.closePicker());
        $('simple-picker-backdrop').addEventListener('click', () => this.closePicker());
        $('simple-file').addEventListener('change', e => {
            this.readFiles(e.target.files, 'local', e.currentTarget);
            e.target.value = '';
        });
        drop.addEventListener('dragover', e => {
            e.preventDefault();
            drop.classList.add('is-over');
        });
        drop.addEventListener('dragleave', () => drop.classList.remove('is-over'));
        drop.addEventListener('drop', e => {
            e.preventDefault();
            drop.classList.remove('is-over');
            this.readFiles(e.dataTransfer.files, 'local', null);
        });
        $('simple-clear').addEventListener('click', () => this.clearProduct());
        // Camera capture (F42): the capture attribute opens the camera on phones and works over plain http, unlike secure-context APIs.
        $('simple-camera-btn').addEventListener('click', () => $('simple-camera').click());
        $('simple-camera').addEventListener('change', e => {
            this.readFiles(e.target.files, 'camera', e.currentTarget);
            e.target.value = '';
        });
        $('simple-cutout-run').addEventListener('click', () => this.runCutout());
        // Angle photos of one saved product (F84): the card's "add another angle" opens this picker, which on a phone also offers the camera.
        $('simple-angle-file').addEventListener('change', e => {
            let target = this.angleTarget;
            this.angleTarget = null;
            if (target) {
                this.addAngleFiles(target, e.target.files);
            }
            e.target.value = '';
        });
        $('simple-angles-ask-same').addEventListener('click', () => this.resolveAngleAsk(true));
        $('simple-angles-ask-separate').addEventListener('click', () => this.resolveAngleAsk(false));
        $('simple-angles-ask-close').addEventListener('click', () => this.resolveAngleAsk(null));
        $('simple-angles-ask-backdrop').addEventListener('click', () => this.resolveAngleAsk(null));
    }

    /** Product naming and removal in step 1; the size slider's committed change saves the placement. */
    static bindProductControls() {
        let $ = this.api.$;
        $('simple-product-name').addEventListener('change', e => this.renameProduct(e.target.value));
        $('simple-product-delete').addEventListener('click', () => this.deleteProduct());
        $('simple-size').addEventListener('change', () => this.savePlacement());
    }

    /** The preview-background toggle of step 1 (F83): white, black or checkerboard behind the uploaded picture and the cut-out, so a bright or dark
     * rim becomes visible before generating. The choice is remembered in this browser. */
    static bindBackgrounds() {
        let $ = this.api.$;
        let buttons = { white: $('simple-bg-white'), black: $('simple-bg-black'), checker: $('simple-bg-checker') };
        let apply = background => {
            $('view-simple').dataset.checkBg = background;
            this.api.writeStorage('ecomcraft.check-bg', background);
            for (let name of Object.keys(buttons)) {
                buttons[name].setAttribute('aria-pressed', name == background ? 'true' : 'false');
            }
        };
        for (let name of Object.keys(buttons)) {
            buttons[name].addEventListener('click', () => apply(name));
        }
        let saved = this.api.readStorage('ecomcraft.check-bg');
        apply(['white', 'black', 'checker'].includes(saved) ? saved : 'checker');
    }
}
SimpleMode.adopt(SimpleProductsPart);
