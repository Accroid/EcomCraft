// API surface snapshot: refactoring must not rename, add or remove an API call or change its permission without updating api-surface.json on purpose.
// Run: node --test src/Extensions/EcomCraft/Tests/api-surface.test.cjs
// Set ECOMCRAFT_UPDATE_API_SURFACE=1 to rewrite the snapshot after a deliberate API change.
'use strict';

const { test } = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');

const root = path.join(__dirname, '..');
const snapshotFile = path.join(__dirname, 'api-surface.json');
const registrationFiles = ['EcomCraft.cs', path.join('MagicPrompt', 'WebAPI', 'MagicPromptAPI.cs')];

/** Every registered API call as { name, permission }, sorted by name. */
function apiSurface() {
    let calls = [];
    for (let file of registrationFiles) {
        let source = fs.readFileSync(path.join(root, file), 'utf8');
        let matches = [...source.matchAll(/RegisterAPICall\(\s*([\w.]+)\s*,\s*(?:true|false)\s*,\s*([\w.]+)\s*\)/g)];
        let total = (source.match(/RegisterAPICall\(/g) || []).length;
        assert.strictEqual(matches.length, total, `${file} has a registration this test cannot read`);
        for (let match of matches) {
            calls.push({ name: match[1].split('.').pop(), permission: match[2].split('.').pop() });
        }
    }
    return calls.sort((a, b) => a.name < b.name ? -1 : (a.name > b.name ? 1 : 0));
}

test('registered API calls and permissions match the snapshot', () => {
    let current = apiSurface();
    if (process.env.ECOMCRAFT_UPDATE_API_SURFACE) {
        fs.writeFileSync(snapshotFile, JSON.stringify(current, null, 2) + '\n');
    }
    assert.deepStrictEqual(current, JSON.parse(fs.readFileSync(snapshotFile, 'utf8')));
});

test('API call names are unique', () => {
    let names = apiSurface().map(call => call.name);
    assert.strictEqual(new Set(names).size, names.length);
});
