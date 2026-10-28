#!/usr/bin/env node
// Template bench (F20): runs a directory of product photos through chosen templates on a running EcomCraft server,
// saves the results next to a CSV and an HTML comparison page with empty "pass / fail / notes" columns for manual scoring.
// A later run can include the previous report's scores with --compare.
//
// Usage:
//   node bench.cjs --url http://127.0.0.1:7801 --products <dir> --templates white-main,scene --out <dir>
//                  [--user admin --pass secret] [--style <style-id>] [--count 2] [--timeout 600]
//
// The tool only calls the public HTTP API and downloads results; it never writes to the server's Data/ or Models/.
'use strict';

const fs = require('node:fs');
const path = require('node:path');

/** Parses the command line into an options object. */
function parseArgs(argv) {
    let options = { count: 1, timeout: 600, url: 'http://127.0.0.1:7801' };
    for (let i = 0; i < argv.length; i++) {
        let key = argv[i];
        if (!key.startsWith('--')) {
            throw new Error(`Unexpected argument: ${key}`);
        }
        let name = key.slice(2);
        if (name == 'help') {
            options.help = true;
            continue;
        }
        let value = argv[++i];
        if (value == null) {
            throw new Error(`Missing value for --${name}`);
        }
        options[name] = value;
    }
    return options;
}

const HELP = `Usage: node bench.cjs --url http://127.0.0.1:7801 --products <dir> --templates id1,id2 --out <dir>
  --url       Platform server address (default http://127.0.0.1:7801)
  --products  Folder of test product photos (PNG/JPEG/WebP)
  --templates Comma-separated template IDs
  --out       Output folder for images, report.csv and report.html
  --user      Account name when the server requires login
  --pass      Account password
  --style     Style ID (default: the first available style)
  --count     Candidates per product and template (default 1)
  --timeout   Seconds to wait per task (default 600)
  --compare   Previous report.csv whose scores are copied into the new report`;

/** One API call: posts JSON and returns the response, throwing on errors. */
async function call(url, route, body) {
    let response = await fetch(`${url.replace(/\/$/, '')}/API/${route}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body || {})
    });
    let data;
    try {
        data = await response.json();
    }
    catch (error) {
        throw new Error(`${route}: the server did not return JSON (status ${response.status})`);
    }
    if (data.error || !response.ok) {
        throw new Error(`${route}: ${data.error || response.status}${data.error_id ? ` (${data.error_id})` : ''}`);
    }
    return data;
}

/** Reads an image file as a data URL. */
function dataUrl(file) {
    let type = path.extname(file).toLowerCase() == '.png' ? 'image/png'
        : path.extname(file).toLowerCase() == '.webp' ? 'image/webp' : 'image/jpeg';
    return `data:${type};base64,${fs.readFileSync(file).toString('base64')}`;
}

/** Downloads one result URL into the run folder. */
async function download(url, from, file) {
    let response = await fetch(`${url.replace(/\/$/, '')}/${from.split('/').map(encodeURIComponent).join('/')}`);
    if (!response.ok) {
        throw new Error(`download failed for ${from} (status ${response.status})`);
    }
    fs.writeFileSync(file, Buffer.from(await response.arrayBuffer()));
}

/** CSV cell escaping. */
function csv(value) {
    let text = String(value == null ? '' : value);
    return /[",\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

/** Writes report.csv and report.html. */
function writeReport(run, rows, previous) {
    let csvLines = ['product,template,candidate,file,previous_verdict,verdict,notes'];
    for (let row of rows) {
        csvLines.push([row.product, row.template, row.candidate, row.file, row.previous || '', '', ''].map(csv).join(','));
    }
    fs.writeFileSync(path.join(run, 'report.csv'), csvLines.join('\n') + '\n');
    let cells = rows.map(row => `
        <figure>
            <img src="${row.file.replace(/"/g, '&quot;')}" alt="">
            <figcaption>${row.product} · ${row.template} #${row.candidate}${row.previous ? `<br>上次：${row.previous}` : ''}</figcaption>
        </figure>`).join('\n');
    let html = `<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><title>模板测试报告</title>
<style>body{font-family:system-ui,sans-serif;margin:24px;background:#f6f6f4;color:#222}
main{max-width:1400px;margin:0 auto}h1{font-size:20px}p{color:#555}
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:16px}
figure{margin:0;background:#fff;border:1px solid #ddd;border-radius:8px;padding:8px}
img{width:100%;height:220px;object-fit:contain;background:#eee}figcaption{font-size:13px;padding-top:6px;color:#444}
a{color:#0a7}</style></head><body><main>
<h1>模板测试报告</h1>
<p>共 ${rows.length} 张结果。评分方法：打开同目录的 report.csv，填写 verdict（合格 / 不合格）与 notes 列。</p>
<div class="grid">${cells}
</div></main></body></html>`;
    fs.writeFileSync(path.join(run, 'report.html'), html);
}

/** The bench run. */
async function main() {
    let options = parseArgs(process.argv.slice(2));
    if (options.help || !options.products || !options.templates || !options.out) {
        console.log(HELP);
        process.exit(options.help ? 0 : 1);
    }
    let products = fs.readdirSync(options.products)
        .filter(name => /\.(png|jpe?g|webp)$/i.test(name))
        .sort()
        .map(name => path.join(options.products, name));
    if (products.length == 0) {
        throw new Error(`No PNG/JPEG/WebP photos found in ${options.products}`);
    }
    let templates = String(options.templates).split(',').map(id => id.trim()).filter(Boolean);

    let session = (await call(options.url, 'GetNewSession', {})).session_id;
    if (options.user) {
        await call(options.url, 'Login', { username: options.user, password: options.pass || '', session_id: session });
        session = (await call(options.url, 'GetNewSession', {})).session_id;
    }
    let request = (route, body) => call(options.url, route, { ...body, session_id: session });

    let catalog = await request('EcomCraftStudioTemplates', { language: 'zh-CN' });
    let known = (catalog.templates || []).filter(template => templates.includes(template.id));
    for (let id of templates.filter(id => !known.some(template => template.id == id))) {
        console.warn(`警告：模板 ${id} 不在可用列表中，可能缺少工作流或依赖，先跳过。`);
    }
    if (known.length == 0) {
        throw new Error('没有可运行的模板。');
    }
    let styles = await request('EcomCraftStudioStyles', { language: 'zh-CN' });
    let style = options.style || (styles.styles || [])[0]?.id;
    if (!style) {
        throw new Error('服务端没有可用的风格。');
    }
    console.log(`商品 ${products.length} 个 × 模板 ${known.length} 个，风格 ${style}，每种 ${options.count} 张候选。`);

    let stamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
    let run = path.join(options.out, `run-${stamp}`);
    fs.mkdirSync(path.join(run, 'images'), { recursive: true });

    // Previous scores, keyed by "product|template|candidate".
    let previous = new Map();
    if (options.compare && fs.existsSync(options.compare)) {
        let lines = fs.readFileSync(options.compare, 'utf8').split(/\r?\n/).filter(Boolean);
        let header = lines.shift().split(',');
        for (let line of lines) {
            let cells = line.match(/(?:^|,)((?:[^,"]|"([^"]*)"|"(?:[^"]*"")*[^"]*")*)/g)?.map(cell => cell.replace(/^,/, '').replace(/^"|"$/g, '').replace(/""/g, '"')) || [];
            let row = Object.fromEntries(header.map((name, index) => [name, cells[index]]));
            if (row.verdict) {
                previous.set(`${row.product}|${row.template}|${row.candidate || 1}`, row.verdict);
            }
        }
    }

    let rows = [];
    let deadlineMs = Number(options.timeout) * 1000;
    for (let productPath of products) {
        let product = path.basename(productPath, path.extname(productPath));
        for (let template of known) {
            let image = dataUrl(productPath);
            let field = (template.fields || []).find(item => item.type == 'image');
            if (template.preserve_product) {
                console.log(`抠图：${product}（模板 ${template.id} 保留原商品像素）`);
                try {
                    image = (await request('EcomCraftStudioCutout', { product_image: image })).cutout;
                }
                catch (error) {
                    console.warn(`  抠图失败，跳过：${error.message}`);
                    continue;
                }
            }
            if (field && !template.preserve_product && !image) {
                continue;
            }
            let payload = {
                source: 'local',
                kind: 'image',
                template_id: template.id,
                template_params: field ? { [field.id]: image } : {},
                style_id: style,
                images: Math.max(1, Number(options.count) || 1),
                async: true
            };
            console.log(`提交：${product} × ${template.id}`);
            let result = await request('EcomCraftStudioGenerate', payload);
            let images = result.images || [];
            if (result.task_id != null) {
                let start = Date.now();
                while (images.length == 0 && Date.now() - start < deadlineMs) {
                    await new Promise(resolve => setTimeout(resolve, 3000));
                    let queue = await request('EcomCraftStudioQueue', {});
                    let outcome = (queue.finished_tasks || []).find(task => task.id == result.task_id);
                    let running = (queue.tasks || []).some(task => task.id == result.task_id);
                    if (outcome) {
                        if (outcome.state == 'failed') {
                            console.warn(`  任务失败：${outcome.error}`);
                        }
                        images = outcome.images || [];
                        break;
                    }
                    if (!running) {
                        break;
                    }
                }
            }
            for (let [index, url] of images.entries()) {
                let file = `images/${product}__${template.id}__${index + 1}${path.extname(url.split('/').pop()) || '.png'}`;
                await download(options.url, url, path.join(run, file));
                rows.push({
                    product,
                    template: template.id,
                    candidate: index + 1,
                    file,
                    previous: previous.get(`${product}|${template.id}|${index + 1}`) || ''
                });
                console.log(`  已保存 ${file}`);
            }
            if (images.length == 0) {
                console.warn(`  ${product} × ${template.id} 没有产出图片。`);
            }
        }
    }
    writeReport(run, rows, previous);
    console.log(`报告已生成：${path.join(run, 'report.csv')} 和 report.html，共 ${rows.length} 张结果。`);
}

if (require.main === module) {
    main().catch(error => {
        console.error(`错误：${error.message}`);
        process.exit(1);
    });
}

module.exports = { parseArgs, call, dataUrl, download, csv, writeReport, main };
