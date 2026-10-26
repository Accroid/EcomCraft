// Local design preview for the EcomCraft Studio page.
// Serves the real index.html / studio.css / studio.js with a mock API and generated placeholder images,
// so the UI can be reviewed without SwarmUI, ComfyUI or a GPU. Listens on 127.0.0.1 only.
// Usage: node src/Extensions/EcomCraft/Tests/studio-preview.cjs [port] [studioDir]   then open http://127.0.0.1:<port>/EcomCraft
// studioDir defaults to ../Studio; pass another folder to reproduce mixed old/new page files.

let http = require('node:http');
let fs = require('node:fs');
let path = require('node:path');

let port = Number(process.argv[2] || 7899);
let studio = process.argv[3] ? path.resolve(process.argv[3]) : path.join(__dirname, '../Studio');

/** In-memory gallery; starring moves files into Starred/ as the real API does. */
let files = [];
let hues = [152, 28, 210, 340, 48, 190, 265, 12, 95, 300, 230, 170];
for (let i = 0; i < 12; i++) {
    files.push({ src: `raw/2026-10-26/${String(1200 + i * 7).padStart(4, '0')}-精华液-${i + 1}.png`, hue: hues[i] });
}
files.push({ src: 'raw/2026-10-25/0930-运动鞋旋转镜头.mp4', hue: 0, video: true });
files[2].src = files[2].src.replace('raw/', 'Starred/');
let requestCounter = 40;
let busyUntil = 0;

/** A simple product-shot placeholder: bottle on a seamless sweep, tinted per file. */
function productSvg(hue, label) {
    return `<svg xmlns="http://www.w3.org/2000/svg" width="768" height="768" viewBox="0 0 768 768">
<defs><linearGradient id="g" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="hsl(${hue} 18% 92%)"/><stop offset="1" stop-color="hsl(${hue} 14% 78%)"/></linearGradient>
<linearGradient id="b" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="hsl(${hue} 45% 38%)"/><stop offset=".55" stop-color="hsl(${hue} 50% 62%)"/><stop offset="1" stop-color="hsl(${hue} 45% 34%)"/></linearGradient></defs>
<rect width="768" height="768" fill="url(#g)"/><ellipse cx="384" cy="610" rx="150" ry="22" fill="rgba(0,0,0,.18)"/>
<rect x="310" y="260" width="148" height="350" rx="26" fill="url(#b)"/><rect x="346" y="196" width="76" height="72" rx="10" fill="hsl(${hue} 12% 22%)"/>
<rect x="330" y="380" width="108" height="90" rx="6" fill="rgba(255,255,255,.82)"/>
<text x="384" y="432" font-family="sans-serif" font-size="22" text-anchor="middle" fill="#333">${label}</text></svg>`;
}

/** Catalog with two LAN servers so grouped workflows and per-server states are visible. */
function catalog() {
    return {
        user_name: 'designer', version: '0.5.0 预览', can_generate: true, can_generate_video: true,
        can_manage_settings: true, can_delete_images: true, cloud_configured: false,
        local_backends_observed: 2, local_backends_starting: 0, local_backends_errored: 0,
        models: [
            { id: 'Flux/flux1-dev', kind: 'image', class_name: 'Flux.1 Dev', width: 1024, height: 1024, resolution_step: 16, image_reference: true },
            { id: 'SDXL/juggernaut-xl', kind: 'image', class_name: 'SDXL 1.0', width: 1024, height: 1024, resolution_step: 8, image_reference: true },
            { id: 'Wan/wan2.1-t2v-1.3b', kind: 'video', class_name: 'Wan 2.1 文生视频', width: 832, height: 480, resolution_step: 16, image_reference: false }
        ],
        loras: ['product-studio-light', 'cosmetics-glass', 'sneaker-detail'],
        samplers: [{ id: 'euler', name: 'Euler' }, { id: 'dpmpp_2m', name: 'DPM++ 2M' }],
        schedulers: [{ id: 'normal', name: 'Normal' }, { id: 'karras', name: 'Karras' }],
        workflows: [
            { id: 'A/白底主图', name: 'A/白底主图', kind: 'image', native: true, description: '从执行服务器“A”的 ComfyUI 自动发现。',
                inputs: [], defaults: { prompt: '', negativeprompt: '', width: 1024, height: 1024, images: 1, steps: 20, cfgscale: 3.5, seed: 1234, sampler: 'euler', scheduler: 'normal' } },
            { id: 'B/场景图', name: 'B/场景图', kind: 'image', native: true, description: '从执行服务器“B”的 ComfyUI 自动发现。',
                inputs: [{ id: 'ecnativebgx', name: '背景风格', type: 'dropdown', values: ['石台', '木纹', '大理石'], default: '石台', description: '' }],
                defaults: { prompt: '', negativeprompt: '', width: 1024, height: 1024, images: 2, steps: 28, cfgscale: 4, seed: 5678, sampler: 'dpmpp_2m', scheduler: 'karras' } },
            { id: 'Examples/商品换背景', name: 'Examples/商品换背景', kind: 'image', native: false, description: '管理员发布的模板。', inputs: [], defaults: null }
        ]
    };
}

/** Mock responses for the API routes the page calls. */
function api(route, body) {
    switch (route) {
        case 'GetNewSession':
            return { session_id: 'preview-session', user_id: 'designer', output_append_user: false };
        case 'EcomCraftStudioCatalog':
            return catalog();
        case 'EcomCraftStudioBackendStatus':
            return { local_backends_observed: 2, local_backends_starting: 0, local_backends_errored: 0 };
        case 'EcomCraftStudioQueue': {
            let busy = Date.now() < busyUntil;
            return {
                backends: [
                    { id: 1, name: 'A', status: 'running', running: busy ? 1 : 0, pending: 0, busy: false },
                    { id: 2, name: 'B', status: 'running', running: 1, pending: 3, busy: true },
                    { id: 3, name: 'C', status: 'unresponsive', running: null, pending: null, busy: false }
                ],
                native_tasks: [{ backend_id: 2, backend_name: 'B', state: 'running', position: null, total_nodes: 14, current_node: '12', node_type: 'KSampler', node_percent: 64, overall_percent: 81, finished_nodes: 11 },
                    { backend_id: 2, backend_name: 'B', state: 'queued', position: 1, total_nodes: 14 }],
                tasks: busy ? [{ id: 7, mine: true, state: 'running', count: 1, elapsed_seconds: 12, request_id: requestCounter }] : [{ id: null, mine: false, state: 'queued', count: 2, elapsed_seconds: 65, request_id: null }]
            };
        }
        case 'EcomCraftStudioGenerate': {
            let count = Math.max(1, Math.min(4, Number(body.images) || 1));
            let created = [];
            for (let i = 0; i < count; i++) {
                let src = `raw/2026-10-26/${String(1500 + requestCounter * 4 + i).padStart(4, '0')}-新作品-${i + 1}.png`;
                files.unshift({ src, hue: hues[(requestCounter + i) % hues.length] });
                created.push(`View/designer/${src}`);
            }
            requestCounter++;
            busyUntil = Date.now() + 3000;
            return { delay: 2500, images: created };
        }
        case 'ListImages': {
            let filter = (body.filter || '').toLowerCase();
            let list = files.filter(file => !filter || file.src.toLowerCase().includes(filter));
            if (body.sortBy == 'Name') {
                list = [...list].sort((a, b) => a.src.split('/').pop().localeCompare(b.src.split('/').pop()));
            }
            return { files: list.map(file => ({ src: file.src })) };
        }
        case 'ToggleImageStarred': {
            let file = files.find(item => item.src == body.path);
            if (!file) {
                return { error: 'That file does not exist, cannot star.' };
            }
            let starred = file.src.startsWith('Starred/');
            file.src = starred ? file.src.replace('Starred/', 'raw/') : file.src.replace('raw/', 'Starred/');
            return { new_state: !starred };
        }
        case 'DeleteImage': {
            let index = files.findIndex(item => item.src == body.path);
            if (index < 0) {
                return { error: 'That file does not exist, cannot delete.' };
            }
            files.splice(index, 1);
            return { success: true };
        }
        case 'GetMagicPromptSettings':
            return { success: true, settings: { backend: 'ollama', model: 'qwen2.5:7b', backends: { ollama: { baseurl: 'http://localhost:11434' } } } };
        case 'MagicPromptPhoneHome':
            return { success: true, response: `${body.messageContent?.text || ''}，专业商业摄影，柔和棚拍灯光，浅灰无缝背景纸，产品居中，细节清晰，8K` };
        case 'PreviewMagicPromptModels':
            return { success: true, models: [{ model: 'qwen2.5:7b', name: 'qwen2.5:7b' }, { model: 'llama3.1:8b', name: 'llama3.1:8b' }] };
        case 'SaveMagicPromptSettings':
            return { success: true, settings: { backend: body.settings?.backend, model: body.settings?.model, backends: {} } };
        case 'GetAPIKeyStatus':
            return { status: 'not set' };
        case 'SetAPIKey':
            return { success: true };
        case 'EcomCraftStudioCancelTask':
            busyUntil = 0;
            return { success: true };
        default:
            return { error: `预览服务器没有模拟接口 ${route}`, error_id: 'api_error' };
    }
}

let types = { '.html': 'text/html; charset=utf-8', '.css': 'text/css; charset=utf-8', '.js': 'text/javascript; charset=utf-8' };

http.createServer((req, res) => {
    let url = new URL(req.url, `http://127.0.0.1:${port}`);
    let pathname = decodeURIComponent(url.pathname);
    if (pathname == '/' || pathname == '/EcomCraft') {
        let html = fs.readFileSync(path.join(studio, 'index.html'), 'utf8');
        if (url.searchParams.has('selftest')) {
            html = html.replace('</body>', '<script src="/preview-selftest.js" defer></script></body>');
        }
        else if (url.searchParams.has('shot')) {
            // Screenshot helper only: drives the page into a named state. Never part of the shipped page.
            html = html.replace('</body>', `<script src="/preview-shot.js?shot=${encodeURIComponent(url.searchParams.get('shot'))}" defer></script></body>`);
        }
        // Same headers as StudioRoutes.cs, so CSP problems show up here before they reach a real server.
        res.writeHead(200, {
            'Content-Type': types['.html'], 'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff', 'Referrer-Policy': 'same-origin',
            'Content-Security-Policy': "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data: blob:; media-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'self'; form-action 'self'"
        });
        res.end(html);
        return;
    }
    if (pathname == '/preview-selftest.js') {
        // Clicks through the page in a real browser and logs one "SELFTEST PASS|FAIL <step>" line per check.
        res.writeHead(200, { 'Content-Type': types['.js'], 'Cache-Control': 'no-store' });
        res.end(`(async () => {
    let $ = id => document.getElementById(id);
    let wait = ms => new Promise(resolve => setTimeout(resolve, ms));
    let key = (k, target = document.activeElement || document.body) => target.dispatchEvent(new KeyboardEvent('keydown', { key: k, bubbles: true }));
    let results = [];
    let check = (name, ok) => { results.push(ok); console.log('SELFTEST ' + (ok ? 'PASS ' : 'FAIL ') + name); };
    try { localStorage.setItem('ecomcraft-setup-guide-seen', '1'); } catch (e) {}
    await wait(1500);
    check('脚本启动后隐藏启动检查提示', $('boot-check').hidden);
    check('连接状态已加载', $('health-text').textContent.includes('在线'));
    check('账号名已加载', $('user-name').textContent == 'designer');
    $('open-setup-guide').click();
    check('查看配置指引：打开弹窗', !$('setup-guide-modal').hidden);
    check('配置指引：焦点进入弹窗', $('setup-guide-modal').contains(document.activeElement));
    key('Escape');
    check('配置指引：Esc 关闭', $('setup-guide-modal').hidden);
    $('enhancer-settings').click();
    await wait(600);
    check('提示词设置：打开弹窗并读取配置', !$('enhancer-modal').hidden && $('enh-cfg-model').value == 'qwen2.5:7b');
    key('Escape');
    check('提示词设置：Esc 关闭', $('enhancer-modal').hidden);
    document.querySelector('[data-mode="img2img"]').click();
    check('切换图生图：显示初始图上传', !$('reference-fld').hidden && $('workflow-tag').textContent == '图生图');
    document.querySelector('[data-mode="outpaint"]').click();
    let cell = document.querySelector('[data-dir="r"]');
    cell.click();
    check('扩图方向：按下状态', cell.getAttribute('aria-pressed') == 'true');
    document.querySelector('[data-mode="text2img"]').click();
    $('mode-video').click();
    check('切换视频：显示视频参数', !$('video-fields').hidden && $('page-title').textContent == '生成商品视频');
    $('mode-image').click();
    $('add-lora').click();
    let statusBefore = $('result-status').textContent;
    document.querySelector('.lora-row button').click();
    await wait(300);
    check('移除 LoRA：不会触发生成', !document.querySelector('.lora-row') && $('result-status').textContent == statusBefore);
    check('工作流按服务器分组', document.querySelectorAll('#workflow-select optgroup').length >= 2);
    $('workflow-select').value = 'A/白底主图';
    $('workflow-select').dispatchEvent(new Event('change'));
    check('选择原生工作流：隐藏模型选择', $('model-fld').hidden);
    $('workflow-select').value = '';
    $('workflow-select').dispatchEvent(new Event('change'));
    $('prompt').value = '一瓶精华液，浅灰石台';
    $('prompt').dispatchEvent(new Event('input'));
    $('images').value = '2';
    // Headless browsers fast-forward virtual time, so record whether the pending state ever appeared instead of sampling once.
    let pendingSeen = false;
    let observer = new MutationObserver(() => { pendingSeen = pendingSeen || (!$('pending-state').hidden && $('generate-label').textContent == '生成中…'); });
    observer.observe($('pending-state'), { attributes: true });
    $('creation-form').requestSubmit();
    await wait(300);
    pendingSeen = pendingSeen || !$('pending-state').hidden;
    observer.disconnect();
    check('生成中：显示指示灯状态并禁止重复提交', pendingSeen);
    await wait(3200);
    check('生成完成：显示 2 张结果', document.querySelectorAll('.result-card').length == 2 && $('result-status').textContent.includes('已生成 2 张'));
    document.querySelector('.result-open').click();
    check('点击结果：打开大图', !$('lightbox').hidden);
    key('ArrowRight');
    check('大图：方向键切到下一张', $('lightbox-name').textContent.includes('2 / 2'));
    key('Escape');
    check('大图：Esc 关闭', $('lightbox').hidden);
    Array.from(document.querySelectorAll('.result-actions button')).find(b => b.textContent == '在画廊查看').click();
    await wait(1200);
    check('在画廊查看：跳到画廊并打开该作品', location.hash == '#gallery' && !$('lightbox').hidden);
    key('Escape');
    check('画廊计数已显示', !$('gallery-count').hidden && Number($('gallery-count').textContent) > 0);
    $('gallery-search').value = '精华液';
    $('gallery-search').dispatchEvent(new Event('input'));
    check('搜索：显示清除按钮', !$('gallery-search-clear').hidden);
    $('gallery-search-clear').click();
    check('搜索：清除后按钮隐藏', $('gallery-search-clear').hidden && $('gallery-search').value == '');
    await wait(800);
    $('gallery-select').click();
    document.querySelector('.sheet-open').click();
    check('批量选择：选中后可删除', !$('gallery-delete-selected').disabled);
    $('gallery-select-cancel').click();
    check('批量选择：取消后退出', $('gallery-selection').hidden);
    $('theme-toggle').click();
    check('主题：切换到深色', document.documentElement.getAttribute('data-theme') == 'dark');
    $('theme-toggle').click();
    location.hash = 'queue';
    await wait(1000);
    check('队列：显示服务器表格', $('queue-backends').querySelectorAll('tr').length == 3 && !$('queue-count').hidden);
    console.log('SELFTEST DONE ' + results.filter(Boolean).length + '/' + results.length);
})().catch(error => console.log('SELFTEST FAIL 自测脚本异常 ' + error.message));`);
        return;
    }
    if (pathname == '/preview-shot.js') {
        res.writeHead(200, { 'Content-Type': types['.js'], 'Cache-Control': 'no-store' });
        res.end(`(() => {
    let shot = new URL(document.currentScript.src).searchParams.get('shot');
    let click = id => document.getElementById(id)?.click();
    let wait = ms => new Promise(resolve => setTimeout(resolve, ms));
    (async () => {
        try { localStorage.setItem('ecomcraft-setup-guide-seen', '1'); } catch (e) {}
        if (shot.includes('dark')) { document.documentElement.setAttribute('data-theme', 'dark'); }
        await wait(800);
        if (shot.startsWith('result')) {
            document.getElementById('prompt').value = '一瓶透明玻璃精华液，置于浅灰色石台，柔和侧光';
            document.getElementById('images').value = shot.includes('one') ? '1' : '3';
            document.getElementById('creation-form').requestSubmit();
            if (shot.includes('pending')) { return; }
            await wait(3200);
            if (shot.includes('lightbox')) { document.querySelector('.result-open')?.click(); }
        }
        if (shot.startsWith('gallery')) {
            await wait(400);
            if (shot.includes('select')) { click('gallery-select'); document.querySelectorAll('.sheet-open')[1]?.click(); document.querySelectorAll('.sheet-open')[3]?.click(); }
            if (shot.includes('lightbox')) { document.querySelectorAll('.sheet-open')[1]?.click(); }
        }
        if (shot == 'inpaint') { document.querySelector('[data-mode="outpaint"]')?.click(); document.querySelector('[data-dir="r"]')?.click(); }
        if (shot == 'settings') { click('enhancer-settings'); }
    })();
})();`);
        return;
    }
    if (pathname == '/EcomCraft/studio.js' || pathname == '/EcomCraft/studio.css') {
        let file = path.join(studio, path.basename(pathname));
        if (fs.existsSync(file)) {
            res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream', 'Cache-Control': 'no-store' });
            res.end(fs.readFileSync(file));
            return;
        }
    }
    if (pathname.startsWith('/API/') && req.method == 'POST') {
        let chunks = [];
        req.on('data', chunk => chunks.push(chunk));
        req.on('end', () => {
            let body = {};
            try {
                body = JSON.parse(Buffer.concat(chunks).toString('utf8') || '{}');
            }
            catch (error) {
                body = {};
            }
            let result = api(pathname.slice(5), body);
            let delay = result.delay || 120;
            delete result.delay;
            setTimeout(() => {
                res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
                res.end(JSON.stringify(result));
            }, delay);
        });
        return;
    }
    if (pathname.startsWith('/Output/') || pathname.startsWith('/View/')) {
        let src = pathname.startsWith('/View/') ? pathname.split('/').slice(3).join('/') : pathname.slice(8);
        let file = files.find(item => item.src == src);
        if (file && !file.video) {
            res.writeHead(200, { 'Content-Type': 'image/svg+xml', 'Cache-Control': 'no-store' });
            res.end(productSvg(file.hue, src.split('/').pop().replace(/\.\w+$/, '').slice(-6)));
            return;
        }
    }
    res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
    res.end('Not found');
}).listen(port, '127.0.0.1', () => {
    console.log(`EcomCraft Studio preview: http://127.0.0.1:${port}/EcomCraft`);
});
