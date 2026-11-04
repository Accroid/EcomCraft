/** Canvas of the professional mode (T127): shows one result at a time fitted to 90% of the canvas, zooms with the wheel around the
 * pointer, pans by dragging and fits again on double-click. Videos are only fitted, so their own controls keep working.
 * The image is resized rather than CSS-scaled, so it stays sharp; from 200% on it is drawn with square pixels for close inspection. */
class ProViewer {
    /** Share of the canvas a fitted result fills. */
    static fitShare = 0.9;

    /** Smallest and largest zoom, relative to the result's own pixels. */
    static minScale = 0.05;
    static maxScale = 8;

    /** Zoom step of the buttons and the + / - keys. */
    static step = 1.25;

    /** Canvas backdrops: the seamless paper of the page, 18% neutral gray for judging colour, and black. */
    static backdrops = ['paper', 'gray', 'black'];

    /** Browser storage key of the chosen backdrop; it is only a per-browser preference. */
    static backdropKey = 'ecomcraft-pro-backdrop';

    constructor() {
        /** Canvas element that takes wheel and pointer input. */
        this.viewport = null;
        /** Element that holds the image or video and carries its size and position. */
        this.frame = null;
        /** Called after every change of zoom, position, backdrop or shown result, so the toolbar can follow. */
        this.onChange = null;
        /** Address of the shown result, so showing the same result again keeps its zoom. */
        this.url = '';
        /** True when the shown result is a video. */
        this.video = false;
        /** Pixel size of the shown result; 0 until it has loaded. */
        this.width = 0;
        this.height = 0;
        /** Zoom (1 = the result's own pixels) and offset of the result's centre from the canvas centre in screen pixels. */
        this.scale = 1;
        this.panX = 0;
        this.panY = 0;
        /** True while the result follows the canvas size; zooming or dragging turns it off until the next fit. */
        this.fitted = true;
        /** Drag in progress: pointer id, start point and the offset at the start. */
        this.drag = null;
        /** Current canvas backdrop. */
        this.backdrop = 'paper';
    }

    /** Binds the canvas and its frame; onChange runs after each change. */
    attach(viewport, frame, onChange) {
        this.viewport = viewport;
        this.frame = frame;
        this.onChange = onChange;
        // A passive listener could not stop the page from scrolling while the wheel zooms.
        viewport.addEventListener('wheel', event => this.wheel(event), { passive: false });
        viewport.addEventListener('pointerdown', event => this.pointerDown(event));
        viewport.addEventListener('pointermove', event => this.pointerMove(event));
        viewport.addEventListener('pointerup', event => this.pointerUp(event));
        viewport.addEventListener('pointercancel', event => this.pointerUp(event));
        viewport.addEventListener('dblclick', () => this.fit());
        if (typeof ResizeObserver != 'undefined') {
            new ResizeObserver(() => this.resized()).observe(viewport);
        }
        else {
            window.addEventListener('resize', () => this.resized());
        }
        this.setBackdrop(this.savedBackdrop());
    }

    /** Shows a result ({ url, video, alt, onError }), or clears the canvas for null. */
    show(media) {
        if (!media) {
            this.clear();
            return;
        }
        if (media.url == this.url) {
            return;
        }
        this.url = media.url;
        this.video = Boolean(media.video);
        this.width = 0;
        this.height = 0;
        this.fitted = true;
        this.endDrag();
        this.frame.classList.remove('is-ready');
        this.viewport.classList.toggle('is-video', this.video);
        let url = media.url;
        let node = document.createElement(this.video ? 'video' : 'img');
        if (this.video) {
            node.controls = true;
            node.preload = 'metadata';
            node.playsInline = true;
            node.addEventListener('loadedmetadata', () => this.loaded(url, node.videoWidth, node.videoHeight));
        }
        else {
            node.alt = media.alt || '';
            node.decoding = 'async';
            node.draggable = false;
            node.addEventListener('load', () => this.loaded(url, node.naturalWidth, node.naturalHeight));
        }
        if (media.onError) {
            node.addEventListener('error', media.onError);
        }
        node.src = url;
        this.frame.replaceChildren(node);
        this.changed();
    }

    /** Removes the shown result. */
    clear() {
        this.url = '';
        this.video = false;
        this.width = 0;
        this.height = 0;
        this.endDrag();
        this.frame.replaceChildren();
        this.frame.classList.remove('is-ready');
        this.changed();
    }

    /** Takes the loaded size of the result, unless another result was shown meanwhile, and fits it. */
    loaded(url, width, height) {
        if (url != this.url) {
            return;
        }
        this.width = width || 1024;
        this.height = height || 1024;
        this.fit();
        this.frame.classList.add('is-ready');
    }

    /** True once the shown result has loaded. */
    ready() {
        return this.width > 0 && this.height > 0;
    }

    /** True when the shown result can be zoomed and dragged: a loaded image. */
    canZoom() {
        return this.ready() && !this.video;
    }

    /** True at the result's own pixels. */
    isActual() {
        return Math.abs(this.scale - 1) < 0.001;
    }

    /** Zoom in percent of the result's own pixels, or null before a result has loaded. */
    zoomPercent() {
        return this.ready() ? Math.round(this.scale * 100) : null;
    }

    /** Zoom that fills the fit share of the canvas; 1 while the canvas has no size yet (hidden). */
    fitScale() {
        let width = this.viewport.clientWidth;
        let height = this.viewport.clientHeight;
        if (!this.ready() || !width || !height) {
            return 1;
        }
        return Math.min(width / this.width, height / this.height) * ProViewer.fitShare;
    }

    /** Fits the result to the canvas and centres it. */
    fit() {
        if (!this.ready()) {
            return;
        }
        this.fitted = true;
        this.scale = this.fitScale();
        this.panX = 0;
        this.panY = 0;
        this.apply();
    }

    /** Shows the result at its own pixels, centred. */
    actual() {
        if (!this.canZoom()) {
            return;
        }
        this.fitted = false;
        this.scale = 1;
        this.panX = 0;
        this.panY = 0;
        this.apply();
    }

    /** Zooms by a factor around a point of the screen, or around the canvas centre without one. */
    zoomBy(factor, clientX, clientY) {
        if (!this.canZoom()) {
            return;
        }
        let next = Math.min(ProViewer.maxScale, Math.max(ProViewer.minScale, this.scale * factor));
        let x = 0;
        let y = 0;
        if (clientX != null && clientY != null) {
            let box = this.viewport.getBoundingClientRect();
            x = clientX - box.left - box.width / 2;
            y = clientY - box.top - box.height / 2;
        }
        // Keep the point under the pointer where it is: its distance to the result's centre grows with the zoom.
        let ratio = next / this.scale;
        this.panX = x - (x - this.panX) * ratio;
        this.panY = y - (y - this.panY) * ratio;
        this.scale = next;
        this.fitted = false;
        this.apply();
    }

    /** Writes size and position to the frame. */
    apply() {
        let style = this.frame.style;
        style.width = `${Math.max(1, Math.round(this.width * this.scale))}px`;
        style.height = `${Math.max(1, Math.round(this.height * this.scale))}px`;
        style.transform = `translate(-50%, -50%) translate(${Math.round(this.panX)}px, ${Math.round(this.panY)}px)`;
        this.frame.classList.toggle('is-pixels', this.canZoom() && this.scale >= 2);
        this.changed();
    }

    /** Follows a canvas size change: a fitted result fits again, a zoomed one keeps its zoom. */
    resized() {
        if (!this.ready()) {
            return;
        }
        if (this.fitted) {
            this.fit();
        }
        else {
            this.apply();
        }
    }

    /** Zooms with the wheel or a touchpad pinch around the pointer. */
    wheel(event) {
        if (!this.canZoom()) {
            return;
        }
        event.preventDefault();
        // Lines (deltaMode 1) come from classic wheels in Firefox; pixels from everything else.
        let delta = event.deltaMode == 1 ? event.deltaY * 16 : event.deltaY;
        let factor = Math.exp(-Math.max(-300, Math.min(300, delta)) * 0.0015);
        this.zoomBy(factor, event.clientX, event.clientY);
    }

    /** Starts dragging the result with the main button. */
    pointerDown(event) {
        if (!this.canZoom() || event.button != 0) {
            return;
        }
        this.drag = { id: event.pointerId, x: event.clientX, y: event.clientY, panX: this.panX, panY: this.panY };
        try {
            this.viewport.setPointerCapture(event.pointerId);
        }
        catch (error) {
            // Capture only keeps the drag going outside the canvas; dragging inside works without it.
        }
        this.viewport.classList.add('is-dragging');
    }

    /** Moves the result with the pointer. */
    pointerMove(event) {
        if (!this.drag || event.pointerId != this.drag.id) {
            return;
        }
        let dx = event.clientX - this.drag.x;
        let dy = event.clientY - this.drag.y;
        if (!dx && !dy) {
            return;
        }
        this.panX = this.drag.panX + dx;
        this.panY = this.drag.panY + dy;
        this.fitted = false;
        this.apply();
    }

    /** Ends a drag. */
    pointerUp(event) {
        if (this.drag && event.pointerId == this.drag.id) {
            this.endDrag();
        }
    }

    /** Forgets a drag in progress. */
    endDrag() {
        if (!this.drag) {
            return;
        }
        try {
            this.viewport.releasePointerCapture(this.drag.id);
        }
        catch (error) {
            // The capture may already be gone, for example after the pointer was cancelled.
        }
        this.drag = null;
        this.viewport.classList.remove('is-dragging');
    }

    /** Handles + / - (zoom), 0 (fit) and 1 (own pixels); returns true when the key was used. */
    keydown(event) {
        if (event.key == '+' || event.key == '=') {
            this.zoomBy(ProViewer.step);
            return this.canZoom();
        }
        if (event.key == '-' || event.key == '_') {
            this.zoomBy(1 / ProViewer.step);
            return this.canZoom();
        }
        if (event.key == '0') {
            this.fit();
            return this.ready();
        }
        if (event.key == '1') {
            this.actual();
            return this.canZoom();
        }
        return false;
    }

    /** Switches the canvas backdrop and remembers it in this browser. */
    setBackdrop(name) {
        this.backdrop = ProViewer.backdrops.includes(name) ? name : 'paper';
        this.viewport.dataset.backdrop = this.backdrop;
        try {
            globalThis.localStorage?.setItem(ProViewer.backdropKey, this.backdrop);
        }
        catch (error) {
            // Blocked storage only means the choice is not remembered.
        }
        this.changed();
    }

    /** The remembered backdrop, or null when there is none or storage is blocked. */
    savedBackdrop() {
        try {
            return globalThis.localStorage?.getItem(ProViewer.backdropKey) ?? null;
        }
        catch (error) {
            return null;
        }
    }

    /** Tells the page that something changed. */
    changed() {
        if (this.onChange) {
            this.onChange();
        }
    }
}

let proViewer = new ProViewer();
