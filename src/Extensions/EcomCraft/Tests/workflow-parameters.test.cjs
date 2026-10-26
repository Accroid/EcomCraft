let { test } = require('node:test');
let assert = require('node:assert/strict');
let fs = require('node:fs');
let path = require('node:path');
let vm = require('node:vm');

/** Exercises the actual form/default and payload code without a browser or GPU. */
function setup(prompt = 'standard prompt') {
    let elements = new Map();
    let element = id => {
        if (!elements.has(id)) {
            let parent = {};
            elements.set(id, { value: '', options: [], tagName: ['sampler', 'scheduler', 'images', 'model'].includes(id) ? 'SELECT' : 'INPUT',
                append(option) { this.options.push(option); }, closest: () => parent, style: {}, classList: { toggle() {} } });
        }
        return elements.get(id);
    };
    let source = fs.readFileSync(path.join(__dirname, '../Studio/studio.js'), 'utf8');
    source = source.slice(0, source.lastIndexOf('    initTheme();')) + `
        renderWorkflowFields = () => {};
        updateWorkflowUi = () => NativeWorkflowForm.sync(currentWorkflow());
        globalThis.api = { state, selectWorkflow, setWorkflowValue, buildPayload, updateControls, sync: () => NativeWorkflowForm.sync(currentWorkflow()) };
    })();`;
    let context = vm.createContext({ document: { getElementById: element }, Option: function(text, value) { this.text = text; this.value = value; } });
    vm.runInContext(source, context);
    let workflow = { id: 'test', kind: 'image', native: true, defaults: { prompt: 'saved prompt', negativeprompt: 'saved negative',
        width: 512, height: 512, images: 1, steps: 20, cfgscale: 8, seed: 48507568374478, sampler: 'euler', scheduler: 'normal' }, inputs: [] };
    context.api.state.session = 'test-session';
    context.api.state.catalog = { models: [], workflows: [workflow], can_generate: true, local_backends_observed: 1 };
    element('prompt').value = prompt;
    element('width').value = '1024';
    element('workflow-select').value = 'test';
    context.api.selectWorkflow();
    return { api: context.api, element, workflow };
}

test('selection reads saved defaults and permits native workflows without a global model', () => {
    let { api, element } = setup('');
    assert.equal(element('prompt').value, 'saved prompt');
    assert.equal(element('width').value, 512);
    assert.equal(element('cfg').value, 8);
    assert.equal(element('seed').value, 48507568374478);
    assert.equal(element('model-fld').hidden, true);
    assert.equal(element('model').required, false);
    api.updateControls();
    assert.equal(element('generate').disabled, false);
});

test('edited fields and workflow model/denoise are submitted without raw graphs or global model', async () => {
    let { api, element } = setup();
    element('prompt').value = 'changed prompt';
    element('negative').value = 'changed negative';
    element('width').value = '768';
    element('height').value = '640';
    element('seed').value = '48507568374479';
    element('steps').value = '27';
    element('cfg').value = '6.5';
    element('sampler').value = 'heun';
    element('scheduler').value = 'karras';
    api.state.workflowValues = { ecnativecheckpoint: 'edited-model.safetensors', ecnativedenoise: 0.65 };
    let payload = await api.buildPayload();
    assert.equal(payload.workflow_id, 'test');
    assert.equal(payload.model, undefined);
    assert.equal(payload.prompt, 'changed prompt');
    assert.equal(payload.negativeprompt, 'changed negative');
    assert.equal(payload.width, 768);
    assert.equal(payload.height, 640);
    assert.equal(payload.seed, 48507568374479);
    assert.equal(payload.steps, 27);
    assert.equal(payload.cfgscale, 6.5);
    assert.equal(payload.sampler, 'heun');
    assert.equal(payload.scheduler, 'karras');
    assert.equal(payload.workflow_params.ecnativecheckpoint, 'edited-model.safetensors');
    assert.equal(payload.workflow_params.ecnativedenoise, 0.65);
    assert.equal(payload.comfyworkflowraw, undefined);
});

test('catalog refresh and switching back preserve prompts while restoring standard dimensions', () => {
    let { api, element, workflow } = setup();
    element('prompt').value = 'unsaved edit';
    workflow.defaults.prompt = 'new saved default';
    api.sync();
    assert.equal(element('prompt').value, 'unsaved edit');
    element('workflow-select').value = '';
    api.selectWorkflow();
    assert.equal(element('prompt').value, 'unsaved edit');
    assert.equal(element('width').value, '1024');
    assert.equal(element('model-fld').hidden, false);
    assert.equal(element('model').required, true);
});


test('model and workflow changes preserve both existing and deliberately cleared prompts', async () => {
    let { api, element, workflow } = setup();
    assert.equal(element('prompt').value, 'standard prompt');
    element('negative').value = 'user exclusions';
    api.setWorkflowValue('ecnativecheckpoint', 'different-model.safetensors');
    assert.equal(element('prompt').value, 'standard prompt');
    assert.equal(element('negative').value, 'user exclusions');
    api.state.catalog.workflows.push({ ...workflow, id: 'second', defaults: { ...workflow.defaults, prompt: 'replacement', negativeprompt: 'replacement negative', steps: 30 } });
    element('workflow-select').value = 'second';
    api.selectWorkflow();
    assert.equal(element('prompt').value, 'standard prompt');
    assert.equal(element('negative').value, 'user exclusions');
    assert.equal(element('steps').value, 30);
    let payload = await api.buildPayload();
    assert.equal(payload.prompt, 'standard prompt');
    assert.equal(payload.negativeprompt, 'user exclusions');
    element('prompt').value = '';
    element('negative').value = '';
    element('workflow-select').value = 'test';
    api.selectWorkflow();
    assert.equal(element('prompt').value, '');
    assert.equal(element('negative').value, '');
});
