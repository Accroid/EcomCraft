// Simple mode (F07): the default view has no prompt box and no professional parameters; the professional mode is offered to administrators only.
// Run: node --test src/Extensions/EcomCraft/Tests/simple-mode.test.cjs
'use strict';

const { test } = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const studioDir = path.join(__dirname, '..', 'Studio');
const html = fs.readFileSync(path.join(studioDir, 'index.html'), 'utf8');

/** The markup of one view section, from its opening tag to the next view section or the end of the main area. */
function section(id) {
    let start = html.indexOf(`<section class="view" id="${id}"`);
    assert.ok(start >= 0, `missing ${id}`);
    let next = html.indexOf('<section class="view"', start + 10);
    return html.slice(start, next < 0 ? undefined : next);
}

/** Loads simple.js the way the browser does and returns the SimpleMode class. */
function loadSimpleMode() {
    let sandbox = {};
    vm.runInNewContext(fs.readFileSync(path.join(studioDir, 'simple.js'), 'utf8') + '\nthis.SimpleMode = SimpleMode;', sandbox);
    return sandbox.SimpleMode;
}

test('the simple view is the default and its only text boxes are the bilingual prompt panel', () => {
    let simple = section('view-simple');
    assert.ok(!/<section class="view" id="view-simple"[^>]*\bhidden\b/.test(simple), 'simple view must be visible by default');
    let textareas = [...simple.matchAll(/<textarea\b[^>]*\bid="([^"]+)"/gi)].map(match => match[1]);
    assert.strictEqual((simple.match(/<textarea\b/gi) || []).length, textareas.length, 'every textarea in simple mode needs an id');
    assert.deepStrictEqual(textareas, ['simple-prompt-zh', 'simple-prompt-en'], 'only the bilingual prompt panel (F54) in simple mode');
    assert.ok(!/id="prompt"|id="negative"/.test(simple));
    assert.match(section('view-create'), /<section class="view" id="view-create"[^>]*\bhidden\b/);
});

test('professional parameters live only in the professional view', () => {
    let simple = section('view-simple');
    let create = section('view-create');
    for (let id of ['sampler', 'scheduler', 'steps', 'cfg', 'seed', 'add-lora', 'model', 'negative']) {
        assert.ok(!simple.includes(`id="${id}"`), `${id} must not be in simple mode`);
        assert.ok(create.includes(`id="${id}"`), `${id} belongs to the professional mode`);
    }
});

test('the professional mode navigation starts hidden', () => {
    assert.match(html, /<a class="nav-item" data-view="create" href="#create" id="nav-create" hidden>/);
    assert.match(html, /<a class="nav-item" data-view="simple" href="#simple">/);
});

test('only administrators get the professional mode', () => {
    let SimpleMode = loadSimpleMode();
    assert.deepStrictEqual({ ...SimpleMode.viewAccess(null) }, { simple: true, pro: false });
    assert.deepStrictEqual({ ...SimpleMode.viewAccess({ can_manage_settings: false }) }, { simple: true, pro: false });
    assert.deepStrictEqual({ ...SimpleMode.viewAccess({ can_manage_settings: true }) }, { simple: true, pro: true });
});

test('templates are filtered by platform, with "*" matching every platform', () => {
    let SimpleMode = loadSimpleMode();
    SimpleMode.data.templates = [{ id: 'a', platforms: ['amazon'] }, { id: 'b', platforms: ['*'] }, { id: 'c', platforms: ['taobao'] }];
    SimpleMode.choice.platform = 'amazon';
    assert.deepStrictEqual(SimpleMode.visibleTemplates().map(template => template.id), ['a', 'b']);
    SimpleMode.choice.platform = '';
    assert.deepStrictEqual(SimpleMode.visibleTemplates().map(template => template.id), ['a', 'b', 'c']);
});

test('placement math matches the server (Placement.Rect)', () => {
    let SimpleMode = loadSimpleMode();
    assert.deepStrictEqual({ ...SimpleMode.placementRect(200, 100, 40, 20, { x: 0.5, y: 0.5, size: 0.5 }) }, { x: 75, y: 38, width: 50, height: 25 });
    assert.deepStrictEqual({ ...SimpleMode.placementRect(300, 300, 1000, 500, { x: 0.25, y: 0.75, size: 1 }) }, { x: -75, y: 150, width: 300, height: 150 });
});

test('dragged placements are clamped to the ranges the server accepts', () => {
    let SimpleMode = loadSimpleMode();
    assert.deepStrictEqual({ ...SimpleMode.clampPlacement({ x: -0.2, y: 1.4, size: 3 }) }, { x: 0, y: 1, size: 1.5 });
    assert.deepStrictEqual({ ...SimpleMode.clampPlacement({ x: 0.3, y: 0.6, size: 0.01 }) }, { x: 0.3, y: 0.6, size: 0.1 });
    assert.deepStrictEqual({ ...SimpleMode.clampPlacement({ x: NaN, y: 0.5, size: 0.7 }) }, { x: 0, y: 0.5, size: 0.7 });
});

test('transparency detection follows the server rule (at least 2% transparent pixels)', () => {
    let SimpleMode = loadSimpleMode();
    let pixels = (count, transparent) => {
        let data = new Uint8ClampedArray(count * 4).fill(255);
        for (let i = 0; i < transparent; i++) {
            data[i * 4 + 3] = 0;
        }
        return { data };
    };
    assert.strictEqual(SimpleMode.hasTransparency(pixels(100, 2)), true);
    assert.strictEqual(SimpleMode.hasTransparency(pixels(100, 1)), false);
});

test('a batch counts as finished only when no task is queued or running', () => {
    let SimpleMode = loadSimpleMode();
    assert.strictEqual(SimpleMode.batchFinished([{ state: 'done' }, { state: 'failed' }, { state: 'cancelled' }]), true);
    assert.strictEqual(SimpleMode.batchFinished([{ state: 'done' }, { state: 'running' }]), false);
    assert.strictEqual(SimpleMode.batchFinished([{ state: 'queued' }]), false);
    assert.strictEqual(SimpleMode.batchFinished([]), true);
});

test('the set button uses the standard set of the chosen platform', () => {
    let SimpleMode = loadSimpleMode();
    SimpleMode.data.templates = [{ id: 'a', platforms: ['*'], sets: ['standard'] }, { id: 'b', platforms: ['amazon'], sets: ['standard'] }, { id: 'c', platforms: ['*'], sets: [] }];
    SimpleMode.choice.platform = 'taobao';
    assert.deepStrictEqual(SimpleMode.setTemplates().map(template => template.id), ['a']);
});

test('the prompt panel sends edited English as is, edited Chinese for translation, and nothing when empty (F54)', () => {
    let SimpleMode = loadSimpleMode();
    let panel = (zh, en, base, last) => ({ zh, en, base: base || { zh: '', en: '' }, last: last || '' });
    let synced = { zh: '红色马克杯', en: 'a red mug' };
    assert.strictEqual(SimpleMode.promptSource(panel('', '')), '');
    assert.strictEqual(SimpleMode.promptSource(panel('红色马克杯', 'a red mug', synced)), 'en', 'a synced panel sends the expanded English');
    assert.strictEqual(SimpleMode.promptSource(panel('红色马克杯，木桌', 'a red mug', synced, 'zh')), 'zh', 'edited Chinese is translated, never the old English');
    assert.strictEqual(SimpleMode.promptSource(panel('红色马克杯', 'a red mug on wood', synced, 'en')), 'en');
    assert.strictEqual(SimpleMode.promptSource(panel('红色马克杯，木桌', 'a red mug on wood', synced, 'zh')), 'zh', 'the last edited language wins');
    assert.strictEqual(SimpleMode.promptSource(panel('红色马克杯，木桌', 'a red mug on wood', synced, 'en')), 'en');
    assert.strictEqual(SimpleMode.promptSource(panel('只写了中文', '')), 'zh', 'typing without expanding counts as an edit');
    assert.strictEqual(SimpleMode.promptSource(panel('', 'a red mug', synced, 'zh')), 'en', 'a cleared Chinese text falls back to the English one');
});
