let { test } = require('node:test');
let assert = require('node:assert/strict');
let fs = require('node:fs');
let vm = require('node:vm');
let path = require('node:path');

/** Loads the real Lightbox class with a minimal DOM so the zoom math can run unchanged. */
function setup() {
    let elements = new Map();
    let element = id => {
        if (!elements.has(id)) {
            elements.set(id, {
                value: '', hidden: false, disabled: false, textContent: '', style: {},
                classList: { toggle() {}, add() {}, remove() {} },
                replaceChildren() {}, append() {}
            });
        }
        return elements.get(id);
    };
    let source = fs.readFileSync(path.join(__dirname, '../Studio/studio.js'), 'utf8');
    source = source.slice(0, source.lastIndexOf('    initTheme();')) + `
        globalThis.lightbox = Lightbox;
    })();`;
    let context = vm.createContext({ document: { getElementById: element } });
    vm.runInContext(source, context);
    // Stand-in media: 400x300 fitted image, transform applied to the same style object each time.
    element('lightbox-media').firstElementChild = { style: {}, offsetWidth: 400, offsetHeight: 300 };
    return { lightbox: context.lightbox, element };
}

test('zooming in anchors the point under the cursor instead of the media center', () => {
    let { lightbox } = setup();
    lightbox.zoomTo(2, 100, 50);
    assert.equal(lightbox.zoom.scale, 2);
    // pan = origin * (1 - scale): the clicked point stays visually fixed.
    assert.equal(lightbox.zoom.x, -100);
    assert.equal(lightbox.zoom.y, -50);
});

test('further zoom keeps re-anchoring relative to the current pan', () => {
    let { lightbox } = setup();
    lightbox.zoomTo(2, 100, 0);
    lightbox.zoomTo(4, 100, 0);
    assert.equal(lightbox.zoom.scale, 4);
    // content x under the cursor stays 100: pan = 100 - 100 * 4 = -300, within the clamp.
    assert.equal(lightbox.zoom.x, -300);
});

test('panning is clamped to the area the media occupied at 100 percent', () => {
    let { lightbox } = setup();
    lightbox.zoomTo(2, 0, 0);
    lightbox.panBy(1000, -1000);
    // maxX = 400 * (2 - 1) / 2, maxY = 300 * (2 - 1) / 2.
    assert.equal(lightbox.zoom.x, 200);
    assert.equal(lightbox.zoom.y, -150);
});

test('zooming out past the fitted size stops at 100 percent and recenters', () => {
    let { lightbox } = setup();
    lightbox.zoomTo(4, 100, 0);
    lightbox.zoomTo(0.25, 0, 0);
    assert.equal(lightbox.zoom.scale, 1);
    assert.equal(lightbox.zoom.x, 0);
    assert.equal(lightbox.zoom.y, 0);
});

test('the scale is capped at maxScale and panning does nothing while fitted', () => {
    let { lightbox } = setup();
    lightbox.zoomTo(99, 0, 0);
    assert.equal(lightbox.zoom.scale, 8);
    lightbox.resetZoom();
    lightbox.panBy(120, 120);
    assert.equal(lightbox.zoom.x, 0);
    assert.equal(lightbox.zoom.y, 0);
});

test('zoom controls and reset follow the current scale', () => {
    let { lightbox, element } = setup();
    let reset = element('lb-zoom-reset');
    let zoomIn = element('lb-zoom-in');
    let zoomOut = element('lb-zoom-out');
    lightbox.resetZoom();
    assert.equal(reset.disabled, true);
    lightbox.zoomBy(1.25);
    assert.equal(reset.textContent, '125%');
    assert.equal(reset.disabled, false);
    assert.equal(zoomOut.disabled, false);
    lightbox.zoomTo(8, 0, 0);
    assert.equal(zoomIn.disabled, true);
    lightbox.resetZoom();
    assert.equal(reset.textContent, '100%');
    assert.equal(reset.disabled, true);
    assert.equal(zoomIn.disabled, false);
});
