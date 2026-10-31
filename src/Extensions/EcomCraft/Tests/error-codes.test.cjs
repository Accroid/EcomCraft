// Plain-language error messages (F41): every error code the server can return is whitelisted in studio.js and has an English
// "error:<code>" entry with a next step, so no browser ever falls back to a raw stack, address or prompt text.
// Run: node --test src/Extensions/EcomCraft/Tests/error-codes.test.cjs
'use strict';

const { test } = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.join(__dirname, '..');
const studioSource = require('./helpers/studio-sources.cjs').readStudioScripts();
const chinese = /[　-〿㐀-鿿＀-￯]/;

/** The error codes studio.js accepts, extracted from the safeErrors set literal. */
function safeErrors() {
    let match = studioSource.match(/const safeErrors = new Set\(\[([\s\S]*?)\]\)/);
    assert.ok(match, 'studio.js must keep its safeErrors set');
    return new Set([...match[1].matchAll(/'([a-z0-9_]+)'/g)].map(item => item[1]));
}

/** Loads lang/en.js the way the browser does and returns its table. */
function loadEnglish() {
    let sandbox = { window: {} };
    vm.runInNewContext(fs.readFileSync(path.join(root, 'Studio', 'lang', 'en.js'), 'utf8'), sandbox);
    return sandbox.window.EcomCraftLanguages.en;
}

/** Every error code the C# side can attach to an Error(...) response. */
function serverCodes() {
    let codes = new Set();
    let files = [];
    let walk = folder => {
        for (let name of fs.readdirSync(folder, { withFileTypes: true })) {
            if (name.isDirectory()) {
                walk(path.join(folder, name.name));
            }
            else if (name.name.endsWith('.cs')) {
                files.push(path.join(folder, name.name));
            }
        }
    };
    walk(path.join(root, 'Studio'));
    walk(path.join(root, 'MagicPrompt'));
    let pattern = /Error\((?:[^()]|\([^()]*\))+,\s*"([a-z][a-z0-9_]*)"\s*\)/g;
    for (let file of files) {
        for (let match of fs.readFileSync(file, 'utf8').matchAll(pattern)) {
            codes.add(match[1]);
        }
    }
    return codes;
}

test('every server error code is whitelisted and translated', () => {
    let safe = safeErrors();
    let english = loadEnglish();
    for (let code of serverCodes()) {
        assert.ok(safe.has(code), `${code} must be in studio.js safeErrors`);
        assert.ok(english[`error:${code}`] != null, `${code} needs an English "error:${code}" entry`);
    }
});

test('the safeErrors set and the English error table carry no orphans', () => {
    let safe = safeErrors();
    let english = loadEnglish();
    for (let code of safe) {
        assert.ok(english[`error:${code}`] != null, `${code} needs an English "error:${code}" entry`);
    }
    for (let key of Object.keys(english)) {
        if (key.startsWith('error:')) {
            assert.ok(safe.has(key.slice('error:'.length)), `English entry "${key}" matches no whitelisted code`);
        }
    }
});

test('English error messages stay plain and suggest a next step', () => {
    let english = loadEnglish();
    for (let [key, value] of Object.entries(english)) {
        if (!key.startsWith('error:')) {
            continue;
        }
        assert.ok(!chinese.test(value), `"${key}" must not contain Chinese`);
        assert.ok(String(value).trim().length >= 20, `"${key}" should explain what to do next`);
    }
});
