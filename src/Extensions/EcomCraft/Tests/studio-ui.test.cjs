let { test } = require('node:test');
let assert = require('node:assert/strict');
let fs = require('node:fs');
let path = require('node:path');
let vm = require('node:vm');

/** Minimal DOM element stand-in with the members the studio code touches. */
function makeElement(tag = 'div') {
    return {
        tagName: String(tag).toUpperCase(), hidden: false, disabled: false, textContent: '', value: '',
        style: {}, dataset: {}, attributes: {}, children: [],
        classList: { add() {}, remove() {}, toggle() {} },
        setAttribute(name, value) { this.attributes[name] = String(value); },
        getAttribute(name) { return this.attributes[name] ?? null; },
        removeAttribute(name) { delete this.attributes[name]; },
        append(...nodes) { this.children.push(...nodes); },
        prepend(...nodes) { this.children.unshift(...nodes); },
        replaceChildren(...nodes) { this.children = [...nodes]; },
        addEventListener() {},
        focus() {},
        querySelectorAll() { return []; },
        closest() { return null; }
    };
}

/** Runs the real studio code up to its init section, with optional overrides for globals such as localStorage. */
function setup(globals = {}) {
    let elements = {};
    let source = fs.readFileSync(path.join(__dirname, '../Studio/studio.js'), 'utf8');
    source = source.slice(0, source.lastIndexOf('    initTheme();')) + `
        globalThis.ui = { state, Lightbox, renderQueue, renderReference, setGallerySearch, maybeOpenSetupGuide };
    })();`;
    let context = vm.createContext({
        document: {
            getElementById: id => elements[id] ??= makeElement(),
            createElement: makeElement,
            createElementNS: (ns, tag) => makeElement(tag),
            querySelector: () => null,
            querySelectorAll: () => [],
            documentElement: makeElement('html'),
            activeElement: null
        },
        window: {}
    });
    for (let [name, descriptor] of Object.entries(globals)) {
        Object.defineProperty(context, name, descriptor);
    }
    vm.runInContext(source, context);
    let element = id => elements[id] ??= makeElement();
    element('lightbox').hidden = true;
    element('gallery-count').hidden = true;
    element('queue-count').hidden = true;
    return { ui: context.ui, element };
}

/** Three gallery-style items for the full-size preview. */
function items() {
    return ['a.png', 'b.png', 'c.png'].map(name => ({ src: `raw/${name}`, name, url: `/Output/raw/${name}`, starred: false }));
}

test('navigation counts stay hidden until their data loads, then show the real number', () => {
    let { ui, element } = setup();
    assert.equal(element('queue-count').hidden, true);
    ui.renderQueue({ backends: [], native_tasks: [{ backend_id: 1, backend_name: 'A', state: 'queued', position: 1, total_nodes: 3 }], tasks: [] });
    assert.equal(element('queue-count').hidden, false);
    assert.equal(element('queue-count').textContent, 1);
});

test('blocked browser storage does not break the first-visit setup guide', () => {
    let { ui, element } = setup({ localStorage: { get() { throw new Error('storage blocked'); } } });
    ui.state.catalog = { local_backends_observed: 0, cloud_configured: false, can_manage_settings: false };
    element('setup-guide-modal').hidden = true;
    assert.doesNotThrow(() => ui.maybeOpenSetupGuide());
    assert.equal(element('setup-guide-modal').hidden, false);
});

test('arrow keys step through the preview and stop at both ends', () => {
    let { ui, element } = setup();
    let event = key => ({ key, target: makeElement('button'), preventDefault() {} });
    ui.Lightbox.open(items(), 0, 'gallery', null);
    assert.equal(element('lightbox').hidden, false);
    assert.equal(element('lb-prev').disabled, true);
    ui.Lightbox.keydown(event('ArrowRight'));
    ui.Lightbox.keydown(event('ArrowRight'));
    ui.Lightbox.keydown(event('ArrowRight'));
    assert.equal(ui.Lightbox.index, 2);
    assert.equal(element('lb-next').disabled, true);
    ui.Lightbox.keydown(event('ArrowLeft'));
    assert.equal(ui.Lightbox.index, 1);
    assert.match(element('lightbox-name').textContent, /b\.png（2 \/ 3）/);
    ui.Lightbox.keydown(event('Escape'));
    assert.equal(element('lightbox').hidden, true);
});

test('arrow keys inside a playing video seek the video instead of switching items', () => {
    let { ui } = setup();
    ui.Lightbox.open(items(), 1, 'gallery', null);
    let prevented = false;
    ui.Lightbox.keydown({ key: 'ArrowRight', target: makeElement('video'), preventDefault() { prevented = true; } });
    assert.equal(ui.Lightbox.index, 1);
    assert.equal(prevented, false);
});

test('result previews hide gallery-only actions', () => {
    let { ui, element } = setup();
    ui.state.catalog = { can_delete_images: true };
    ui.Lightbox.open([{ name: 'r.png', url: '/View/u/raw/r.png', src: 'raw/r.png' }], 0, 'result', null);
    assert.equal(element('lb-star').hidden, true);
    assert.equal(element('lb-delete').hidden, true);
    assert.equal(element('lb-gallery').hidden, false);
});

test('the remove button names the chosen initial image', () => {
    let { ui, element } = setup();
    ui.state.reference = { url: 'blob:x', name: '精华液.png' };
    ui.renderReference();
    assert.equal(element('remove-reference').getAttribute('aria-label'), '移除初始图 精华液.png');
    ui.state.reference = null;
    ui.renderReference();
    assert.equal(element('remove-reference').getAttribute('aria-label'), '移除初始图');
});

test('the search clear button follows the search text', () => {
    let { ui, element } = setup();
    ui.setGallerySearch('精华液 ');
    assert.equal(element('gallery-search-clear').hidden, false);
    assert.equal(ui.state.gallery.search, '精华液');
    ui.setGallerySearch('');
    assert.equal(element('gallery-search-clear').hidden, true);
    assert.equal(ui.state.gallery.search, '');
});
