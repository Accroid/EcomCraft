let { test } = require('node:test');
let assert = require('node:assert/strict');
let fs = require('node:fs');
let path = require('node:path');
let vm = require('node:vm');

/** Runs the actual catalog poller, replacing only the DOM renderer and transport. */
function setup(response) {
    let calls = 0;
    let renders = 0;
    let message = { textContent: '' };
    let source = fs.readFileSync(path.join(__dirname, '../Studio/studio.js'), 'utf8');
    source = source.slice(0, source.lastIndexOf('    initTheme();')) + `
        populateWorkflows = () => rendered();
        updateControls = () => {};
        globalThis.poller = WorkflowCatalogRefresh;
        globalThis.state = state;
    })();`;
    let context = vm.createContext({
        document: { hidden: false, getElementById: () => message },
        rendered: () => renders++,
        fetch: async () => {
            calls++;
            return { ok: true, status: 200, json: async () => response() };
        }
    });
    vm.runInContext(source, context);
    context.state.session = 'test-session';
    context.state.catalog = { workflows: [{ id: 'existing' }] };
    context.state.workflowId = 'existing';
    context.state.workflowValues = { text: 'unsaved edit' };
    return { context, calls: () => calls, renders: () => renders, message };
}

test('new workflows arrive even when the five-second backend status poll is in flight', async () => {
    let harness = setup(() => ({ workflows: [{ id: 'existing' }, { id: 'new-blueprint' }] }));
    let { context } = harness;
    context.state.backendStatusLoading = true;
    await context.poller.refresh();
    assert.equal(context.state.catalog.workflows[1].id, 'new-blueprint');
    assert.equal(context.state.workflowValues.text, 'unsaved edit');
    assert.equal(context.state.workflowId, 'existing');
    assert.equal(harness.renders(), 1);
});

test('unchanged lists do not rerender fields being edited', async () => {
    let harness = setup(() => ({ workflows: [{ id: 'existing' }] }));
    await harness.context.poller.refresh();
    assert.equal(harness.calls(), 1);
    assert.equal(harness.renders(), 0);
});

test('hidden pages and generation do not trigger catalog scans', async () => {
    let harness = setup(() => ({ workflows: [] }));
    harness.context.document.hidden = true;
    await harness.context.poller.refresh();
    harness.context.document.hidden = false;
    harness.context.state.busy = true;
    await harness.context.poller.refresh();
    assert.equal(harness.calls(), 0);
});

test('a manual refresh supersedes an older background response', async () => {
    let finish;
    let started;
    let ready = new Promise(resolve => { started = resolve; });
    let harness = setup(() => new Promise(resolve => { finish = resolve; started(); }));
    let pending = harness.context.poller.refresh();
    await ready;
    harness.context.state.catalog = { workflows: [{ id: 'manually-refreshed' }] };
    finish({ workflows: [] });
    await pending;
    assert.equal(harness.context.state.catalog.workflows[0].id, 'manually-refreshed');
    assert.equal(harness.renders(), 0);
});

test('failed refresh preserves the list and reports a retry message', async () => {
    let harness = setup(() => { throw new Error('offline'); });
    await harness.context.poller.refresh();
    assert.equal(harness.context.state.catalog.workflows[0].id, 'existing');
    assert.match(harness.message.textContent, /刷新失败/);
    assert.equal(harness.context.poller.loading, false);
});
