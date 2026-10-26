// Run against an already started local server with ECOMCRAFT_TEST_URL=http://127.0.0.1:7801.
let { test } = require('node:test');
let assert = require('node:assert/strict');
let http = require('node:http');
let { once } = require('node:events');
let { createHash } = require('node:crypto');

/** Uses disposable upstream servers and synthetic keys; never saves user settings. */
test('model preview API', { skip: !process.env.ECOMCRAFT_TEST_URL }, async t => {
    let base = process.env.ECOMCRAFT_TEST_URL;
    assert.ok(['127.0.0.1', 'localhost'].includes(new URL(base).hostname));
    let session;
    let api = async (route, body = {}, signal) => {
        let response = await fetch(`${base}/API/${route}`, {
            method: 'POST', headers: { 'Content-Type': 'application/json' }, signal,
            body: JSON.stringify({ ...body, ...(session ? { session_id: session } : {}) })
        });
        assert.equal(response.status, 200);
        return response.json();
    };
    session = (await api('GetNewSession')).session_id;
    assert.ok(session);
    // Hash snapshots to keep private settings out of assertion output.
    let digest = value => createHash('sha256').update(JSON.stringify(value)).digest('hex');
    let before = digest((await api('GetMagicPromptSettings')).settings);
    let keyBefore = digest(await api('GetAPIKeyStatus', { keyType: 'openaiapi_local' }));
    let received = [];
    let mode = 'ok';
    let slowClosed;
    let slowSeen;
    let server = http.createServer((request, response) => {
        received.push({ path: request.url, auth: request.headers.authorization });
        if (mode == 'slow') {
            response.on('close', () => slowClosed?.());
            slowSeen?.();
            return;
        }
        if (mode == 'unauthorized') {
            response.writeHead(401).end('Upstream echoed synthetic-secret');
            return;
        }
        if (mode == 'malformed') {
            response.end('synthetic-secret: invalid JSON');
            return;
        }
        response.setHeader('Content-Type', 'application/json');
        response.end(JSON.stringify(request.url.endsWith('/api/tags')
            ? { models: [{ name: 'local-model' }] } : { data: [{ id: 'preview-model' }] }));
    });
    server.listen(0, '127.0.0.1');
    await once(server, 'listening');
    let upstream = `http://127.0.0.1:${server.address().port}`;
    let preview = (overrides = {}, signal) => api('PreviewMagicPromptModels', {
        backend: 'openaiapi', baseurl: `${upstream}/v1`, api_key: 'synthetic-secret', ...overrides
    }, signal);
    try {
        await t.test('uses the unsaved key without duplicating v1 or returning settings', async () => {
            let result = await preview();
            assert.equal(result.success, true);
            assert.equal(result.models[0].model, 'preview-model');
            assert.deepEqual(received.at(-1), { path: '/v1/models', auth: 'Bearer synthetic-secret' });
            assert.equal(result.settings, undefined);
            assert.equal(JSON.stringify(result).includes('synthetic-secret'), false);
        });
        await t.test('supports root, trailing slash and prefixed base paths', async () => {
            for (let [suffix, expected] of [['', '/v1/models'], ['/v1/', '/v1/models'], ['/gateway/v1', '/gateway/v1/models']]) {
                assert.equal((await preview({ baseurl: upstream + suffix })).success, true);
                assert.equal(received.at(-1).path, expected);
            }
        });
        await t.test('does not forward a saved credential to a new address', async () => {
            assert.equal((await preview({ api_key: '' })).success, true);
            assert.equal(received.at(-1).auth, undefined);
        });
        await t.test('keeps credentials separate across simultaneous previews', async () => {
            received.length = 0;
            await Promise.all([preview({ api_key: 'draft-a' }), preview({ api_key: 'draft-b' })]);
            assert.deepEqual(received.map(item => item.auth).sort(), ['Bearer draft-a', 'Bearer draft-b']);
        });
        await t.test('supports Ollama endpoint and model format', async () => {
            let result = await preview({ backend: 'ollama', baseurl: upstream, api_key: '' });
            assert.equal(result.models[0].model, 'local-model');
            assert.equal(received.at(-1).path, '/api/tags');
        });
        await t.test('does not expose upstream error bodies or credentials', async () => {
            for (let failure of ['unauthorized', 'malformed']) {
                mode = failure;
                let result = await preview();
                assert.equal(result.success, false);
                assert.equal(JSON.stringify(result).includes('synthetic-secret'), false);
            }
            mode = 'ok';
        });
        await t.test('rejects invalid provider, URL or key before connecting', async () => {
            let count = received.length;
            for (let input of [{ backend: 'unknown' }, { baseurl: 'file:///C:/private' },
                { baseurl: `${upstream}?key=secret` }, { api_key: 'bad\r\nkey' }]) {
                assert.equal((await preview(input)).success, false);
            }
            assert.equal(received.length, count);
        });
        await t.test('cancels the upstream when the browser cancels', async () => {
            mode = 'slow';
            let controller = new AbortController();
            let seen = new Promise(resolve => { slowSeen = resolve; });
            let closed = new Promise(resolve => { slowClosed = resolve; });
            let pending = preview({}, controller.signal).catch(error => error);
            await Promise.race([seen, pending.then(() => { throw new Error('Preview returned before reaching the test server'); })]);
            controller.abort();
            assert.equal((await pending).name, 'AbortError');
            let deadline;
            try {
                await Promise.race([closed, new Promise((_, reject) => {
                    deadline = setTimeout(() => reject(new Error('Upstream request was not canceled')), 5000);
                })]);
            }
            finally {
                clearTimeout(deadline);
            }
            mode = 'ok';
        });
        await t.test('times out an unresponsive upstream without returning credentials', async () => {
            mode = 'slow';
            let started = Date.now();
            let result = await preview();
            assert.equal(result.success, false);
            assert.match(result.error, /超时/);
            assert.ok(Date.now() - started < 25000);
            assert.equal(JSON.stringify(result).includes('synthetic-secret'), false);
            mode = 'ok';
        });
        await t.test('leaves configuration and stored key status unchanged', async () => {
            assert.equal(digest((await api('GetMagicPromptSettings')).settings), before);
            assert.equal(digest(await api('GetAPIKeyStatus', { keyType: 'openaiapi_local' })), keyBefore);
        });
    }
    finally {
        server.closeAllConnections();
        server.close();
    }
});
