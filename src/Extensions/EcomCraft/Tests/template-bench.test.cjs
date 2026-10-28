// Template bench (F20): report files come out complete, and previous scores are carried into a new run.
// Run: node --test src/Extensions/EcomCraft/Tests/template-bench.test.cjs
'use strict';

const { test } = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

const bench = require('../Tools/template-bench/bench.cjs');

/** A fresh output folder. */
function runDir() {
    let dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ecomcraft-bench-'));
    fs.mkdirSync(path.join(dir, 'images'), { recursive: true });
    return dir;
}

test('the CSV keeps the manual scoring columns and the HTML compares side by side', () => {
    let run = runDir();
    fs.writeFileSync(path.join(run, 'images', 'mug__white-main__1.png'), 'x');
    bench.writeReport(run, [
        { product: '马克杯', template: 'white-main', candidate: 1, file: 'images/mug__white-main__1.png', previous: '合格' },
        { product: '马克杯', template: 'scene', candidate: 1, file: 'images/missing.png', previous: '' }
    ], new Map());
    let csv = fs.readFileSync(path.join(run, 'report.csv'), 'utf8');
    let lines = csv.trim().split('\n');
    assert.match(lines[0], /^product,template,candidate,file,previous_verdict,verdict,notes$/);
    assert.match(lines[1], /马克杯,white-main,1,images\/mug__white-main__1\.png,合格,,/);
    assert.match(lines[2], /,scene,1,images\/missing\.png,,,/);
    let html = fs.readFileSync(path.join(run, 'report.html'), 'utf8');
    assert.match(html, /<img src="images\/mug__white-main__1\.png"/);
    assert.match(html, /上次：合格/);
    assert.match(html, /report\.csv/);
});

test('arguments are parsed and required ones checked', () => {
    let options = bench.parseArgs(['--url', 'http://a:1', '--products', 'p', '--templates', 'a,b', '--out', 'o', '--count', '2']);
    assert.equal(options.url, 'http://a:1');
    assert.deepEqual(options.templates, 'a,b');
    assert.equal(options.count, '2');
    assert.throws(() => bench.parseArgs(['--products']));
});
