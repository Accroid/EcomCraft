let { test } = require('node:test');
let assert = require('node:assert/strict');
let fs = require('node:fs');
let vm = require('node:vm');
let path = require('node:path');

/** Runs the real Studio functions with a minimal DOM and a controlled API transport. */
function setup(respond) {
    let elements = new Map();
    let element = id => {
        if (!elements.has(id)) {
            elements.set(id, {
                value: '', hidden: false, disabled: false, textContent: '', options: [],
                classList: { toggle() {} },
                replaceChildren(...options) { this.options = options; },
                append(option) { this.options.push(option); }
            });
        }
        return elements.get(id);
    };
    let calls = [];
    let source = fs.readFileSync(path.join(__dirname, '../Studio/studio.js'), 'utf8');
    source = source.slice(0, source.lastIndexOf('    initTheme();')) + `
        refreshCatalog = async () => {};
        globalThis.form = { fetchMpModels, closeEnhancerSettings, onBackendChange, openEnhancerSettings,
            saveEnhancerSettings, requests: PromptSettingsRequests,
            setSettings(value) { mpSettings = value; } };
    })();`;
    let context = vm.createContext({
        document: { getElementById: element }, AbortController, setTimeout, clearTimeout,
        Option: function(text, value) { this.text = text; this.value = value; },
        fetch: async (url, options) => {
            let call = { route: url.slice(5), body: JSON.parse(options.body), signal: options.signal };
            calls.push(call);
            let data = await respond(call);
            return { ok: true, status: 200, json: async () => data };
        }
    });
    vm.runInContext(source, context);
    element('enh-cfg-backend').value = 'openaiapi';
    element('enh-cfg-endpoint').value = 'http://localhost:1234/v1';
    element('enh-cfg-model').value = 'unsaved-model';
    element('enh-cfg-key').value = ' draft-key ';
    context.form.setSettings({ backend: 'ollama', model: 'saved-model', backends: {} });
    return { form: context.form, element, calls };
}

test('new key and URL are used immediately without saving or changing the chosen model', async () => {
    let { form, element, calls } = setup(async () => ({ models: [{ model: 'new-model' }] }));
    await form.fetchMpModels();
    assert.deepEqual(calls.map(call => call.route), ['PreviewMagicPromptModels']);
    assert.equal(calls[0].body.api_key, 'draft-key');
    assert.equal(calls[0].body.baseurl, 'http://localhost:1234/v1');
    assert.equal(element('enh-cfg-model').value, 'unsaved-model');
    assert.equal(element('enh-cfg-model-options').options[0].value, 'new-model');
    assert.equal(element('enh-cfg-key').value, ' draft-key ');
    assert.equal(element('enh-cfg-fetch-models').disabled, false);
});

test('closing cancels the preview and discards even a late successful response', async () => {
    let finish;
    let { form, element, calls } = setup(() => new Promise(resolve => { finish = resolve; }));
    let pending = form.fetchMpModels();
    form.closeEnhancerSettings();
    assert.equal(calls[0].signal.aborted, true);
    assert.equal(element('enh-cfg-key').value, '');
    finish({ models: [{ model: 'obsolete' }] });
    await pending;
    assert.equal(element('enh-cfg-model-options').options.length, 0);
    assert.equal(calls.length, 1);
});

test('switching backend discards old results and does not carry over its model or key', async () => {
    let finish;
    let { form, element } = setup(() => new Promise(resolve => { finish = resolve; }));
    let pending = form.fetchMpModels();
    element('enh-cfg-backend').value = 'ollama';
    await form.onBackendChange();
    assert.equal(element('enh-cfg-model').value, 'saved-model');
    assert.equal(element('enh-cfg-key').value, '');
    finish({ models: [{ model: 'obsolete' }] });
    await pending;
    assert.equal(element('enh-cfg-model-options').options.length, 0);
});

test('authentication failure preserves the draft and allows retry without saving', async () => {
    let { form, element, calls } = setup(async () => ({ error: '身份验证失败，请检查当前填写的密钥和账号权限。' }));
    await form.fetchMpModels();
    assert.equal(calls.length, 1);
    assert.match(element('enh-cfg-message').textContent, /身份验证失败/);
    assert.equal(element('enh-cfg-key').value, ' draft-key ');
    assert.equal(element('enh-cfg-fetch-models').disabled, false);
});

test('rapid repeated clicks issue only one model request', async () => {
    let finish;
    let { form, calls } = setup(() => new Promise(resolve => { finish = resolve; }));
    let pending = form.fetchMpModels();
    await form.fetchMpModels();
    assert.equal(calls.length, 1);
    finish({ models: [] });
    await pending;
});

test('only Save writes the key and selected text model, without changing vision settings', async () => {
    let { form, element, calls } = setup(async call => call.route == 'GetAPIKeyStatus'
        ? { status: 'set' } : { success: true, settings: call.body.settings });
    await form.saveEnhancerSettings();
    assert.deepEqual(calls.map(call => call.route), ['SetAPIKey', 'SaveMagicPromptSettings', 'GetAPIKeyStatus']);
    assert.equal(calls[0].body.key, 'draft-key');
    assert.deepEqual(calls[1].body.settings, {
        backend: 'openaiapi', model: 'unsaved-model', baseurl: 'http://localhost:1234/v1'
    });
    assert.equal(element('enh-cfg-key').value, '');
});

test('closing during configuration loading prevents a late response from overwriting the form', async () => {
    let finish;
    let { form, element } = setup(() => new Promise(resolve => { finish = resolve; }));
    let pending = form.openEnhancerSettings();
    assert.equal(element('enh-cfg-save').disabled, true);
    form.closeEnhancerSettings();
    finish({ settings: { backend: 'ollama', model: 'obsolete' } });
    await pending;
    assert.equal(element('enh-cfg-model').value, 'unsaved-model');
    assert.equal(form.requests.loading, false);
});


test('model choices belong to the original editable field without a second select', () => {
    let html = fs.readFileSync(path.join(__dirname, '../Studio/index.html'), 'utf8');
    assert.match(html, /<input[^>]*id="enh-cfg-model"[^>]*list="enh-cfg-model-options"/);
    assert.match(html, /<datalist id="enh-cfg-model-options"><\/datalist>/);
    assert.doesNotMatch(html, /<select[^>]*id="enh-cfg-model-options"/);
});

test('choosing a fetched model saves its ID from the original field', async () => {
    let { form, element, calls } = setup(async call => {
        if (call.route == 'PreviewMagicPromptModels') {
            return { models: [{ model: 'provider/model-id', name: 'Friendly name' }] };
        }
        return call.route == 'GetAPIKeyStatus' ? { status: 'set' } : { success: true, settings: call.body.settings };
    });
    await form.fetchMpModels();
    let option = element('enh-cfg-model-options').options[0];
    assert.equal(option.value, 'provider/model-id');
    element('enh-cfg-model').value = option.value;
    await form.saveEnhancerSettings();
    assert.equal(calls.find(call => call.route == 'SaveMagicPromptSettings').body.settings.model, 'provider/model-id');
});
