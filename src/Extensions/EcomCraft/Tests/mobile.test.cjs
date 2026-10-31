// Mobile support (F42): camera capture upload works over plain http (no secure-context APIs) and the gallery collapses to one column on phones.
// Run: node --test src/Extensions/EcomCraft/Tests/mobile.test.cjs
'use strict';

const { test } = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const sources = require('./helpers/studio-sources.cjs');

const studioDir = sources.studioDir;
const html = fs.readFileSync(path.join(studioDir, 'index.html'), 'utf8');
const css = fs.readFileSync(path.join(studioDir, 'studio.css'), 'utf8');

test('the upload step offers camera capture without secure-context APIs', () => {
    let input = html.match(/<input type="file" id="simple-camera"[^>]*>/);
    assert.ok(input, 'the hidden camera input must exist');
    assert.match(input[0], /accept="image\/\*"/);
    assert.match(input[0], /capture=/, 'capture opens the camera on phones');
    assert.ok(/id="simple-camera-btn"/.test(html), 'a real button opens the picker');
});

test('no browser feature that needs HTTPS is used', () => {
    assert.ok(!/navigator\.clipboard|new Notification|serviceWorker|navigator\.share/.test(sources.readAllScripts()), 'workspace scripts must not use secure-context APIs');
});

test('the gallery collapses to a single column on narrow phones', () => {
    let narrow = css.match(/@media \(max-width: 560px\) \{[\s\S]*?\n\}/);
    assert.ok(narrow, 'a max-width: 560px block must exist');
    assert.match(narrow[0], /\.contact-sheet \{[^}]*grid-template-columns: 1fr/);
});

test('phones get the simple mode only: professional entries are hidden and #create returns to creating images (F63)', () => {
    assert.match(css, /@media \(max-width: 720px\) \{\s*\.simple-refine, \.pro-only \{ display: none; \}/);
    for (let id of ['nav-create', 'lb-edit']) {
        assert.match(html, new RegExp(`class="[^"]*\\bpro-only\\b[^"]*"[^>]*id="${id}"`), `${id} is hidden on phones`);
    }
    let studio = sources.readStudioScripts();
    assert.match(studio, /if \(view == 'create' && phoneScreen\(\)\) \{\s*view = 'simple';/);
});
