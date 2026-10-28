// Simple mode (F07): the default view has no prompt box and no professional parameters; the professional mode is offered to every account (A2).
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
    assert.deepStrictEqual(textareas, ['simple-prompt-zh', 'simple-prompt-en'], 'only the bilingual prompt panel (F54) in the static page; reference prompts (F59) are created per image in JS');
    assert.ok(!/id="prompt"|id="negative"/.test(simple));
    assert.match(section('view-create'), /<section class="view" id="view-create"[^>]*\bhidden\b/);
});

test('reference mode (F59): upload entry, standalone generate button and per-reference prompts created in JS', () => {
    let simple = section('view-simple');
    for (let id of ['simple-reference-add', 'simple-reference-file', 'simple-references', 'simple-reference-list-block', 'simple-generate-reference', 'simple-reference-hint']) {
        assert.ok(simple.includes(`id="${id}"`), `step 2 needs the reference entry ${id}`);
    }
    let js = fs.readFileSync(path.join(studioDir, 'simple.js'), 'utf8');
    assert.match(js, /createElement\('textarea'\)/, 'each reference gets its own prompt box at runtime');
    assert.match(js, /EcomCraftStudioReferenceDescribe/, 'references are described by the server');
    assert.match(js, /reference_prompts/, 'generation sends one prompt per reference');
    assert.match(js, /reference_index/, 'task cards can show their reference');
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
    assert.match(html, /<a class="nav-item pro-only" data-view="create" href="#create" id="nav-create" hidden>/);
    assert.match(html, /<a class="nav-item" data-view="simple" href="#simple">/);
});

test('every signed-in account gets the professional mode (A2, F60)', () => {
    let SimpleMode = loadSimpleMode();
    assert.deepStrictEqual({ ...SimpleMode.viewAccess(null) }, { simple: true, pro: false }, 'not before the account is known');
    assert.deepStrictEqual({ ...SimpleMode.viewAccess({ can_manage_settings: false }) }, { simple: true, pro: true });
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
    assert.strictEqual(SimpleMode.batchFinished([{ state: 'prompt' }]), false, 'tasks waiting for the batch prompt are not finished (F56)');
});

test('step 1 offers four equal ways to add a product photo (F52)', () => {
    let simple = section('view-simple');
    for (let id of ['simple-file-btn', 'simple-camera-btn', 'simple-library-btn', 'simple-gallery-btn']) {
        assert.match(simple, new RegExp(`<button type="button" class="btn" id="${id}">`), `${id} must be a real button`);
    }
    assert.ok(html.includes('id="simple-picker-modal"'), 'the library and the gallery open in a picker dialog');
    let SimpleMode = loadSimpleMode();
    SimpleMode.api = { tr: text => text };
    assert.strictEqual(SimpleMode.originText('gallery'), '画廊');
    assert.strictEqual(SimpleMode.originText('library'), '商品库');
    assert.strictEqual(SimpleMode.originText('camera'), '拍照');
    assert.strictEqual(SimpleMode.originText(undefined), '本机');
});

test('gallery images are drawn at most 16 megapixels before compressing (F52)', () => {
    let SimpleMode = loadSimpleMode();
    assert.strictEqual(SimpleMode.compressionScale(2000, 2000), 1);
    assert.ok(Math.abs(SimpleMode.compressionScale(8000, 8000) - 0.5) < 1e-9);
    assert.strictEqual(SimpleMode.compressionScale(0, 0), 1);
});

test('a storyboard only uses image types of the chosen platform (F25)', () => {
    let simple = section('view-simple');
    for (let id of ['simple-storyboard', 'simple-storyboard-run', 'simple-shots', 'simple-points']) {
        assert.ok(simple.includes(`id="${id}"`), `${id} belongs to step 2`);
    }
    let SimpleMode = loadSimpleMode();
    SimpleMode.data.templates = [{ id: 'scene', platforms: ['*'] }, { id: 'amazon-only', platforms: ['amazon'] }];
    SimpleMode.choice.platform = 'amazon';
    SimpleMode.storyboard.shots = [{ template: 'scene', purpose: '', prompt: '' }, { template: 'amazon-only', purpose: '', prompt: '' }];
    assert.strictEqual(SimpleMode.storyboardProblem(), '');
    SimpleMode.choice.platform = 'taobao';
    assert.notStrictEqual(SimpleMode.storyboardProblem(), '', 'a shot whose type the platform lacks blocks the set');
    SimpleMode.storyboard.shots = [];
    assert.strictEqual(SimpleMode.storyboardProblem(), '');
});

test('generation only sends the brand kit ID (F12)', () => {
    assert.ok(section('view-simple').includes('id="simple-brand"'), 'step 2 offers a brand kit list');
    assert.ok(html.includes('id="brand-modal"'), 'brand kits are managed in a dialog');
    let SimpleMode = loadSimpleMode();
    SimpleMode.choice.brandKit = '';
    assert.deepStrictEqual({ ...SimpleMode.brandFields() }, {});
    SimpleMode.choice.brandKit = 'k1';
    assert.deepStrictEqual({ ...SimpleMode.brandFields() }, { brand_kit_id: 'k1' });
});

test('selling-point images offer the server layouts and plain text inputs only (F47)', () => {
    let simple = section('view-simple');
    let layouts = [...simple.matchAll(/data-layout="([a-z]+)"/g)].map(match => match[1]);
    let rules = fs.readFileSync(path.join(studioDir, 'Export', 'SellingImageRules.cs'), 'utf8');
    let serverLayouts = [...rules.match(/Layouts = \[([^\]]*)\]/)[1].matchAll(/"([a-z]+)"/g)].map(match => match[1]);
    assert.deepStrictEqual(layouts, serverLayouts, 'the page offers exactly the layouts the server draws');
    for (let id of ['simple-selling-title', 'simple-selling-point-1', 'simple-selling-point-3', 'simple-selling-run']) {
        assert.ok(simple.includes(`id="${id}"`), `${id} belongs to step 3`);
    }
    assert.match(simple, /id="simple-selling-title" type="text" maxlength="20"/);
});

test('task stages read: writing the prompt, queued or running on a server, done (F56)', () => {
    let SimpleMode = loadSimpleMode();
    SimpleMode.api = { tr: (text, ...args) => String(text).replace(/\{(\d+)\}/g, (match, index) => args[Number(index)]) };
    assert.strictEqual(SimpleMode.stageText('prompt', ''), '正在生成提示词…');
    assert.strictEqual(SimpleMode.stageText('queued', 'GPU-1'), '排队中（GPU-1）');
    assert.strictEqual(SimpleMode.stageText('queued', ''), '排队中');
    assert.strictEqual(SimpleMode.stageText('running', 'GPU-1'), '执行中（GPU-1）');
    assert.strictEqual(SimpleMode.stageText('done', 'GPU-1'), '已完成（GPU-1）');
    assert.strictEqual(SimpleMode.stageText('failed', 'GPU-1'), '失败');
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

test('simple mode starts by choosing a task, "follow a reference" first and default (F61)', () => {
    let simple = section('view-simple');
    assert.match(simple, /<section class="view" id="view-simple"[^>]*data-simple-task="reference"/);
    let tasks = [...simple.matchAll(/<button type="button" class="simple-task" data-task="([a-z]+)" aria-pressed="(true|false)"/g)].map(match => [match[1], match[2]]);
    assert.deepStrictEqual(tasks, [['reference', 'true'], ['single', 'false'], ['set', 'false']]);
});

test('each task shows only its own step-2 fields and main button (F61)', () => {
    let simple = section('view-simple');
    let marks = { 'simple-generate': 'single', 'simple-generate-set': 'set', 'simple-generate-reference': 'reference',
        'simple-references-fld': 'reference', 'simple-reference-list-block': 'reference', 'simple-storyboard': 'set', 'simple-more': 'single set' };
    for (let [id, tasks] of Object.entries(marks)) {
        assert.match(simple, new RegExp(`id="${id}"[^>]*data-tasks="${tasks}"`), `${id} belongs to ${tasks}`);
    }
    let start = simple.indexOf('id="simple-more"');
    let more = simple.slice(start, simple.indexOf('</details>', start));
    for (let id of ['simple-styles', 'simple-model', 'simple-brand', 'simple-description', 'simple-expand', 'simple-prompt-zh', 'simple-prompt-en']) {
        assert.ok(more.includes(`id="${id}"`), `${id} is folded under "More options"`);
    }
    let css = fs.readFileSync(path.join(studioDir, 'studio.css'), 'utf8');
    for (let task of ['reference', 'single', 'set']) {
        assert.ok(css.includes(`[data-simple-task="${task}"] [data-tasks]:not([data-tasks~="${task}"])`), `CSS hides the other tasks' fields for ${task}`);
    }
});

test('"follow a reference" explains a missing vision model: steps for administrators, "ask your administrator" for others (F68)', () => {
    let simple = section('view-simple');
    assert.match(simple, /id="simple-references-fld" data-tasks="reference">[\s\S]*id="simple-vision"/, 'the guidance sits with the references');
    let SimpleMode = loadSimpleMode();
    SimpleMode.api = { tr: (text, ...args) => text.replace(/\{(\d+)\}/g, (match, index) => String(args[Number(index)])), state: { session: 'x' } };
    SimpleMode.vision = { state: 'ready', provider: 'ollama', model: 'llama3.2-vision:latest', can_manage: true };
    assert.strictEqual(SimpleMode.visionGuide(), null);
    SimpleMode.vision.state = 'unknown';
    assert.strictEqual(SimpleMode.visionGuide(), null, 'an unanswered check never blocks');
    SimpleMode.vision.state = 'model_missing';
    assert.ok(SimpleMode.visionGuide().steps.some(step => step.includes('ollama pull llama3.2-vision:latest')));
    SimpleMode.vision.can_manage = false;
    assert.ok(SimpleMode.visionGuide().steps.every(step => !step.includes('ollama')), 'other accounts are asked to contact an administrator');
    SimpleMode.chosenTask = 'reference';
    SimpleMode.references = [{ promptEn: 'a', describing: false }];
    assert.match(SimpleMode.referenceProblem(), /暂不可用/);
});

test('reference mode follows the chosen task and needs at least one reference (F61)', () => {
    let SimpleMode = loadSimpleMode();
    SimpleMode.api = { tr: text => text, state: { session: 'x' } };
    SimpleMode.references = [];
    SimpleMode.chosenTask = 'reference';
    assert.strictEqual(SimpleMode.referenceMode(), true);
    assert.match(SimpleMode.referenceProblem(), /参考图/);
    SimpleMode.chosenTask = 'set';
    assert.strictEqual(SimpleMode.referenceMode(), false);
    assert.strictEqual(SimpleMode.referenceProblem(), '');
});

test('the top bar: create images, professional mode, gallery and queue; administration lives in the hidden "Manage" menu (F62)', () => {
    let start = html.indexOf('<header class="topbar">');
    let topbar = html.slice(start, html.indexOf('</header>', start));
    let labels = [...topbar.matchAll(/<a class="nav-item[^"]*" data-view="(simple|create|gallery|queue)"[^>]*>([^<]+)/g)].map(match => match[2]);
    assert.deepStrictEqual(labels, ['出图', '专业模式', '画廊', '队列']);
    assert.match(topbar, /<div class="admin-menu" id="admin-menu" hidden>/, 'the menu starts hidden and is shown to administrators only');
    let native = [...topbar.matchAll(/<a [^>]*href="\/Text2Image"[^>]*>/g)].map(match => match[0]);
    assert.strictEqual(native.length, 1, 'one native engine link');
    assert.match(native[0], /data-admin-only/, 'the native engine link is for administrators only');
    for (let id of ['admin-menu-workflows', 'admin-menu-wizard', 'admin-menu-prompt', 'admin-menu-storage', 'admin-menu-diagnostics']) {
        assert.ok(topbar.includes(`id="${id}"`), `${id} is in the menu`);
    }
});

test('own workflows are uploaded in professional mode only, as a name and the file text (F60)', () => {
    assert.ok(section('view-create').includes('id="own-workflow-upload"'));
    assert.ok(!section('view-simple').includes('own-workflow'), 'operators never see the upload');
    let studio = fs.readFileSync(path.join(studioDir, 'studio.js'), 'utf8');
    assert.match(studio, /request\('EcomCraftStudioWorkflowUpload', \{ name: file\.name, workflow: text \}\)/);
});

test('one picked candidate can be refined in professional mode, with the prompt read by the server (F64)', () => {
    assert.match(section('view-simple'), /<button type="button" class="btn simple-refine" id="simple-refine" hidden>/);
    let studio = fs.readFileSync(path.join(studioDir, 'studio.js'), 'utf8');
    assert.match(studio, /request\('EcomCraftStudioWorkRecipe', \{ image: url \}\)/, 'the prompt comes from the work itself, not from page memory');
    let css = fs.readFileSync(path.join(studioDir, 'studio.css'), 'utf8');
    assert.match(css, /@media \(max-width: 720px\) \{\s*\.simple-refine, \.pro-only \{ display: none; \}/, 'phones keep to the simple mode');
});

test('candidates are starred with the gallery star, by their gallery path (F65)', () => {
    let SimpleMode = loadSimpleMode();
    assert.strictEqual(SimpleMode.galleryPath('View/alice/raw/2026-10-29/a.png'), 'raw/2026-10-29/a.png');
    assert.strictEqual(SimpleMode.galleryPath('/Output/raw/a.png'), 'raw/a.png');
    let js = fs.readFileSync(path.join(studioDir, 'simple.js'), 'utf8');
    assert.match(js, /request\('ToggleImageStarred', \{ path: this\.galleryPath\(url\) \}\)/);
    assert.match(js, /new_state\) \{\s*this\.starred\.add\(url\);\s*this\.api\.request\('EcomCraftStudioMarkKept'/, 'a starred candidate is kept from cleanup');
});

test('the gallery filters by source and the queue lists the account\'s own requests first (F66)', () => {
    let gallery = section('view-gallery');
    for (let value of ['all', 'simple', 'pro']) {
        assert.ok(gallery.includes(`<option value="${value}">`), `source option ${value}`);
    }
    let studio = fs.readFileSync(path.join(studioDir, 'studio.js'), 'utf8');
    assert.match(studio, /extra\.ecomcraft_template \? 'simple' : 'pro'/, 'works made from a template come from the simple mode');
    let own = studio.indexOf('for (let task of tasks.filter(task => task.mine))');
    let servers = studio.indexOf('for (let task of nativeTasks)');
    let others = studio.indexOf('for (let task of tasks.filter(task => !task.mine))');
    assert.ok(own > 0 && own < servers && servers < others, 'own requests, then server tasks, then other accounts');
});

test('administrators reach the standard workflows from the "Manage" menu; the page only reads states or asks to initialize (F57)', () => {
    assert.match(html, /<button type="button" class="admin-menu-item" id="admin-menu-standard" data-admin-only>/);
    assert.match(html, /<div class="modal" id="standard-modal" hidden>/);
    let studio = fs.readFileSync(path.join(studioDir, 'studio.js'), 'utf8');
    assert.match(studio, /request\('EcomCraftStandardWorkflows', action \? \{ action \} : \{\}\)/, 'no graph or model names are sent from the page');
});
