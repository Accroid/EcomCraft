let { test } = require('node:test');
let assert = require('node:assert/strict');
let fs = require('node:fs');
let path = require('node:path');
let vm = require('node:vm');

/** Builds a minimal element stub that records the properties the gallery toolbar writes. */
function element() {
    return {
        hidden: false, textContent: '', disabled: false,
        classList: { add() {}, remove() {}, toggle() {} },
        setAttribute() {}
    };
}

/** Runs the actual gallery deletion class, replacing only rendering, list reloads and transport. */
function setup(respond, { confirmed = true, canDelete = true } = {}) {
    let calls = [];
    let elements = {};
    let source = fs.readFileSync(path.join(__dirname, '../Studio/studio.js'), 'utf8');
    source = source.slice(0, source.lastIndexOf('    initTheme();')) + `
        renderGallery = () => {};
        loadGallery = async () => {};
        closeLightbox = () => {};
        globalThis.selection = GallerySelection;
        globalThis.state = state;
    })();`;
    let context = vm.createContext({
        document: { hidden: false, getElementById: id => elements[id] ??= element() },
        window: { confirm: () => confirmed },
        fetch: async (url, options) => {
            let body = JSON.parse(options.body);
            calls.push(body.path);
            return { ok: true, status: 200, json: async () => respond(body.path) };
        }
    });
    vm.runInContext(source, context);
    context.state.session = 'test-session';
    context.state.catalog = { can_delete_images: canDelete };
    context.state.gallery.items = [
        { src: 'raw/a.png', name: 'a.png' },
        { src: 'raw/b.png', name: 'b.png' },
        { src: 'raw/c.png', name: 'c.png' }
    ];
    return { context, calls, message: () => elements['gallery-message'] };
}

test('batch delete removes every selected file and reports success', async () => {
    let harness = setup(() => ({ success: true }));
    let { selection, state } = harness.context;
    selection.setActive(true);
    selection.toggle(state.gallery.items[0]);
    selection.toggle(state.gallery.items[2]);
    await selection.deleteSelected();
    assert.deepEqual(harness.calls, ['raw/a.png', 'raw/c.png']);
    assert.deepEqual(state.gallery.items.map(item => item.src), ['raw/b.png']);
    assert.equal(selection.active, false);
    assert.equal(selection.deleting, false);
    assert.match(harness.message().textContent, /已删除 2 个作品/);
});

test('one failed file does not stop the batch and is named in the message', async () => {
    let harness = setup(src => src == 'raw/b.png' ? { error: 'That file does not exist, cannot delete.' } : { success: true });
    let { selection, state } = harness.context;
    selection.setActive(true);
    selection.selectAllVisible();
    await selection.deleteSelected();
    assert.deepEqual(harness.calls, ['raw/a.png', 'raw/b.png', 'raw/c.png']);
    assert.deepEqual(state.gallery.items.map(item => item.src), ['raw/b.png']);
    assert.match(harness.message().textContent, /已删除 2 个作品，1 个删除失败/);
    assert.match(harness.message().textContent, /b\.png/);
});

test('declining the confirmation sends no request', async () => {
    let harness = setup(() => ({ success: true }), { confirmed: false });
    await harness.context.selection.deleteOne(harness.context.state.gallery.items[0]);
    assert.equal(harness.calls.length, 0);
    assert.equal(harness.context.state.gallery.items.length, 3);
});

test('accounts without delete permission cannot enter selection mode', () => {
    let harness = setup(() => ({ success: true }), { canDelete: false });
    harness.context.selection.setActive(true);
    assert.equal(harness.context.selection.active, false);
});
