let test = require('node:test');
let assert = require('node:assert/strict');
let fs = require('node:fs');
let path = require('node:path');
let vm = require('node:vm');

class Element {
    constructor(tag = 'div') {
        this.tagName = tag;
        this.children = [];
        this.attributes = {};
        this.listeners = {};
        this.hidden = false;
        this.disabled = false;
        this.textContent = '';
        this.className = '';
        this.clicks = 0;
    }
    appendChild(child) { this.children.push(child); return child; }
    append(...children) { this.children.push(...children); }
    replaceChildren(...children) { this.children = children; }
    setAttribute(name, value) { this.attributes[name] = value; }
    addEventListener(name, callback) { this.listeners[name] = callback; }
    closest() { return this.parentElement; }
    click() { this.clicks++; }
    remove() { this.removed = true; }
    set innerHTML(value) { throw new Error(`Unsafe HTML assignment: ${value}`); }
    allText() { return [this.textContent, ...this.children.map(child => child.allText())].join(' '); }
}

function setup({ loaded = true, allowed = true } = {}) {
    let elements = {};
    for (let id of ['ecomcraft_diagnostics', 'ecomcraft_refresh', 'ecomcraft_download', 'ecomcraft_message', 'ecomcraft_results', 'maintab_ecomcraft']) {
        elements[id] = new Element();
    }
    elements.maintab_ecomcraft.parentElement = new Element('li');
    let requests = [];
    let urls = [];
    let revoked = [];
    let created = [];
    let body = new Element('body');
    let context = vm.createContext({
        permissions: { hasLoaded: loaded, hasPermission: () => allowed },
        sessionReadyCallbacks: [],
        document: {
            body,
            getElementById: id => elements[id] || null,
            createElement: tag => { let element = new Element(tag); created.push(element); return element; }
        },
        getRequiredElementById: id => { assert.ok(elements[id], id); return elements[id]; },
        createDiv: (id, classes, html = null) => {
            assert.equal(html, null, 'No dynamic HTML');
            let element = new Element(); element.className = classes; return element;
        },
        genericRequest: (...args) => requests.push(args),
        Blob,
        URL: {
            createObjectURL: blob => { urls.push(blob); return 'blob:diagnostics'; },
            revokeObjectURL: url => revoked.push(url)
        },
        setTimeout: callback => callback()
    });
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../Assets/ecomcraft.js'), 'utf8'), context);
    return { context, elements, requests, urls, revoked, created, ui: vm.runInContext('ecomCraftDiagnostics', context) };
}

function report(overrides = {}) {
    return {
        schema_version: 1, scope: 'configuration_snapshot_only', extension_version: '0.1.0',
        sampled_at: '2026-10-17T00:00:00Z', m0_status: 'pending_verification',
        checks: [{ id: 'authentication', status: 'passed', summary: '账号认证', detail: '只是配置检查' }],
        nodes: [], spikes: [{ id: 'SPIKE-01', title: '双机执行', status: 'pending_verification', evidence: '未执行' }],
        ...overrides
    };
}

function success(state, value = report()) {
    state.requests.at(-1)[2](value);
}

test('unknown permissions remain hidden and do not request', () => {
    let state = setup({ loaded: false });
    state.context.sessionReadyCallbacks[0]();
    state.ui.refresh();
    assert.equal(state.requests.length, 0);
    assert.equal(state.elements.ecomcraft_diagnostics.hidden, true);
    assert.equal(state.elements.maintab_ecomcraft.parentElement.hidden, true);
});

test('ordinary employees never request diagnostics', () => {
    let state = setup({ allowed: false });
    state.context.sessionReadyCallbacks[0]();
    assert.equal(state.requests.length, 0);
    assert.equal(state.elements.ecomcraft_diagnostics.hidden, true);
    assert.equal(state.elements.ecomcraft_download.disabled, true);
});

test('administrator uses the session-aware API and repeated clicks do not overlap', () => {
    let state = setup();
    state.context.sessionReadyCallbacks[0]();
    state.ui.refresh();
    assert.equal(state.requests.length, 1);
    assert.equal(state.requests[0][0], 'EcomCraftGetDiagnostics');
    assert.equal(state.elements.ecomcraft_refresh.disabled, true);
    assert.equal(state.elements.ecomcraft_download.disabled, true);
    assert.equal(state.elements.ecomcraft_diagnostics.attributes['aria-busy'], 'true');
    success(state);
    assert.equal(state.elements.ecomcraft_refresh.disabled, false);
    assert.equal(state.elements.ecomcraft_download.disabled, false);
    assert.match(state.elements.ecomcraft_results.allText(), /待实测/);
    assert.match(state.elements.ecomcraft_results.allText(), /尚未配置后端/);
    assert.equal(state.elements.ecomcraft_diagnostics.attributes['aria-busy'], 'false');
});

test('refresh clears prior report and server failure never leaves downloadable stale evidence', () => {
    let state = setup();
    state.ui.onSessionReady(); success(state);
    state.ui.refresh();
    assert.equal(state.ui.report, null);
    assert.equal(state.elements.ecomcraft_results.children.length, 0);
    state.requests[1][4]('secret upstream error must not appear');
    assert.equal(state.ui.report, null);
    assert.equal(state.elements.ecomcraft_download.disabled, true);
    assert.equal(state.elements.ecomcraft_refresh.disabled, false);
    assert.doesNotMatch(state.elements.ecomcraft_message.textContent, /secret/);
    state.ui.download();
    assert.equal(state.urls.length, 0);
});

test('unknown overall readiness and malformed payloads fail closed', () => {
    for (let invalid of [report({ m0_status: 'ready' }), report({ scope: 'generation' }), report({ nodes: null }), null]) {
        let state = setup(); state.ui.onSessionReady(); success(state, invalid);
        assert.equal(state.ui.report, null);
        assert.equal(state.elements.ecomcraft_download.disabled, true);
        assert.equal(state.elements.ecomcraft_refresh.disabled, false);
    }
});

test('node names and backend types are rendered as text, including executors', () => {
    let state = setup(); state.ui.onSessionReady();
    let hostile = '<img src=x onerror=alert(1)>';
    success(state, report({ nodes: [{
        configured_backend_id: 0, name: hostile, backend_type: hostile, kind: 'remote_swarm',
        engine_reports_capacity: false, allow_forwarding: false,
        engine: { status: 'running', enabled: true, usages: 0, max_usages: 1 },
        execution_backends: [{ host_backend_id: -3, remote_backend_id: 2, backend_type: hostile, status: 'running', usages: 0, max_usages: 1 }]
    }] }));
    assert.match(state.elements.ecomcraft_results.allText(), /<img src=x/);
    assert.equal(state.elements.ecomcraft_download.disabled, false);
    assert.match(state.elements.ecomcraft_results.allText(), /心跳/);
});

test('permission revocation during a request discards the response and hides diagnostics', () => {
    let state = setup(); state.ui.onSessionReady();
    state.context.permissions.hasPermission = () => false;
    success(state);
    assert.equal(state.ui.report, null);
    assert.equal(state.elements.ecomcraft_diagnostics.hidden, true);
    assert.equal(state.elements.ecomcraft_download.disabled, true);
});

test('download exports only the current report and releases the object URL', async () => {
    let state = setup(); state.ui.onSessionReady(); success(state);
    state.ui.download();
    assert.equal(state.urls.length, 1);
    assert.equal(JSON.parse(await state.urls[0].text()).scope, 'configuration_snapshot_only');
    let anchor = state.created.find(element => element.tagName == 'a');
    assert.equal(anchor.clicks, 1);
    assert.equal(anchor.removed, true);
    assert.deepEqual(state.revoked, ['blob:diagnostics']);
    state.context.permissions.hasPermission = () => false;
    state.ui.download();
    assert.equal(state.urls.length, 1);
    assert.equal(state.ui.report, null);
});
