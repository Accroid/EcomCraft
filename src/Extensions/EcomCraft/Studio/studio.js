(() => {
    'use strict';

    const $ = id => document.getElementById(id);
    const draftFields = ['prompt', 'negative', 'model', 'width', 'height', 'images', 'frames', 'fps', 'steps', 'cfg', 'seed', 'creativity'];
    const makeDraft = kind => ({ prompt: '', negative: '', model: '', width: kind === 'image' ? 1024 : 768,
        height: kind === 'image' ? 1024 : 512, images: 1, frames: 81, fps: '', steps: '', cfg: '', seed: '', creativity: 0.6 });
    const initialNote = '等待首次创作。模型兼容性与实际生成仍需在部署环境验证。';
    const state = {
        kind: 'image', session: null, catalog: null, catalogLoading: false, busy: false, uncertain: false,
        drafts: { image: makeDraft('image'), video: makeDraft('video') },
        results: { image: [], video: [] }, notes: { image: initialNote, video: initialNote },
        reference: null, referenceLoading: false, referenceRevision: 0, timer: null
    };
    const safeErrors = new Set(['bad_permissions', 'cloud_unavailable', 'invalid_model', 'local_unavailable', 'invalid_prompt',
        'unsupported_prompt_syntax', 'invalid_parameters', 'invalid_reference', 'generation_failed', 'generation_uncertain']);
    const uncertainErrors = new Set(['generation_failed', 'generation_uncertain', 'transport_error', 'api_error']);

    class ApiError extends Error {
        constructor(message, code) { super(message); this.code = code; }
    }

    // Generation is deliberately never retried, timed out or silently redirected to another provider.
    async function request(route, body = {}) {
        let response;
        let data;
        try {
            response = await fetch(`/API/${route}`, {
                method: 'POST', credentials: 'same-origin', cache: 'no-store',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ ...body, ...(state.session ? { session_id: state.session } : {}) })
            });
            data = await response.json();
        }
        catch {
            throw new ApiError('与服务的连接中断，无法确认执行结果。请先核对原生队列和个人历史。', 'transport_error');
        }
        if (data.error || !response.ok) {
            if (data.error_id === 'invalid_session_id' || response.status === 401) {
                state.session = null;
                throw new ApiError('账号会话已失效。请刷新模型列表重新连接；如果需要，请先在原生引擎登录。任务不会自动重试。', 'invalid_session_id');
            }
            const code = safeErrors.has(data.error_id) ? data.error_id : 'api_error';
            throw new ApiError(safeErrors.has(code) ? String(data.error) : '服务未正常返回，请检查登录状态或联系管理员。', code);
        }
        return data;
    }

    function status(text, error = false) {
        $('result-status').textContent = text;
        $('result-status').classList.toggle('error', error);
        state.notes[state.kind] = text;
    }

    function currentModel() {
        return state.catalog?.models.find(model => model.id === $('model').value && model.kind === state.kind);
    }

    function saveDraft() {
        for (const field of draftFields) { state.drafts[state.kind][field] = $(field).value; }
    }

    function loadModels(keepSelection = true) {
        const previous = keepSelection ? state.drafts[state.kind].model : '';
        const models = (state.catalog?.models || []).filter(model => model.kind === state.kind);
        $('model').replaceChildren();
        if (!models.length) {
            const option = new Option(state.kind === 'video' ? '没有可访问的文生视频模型' : '没有可访问的图片模型', '');
            $('model').append(option);
        }
        else {
            for (const model of models) { $('model').append(new Option(model.id, model.id)); }
            if (models.some(model => model.id === previous)) { $('model').value = previous; }
        }
        state.drafts[state.kind].model = $('model').value;
        updateModelHelp();
    }

    function updateModelHelp() {
        const model = currentModel();
        const step = model?.resolution_step || 16;
        for (const field of ['width', 'height']) {
            $(field).step = step;
            // HTML number step is based on min, so align min to the model's precision too.
            $(field).min = Math.ceil(256 / step) * step;
        }
        $('resolution-hint').textContent = `256–2048 px · ${step} 的倍数`;
        $('model-help').textContent = model ? `${model.class_name} · 类型来自模型元数据，兼容性待实测。` :
            '请在原生引擎配置模型，或由管理员检查模型类型与账号权限。';
        $('reference-section').hidden = state.kind !== 'image' || !model?.image_reference;
        updateControls();
    }

    function applyModelSize() {
        const model = currentModel();
        if (!model) { return; }
        const step = model.resolution_step || 16;
        for (const field of ['width', 'height']) {
            const fallback = state.kind === 'image' ? 1024 : (field === 'width' ? 768 : 512);
            const suggested = Number(model[field]) || fallback;
            $(field).value = Math.max(Math.ceil(256 / step) * step, Math.min(Math.floor(2048 / step) * step, Math.round(suggested / step) * step));
        }
    }

    function updateControls() {
        const cloud = $('source').value === 'cloud';
        $('cloud-notice').hidden = !cloud;
        $('source-help').textContent = cloud ? '未连接任何云端服务，不会发送提示词或素材。' : '只向部署本服务的机器路由，不会自动转到远程或云端。';
        $('location-badge').textContent = cloud ? '云端未配置' : '部署机执行';
        $('model').disabled = cloud || state.busy;
        $('refresh-models').disabled = state.catalogLoading || state.busy;
        $('creation-fields').disabled = state.busy;
        $('mode-image').disabled = state.busy || state.uncertain;
        $('mode-video').disabled = state.busy || state.uncertain || !state.catalog?.can_generate_video;
        $('frames').disabled = state.kind !== 'video';
        $('fps').disabled = state.kind !== 'video';
        let hint = '';
        if (state.busy) { hint = '任务已发送到原生引擎，请勿刷新或重复提交。'; }
        else if (state.uncertain) { hint = '执行结果待核对，请先在右侧确认后再提交。'; }
        else if (state.catalogLoading) { hint = '正在刷新模型和节点信息…'; }
        else if (cloud) { hint = '云端平台未配置，当前不能提交。'; }
        else if (!state.session || !state.catalog) { hint = '尚未连接服务，请刷新模型列表或先登录。'; }
        else if (!state.catalog.can_generate) { hint = '此账号没有生成权限，请联系管理员。'; }
        else if (state.kind === 'video' && !state.catalog.can_generate_video) { hint = '此账号没有视频参数权限，请联系管理员。'; }
        else if (!state.catalog.local_backends_observed) { hint = '未观测到可用的部署机 ComfyUI 节点。'; }
        else if (!currentModel()) { hint = '请先在原生引擎配置此类型的模型。'; }
        else if (state.referenceLoading) { hint = '正在读取初始图，请稍候。'; }
        $('submit-hint').textContent = hint || '将提交到部署机的 ComfyUI；实际耗时与显存取决于模型。';
        $('generate').disabled = Boolean(hint);
        $('generate-label').textContent = state.busy ? '引擎处理中…' : (state.kind === 'image' ? '生成图片' : '生成视频');
        $('prompt-count').textContent = `${$('prompt').value.length} / 12000`;
        $('uncertain-actions').hidden = !state.uncertain;
    }

    function renderReference() {
        $('upload-zone').hidden = Boolean(state.reference);
        $('reference-preview').hidden = !state.reference;
        $('creativity-field').hidden = !state.reference;
        if (state.reference) {
            $('reference-image').src = state.reference.url;
            $('reference-name').textContent = state.reference.name;
        }
        else {
            $('reference-image').removeAttribute('src');
            $('reference-file').value = '';
        }
    }

    function switchMode(kind) {
        if (kind === state.kind || state.busy || state.uncertain || (kind === 'video' && !state.catalog?.can_generate_video)) { return; }
        saveDraft();
        state.kind = kind;
        for (const field of draftFields) { $(field).value = state.drafts[kind][field]; }
        const video = kind === 'video';
        for (const mode of ['image', 'video']) {
            $(`mode-${mode}`).classList.toggle('selected', mode === kind);
            $(`mode-${mode}`).setAttribute('aria-pressed', String(mode === kind));
        }
        $('workflow-label').textContent = video ? '文生视频' : '文生图 / 图生图';
        $('preview-kind').textContent = video ? 'VIDEO CANVAS' : 'IMAGE CANVAS';
        $('video-fields').hidden = !video;
        $('image-count-row').hidden = video;
        $('preview-stage').classList.toggle('video-mode', video);
        $('empty-title').textContent = video ? '让商品故事，动起来。' : '下一张商品图，从这里开始。';
        $('prompt').placeholder = video ? '例如：镜头缓慢环绕一双白色运动鞋，鞋底细节逐渐进入画面，柔和棚拍灯光，背景保持简洁。' : '例如：一瓶透明玻璃精华液，置于浅灰色石台，柔和侧光，突出瓶身质感，画面右侧留白用于排版。';
        loadModels();
        if (!state.drafts[kind].sizeInitialized && currentModel()) {
            applyModelSize(); state.drafts[kind].sizeInitialized = true;
        }
        renderResults();
        status(state.notes[kind]);
        updateControls();
    }

    async function refreshCatalog() {
        if (state.catalogLoading || state.busy) { return; }
        state.catalogLoading = true;
        saveDraft();
        $('connection-message').textContent = '正在读取账号、模型与部署机节点信息…';
        $('connection-message').classList.remove('error');
        updateControls();
        try {
            if (!state.session) {
                const session = await request('GetNewSession');
                if (!session.session_id) { throw new ApiError('未能建立账号会话，请先登录原生引擎。', 'api_error'); }
                state.session = session.session_id;
            }
            const catalog = await request('EcomCraftStudioCatalog');
            if (!Array.isArray(catalog.models)) { throw new ApiError('模型列表格式不兼容，请联系管理员。', 'api_error'); }
            state.catalog = catalog;
            $('user-name').textContent = catalog.user_name;
            $('avatar').textContent = String(catalog.user_name || 'E').slice(0, 1).toUpperCase();
            $('diagnostics-link').hidden = !catalog.can_diagnose;
            $('connection-message').textContent = `部署机节点观测：${catalog.local_backends_observed} 个可用 · ${catalog.models.length} 个可访问模型 · 云端未配置。此信息不是成功心跳或生成验收。`;
            loadModels();
            if (!state.drafts[state.kind].sizeInitialized && currentModel()) {
                applyModelSize(); state.drafts[state.kind].sizeInitialized = true;
            }
        }
        catch (error) {
            state.catalog = null; // Do not allow generation from stale permissions or node observations.
            loadModels();
            $('connection-message').textContent = error.message;
            $('connection-message').classList.add('error');
            if (!state.session) {
                const login = document.createElement('a');
                login.href = '/Login'; login.textContent = '打开登录页';
                $('connection-message').append(login);
            }
        }
        finally { state.catalogLoading = false; updateControls(); }
    }

    function clearReference() {
        state.referenceRevision++;
        state.referenceLoading = false;
        if (state.reference) { URL.revokeObjectURL(state.reference.url); }
        state.reference = null;
        renderReference(); updateControls();
    }

    async function readReference() {
        const file = $('reference-file').files[0];
        if (!file) { return; }
        const revision = ++state.referenceRevision;
        if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type) || file.size > 5 * 1024 * 1024 || !file.size) {
            $('reference-file').value = '';
            status('请选择不超过 5 MB 的 PNG、JPEG 或 WebP 图片。文件尚未上传。', true);
            return;
        }
        state.referenceLoading = true; updateControls();
        const url = URL.createObjectURL(file);
        try {
            const image = new Image(); image.src = url;
            await image.decode();
            if (image.width > 8192 || image.height > 8192 || image.width * image.height > 40000000) {
                throw new Error('初始图尺寸过大，请先缩小到最长边 8192 像素以内且不超过 4000 万像素。');
            }
            const data = await new Promise((resolve, reject) => {
                const reader = new FileReader(); reader.onload = () => resolve(reader.result);
                reader.onerror = () => reject(new Error('无法读取文件，请重新选择。'));
                reader.readAsDataURL(file);
            });
            if (revision !== state.referenceRevision) { URL.revokeObjectURL(url); return; }
            if (state.reference) { URL.revokeObjectURL(state.reference.url); }
            state.reference = { data, url, name: file.name };
            renderReference();
            status('初始图已选好，尚未上传。提交图片生成时才发送给部署机服务。');
        }
        catch (error) {
            URL.revokeObjectURL(url);
            if (revision === state.referenceRevision) {
                $('reference-file').value = '';
                status(error.message || '无法读取图片，请重新选择。文件尚未上传。', true);
            }
        }
        finally {
            if (revision === state.referenceRevision) { state.referenceLoading = false; updateControls(); }
        }
    }

    function safeMedia(raw) {
        if (typeof raw !== 'string' || !raw) { return null; }
        if (/^data:image\/(png|jpeg|webp|gif);base64,[A-Za-z0-9+/=\r\n]+$/.test(raw)) {
            return { url: raw, video: false };
        }
        try {
            const url = new URL(raw, `${window.location.origin}/`);
            if (url.origin !== window.location.origin || url.username || url.password
                || !/^\/(View|Output)\//.test(url.pathname)) { return null; }
            const path = url.pathname.toLowerCase();
            const video = /\.(mp4|webm)$/.test(path);
            if (!video && !/\.(png|jpe?g|webp|gif|avif)$/.test(path)) { return null; }
            return { url: url.href, video };
        }
        catch { return null; }
    }

    function renderResults() {
        const results = state.results[state.kind];
        $('results').replaceChildren();
        $('results').hidden = !results.length;
        $('results').classList.toggle('multiple', results.length > 1);
        $('empty-state').hidden = Boolean(results.length) || state.busy;
        $('pending-state').hidden = !state.busy;
        for (let index = 0; index < results.length; index++) {
            const media = results[index];
            const card = document.createElement('figure'); card.className = 'result-card';
            const element = document.createElement(media.video ? 'video' : 'img');
            if (media.video) { element.controls = true; element.preload = 'metadata'; element.playsInline = true; }
            else { element.alt = `本次创作结果 ${index + 1}`; element.loading = 'lazy'; }
            element.src = media.url;
            element.addEventListener('error', () => status('引擎返回了地址，但当前无法预览。请在原生引擎核对文件保存、权限或浏览器格式支持。', true));
            const caption = document.createElement('figcaption');
            const label = document.createElement('span'); label.textContent = `${media.video ? '视频' : '画面'} ${String(index + 1).padStart(2, '0')} · 本次结果`;
            const download = document.createElement('a'); download.href = media.url; download.download = '';
            download.textContent = '下载原文件 ↓';
            caption.append(label, download); card.append(element, caption); $('results').append(card);
        }
    }

    function buildPayload() {
        const payload = { source: $('source').value, kind: state.kind, model: $('model').value,
            prompt: $('prompt').value.trim(), negativeprompt: $('negative').value.trim(),
            width: Number($('width').value), height: Number($('height').value), images: state.kind === 'video' ? 1 : Number($('images').value) };
        for (const [field, key] of [['steps', 'steps'], ['cfg', 'cfgscale'], ['seed', 'seed']]) {
            if ($(field).value !== '') { payload[key] = Number($(field).value); }
        }
        if (state.kind === 'video') {
            payload.text2videoframes = Number($('frames').value);
            if ($('fps').value !== '') { payload.videofps = Number($('fps').value); }
        }
        else if (state.reference && currentModel()?.image_reference) {
            payload.initimage = state.reference.data;
            payload.initimagecreativity = Number($('creativity').value);
        }
        return payload;
    }

    async function generate(event) {
        event.preventDefault();
        if (state.busy || state.uncertain || $('generate').disabled || !$('creation-form').reportValidity()) { return; }
        const payload = buildPayload();
        if (!payload.prompt) { status('请先填写创作描述。', true); $('prompt').focus(); return; }
        if (/[<>]|__/.test(payload.prompt + payload.negativeprompt)) {
            status('本版只支持普通描述。原生 <…> 指令、预设或 __通配符__ 请在原生引擎使用。', true); return;
        }
        if (state.results[state.kind].length && !window.confirm('开始新创作会替换当前预览。需要的结果请先下载。继续吗？')) { return; }
        saveDraft();
        state.busy = true;
        state.results[state.kind] = [];
        const started = Date.now();
        $('elapsed').textContent = '已等待 0 秒';
        state.timer = window.setInterval(() => { $('elapsed').textContent = `已等待 ${Math.floor((Date.now() - started) / 1000)} 秒`; }, 1000);
        status('请求已发送，等待引擎返回。当前接口不提供百分比进度。');
        updateControls(); renderResults();
        try {
            const result = await request('EcomCraftStudioGenerate', payload);
            if (!Array.isArray(result.images)) { throw new ApiError('引擎响应格式不兼容，请到原生引擎核对结果。', 'generation_uncertain'); }
            const media = result.images.map(safeMedia).filter(Boolean);
            if (!media.length || media.length !== result.images.length) {
                throw new ApiError('没有完整收到可安全预览的部署机输出地址，请在原生引擎核对个人历史。', 'generation_uncertain');
            }
            state.results[state.kind] = media;
            status(`引擎已返回 ${media.length} 个结果。请核对画面并下载；集中归档尚未接入。`);
        }
        catch (error) {
            state.uncertain = uncertainErrors.has(error.code) || !(error instanceof ApiError);
            status(error.message || '无法确认执行结果，请在原生引擎核对。', true);
            if (error.code === 'invalid_session_id') {
                state.catalog = null;
                $('connection-message').textContent = '会话已失效。请刷新模型列表重新连接；不会自动重试任务。';
                $('connection-message').classList.add('error');
            }
        }
        finally {
            clearInterval(state.timer); state.timer = null; state.busy = false;
            renderResults(); updateControls();
        }
    }

    $('mode-image').addEventListener('click', () => switchMode('image'));
    $('mode-video').addEventListener('click', () => switchMode('video'));
    $('source').addEventListener('change', updateControls);
    $('refresh-models').addEventListener('click', refreshCatalog);
    $('model').addEventListener('change', () => { updateModelHelp(); applyModelSize(); });
    $('prompt').addEventListener('input', updateControls);
    $('reference-file').addEventListener('change', readReference);
    $('remove-reference').addEventListener('click', clearReference);
    $('swap-dimensions').addEventListener('click', () => {
        const width = $('width').value; $('width').value = $('height').value; $('height').value = width;
    });
    $('insert-example').addEventListener('click', () => {
        if ($('prompt').value.trim() && !window.confirm('用示例替换当前描述？')) { return; }
        $('prompt').value = state.kind === 'image' ? '一瓶透明玻璃精华液，置于浅灰色石台，柔和侧光，突出瓶身质感，画面右侧留白用于排版。' : '镜头缓慢环绕一双白色运动鞋，鞋底细节逐渐进入画面，柔和棚拍灯光，背景保持简洁，镜头运动平稳。';
        $('prompt').focus(); updateControls();
    });
    $('unlock-generation').addEventListener('click', () => {
        if (!window.confirm('请确认已在原生引擎核对队列和个人历史。重新提交可能产生第二个任务，确定解锁吗？')) { return; }
        state.uncertain = false; updateControls();
    });
    $('creation-form').addEventListener('submit', generate);
    window.addEventListener('beforeunload', event => {
        if (state.busy || state.uncertain) { event.preventDefault(); event.returnValue = ''; }
    });
    updateControls();
    refreshCatalog();
})();
