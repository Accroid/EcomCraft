// Front-end structure: every script and style index.html loads is routed and exists, cache versions match, and scripts only look up ids the page has.
// Run: node --test src/Extensions/EcomCraft/Tests/frontend-structure.test.cjs
'use strict';

const { test } = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const sources = require('./helpers/studio-sources.cjs');

const html = fs.readFileSync(path.join(sources.studioDir, 'index.html'), 'utf8');
// Ids created at runtime that $('...') looks up by name. Empty at the baseline: every literal id is in index.html.
const runtimeIds = new Set([]);

/** Local assets index.html references, with their route and cache version. */
function pageAssets() {
    return [...html.matchAll(/<(?:script|link)\b[^>]*\b(?:src|href)="(\/EcomCraft\/[^"?]+)\?v=([^"]+)"/g)].map(match => ({ route: match[1], version: match[2] }));
}

/** Route to file table of StudioRoutes.Assets. */
function routedAssets() {
    let routes = fs.readFileSync(path.join(sources.studioDir, 'StudioRoutes.cs'), 'utf8');
    return new Map([...routes.matchAll(/\("(\/EcomCraft\/[^"]+)",\s*"([^"]+)",\s*"text\//g)].map(match => [match[1], match[2]]));
}

test('every asset of index.html has a route and a file', () => {
    let routes = routedAssets();
    let assets = pageAssets();
    assert.ok(assets.length >= 5, 'the page assets must be found');
    for (let asset of assets) {
        assert.ok(routes.has(asset.route), `${asset.route} needs an entry in StudioRoutes.Assets`);
        assert.ok(fs.existsSync(path.join(sources.studioDir, ...routes.get(asset.route).split('/'))), `${asset.route} needs its file`);
    }
});

test('every script tag carries a cache version', () => {
    let tags = [...html.matchAll(/<script\b[^>]*\bsrc="([^"]+)"/g)].map(match => match[1]);
    assert.strictEqual(tags.length, sources.scriptFiles().length);
    for (let tag of tags) {
        assert.match(tag, /\?v=/, `${tag} needs ?v=`);
    }
});

test('scripts and styles share one cache version', () => {
    let versions = new Set(pageAssets().map(asset => asset.version));
    assert.strictEqual(versions.size, 1, `found ${[...versions].join(', ')}`);
});

test('every literal id scripts look up exists in index.html or is a known runtime id', () => {
    let missing = new Set();
    for (let match of sources.readAllScripts().matchAll(/\$\('([^']+)'\)/g)) {
        if (!html.includes(`id="${match[1]}"`) && !runtimeIds.has(match[1])) {
            missing.add(match[1]);
        }
    }
    assert.deepStrictEqual([...missing], []);
});
