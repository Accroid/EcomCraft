// Upload quality checks (F38): a too-small, blurry or distant photo gets a plain warning with a suggestion; the user can always continue.
// Run: node --test src/Extensions/EcomCraft/Tests/upload-quality.test.cjs
'use strict';

const { test } = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const studioDir = path.join(__dirname, '..', 'Studio');

/** Loads simple.js the way the browser does and returns the SimpleMode class. */
function loadSimpleMode() {
    let sandbox = {};
    vm.runInNewContext(fs.readFileSync(path.join(studioDir, 'simple.js'), 'utf8') + '\nthis.SimpleMode = SimpleMode;', sandbox);
    return sandbox.SimpleMode;
}

/** An ImageData-like RGBA buffer filled pixel by pixel. */
function pixels(width, height, fill) {
    let data = new Uint8ClampedArray(width * height * 4);
    for (let y = 0; y < height; y++) {
        for (let x = 0; x < width; x++) {
            let [r, g, b, a] = fill(x, y);
            let i = (y * width + x) * 4;
            data[i] = r;
            data[i + 1] = g;
            data[i + 2] = b;
            data[i + 3] = a;
        }
    }
    return { width, height, data };
}

/** A sharp black/white checkerboard, so the Laplacian variance is high. */
let sharp = pixels(64, 64, (x, y) => ((x + y) % 2 ? [255, 255, 255, 255] : [0, 0, 0, 255]));

/** A flat gray picture, the classic fully blurry case. */
let flat = pixels(64, 64, () => [128, 128, 128, 255]);

let quality = { min_side: 512, blur_variance: 30, min_cutout_ratio: 0.08 };

test('photos below the configured side get a resolution warning, larger ones do not', () => {
    let SimpleMode = loadSimpleMode();
    let small = SimpleMode.uploadIssues(400, 300, sharp, quality);
    assert.ok(small.some(issue => issue.code == 'resolution' && issue.min_side == 512));
    let fine = SimpleMode.uploadIssues(2000, 1500, sharp, quality);
    assert.ok(!fine.some(issue => issue.code == 'resolution'));
});

test('a flat picture is flagged as blurry, a checkerboard is not', () => {
    let SimpleMode = loadSimpleMode();
    assert.ok(SimpleMode.laplacianVariance(flat) < 30);
    assert.ok(SimpleMode.laplacianVariance(sharp) > 30);
    let blurry = SimpleMode.uploadIssues(2000, 1500, flat, quality);
    assert.ok(blurry.some(issue => issue.code == 'blur'));
    let crisp = [...SimpleMode.uploadIssues(2000, 1500, sharp, quality)];
    assert.deepStrictEqual(crisp, []);
});

test('a cut-out that covers too little of the picture is flagged', () => {
    let SimpleMode = loadSimpleMode();
    let distant = pixels(100, 100, (x, y) => (x >= 45 && x < 55 && y >= 45 && y < 55 ? [200, 10, 10, 255] : [0, 0, 0, 0]));
    let issues = [...SimpleMode.cutoutIssues(distant, quality)];
    assert.ok(issues.some(issue => issue.code == 'ratio' && issue.ratio < 0.08));
    let close = pixels(100, 100, (x, y) => (x >= 10 && x < 90 && y >= 10 && y < 90 ? [200, 10, 10, 255] : [0, 0, 0, 0]));
    assert.deepStrictEqual([...SimpleMode.cutoutIssues(close, quality)], []);
});

test('a zero cut-out threshold disables the share warning', () => {
    let SimpleMode = loadSimpleMode();
    let distant = pixels(100, 100, () => [0, 0, 0, 0]);
    assert.deepStrictEqual([...SimpleMode.cutoutIssues(distant, { min_cutout_ratio: 0 })], []);
});

test('the platform file carries the thresholds the server hands to the page', () => {
    let file = JSON.parse(fs.readFileSync(path.join(studioDir, '..', 'Templates', 'platforms.json'), 'utf8'));
    let qualityBlock = file.upload_quality || {};
    for (let key of ['min_side', 'blur_variance', 'min_cutout_ratio']) {
        assert.ok(qualityBlock[key] != null, `${key} must stay configurable in platforms.json`);
    }
});
