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

test('the simple view is the default and has no prompt box', () => {
    let simple = section('view-simple');
    assert.ok(!/<section class="view" id="view-simple"[^>]*\bhidden\b/.test(simple), 'simple view must be visible by default');
    assert.ok(!/<textarea/i.test(simple), 'no prompt box in simple mode');
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
