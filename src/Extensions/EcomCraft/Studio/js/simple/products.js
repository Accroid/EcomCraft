'use strict';

/**
 * Simple mode: the product library, the photo picker and the cut-out. Part of simple.js since R15; static methods are copied onto SimpleMode by adopt(), so callers and this stay unchanged.
 */
class SimpleProductsPart {
    /** Reads the account's saved products for the "from the product library" list. */
    static async loadLibrary() {
        try {
            let result = await this.api.request('EcomCraftStudioProducts');
            this.library = result.products || [];
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
        let product = { id: saved.id, name: saved.name, origin: 'library', data: null, source: saved.image, cutout: saved.cutout_image || null, width: 0, height: 0 };
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

    /** Reads a product photo from this computer or the camera into memory (origin 'local' or 'camera'). PNG and WebP can carry a cut-out (F83),
     * so up to maxLocalReadBytes they are read anyway and shrunk in the browser later, instead of being refused here at the 5 MB upload limit;
     * a JPEG over 5 MB has no alpha to lose and is refused as before. */
    static readFile(file, origin) {
        let tr = this.api.tr;
        if (!file) {
            return;
        }
        let typeOk = ['image/png', 'image/jpeg', 'image/webp'].includes(file.type);
        let canHoldAlpha = file.type == 'image/png' || file.type == 'image/webp';
        if (!typeOk || (!canHoldAlpha && file.size > this.maxProductBytes)) {
            this.message(tr('请选择不超过 5 MB 的 PNG、JPEG 或 WebP 图片。文件尚未上传。'), true);
            return;
        }
        if (file.size > this.maxLocalReadBytes) {
            this.message(tr('这张图片超过 64 MB，请先用系统工具压缩或裁小后再上传。文件尚未上传。'), true);
            return;
        }
        let reader = new FileReader();
        reader.onload = () => this.addPhoto(String(reader.result), file.name, origin || 'local');
        reader.onerror = () => this.message(tr('无法读取文件，请重新选择。'), true);
        reader.readAsDataURL(file);
    }

    /** Adds a new product photo (a data URL of at most 5 MB) from any entry (F52): checks its quality (F38), makes it the current photo and saves it to the product library.
     * <origin> is 'local', 'camera' or 'gallery' and is shown on the thumbnail. A photo over 5 MB or over 16 megapixels is shrunk here (F83):
     * a transparent one is scaled down as a PNG, an opaque local file is refused with the picker's own message, and a transparent one that not even
     * the smallest recommended size fits is refused with its own message — the server would reject anything larger. */
    static async addPhoto(data, name, origin) {
        if (this.products.length >= 20) {
            this.message(this.api.tr('一次最多 20 张商品图。'), true);
            return;
        }
        let tr = this.api.tr;
        let product = { id: null, name, origin, data, source: data, cutout: null, width: 0, height: 0 };
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
            if (overBytes && !transparent && (origin == 'local' || origin == 'camera')) {
                // An opaque local file over 5 MB cannot keep anything by shrinking: refuse it right away, as the picker itself does.
                this.dropProduct(product);
                this.message(tr('请选择不超过 5 MB 的 PNG、JPEG 或 WebP 图片。文件尚未上传。'), true);
                return;
            }
            if (overBytes || this.compressionScale(image.naturalWidth, image.naturalHeight) < 1) {
                let before = product.data;
                try {
                    product.data = await this.shrinkImage(image, transparent);
                }
                catch (error) {
                    if (error && error.message == 'png_oversize') {
                        this.dropProduct(product);
                        this.message(tr('这张透明图压缩后仍超过 5 MB，无法上传。请换一张更简单的透明图，或先用系统工具压小。'), true);
                        return;
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
        this.saveToLibrary(product);
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
        box.replaceChildren(...issues.map(issue => this.api.el('p', '', this.qualityText(issue))));
        box.hidden = !issues.length;
        this.api.$('simple-bg-row').hidden = !this.product;
    }

    /** The plain-language source of a product photo (F52): this computer, the camera, the product library or the gallery. */
    static originText(origin) {
        let names = { local: '本机', camera: '拍照', library: '商品库', gallery: '画廊' };
        return this.api.tr(names[origin] || '本机');
    }

    /** Shows the product photos as thumbnails with their source (F52); clicking one makes it the current photo. */
    static renderProducts() {
        let el = this.api.el;
        let list = this.api.$('simple-products');
        let items = [];
        for (let [index, product] of this.products.entries()) {
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
            items.push(button);
        }
        list.replaceChildren(...items);
        list.hidden = this.products.length == 0;
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
            for (let file of Array.from(e.target.files || [])) {
                this.readFile(file, 'local');
            }
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
            for (let file of Array.from(e.dataTransfer.files || [])) {
                this.readFile(file, 'local');
            }
        });
        $('simple-clear').addEventListener('click', () => this.clearProduct());
        // Camera capture (F42): the capture attribute opens the camera on phones and works over plain http, unlike secure-context APIs.
        $('simple-camera-btn').addEventListener('click', () => $('simple-camera').click());
        $('simple-camera').addEventListener('change', e => {
            for (let file of Array.from(e.target.files || [])) {
                this.readFile(file, 'camera');
            }
            e.target.value = '';
        });
        $('simple-cutout-run').addEventListener('click', () => this.runCutout());
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
