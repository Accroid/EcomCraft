(() => {
    'use strict';

    /* ============================================================
       Helpers
       ============================================================ */
    const $ = id => document.getElementById(id);
    const el = (tag, cls, text) => { const e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; };
    const debounce = (fn, ms) => { let t; return (...a) => { clearTimeout(t); t = setTimeout(() => fn(...a), ms); }; };

    class ApiError extends Error { constructor(message, code) { super(message); this.code = code; } }

    const state = {
        session: null, catalog: null, catalogLoading: false, backendStatusLoading: false,
        userId: null, outputAppendUser: false,
        view: 'create', type: 'image', mode: 'text2img',
        workflowId: '', workflowValues: {},
        busy: false, uncertain: false,
        reference: null, referenceLoading: false, referenceRevision: 0,
        loras: [], results: [], timer: null,
        gallery: { items: [], filter: 'all', search: '', sort: 'Date', loading: false, current: null },
        queue: { loading: false, timer: null, stopping: new Set() }
    };

    // Builds a viewable URL for a path returned by ListImages (relative to the user output root).
    function viewUrl(src) {
        const encoded = String(src).split('/').map(seg => encodeURIComponent(seg)).join('/');
        const prefix = state.outputAppendUser ? `View/${encodeURIComponent(state.userId)}` : 'Output';
        return `/${prefix}/${encoded}`;
    }
    const safeErrors = new Set(['bad_permissions','cloud_unavailable','invalid_model','invalid_mode','invalid_workflow','invalid_workflow_parameters',
        'local_unavailable','invalid_prompt','unsupported_prompt_syntax','invalid_parameters','invalid_reference','invalid_mask','invalid_loras','invalid_sampler',
        'invalid_scheduler','generation_failed','generation_uncertain','task_not_found',
        'enhancer_not_configured','enhancer_upstream_error','enhancer_bad_response','enhancer_timeout','enhancer_transport_error']);
    const uncertainErrors = new Set(['generation_failed','generation_uncertain','transport_error','api_error']);

    function magicPromptError(message) {
        const raw = String(message || '');
        const title = raw.split(/\r?\n/).map(line => line.trim()).find(Boolean) || '';
        const detail = raw.includes('Original error:') ? raw.split('Original error:').pop() : title;
        if (/Failed to fetch (vision )?models/i.test(title)) {
            return '拉取模型失败。请检查服务地址、API Key，以及服务是否提供 /v1/models 接口。';
        }
        if (/401|403|unauthorized|forbidden|invalid.api.key|authentication/i.test(detail)
            || /API Key Error/i.test(title)) {
            return '身份验证失败。请检查 API Key 是否正确，以及账号是否有权使用该模型。';
        }
        if (/404|not found|does not exist/i.test(detail) || /Model Not Found/i.test(title)) {
            return '接口或模型不存在。请检查 Base URL 是否重复包含 /v1，并核对模型 ID。';
        }
        if (/429|rate.limit|quota|insufficient.credits/i.test(detail) || /API Usage Limit/i.test(title)) {
            return '服务额度或调用频率已达上限，请检查服务商账号额度后重试。';
        }
        if (/timeout|timed out|request exceeded/i.test(detail) || /Request Timeout/i.test(title)) {
            return '请求超时。请检查模型服务状态、网络连接，或稍后重试。';
        }
        if (/max.tokens|temperature|top.p|unsupported.parameter|invalid.parameter/i.test(detail)
            || /Parameter Error/i.test(title)) {
            return '模型不接受当前请求参数。请确认该模型支持 OpenAI 兼容聊天接口。';
        }
        if (/connection|connect|network|dns|http request/i.test(detail)) {
            return '无法连接模型服务。请检查 Base URL、网络连接和服务状态。';
        }
        if (/Configuration not found|Failed to load settings/i.test(title)) {
            return '提示词优化配置不可用，请重新保存设置后重试。';
        }
        if (/^[\u3400-\u9fff\s\d，。！？：；、（）《》“”‘’.-]+$/.test(raw)) {
            return raw;
        }
        return '提示词优化服务返回错误。请检查服务地址、模型 ID 和服务端日志。';
    }

    async function request(route, body = {}, signal = undefined) {
        let response, data;
        const isMagicPromptRoute = /MagicPrompt/.test(route) || route === 'SetAPIKey' || route === 'GetAPIKeyStatus';
        try {
            response = await fetch(`/API/${route}`, {
                method: 'POST', credentials: 'same-origin', cache: 'no-store',
                signal,
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ ...body, ...(state.session ? { session_id: state.session } : {}) })
            });
            data = await response.json();
        } catch (error) {
            if (signal?.aborted) {
                throw error;
            }
            const message = isMagicPromptRoute
                ? '无法连接提示词优化服务，请检查网络和服务状态。'
                : '与服务的连接中断，无法确认执行结果。请先核对原生队列和个人历史。';
            throw new ApiError(message, 'transport_error');
        }
        if (data.error || !response.ok) {
            if (data.error_id === 'invalid_session_id' || response.status === 401) {
                state.session = null;
                throw new ApiError('账号会话已失效。请刷新重新连接；任务不会自动重试。', 'invalid_session_id');
            }
            // Business-level error returned normally (HTTP 200) with a user-facing message but no
            // whitelisted error_id (e.g. MagicPrompt's formatted upstream errors): pass it through.
            if (response.ok && data.error_id == null) {
                const message = isMagicPromptRoute ? magicPromptError(data.error) : String(data.error);
                throw new ApiError(message, 'api_error');
            }
            const code = safeErrors.has(data.error_id) ? data.error_id : 'api_error';
            throw new ApiError(safeErrors.has(code) ? String(data.error) : '服务未正常返回，请检查登录状态或联系管理员。', code);
        }
        return data;
    }

    /* ============================================================
       Theme
       ============================================================ */
    function initTheme() {
        const saved = localStorage.getItem('ecomcraft-theme');
        const theme = saved === 'dark' || saved === 'light' ? saved
            : (window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
        document.documentElement.setAttribute('data-theme', theme);
    }
    function toggleTheme() {
        const next = document.documentElement.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
        document.documentElement.setAttribute('data-theme', next);
        localStorage.setItem('ecomcraft-theme', next);
    }

    /* ============================================================
       View routing
       ============================================================ */
    function setView(view) {
        if (!['create', 'gallery', 'queue'].includes(view)) { view = 'create'; }
        state.view = view;
        $('view-create').hidden = view !== 'create';
        $('view-gallery').hidden = view !== 'gallery';
        $('view-queue').hidden = view !== 'queue';
        $('crumb-title').textContent = view === 'create' ? '创作工作台' : view === 'gallery' ? '作品画廊' : '任务队列';
        document.querySelectorAll('.rail-item[data-view]').forEach(i =>
            i.classList.toggle('active', i.dataset.view === view));
        if (view === 'gallery') { loadGallery(); }
        if (view === 'queue') { loadQueue(); }
    }
    function route() {
        const hash = (location.hash || '').replace('#', '');
        setView(['gallery', 'queue'].includes(hash) ? hash : 'create');
    }

    /** Opens the setup guide and records that the first-visit prompt was dismissed. */
    function openSetupGuide() {
        let modal = $('setup-guide-modal');
        if (!state.catalog) {
            $('setup-status').textContent = '正在读取配置状态…';
        }
        else {
            updateSetupGuideStatus();
        }
        modal.hidden = false;
        localStorage.setItem('ecomcraft-setup-guide-seen', '1');
    }

    function closeSetupGuide() {
        $('setup-guide-modal').hidden = true;
    }

    function updateSetupGuideStatus() {
        let catalog = state.catalog;
        if (!catalog) { return; }
        let comfyReady = Number(catalog.local_backends_observed || 0) > 0;
        let cloudReady = !!catalog.cloud_configured;
        $('setup-status').textContent = comfyReady && cloudReady
            ? '本机 ComfyUI 和云端模型均已配置。'
            : comfyReady ? '本机 ComfyUI 已就绪，云端提示词模型尚未配置。'
            : cloudReady ? '云端提示词模型已配置，本机 ComfyUI 尚未就绪。'
            : '尚未检测到可用配置，请按下面步骤完成设置。';
        $('setup-status').classList.toggle('is-ready', comfyReady && cloudReady);
        $('setup-comfy-step').classList.toggle('is-ready', comfyReady);
        $('setup-cloud-step').classList.toggle('is-ready', cloudReady);
        $('setup-cloud-settings').hidden = !catalog.can_manage_settings;
    }

    function maybeOpenSetupGuide() {
        if (!state.catalog || localStorage.getItem('ecomcraft-setup-guide-seen') === '1') { return; }
        if (!state.catalog.local_backends_observed || !state.catalog.cloud_configured) { openSetupGuide(); }
    }

    /* ============================================================
       Catalog / models
       ============================================================ */
    function currentModel() {
        return state.catalog?.models.find(m => m.id === $('model').value && m.kind === state.type);
    }

    function populateModels() {
        const models = (state.catalog?.models || []).filter(m => m.kind === state.type);
        const select = $('model');
        const previous = select.value;
        select.replaceChildren();
        if (!models.length) {
            select.append(new Option(state.type === 'video' ? '没有可访问的文生视频模型' : '没有可访问的图片模型', ''));
        } else {
            for (const m of models) { select.append(new Option(m.id, m.id)); }
            if (models.some(m => m.id === previous)) { select.value = previous; }
        }
        updateModelMeta();
    }

    function populateSamplers() {
        const samplers = state.catalog?.samplers || [];
        const schedulers = state.catalog?.schedulers || [];
        const previousSampler = $('sampler').value;
        const previousScheduler = $('scheduler').value;
        $('sampler').replaceChildren(new Option('默认（由模型决定）', ''));
        $('scheduler').replaceChildren(new Option('默认（由模型决定）', ''));
        for (const s of samplers) { $('sampler').append(new Option(s.name, s.id)); }
        for (const s of schedulers) { $('scheduler').append(new Option(s.name, s.id)); }
        if (currentWorkflow()?.native) {
            NativeWorkflowForm.setValue($('sampler'), previousSampler);
            NativeWorkflowForm.setValue($('scheduler'), previousScheduler);
        }
        if (samplers.some(s => s.id === previousSampler)) { $('sampler').value = previousSampler; }
        if (schedulers.some(s => s.id === previousScheduler)) { $('scheduler').value = previousScheduler; }
    }

    /** Keeps native workflow defaults separate from edits and from standard-mode form values. */
    class NativeWorkflowForm {
        static selection = '';
        static previous = null;
        static promptsInitialized = false;
        static controls = { prompt: 'prompt', negativeprompt: 'negative', width: 'width', height: 'height',
            images: 'images', steps: 'steps', cfgscale: 'cfg', seed: 'seed', sampler: 'sampler', scheduler: 'scheduler' };

        /** Initializes empty prompts once; later selections and catalog polling preserve the current text. */
        static sync(workflow) {
            let native = Boolean(workflow?.native);
            let selection = native ? workflow.id : '';
            if (selection != this.selection) {
                if (native) {
                    if (!this.previous) {
                        this.previous = {};
                        for (let key of Object.keys(this.controls)) {
                            this.previous[key] = $(this.controls[key]).value;
                        }
                    }
                    for (let key of Object.keys(this.controls)) {
                        let control = $(this.controls[key]);
                        if (['prompt', 'negativeprompt'].includes(key) && (this.promptsInitialized || control.value != '')) {
                            continue;
                        }
                        let value = workflow.defaults?.[key] ?? (key == 'images' ? 1 : ['width', 'height'].includes(key) ? 512 : '');
                        this.setValue(control, value);
                    }
                    this.promptsInitialized = true;
                }
                else if (this.previous) {
                    for (let key of Object.keys(this.controls)) {
                        if (['prompt', 'negativeprompt'].includes(key)) {
                            continue;
                        }
                        this.setValue($(this.controls[key]), this.previous[key]);
                    }
                    this.previous = null;
                }
                this.selection = selection;
            }
            $('model-fld').hidden = native;
            $('model').required = !native;
            $('seed').max = native ? '9007199254740991' : '4294967295';
            for (let key of ['steps', 'cfgscale', 'seed', 'sampler', 'scheduler']) {
                let control = $(this.controls[key]);
                control.closest('.num-fld').hidden = native && workflow.defaults?.[key] == null;
            }
            if (native) {
                for (let key of ['width', 'height']) {
                    $(key).step = 8;
                }
                $('resolution-hint').textContent = '来自工作流 · 修改后传入 ComfyUI';
            }
        }

        /** Retains saved sampler/scheduler names even when the currently running backend lacks them. */
        static setValue(control, value) {
            if (control.tagName == 'SELECT' && value != '' && !Array.from(control.options).some(option => option.value == String(value))) {
                control.append(new Option(String(value), String(value)));
            }
            control.value = value;
        }
    }

    function currentWorkflow() {
        return (state.catalog?.workflows || []).find(workflow => workflow.id === state.workflowId && (!workflow.kind || workflow.kind === state.type)) || null;
    }

    function populateWorkflows() {
        const select = $('workflow-select');
        const previous = state.workflowId;
        const workflows = (state.catalog?.workflows || []).filter(workflow => !workflow.kind || workflow.kind === state.type);
        select.replaceChildren(new Option('标准工作台（当前模式）', ''));
        for (const workflow of workflows) {
            select.append(new Option(workflow.name || workflow.id, workflow.id));
        }
        state.workflowId = workflows.some(workflow => workflow.id === previous) ? previous : '';
        select.value = state.workflowId;
        if (state.workflowId !== previous) { state.workflowValues = {}; }
        renderWorkflowFields();
        updateWorkflowUi();
    }

    function selectWorkflow() {
        const next = $('workflow-select').value;
        if (next !== state.workflowId) { state.workflowValues = {}; }
        state.workflowId = next;
        renderWorkflowFields();
        updateWorkflowUi();
        updateControls();
    }

    function updateWorkflowUi() {
        const workflow = currentWorkflow();
        const selected = Boolean(workflow);
        NativeWorkflowForm.sync(workflow);
        $('image-mode-tabs').style.display = selected || state.type === 'video' ? 'none' : '';
        $('workflow-description').textContent = selected
            ? `${workflow.description || '使用管理员登记的工作流模板与参数。'} 创作方式由工作流节点决定。使用标准图生图、局部重绘等模式请切回标准工作台；工作流处理初始图或蒙版需要发布包含对应输入和节点的工作流。`
            : ((state.catalog?.workflows || []).length
                ? '已合并相同的原生工作流，并过滤当前本机缺少模型或节点的项。列表每 30 秒更新，也可点击上方“刷新”。'
                : '暂无通过本机可用性检查的工作流。请等待本机后端就绪，确认工作流已保存、所需模型和节点已安装，再点击“刷新”。');
        $('workflow-params').hidden = !selected || !(workflow.inputs || []).length;
        const meta = modeMeta[state.mode] || modeMeta.text2img;
        $('reference-fld').hidden = selected || !meta.needsRef;
        $('mask-wrap').hidden = selected || !meta.mask || state.mode === 'outpaint';
        $('creativity-fld').hidden = selected || !meta.creativity;
        $('outpaint-fld').hidden = selected || !meta.outpaint;
        $('upscale-fld').hidden = selected || !meta.upscale;
        $('lora-fld').hidden = selected;
        $('workflow-tag').textContent = selected ? (workflow.name || '自定义工作流') : meta.tag;
    }

    function workflowDefaultValue(field) {
        return field && field.default !== undefined && field.default !== null ? field.default : '';
    }

    function workflowInputValue(field) {
        if (Object.prototype.hasOwnProperty.call(state.workflowValues, field.id)) {
            return state.workflowValues[field.id];
        }
        return workflowDefaultValue(field);
    }

    function setWorkflowValue(id, value) {
        state.workflowValues[id] = value;
        updateControls();
    }

    function renderWorkflowFields() {
        const container = $('workflow-params');
        container.replaceChildren();
        const workflow = currentWorkflow();
        if (!workflow) {
            container.hidden = true;
            return;
        }
        const fields = Array.isArray(workflow.inputs) ? workflow.inputs : [];
        container.hidden = !fields.length;
        let index = 0;
        for (const field of fields) {
            if (!field || !field.id || (workflow.native && field.form)) { continue; }
            const wrapper = el('div', 'fld');
            const labelId = `workflow-field-${index++}`;
            const label = el('label', null, field.name || field.id);
            label.htmlFor = labelId;
            const head = el('div', 'fld-head');
            head.append(label);
            wrapper.append(head);
            const type = String(field.type || 'text').toLowerCase();
            const initial = workflowInputValue(field);
            let control;
            if (type === 'dropdown' || (type === 'model' && Array.isArray(field.values) && field.values.length)) {
                control = document.createElement('select');
                const values = Array.isArray(field.values) ? field.values : [];
                for (const option of values) {
                    const raw = String(option ?? '');
                    const split = raw.indexOf('///');
                    const value = split >= 0 ? raw.slice(0, split) : raw;
                    const title = split >= 0 ? raw.slice(split + 3) : raw;
                    control.append(new Option(title, value));
                }
                if (initial !== '') {
                    const rawInitial = String(initial);
                    const found = [...control.options].some(option => option.value === rawInitial || option.textContent === rawInitial);
                    if (found) { control.value = rawInitial; }
                }
                if (control.selectedIndex < 0 && control.options.length) { control.selectedIndex = 0; }
                control.addEventListener('change', () => setWorkflowValue(field.id, control.value));
            }
            else if (type === 'boolean') {
                control = document.createElement('input');
                control.type = 'checkbox';
                control.checked = initial === true || String(initial).toLowerCase() === 'true';
                control.addEventListener('change', () => setWorkflowValue(field.id, control.checked));
            }
            else if (type === 'image') {
                control = document.createElement('input');
                control.type = 'file';
                control.accept = 'image/png,image/jpeg,image/webp';
                control.addEventListener('change', () => {
                    const file = control.files?.[0];
                    if (!file) { return; }
                    if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type) || !file.size || file.size > 5 * 1024 * 1024) {
                        control.value = '';
                        setStatus(`“${field.name || field.id}”需要不超过 5 MB 的 PNG、JPEG 或 WebP 图片。`, true);
                        return;
                    }
                    const reader = new FileReader();
                    reader.onload = () => {
                        if (typeof reader.result === 'string') {
                            setWorkflowValue(field.id, reader.result);
                            setStatus(`已读取工作流图片参数“${field.name || field.id}”，提交生成时才发送。`);
                        }
                    };
                    reader.onerror = () => setStatus(`无法读取工作流图片参数“${field.name || field.id}”。`, true);
                    reader.readAsDataURL(file);
                });
                if (initial && String(initial).startsWith('data:image/')) {
                    wrapper.append(el('p', 'hint', '此工作流有默认图片；选择新文件即可覆盖。'));
                }
            }
            else if (type === 'integer' || type === 'decimal') {
                control = document.createElement('input');
                control.type = 'number';
                if (field.min !== undefined && field.min !== null) { control.min = field.min; }
                if (field.max !== undefined && field.max !== null && Number(field.max) !== 0) { control.max = field.max; }
                if (field.step !== undefined && field.step !== null && Number(field.step) > 0) { control.step = field.step; }
                control.value = initial === null || initial === undefined ? '' : initial;
                control.addEventListener('change', () => {
                    if (control.value !== '') { setWorkflowValue(field.id, control.value); }
                });
            }
            else {
                control = type === 'text' && String(initial ?? '').length > 100 ? document.createElement('textarea') : document.createElement('input');
                if (control.tagName === 'INPUT') { control.type = 'text'; }
                control.value = initial === null || initial === undefined ? '' : (typeof initial === 'object' ? JSON.stringify(initial) : String(initial));
                control.maxLength = type === 'list' ? 4000 : 12000;
                control.addEventListener('input', () => setWorkflowValue(field.id, control.value));
            }
            control.id = labelId;
            wrapper.append(control);
            if (field.description) { wrapper.append(el('p', 'hint', field.description)); }
            container.append(wrapper);
        }
    }

    function updateModelMeta() {
        const model = currentModel();
        const step = currentWorkflow()?.native ? 8 : model?.resolution_step || 16;
        for (const field of ['width', 'height']) {
            $(field).step = step;
            $(field).min = Math.ceil(256 / step) * step;
        }
        $('resolution-hint').textContent = `256–2048 px · ${step} 的倍数`;
        $('model-help').textContent = model ? `${model.class_name} · 类型来自模型元数据，兼容性待实测。`
            : '请在原生引擎配置模型，或由管理员检查模型类型与账号权限。';
        updateControls();
    }

    function applyModelSize() {
        const model = currentModel();
        if (!model || currentWorkflow()?.native) { return; }
        const step = model.resolution_step || 16;
        for (const field of ['width', 'height']) {
            const fallback = state.type === 'image' ? 1024 : (field === 'width' ? 768 : 512);
            const suggested = Number(model[field]) || fallback;
            $(field).value = Math.max(Math.ceil(256 / step) * step,
                Math.min(Math.floor(2048 / step) * step, Math.round(suggested / step) * step));
        }
    }

    /** Reloads authorized models and workflows while preserving the current selections when available. */
    async function refreshCatalog() {
        if (state.catalogLoading || state.busy) { return; }
        state.catalogLoading = true;
        const msg = $('connection-message-text');
        const previousModel = $('model').value;
        msg.textContent = '正在读取账号、工作流、模型与部署机节点信息…';
        $('connection-message').classList.remove('error');
        updateControls();
        try {
            if (!state.session) {
                const session = await request('GetNewSession');
                if (!session.session_id) { throw new ApiError('未能建立账号会话，请先登录原生引擎。', 'api_error'); }
                state.session = session.session_id;
                state.userId = session.user_id;
                state.outputAppendUser = !!session.output_append_user;
            }
            const catalog = await request('EcomCraftStudioCatalog');
            if (!Array.isArray(catalog.models)) { throw new ApiError('模型列表格式不兼容，请联系管理员。', 'api_error'); }
            catalog.workflows = Array.isArray(catalog.workflows) ? catalog.workflows : [];
            state.catalog = catalog;
            populateWorkflows();
            $('user-name').textContent = catalog.user_name;
            $('avatar').textContent = String(catalog.user_name || 'E').slice(0, 1).toUpperCase();
            $('enhancer-settings').hidden = !catalog.can_manage_settings;
            const backendState = catalog.local_backends_observed > 0
                ? `${catalog.local_backends_observed} 个可用`
                : catalog.local_backends_starting > 0 ? '正在启动'
                : catalog.local_backends_errored > 0 ? '启动失败，请检查服务器日志'
                : '未配置可用节点';
            msg.textContent = `本机 ComfyUI 后端：${backendState} · 标准工作台模型：${catalog.models.length} 个 · 工作流使用自身模型配置`;
            populateModels();
            populateSamplers();
            if ($('model').value !== previousModel) { applyModelSize(); }
            updateHealth();
            updateSetupGuideStatus();
            maybeOpenSetupGuide();
        } catch (error) {
            state.catalog = null;
            state.workflowId = '';
            state.workflowValues = {};
            populateWorkflows();
            populateModels();
            msg.textContent = error.message;
            $('connection-message').classList.add('error');
            msg.append(Object.assign(document.createElement('a'), { href: '/Login', textContent: '打开登录页' }));
            updateHealth();
        } finally {
            state.catalogLoading = false;
            updateControls();
        }
    }

    /** Keeps the displayed ComfyUI availability current while the Studio remains open. */
    async function pollBackendStatus() {
        if (document.hidden || state.backendStatusLoading || state.catalogLoading || state.busy) { return; }
        state.backendStatusLoading = true;
        try {
            if (!state.session || !state.catalog) {
                await refreshCatalog();
                return;
            }
            let status;
            try {
                status = await request('EcomCraftStudioBackendStatus');
            } catch (error) {
                if (!['api_error', 'transport_error'].includes(error.code)) { throw error; }
                status = await request('EcomCraftStudioCatalog');
            }
            if (state.catalogLoading || state.busy) { return; }
            const previous = state.catalog.local_backends_observed || 0;
            state.catalog.local_backends_observed = status.local_backends_observed;
            state.catalog.local_backends_starting = status.local_backends_starting;
            state.catalog.local_backends_errored = status.local_backends_errored;
            updateHealth();
            updateControls();
            $('connection-message').classList.remove('error');
            if (previous !== status.local_backends_observed || !status.local_backends_observed) {
                const backendState = status.local_backends_observed > 0 ? `${status.local_backends_observed} 个可用`
                    : status.local_backends_starting > 0 ? '正在启动'
                    : status.local_backends_errored > 0 ? '启动失败，请检查服务器日志' : '未配置可用节点';
                $('connection-message-text').textContent = `本机 ComfyUI 后端：${backendState} · 标准工作台模型：${state.catalog.models.length} 个 · 工作流使用自身模型配置`;
            }
            if (previous == 0 && status.local_backends_observed > 0) {
                await refreshCatalog();
            }
        } catch (error) {
            if (state.catalog) { state.catalog.local_backends_observed = 0; }
            updateHealth();
            updateControls();
            $('connection-message-text').textContent = error.message;
            $('connection-message').classList.add('error');
        } finally {
            state.backendStatusLoading = false;
        }
    }

    /** Refreshes discovered workflows without rebuilding other form controls or discarding edits. */
    class WorkflowCatalogRefresh {
        static loading = false;

        /** Polls only while visible and idle; unchanged catalogs leave current parameter fields intact. */
        static async refresh() {
            if (document.hidden || this.loading || state.catalogLoading || state.busy || !state.catalog || !state.session) {
                return;
            }
            this.loading = true;
            let catalogBefore = state.catalog;
            try {
                let catalog = await request('EcomCraftStudioCatalog');
                if (state.busy || state.catalogLoading || state.catalog != catalogBefore || !Array.isArray(catalog.workflows)) {
                    return;
                }
                if (JSON.stringify(catalog.workflows) != JSON.stringify(state.catalog.workflows)) {
                    state.catalog.workflows = catalog.workflows;
                    populateWorkflows();
                    updateControls();
                }
            }
            catch (error) {
                if (!state.busy && state.catalog == catalogBefore) {
                    $('workflow-description').textContent = '工作流自动刷新失败，可点击“刷新”重试。';
                }
            }
            finally {
                this.loading = false;
            }
        }
    }

    function updateHealth() {
        const h = $('health');
        const dot = $('health-dot');
        const text = $('health-text');
        h.classList.remove('ok', 'warn', 'err');
        const count = state.catalog?.local_backends_observed || 0;
        if (state.catalog && count > 0) { h.classList.add('ok'); text.textContent = `${count} 个节点可用`; }
        else if (state.catalog) {
            h.classList.add('warn');
            text.textContent = state.catalog.local_backends_starting > 0 ? '节点正在启动'
                : state.catalog.local_backends_errored > 0 ? '节点启动失败' : '无可用节点';
        }
        else { h.classList.add('err'); text.textContent = '未连接'; }
        updateSetupGuideStatus();
    }

    /* ============================================================
       Type (image/video) & create mode
       ============================================================ */
    function setType(type) {
        if (type === state.type || state.busy || state.uncertain) { return; }
        if (type === 'video' && !state.catalog?.can_generate_video) { return; }
        state.type = type;
        $('mode-image').classList.toggle('selected', type === 'image');
        $('mode-video').classList.toggle('selected', type === 'video');
        $('mode-image').setAttribute('aria-pressed', String(type === 'image'));
        $('mode-video').setAttribute('aria-pressed', String(type === 'video'));
        $('video-fields').hidden = type !== 'video';
        $('image-count-row').hidden = type === 'video';
        $('image-mode-tabs').style.display = type === 'video' ? 'none' : '';
        if (type === 'video') { setMode('text2img'); }
        else if (state.mode === 'video') { setMode('text2img'); }
        $('empty-title').textContent = type === 'video' ? '让商品故事，动起来。' : '下一张商品图，从这里开始。';
        $('empty-desc').textContent = type === 'video' ? '描述镜头与运动，生成一段商品视频。' : '填写描述、选择模型，结果会出现在这里。';
        $('prompt').placeholder = type === 'video'
            ? '例如：镜头缓慢环绕一双白色运动鞋，鞋底细节逐渐进入画面，柔和棚拍灯光，背景保持简洁。'
            : '例如：一瓶透明玻璃精华液，置于浅灰色石台，柔和侧光，突出瓶身质感，画面右侧留白用于排版。';
        populateModels();
        applyModelSize();
        populateWorkflows();
        renderResults();
        updateWorkflowUi();
    }

    const modeMeta = {
        text2img: { tag: '文生图', needsRef: false, mask: false, creativity: false, outpaint: false, upscale: false },
        img2img:  { tag: '图生图', needsRef: true,  mask: false, creativity: true,  outpaint: false, upscale: false },
        inpaint:  { tag: '局部重绘', needsRef: true, mask: true,  creativity: true,  outpaint: false, upscale: false },
        outpaint: { tag: '扩图', needsRef: true,   mask: true,  creativity: false,  outpaint: true,  upscale: false },
        upscale:  { tag: '放大', needsRef: true,   mask: false, creativity: false,  outpaint: false, upscale: true }
    };

    function setMode(mode) {
        if (!modeMeta[mode] || state.busy || state.uncertain) { return; }
        state.mode = mode;
        const meta = modeMeta[mode];
        document.querySelectorAll('.mode-tab').forEach(t =>
            t.classList.toggle('active', t.dataset.mode === mode));
        $('workflow-tag').textContent = meta.tag;
        // Reference block
        $('reference-fld').hidden = !meta.needsRef;
        $('ref-optional').textContent = meta.tag;
        $('mask-wrap').hidden = !meta.mask || mode === 'outpaint';
        $('creativity-fld').hidden = !meta.creativity;
        $('outpaint-fld').hidden = !meta.outpaint;
        $('upscale-fld').hidden = !meta.upscale;
        $('reference-help').textContent = mode === 'inpaint'
            ? '涂抹需要修改的区域，其余部分保持不变。'
            : mode === 'upscale' ? '放大后会用模型重新精炼细节。'
            : mode === 'outpaint' ? '向指定方向扩展画面，扩展区域由 AI 补全。'
            : '图生图会重绘原图，不保证商品细节、文字或标志完全不变。';
        if (meta.mask && mode === 'inpaint' && state.reference) { MaskEditor.init(); }
        if (meta.outpaint) { /* mask generated at submit */ }
        updateWorkflowUi();
        updateControls();
    }

    /* ============================================================
       Reference image
       ============================================================ */
    function renderReference() {
        $('dropzone').style.display = state.reference ? 'none' : '';
        $('reference-preview').hidden = !state.reference;
        if (state.reference) {
            $('reference-image').src = state.reference.url;
            $('reference-name').textContent = state.reference.name;
        } else {
            $('reference-image').removeAttribute('src');
            $('reference-file').value = '';
        }
    }

    function clearReference() {
        state.referenceRevision++;
        state.referenceLoading = false;
        if (state.reference) { URL.revokeObjectURL(state.reference.url); }
        state.reference = null;
        MaskEditor.reset();
        renderReference();
        updateControls();
    }

    async function readReference() {
        const file = $('reference-file').files[0];
        if (!file) { return; }
        const revision = ++state.referenceRevision;
        if (!['image/png','image/jpeg','image/webp'].includes(file.type) || !file.size || file.size > 5 * 1024 * 1024) {
            $('reference-file').value = '';
            setStatus('请选择不超过 5 MB 的 PNG、JPEG 或 WebP 图片。文件尚未上传。', true);
            return;
        }
        state.referenceLoading = true;
        updateControls();
        const objectUrl = URL.createObjectURL(file);
        try {
            const image = new Image(); image.src = objectUrl;
            await image.decode();
            if (image.width > 8192 || image.height > 8192 || image.width * image.height > 40000000) {
                throw new Error('初始图尺寸过大，请先缩小到最长边 8192 像素以内且不超过 4000 万像素。');
            }
            const data = await new Promise((resolve, reject) => {
                const reader = new FileReader();
                reader.onload = () => resolve(reader.result);
                reader.onerror = () => reject(new Error('无法读取文件，请重新选择。'));
                reader.readAsDataURL(file);
            });
            if (revision !== state.referenceRevision) { URL.revokeObjectURL(objectUrl); return; }
            if (state.reference) { URL.revokeObjectURL(state.reference.url); }
            state.reference = { data, url: objectUrl, name: file.name, width: image.width, height: image.height };
            renderReference();
            setStatus('初始图已选好，提交生成时才发送给服务。');
            if (state.mode === 'inpaint') { MaskEditor.init(); }
            if (state.mode === 'upscale') { syncUpscaleSize(); }
        } catch (error) {
            URL.revokeObjectURL(objectUrl);
            if (revision === state.referenceRevision) {
                $('reference-file').value = '';
                setStatus(error.message || '无法读取图片，请重新选择。文件尚未上传。', true);
            }
        } finally {
            if (revision === state.referenceRevision) { state.referenceLoading = false; updateControls(); }
        }
    }

    /* ============================================================
       Mask editor (inpaint)
       ============================================================ */
    const MaskEditor = {
        canvas: null, ctx: null, mask: null, mctx: null, history: [], drawing: false, last: null,
        reset() {
            this.canvas = null; this.ctx = null; this.mask = null; this.mctx = null;
            this.history = []; this.drawing = false; this.last = null;
        },
        init() {
            const ref = state.reference;
            if (!ref) { return; }
            const canvas = $('mask-canvas');
            canvas.width = ref.width;
            canvas.height = ref.height;
            this.canvas = canvas;
            this.ctx = canvas.getContext('2d');
            this.mask = document.createElement('canvas');
            this.mask.width = ref.width;
            this.mask.height = ref.height;
            this.mctx = this.mask.getContext('2d');
            this.history = [];
            this.redraw();
        },
        pushHistory() {
            this.history.push(this.mctx.getImageData(0, 0, this.mask.width, this.mask.height));
            if (this.history.length > 20) { this.history.shift(); }
        },
        undo() {
            if (!this.history.length) { return; }
            const item = this.history.pop();
            this.mctx.putImageData(item, 0, 0);
            this.redraw();
            updateControls();
        },
        clear() {
            if (!this.mctx) { return; }
            this.pushHistory();
            this.mctx.clearRect(0, 0, this.mask.width, this.mask.height);
            this.redraw();
            updateControls();
        },
        redraw() {
            const img = new Image();
            img.onload = () => {
                this.ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);
                this.ctx.drawImage(img, 0, 0);
                this.ctx.save();
                this.ctx.globalAlpha = 0.5;
                this.ctx.drawImage(this.mask, 0, 0);
                this.ctx.restore();
            };
            img.src = state.reference.url;
        },
        pos(e) {
            const rect = this.canvas.getBoundingClientRect();
            const sx = this.canvas.width / rect.width;
            const sy = this.canvas.height / rect.height;
            return { x: (e.clientX - rect.left) * sx, y: (e.clientY - rect.top) * sy, sx, sy };
        },
        start(e) {
            if (!this.mctx) { return; }
            e.preventDefault();
            this.pushHistory();
            this.drawing = true;
            this.last = this.pos(e);
            this.stroke(this.last, this.last);
        },
        move(e) {
            if (!this.drawing) { return; }
            e.preventDefault();
            const p = this.pos(e);
            this.stroke(this.last, p);
            this.last = p;
        },
        end() {
            if (!this.drawing) { return; }
            this.drawing = false;
            updateControls();
        },
        stroke(a, b) {
            const scale = a.sx;
            const size = Number($('brush-size').value) * scale;
            this.mctx.lineCap = 'round';
            this.mctx.lineJoin = 'round';
            this.mctx.lineWidth = size;
            this.mctx.strokeStyle = '#ffffff';
            this.mctx.beginPath();
            this.mctx.moveTo(a.x, a.y);
            this.mctx.lineTo(b.x, b.y);
            this.mctx.stroke();
            // also draw a dot so single clicks register
            this.mctx.beginPath();
            this.mctx.fillStyle = '#ffffff';
            this.mctx.arc(b.x, b.y, size / 2, 0, Math.PI * 2);
            this.mctx.fill();
            this.ctx.save();
            this.ctx.globalAlpha = 0.5;
            this.ctx.drawImage(this.mask, 0, 0);
            this.ctx.restore();
        },
        hasMask() {
            if (!this.mctx) { return false; }
            const data = this.mctx.getImageData(0, 0, this.mask.width, this.mask.height).data;
            for (let i = 3; i < data.length; i += 4) { if (data[i] > 0) { return true; } }
            return false;
        },
        dataURL() { return this.mask.toDataURL('image/png'); }
    };

    /* ============================================================
       Outpaint (build composite + mask at submit)
       ============================================================ */
    const outpaintDirs = new Set();
    function toggleOutpaintDir(btn) {
        const dir = btn.dataset.dir;
        if (outpaintDirs.has(dir)) { outpaintDirs.delete(dir); btn.classList.remove('on'); }
        else { outpaintDirs.add(dir); btn.classList.add('on'); }
        updateControls();
    }
    function outpaintTarget() {
        const ref = state.reference;
        const amount = Number($('outpaint-amount').value) || 0;
        const left = outpaintDirs.has('l') || outpaintDirs.has('tl') || outpaintDirs.has('bl') ? amount : 0;
        const right = outpaintDirs.has('r') || outpaintDirs.has('tr') || outpaintDirs.has('br') ? amount : 0;
        const top = outpaintDirs.has('t') || outpaintDirs.has('tl') || outpaintDirs.has('tr') ? amount : 0;
        const bottom = outpaintDirs.has('b') || outpaintDirs.has('bl') || outpaintDirs.has('br') ? amount : 0;
        return {
            newW: ref.width + left + right,
            newH: ref.height + top + bottom,
            left, top, right, bottom
        };
    }
    // Build a stretched-edge composite and an extension mask.
    function buildOutpaint() {
        const ref = state.reference;
        const t = outpaintTarget();
        const comp = document.createElement('canvas');
        comp.width = t.newW; comp.height = t.newH;
        const cctx = comp.getContext('2d');
        const maskC = document.createElement('canvas');
        maskC.width = t.newW; maskC.height = t.newH;
        const mctx = maskC.getContext('2d');
        // Fill new canvas with edge-stretched pixels (a simple heuristic), then draw original.
        const img = new Image();
        return new Promise((resolve, reject) => {
            img.onload = () => {
                cctx.fillStyle = '#888';
                cctx.fillRect(0, 0, t.newW, t.newH);
                // stretch each edge band
                cctx.drawImage(img, 0, 0, img.width, 1, 0, 0, t.newW, t.top); // top
                cctx.drawImage(img, 0, img.height - 1, img.width, 1, 0, t.newH - t.bottom, t.newW, t.bottom); // bottom
                cctx.drawImage(img, 0, 0, 1, img.height, 0, 0, t.left, t.newH); // left
                cctx.drawImage(img, img.width - 1, 0, 1, img.height, t.newW - t.right, 0, t.right, t.newH); // right
                cctx.drawImage(img, t.left, t.top);
                // mask: white where extended, black over the original
                mctx.fillStyle = '#ffffff';
                mctx.fillRect(0, 0, t.newW, t.newH);
                mctx.fillStyle = '#000000';
                mctx.fillRect(t.left, t.top, ref.width, ref.height);
                resolve({
                    init: comp.toDataURL('image/png'),
                    mask: maskC.toDataURL('image/png'),
                    width: t.newW, height: t.newH
                });
            };
            img.onerror = () => reject(new Error('无法生成扩图画布。'));
            img.src = ref.url;
        });
    }

    /* ============================================================
       Upscale
       ============================================================ */
    function syncUpscaleSize() {
        const ref = state.reference;
        if (!ref) { return; }
        const step = currentModel()?.resolution_step || 16;
        const align = v => Math.round(v / step) * step;
        $('width').value = Math.max(256, Math.min(2048, align(ref.width)));
        $('height').value = Math.max(256, Math.min(2048, align(ref.height)));
    }

    /* ============================================================
       LoRAs
       ============================================================ */
    function renderLoraList() {
        const holder = $('lora-list');
        holder.replaceChildren();
        if (!state.loras.length) {
            holder.append(el('p', 'hint', 'LoRA 可定制商品风格、人物等，最多 5 个。'));
            return;
        }
        for (const item of state.loras) { holder.append(item.row); }
    }

    function addLoraRow(id = '', weight = 1) {
        if (state.loras.length >= 5) { return; }
        const row = el('div', 'lora-row');
        const select = el('select');
        select.append(new Option('选择 LoRA', ''));
        for (const name of (state.catalog?.loras || [])) { select.append(new Option(name, name)); }
        select.value = id;
        const w = el('input'); w.type = 'number'; w.min = '-10'; w.max = '10'; w.step = '0.1'; w.value = weight;
        const remove = el('button', 'icon-btn sm');
        remove.textContent = '✕';
        remove.setAttribute('aria-label', '移除 LoRA');
        row.append(select, w, remove);
        const item = { select, w, row };
        state.loras.push(item);
        renderLoraList();
        remove.addEventListener('click', () => {
            const idx = state.loras.indexOf(item);
            if (idx >= 0) { state.loras.splice(idx, 1); }
            renderLoraList();
            updateControls();
        });
        updateControls();
    }

    function collectLoras() {
        const ids = [], weights = [];
        for (const item of state.loras) {
            const id = item.select.value;
            if (!id) { continue; }
            ids.push(id);
            weights.push(String(item.w.value || 1));
        }
        return { ids, weights };
    }

    /* ============================================================
       Controls / status
       ============================================================ */
    function setStatus(text, error = false) {
        const s = $('result-status');
        s.textContent = text;
        s.classList.toggle('error', error);
    }

    /* ============================================================
       Prompt enhancement
       ============================================================ */
    // Backend metadata: keyType maps to SwarmUI UserUpstreamApiKeys; null means no key needed.
    const MP_BACKENDS = {
        ollama:     { keyType: null,              editableUrl: true,  url: 'http://localhost:11434' },
        openaiapi:  { keyType: 'openaiapi_local', editableUrl: true,  url: 'http://localhost:1234/v1' },
        openrouter: { keyType: 'openrouter_api',  editableUrl: false, url: 'https://openrouter.ai' },
        openai:     { keyType: 'openai_api',      editableUrl: false, url: 'https://api.openai.com' },
        anthropic:  { keyType: 'anthropic_api',   editableUrl: false, url: 'https://api.anthropic.com' },
        grok:       { keyType: 'grok_api',        editableUrl: false, url: 'https://api.x.ai' }
    };
    let enhanceBusy = false;
    let enhancedText = '';
    let mpSettings = null;

    /** Tracks modal requests so canceled or superseded results cannot replace the current form. */
    class PromptSettingsRequests {
        static revision = 0;
        static controller = null;
        static loading = false;
        static saving = false;

        /** Invalidates model results when credentials, destination, backend, or modal visibility changes. */
        static invalidate() {
            this.revision++;
            this.controller?.abort();
            this.controller = null;
            $('enh-cfg-model-options').replaceChildren();
            $('enh-cfg-message').hidden = true;
            this.updateControls();
        }

        /** Prevents overlapping saves and edits while loading or persisting the form. */
        static updateControls() {
            let busy = this.loading || this.saving;
            for (let id of ['enh-cfg-backend', 'enh-cfg-endpoint', 'enh-cfg-model', 'enh-cfg-key', 'enh-cfg-save']) {
                $(id).disabled = busy;
            }
            $('enh-cfg-fetch-models').disabled = busy || !!this.controller;
            $('enh-cfg-fetch-models').textContent = this.controller ? '拉取中…' : '拉取';
        }
    }

    function showEnhanceCard(mode) {
        const card = $('enhance-card');
        const loading = $('enhance-loading');
        const result = $('enhance-result');
        const actions = $('enhance-card-actions');
        card.hidden = false;
        card.classList.toggle('is-error', mode === 'error');
        const spinner = loading.querySelector('.spinner');
        if (spinner) { spinner.hidden = mode === 'error'; }
        loading.hidden = mode !== 'loading' && mode !== 'error';
        result.hidden = mode !== 'result';
        actions.hidden = mode !== 'result' && mode !== 'error';
        $('enhance-apply').hidden = mode !== 'result';
        $('enhance-retry').hidden = false;
        $('enhance-dismiss').hidden = false;
    }

    function resetEnhanceCard() {
        $('enhance-card').hidden = true;
        enhancedText = '';
    }

    async function enhancePrompt() {
        const original = $('prompt').value.trim();
        if (!original) {
            setStatus('请先填写原始提示词，再进行优化。', true);
            $('prompt').focus();
            return;
        }
        if (enhanceBusy) { return; }
        enhanceBusy = true;
        showEnhanceCard('loading');
        $('enhance-loading').children[1].textContent = '正在调用提示词优化服务…';
        try {
            const cfg = await request('GetMagicPromptSettings');
            mpSettings = cfg.settings;
            const modelId = mpSettings.model;
            if (!modelId) {
                throw new ApiError('尚未选择提示词模型，请先在“设置”中选择。', 'magicprompt_no_model');
            }
            const res = await request('MagicPromptPhoneHome', {
                messageContent: { text: original },
                modelId: modelId,
                messageType: 'Text',
                action: 'prompt'
            });
            enhancedText = res.response;
            if (!enhancedText) {
                throw new ApiError('优化服务返回为空，请更换模型或稍后重试。', 'magicprompt_empty');
            }
            $('enhance-result').textContent = enhancedText;
            showEnhanceCard('result');
        } catch (err) {
            $('enhance-loading').children[1].textContent = err.message;
            showEnhanceCard('error');
        } finally {
            enhanceBusy = false;
        }
    }

    function applyEnhanced() {
        if (!enhancedText) { return; }
        $('prompt').value = enhancedText;
        $('prompt').focus();
        resetEnhanceCard();
        updateControls();
        setStatus('已采用优化后的提示词，可直接生成。');
    }

    // ----- Admin settings modal -----
    // Shows/hides the Base-URL and API-Key fields based on the selected backend.
    function applyBackendFields() {
        const backend = $('enh-cfg-backend').value;
        const meta = MP_BACKENDS[backend];
        $('enh-cfg-url-fld').hidden = !meta.editableUrl;
        $('enh-cfg-key-fld').hidden = meta.keyType === null;
    }

    /** Loads the selected backend's saved fields and ignores obsolete credential status responses. */
    async function onBackendChange() {
        PromptSettingsRequests.invalidate();
        let revision = PromptSettingsRequests.revision;
        applyBackendFields();
        const backend = $('enh-cfg-backend').value;
        const meta = MP_BACKENDS[backend];
        $('enh-cfg-endpoint').value = mpSettings?.backends?.[backend]?.baseurl || meta.url;
        $('enh-cfg-model').value = mpSettings?.backend == backend ? mpSettings.model || '' : '';
        $('enh-cfg-key').value = '';
        $('enh-cfg-key-status').textContent = '';
        if (meta.keyType) {
            try {
                const st = await request('GetAPIKeyStatus', { keyType: meta.keyType });
                if (revision == PromptSettingsRequests.revision) {
                    $('enh-cfg-key-status').textContent = st.status == 'not set' ? '尚未设置 Key' : `Key ${st.status}`;
                }
            } catch (err) {
                if (revision == PromptSettingsRequests.revision) {
                    $('enh-cfg-key-status').textContent = '无法读取已保存密钥的状态，可填写新密钥后拉取。';
                }
            }
        } else {
            $('enh-cfg-key-status').textContent = '';
        }
    }

    /** Loads persisted settings before allowing edits and discards responses for a closed modal. */
    async function openEnhancerSettings() {
        if (PromptSettingsRequests.saving) {
            return;
        }
        PromptSettingsRequests.loading = true;
        PromptSettingsRequests.invalidate();
        let revision = PromptSettingsRequests.revision;
        $('enhancer-modal').hidden = false;
        $('enh-cfg-message').hidden = true;
        $('enh-cfg-key').value = '';
        try {
            const cfg = await request('GetMagicPromptSettings');
            if (revision != PromptSettingsRequests.revision) {
                return;
            }
            mpSettings = cfg.settings;
            const backend = mpSettings.backend || 'ollama';
            if (!MP_BACKENDS[backend]) {
                throw new ApiError('已保存的模型后端不受支持，请重新选择。', 'api_error');
            }
            $('enh-cfg-backend').value = backend;
            $('enh-cfg-endpoint').value = mpSettings.backends?.[backend]?.baseurl || MP_BACKENDS[backend].url;
            $('enh-cfg-model').value = mpSettings.model || '';
            applyBackendFields();
            const meta = MP_BACKENDS[backend];
            if (meta.keyType) {
                const st = await request('GetAPIKeyStatus', { keyType: meta.keyType });
                if (revision == PromptSettingsRequests.revision) {
                    $('enh-cfg-key-status').textContent = st.status == 'not set' ? '尚未设置 Key' : `Key ${st.status}`;
                }
            } else {
                $('enh-cfg-key-status').textContent = '';
            }
        } catch (err) {
            if (revision == PromptSettingsRequests.revision) {
                showEnhCfgMessage(err.message, true);
            }
        } finally {
            if (revision == PromptSettingsRequests.revision) {
                PromptSettingsRequests.loading = false;
                PromptSettingsRequests.updateControls();
            }
        }
    }

    // Saves backend/model/base-url into MagicPrompt settings (keys are handled separately).
    async function saveMpCore(partial) {
        const backend = partial.backend;
        const meta = MP_BACKENDS[backend];
        const settings = {
            backend: backend,
            model: partial.model
        };
        if (meta.editableUrl && partial.baseurl) {
            settings.baseurl = partial.baseurl;
        }
        let result = await request('SaveMagicPromptSettings', { settings });
        if (!result.success) {
            throw new ApiError('配置保存失败，请稍后重试。', 'api_error');
        }
        mpSettings = result.settings;
        return result;
    }

    /** Previews models with the unsaved form; neither configuration nor credentials are persisted. */
    async function fetchMpModels() {
        if (PromptSettingsRequests.loading || PromptSettingsRequests.saving || PromptSettingsRequests.controller) {
            return;
        }
        PromptSettingsRequests.invalidate();
        let revision = PromptSettingsRequests.revision;
        let controller = new AbortController();
        PromptSettingsRequests.controller = controller;
        PromptSettingsRequests.updateControls();
        let timeout = setTimeout(() => controller.abort(), 25000);
        try {
            const backend = $('enh-cfg-backend').value;
            const meta = MP_BACKENDS[backend];
            const baseurl = meta.editableUrl ? $('enh-cfg-endpoint').value.trim() : null;
            if (meta.editableUrl && !baseurl) {
                showEnhCfgMessage('请先填写服务地址。', true);
                return;
            }
            let res = await request('PreviewMagicPromptModels', {
                backend,
                baseurl: baseurl || '',
                api_key: meta.keyType ? $('enh-cfg-key').value.trim() : ''
            }, controller.signal);
            if (revision != PromptSettingsRequests.revision) {
                return;
            }
            let list = $('enh-cfg-model-options');
            list.replaceChildren();
            for (let model of res.models || []) {
                list.append(new Option(model.name || model.model, model.model));
            }
            if (!res.models || res.models.length === 0) {
                showEnhCfgMessage('未拉取到模型，请确认服务已启动、地址与 Key 正确。', true);
            } else {
                showEnhCfgMessage(`已拉取 ${res.models.length} 个模型。请在上方“提示词模型”框中选择或输入，点击“保存”才会应用配置。`, false);
            }
        } catch (err) {
            if (revision == PromptSettingsRequests.revision) {
                showEnhCfgMessage(controller.signal.aborted ? '拉取模型超时，请检查服务后重试。' : err.message, true);
            }
        } finally {
            clearTimeout(timeout);
            if (PromptSettingsRequests.controller == controller) {
                PromptSettingsRequests.controller = null;
                PromptSettingsRequests.updateControls();
            }
        }
    }

    /** Cancels preview requests and clears the unsaved credential when closing the form. */
    function closeEnhancerSettings() {
        if (PromptSettingsRequests.saving) {
            return;
        }
        PromptSettingsRequests.loading = false;
        PromptSettingsRequests.invalidate();
        $('enh-cfg-key').value = '';
        $('enhancer-modal').hidden = true;
    }

    /** Persists the selected backend and credential only on explicit Save. */
    async function saveEnhancerSettings() {
        if (PromptSettingsRequests.saving || PromptSettingsRequests.loading) {
            return;
        }
        PromptSettingsRequests.saving = true;
        PromptSettingsRequests.invalidate();
        try {
            const backend = $('enh-cfg-backend').value;
            const meta = MP_BACKENDS[backend];
            const model = $('enh-cfg-model').value.trim();
            if (!model) {
                showEnhCfgMessage('请选择或填写提示词模型。', true);
                return;
            }
            const baseurl = meta.editableUrl ? $('enh-cfg-endpoint').value.trim() : null;
            if (meta.editableUrl && !baseurl) {
                showEnhCfgMessage('请填写服务地址。', true);
                return;
            }
            // 1) Save the key if provided.
            const key = $('enh-cfg-key').value.trim();
            if (meta.keyType && key) {
                const kr = await request('SetAPIKey', { keyType: meta.keyType, key: key });
                if (!kr.success) { throw new ApiError('Key 保存失败。', 'magicprompt_key'); }
            }
            // 2) Save backend/model/url settings.
            await saveMpCore({ backend, model, baseurl });
            // 3) Report status.
            if (meta.keyType) {
                const st = await request('GetAPIKeyStatus', { keyType: meta.keyType });
                $('enh-cfg-key-status').textContent = st.status === 'not set' ? '尚未设置 Key' : `Key ${st.status}`;
                if (backend === 'openaiapi') {
                    showEnhCfgMessage('已保存配置。请确认服务地址、网络和所需的 Key 均可用。', false);
                } else {
                    showEnhCfgMessage(st.status === 'not set'
                        ? '已保存设置，但当前后端未设置 Key，优化暂不可用。'
                        : '已保存配置，可尝试优化提示词。', st.status === 'not set');
                }
            } else {
                $('enh-cfg-key-status').textContent = '';
                showEnhCfgMessage('已保存配置。请确保本地 Ollama 服务已运行，并已拉取所选模型。', false);
            }
            $('enh-cfg-key').value = '';
            await refreshCatalog();
        } catch (err) {
            showEnhCfgMessage(err.message, true);
        } finally {
            PromptSettingsRequests.saving = false;
            PromptSettingsRequests.updateControls();
        }
    }

    function showEnhCfgMessage(text, isError) {
        const msg = $('enh-cfg-message');
        msg.hidden = false;
        msg.textContent = text;
        msg.classList.toggle('is-error', !!isError);
    }

    function updateControls() {
        $('creation-fields').disabled = state.busy;
        $('refresh-models').disabled = state.catalogLoading || state.busy;
        $('refresh-workflows').disabled = state.catalogLoading || state.busy;
        $('refresh-workflows').textContent = state.catalogLoading ? '刷新中…' : '刷新';
        $('model').disabled = state.busy || Boolean(currentWorkflow()?.native);
        $('mode-image').disabled = state.busy || state.uncertain;
        $('mode-video').disabled = state.busy || state.uncertain || !state.catalog?.can_generate_video;
        let hint = '';
        if (state.busy) { hint = '任务已发送到原生引擎，请勿刷新或重复提交。'; }
        else if (state.uncertain) { hint = '执行结果待核对，请先在右侧确认后再提交。'; }
        else if (state.catalogLoading) { hint = '正在刷新工作流、模型和节点信息…'; }
        else if (!state.session || !state.catalog) { hint = '尚未连接服务，请刷新或先登录。'; }
        else if (!state.catalog.can_generate) { hint = '此账号没有生成权限，请联系管理员。'; }
        else if (state.type === 'video' && !state.catalog.can_generate_video) { hint = '此账号没有视频参数权限，请联系管理员。'; }
        else if (!state.catalog.local_backends_observed) { hint = '未观测到可用的部署机 ComfyUI 节点。'; }
        else if (!currentWorkflow()?.native && !currentModel()) { hint = '请先在原生引擎配置此类型的模型。'; }
        else if (!state.workflowId && state.referenceLoading) { hint = '正在读取初始图，请稍候。'; }
        else if (!state.workflowId && state.mode === 'inpaint' && state.reference && !MaskEditor.hasMask()) {
            hint = '请先在图上涂抹要修改的区域。';
        }
        else if (!state.workflowId && state.mode === 'outpaint' && state.reference && !outpaintDirs.size) { hint = '请先选择至少一个扩图方向。'; }
        $('submit-hint').textContent = hint || '将提交到部署机的 ComfyUI；实际耗时与显存取决于模型。';
        $('generate').disabled = Boolean(hint);
        const isVideo = state.type === 'video';
        $('generate-label').textContent = state.busy ? '引擎处理中…' : (isVideo ? '生成视频' : '生成图片');
        $('preview-tag').textContent = isVideo ? 'VIDEO' : 'IMAGE';
        $('prompt-count').textContent = `${$('prompt').value.length} / 12000`;
        $('recover').hidden = !state.uncertain;
    }

    /* ============================================================
       Results
       ============================================================ */
    function safeMedia(raw) {
        if (typeof raw !== 'string' || !raw) { return null; }
        if (/^data:image\/(png|jpeg|webp|gif);base64,[A-Za-z0-9+/=\r\n]+$/.test(raw)) { return { url: raw, video: false }; }
        try {
            const url = new URL(raw, `${window.location.origin}/`);
            if (url.origin !== window.location.origin || url.username || url.password || !/^\/(View|Output)\//.test(url.pathname)) { return null; }
            const path = url.pathname.toLowerCase();
            const video = /\.(mp4|webm)$/.test(path);
            if (!video && !/\.(png|jpe?g|webp|gif|avif)$/.test(path)) { return null; }
            return { url: url.href, video };
        } catch { return null; }
    }

    function renderResults() {
        const results = state.results;
        $('results').replaceChildren();
        $('results').hidden = !results.length;
        $('results').classList.toggle('multiple', results.length > 1);
        $('empty-state').hidden = Boolean(results.length) || state.busy;
        $('pending-state').hidden = !state.busy;
        for (let i = 0; i < results.length; i++) {
            const media = results[i];
            const card = el('figure', 'result-card');
            const node = el(media.video ? 'video' : 'img');
            if (media.video) { node.controls = true; node.preload = 'metadata'; node.playsInline = true; }
            else { node.alt = `本次创作结果 ${i + 1}`; node.loading = 'lazy'; }
            node.src = media.url;
            node.addEventListener('error', () => setStatus('引擎返回了地址，但当前无法预览。请在原生引擎核对。', true));
            const caption = el('figcaption');
            const label = el('span', null, `${media.video ? '视频' : '画面'} ${String(i + 1).padStart(2, '0')} · 本次结果`);
            const download = el('a'); download.href = media.url; download.download = '';
            download.textContent = '下载原文件 ↓';
            caption.append(label, download);
            card.append(node, caption);
            $('results').append(card);
        }
    }

    /* ============================================================
       Generate
       ============================================================ */
    async function buildPayload() {
        const isVideo = state.type === 'video';
        const payload = {
            source: 'local',
            mode: isVideo ? 'video' : state.mode,
            kind: state.type,
            model: $('model').value,
            prompt: $('prompt').value.trim(),
            negativeprompt: $('negative').value.trim(),
            width: Number($('width').value),
            height: Number($('height').value),
            images: isVideo ? 1 : Number($('images').value)
        };
        for (const [field, key] of [['steps','steps'], ['cfg','cfgscale'], ['seed','seed']]) {
            if ($(field).value !== '') { payload[key] = Number($(field).value); }
        }
        if ($('sampler').value) { payload.sampler = $('sampler').value; }
        if ($('scheduler').value) { payload.scheduler = $('scheduler').value; }
        if (state.workflowId) {
            payload.workflow_id = state.workflowId;
            payload.workflow_params = { ...state.workflowValues };
            if (currentWorkflow()?.native) {
                delete payload.model;
            }
            if (isVideo) {
                payload.text2videoframes = Number($('frames').value);
                if ($('fps').value !== '') { payload.videofps = Number($('fps').value); }
            }
            return payload;
        }
        if (isVideo) {
            payload.text2videoframes = Number($('frames').value);
            if ($('fps').value !== '') { payload.videofps = Number($('fps').value); }
            return payload;
        }
        // Image edits
        if (state.mode === 'outpaint') {
            const built = await buildOutpaint();
            payload.initimage = built.init;
            payload.maskimage = built.mask;
            payload.width = built.width;
            payload.height = built.height;
            payload.maskshrinkgrow = 0;
            payload.maskblur = 8;
            payload.initimagecreativity = 1;
            return payload;
        }
        if (state.reference) {
            payload.initimage = state.reference.data;
            if (state.mode === 'inpaint') {
                payload.maskimage = MaskEditor.dataURL();
                payload.maskshrinkgrow = 8;
                payload.maskblur = 4;
                payload.initimagecreativity = Number($('creativity').value);
            } else if (state.mode === 'img2img') {
                payload.initimagecreativity = Number($('creativity').value);
            } else if (state.mode === 'upscale') {
                payload.refinerupscale = Number($('upscale').value);
            }
        }
        const { ids, weights } = collectLoras();
        if (ids.length) { payload.loras = ids; payload.loraweights = weights; }
        return payload;
    }

    async function generate(event) {
        event.preventDefault();
        if (state.busy || state.uncertain || $('generate').disabled || !$('creation-form').reportValidity()) { return; }
        let payload;
        try { payload = await buildPayload(); }
        catch (error) { setStatus(error.message || '无法组装请求。', true); return; }
        if (!payload.prompt) { setStatus('请先填写创作描述。', true); $('prompt').focus(); return; }
        if (/[<>]|__/.test(payload.prompt + payload.negativeprompt)) {
            setStatus('本版只支持普通描述。原生 <…> 指令、预设或 __通配符__ 请在原生引擎使用。', true);
            return;
        }
        if (state.results.length && !window.confirm('开始新创作会替换当前预览。需要的结果请先下载。继续吗？')) { return; }
        state.busy = true;
        state.results = [];
        const started = Date.now();
        $('elapsed').textContent = '已等待 0 秒';
        state.timer = setInterval(() => { $('elapsed').textContent = `已等待 ${Math.floor((Date.now() - started) / 1000)} 秒`; }, 1000);
        setStatus('请求已发送，等待引擎返回。当前接口不提供百分比进度。');
        updateControls();
        renderResults();
        try {
            const result = await request('EcomCraftStudioGenerate', payload);
            if (!Array.isArray(result.images)) { throw new ApiError('引擎响应格式不兼容，请到原生引擎核对结果。', 'generation_uncertain'); }
            const media = result.images.map(safeMedia).filter(Boolean);
            if (!media.length || media.length !== result.images.length) {
                throw new ApiError('没有完整收到可安全预览的输出地址，请在原生引擎核对个人历史。', 'generation_uncertain');
            }
            state.results = media;
            setStatus(`引擎已返回 ${media.length} 个结果。请核对画面并下载。`);
        } catch (error) {
            state.uncertain = uncertainErrors.has(error.code) || !(error instanceof ApiError);
            setStatus(error.message || '无法确认执行结果，请在原生引擎核对。', true);
            if (error.code === 'invalid_session_id') {
                state.catalog = null;
                const m = $('connection-message-text');
                m.textContent = '会话已失效。请刷新重新连接；不会自动重试任务。';
                $('connection-message').classList.add('error');
            }
        } finally {
            clearInterval(state.timer);
            state.timer = null;
            state.busy = false;
            renderResults();
            updateControls();
        }
    }

    /* ============================================================
       Queue
       ============================================================ */
    /** Reads local backend queue counts and the server's active generation claims. */
    async function loadQueue() {
        if (state.queue.loading) { return; }
        state.queue.loading = true;
        try {
            if (!state.session) {
                let session = await request('GetNewSession');
                state.session = session.session_id;
            }
            let result = await request('EcomCraftStudioQueue');
            renderQueue(result);
            $('queue-message').hidden = true;
            $('queue-updated').textContent = `更新于 ${new Date().toLocaleTimeString('zh-CN')}`;
        }
        catch (error) {
            $('queue-message').textContent = error.message || '队列读取失败。';
            $('queue-message').hidden = false;
        }
        finally {
            state.queue.loading = false;
        }
    }

    /** Renders native ComfyUI entries and SwarmUI claims without exposing workflow inputs. */
    function renderQueue(result) {
        let backendList = $('queue-backends');
        backendList.replaceChildren();
        for (let backend of result.backends || []) {
            let row = el('div', 'queue-backend');
            let name = el('strong', '', `本机 ComfyUI #${backend.id}`);
            let counts = el('span', '', backend.running == null
                ? '节点无响应'
                : `执行中 ${backend.running} · 等待中 ${backend.pending}`);
            let status = el('span', `queue-state ${backend.status === 'running' ? 'is-running' : 'is-waiting'}`,
                backend.status === 'running' ? '运行中' : backend.status === 'unresponsive' ? '无响应' : '启动中');
            row.append(name, counts, status);
            backendList.append(row);
        }
        if (!backendList.children.length) { backendList.append(el('p', 'queue-empty', '没有本机 ComfyUI 节点。')); }

        let tasks = result.tasks || [];
        let nativeTasks = result.native_tasks || [];
        $('queue-count').textContent = nativeTasks.length || tasks.length;
        $('queue-task-count').textContent = `${nativeTasks.length} 项原生任务 · ${tasks.length} 项已登记请求`;
        let list = $('queue-list');
        list.replaceChildren();
        $('queue-empty').hidden = nativeTasks.length > 0 || tasks.length > 0;
        $('queue-empty').textContent = '当前没有已登记的生成任务。';
        for (let task of nativeTasks) {
            let row = el('div', 'queue-task queue-native-task');
            let main = el('div', 'queue-task-main');
            main.append(el('strong', '', `ComfyUI #${task.backend_id} · ${task.state === 'running' ? '执行中' : `等待中（第 ${task.position} 位）`}`));
            let node = task.current_node
                ? `节点 ${task.current_node}${task.node_type ? ` · ${task.node_type}` : ''}`
                : task.state === 'running' ? '当前节点暂无数据' : `${task.total_nodes} 个节点`;
            main.append(el('span', '', node));
            if (task.state === 'running') {
                let overall = Number.isFinite(task.overall_percent) ? Math.round(task.overall_percent) : null;
                let current = Number.isFinite(task.node_percent) ? Math.round(task.node_percent) : null;
                main.append(el('span', '', `节点进度 ${current == null ? '暂无数据' : `${current}%`} · 总体进度 ${overall == null ? '暂无数据' : `约 ${overall}%`}`));
                if (overall != null) {
                    let bar = el('progress', 'queue-progress');
                    bar.max = 100;
                    bar.value = overall;
                    bar.setAttribute('aria-label', '工作流总体进度');
                    main.append(bar);
                }
            }
            row.append(main, el('span', 'queue-task-age', '原生队列'));
            list.append(row);
        }
        let labels = { running: '执行中', waiting_backend: '等待节点', queued: '排队中', stopping: '正在结束' };
        for (let task of tasks) {
            let row = el('div', 'queue-task');
            let main = el('div', 'queue-task-main');
            main.append(el('strong', '', task.mine ? `我的请求 #${task.request_id || task.id}` : '其他用户的请求'));
            main.append(el('span', '', `${labels[task.state] || '处理中'} · ${task.count} 张`));
            let age = el('span', 'queue-task-age', `${Math.floor(task.elapsed_seconds / 60)} 分 ${task.elapsed_seconds % 60} 秒`);
            row.append(main, age);
            if (task.mine && task.state !== 'stopping') {
                let button = el('button', 'btn-ghost btn-sm queue-cancel', '结束任务');
                button.type = 'button';
                button.disabled = state.queue.stopping.has(task.id);
                button.addEventListener('click', () => cancelQueueTask(task.id));
                row.append(button);
            }
            list.append(row);
        }
    }

    /** Cancels one task after the server verifies its authenticated owner. */
    async function cancelQueueTask(id) {
        if (state.queue.stopping.has(id) || !window.confirm('结束这个任务？已经生成的结果可能仍会保留。')) { return; }
        state.queue.stopping.add(id);
        try {
            await request('EcomCraftStudioCancelTask', { task_id: id });
            await loadQueue();
        }
        catch (error) {
            $('queue-message').textContent = error.message || '任务结束失败。';
            $('queue-message').hidden = false;
        }
        finally {
            state.queue.stopping.delete(id);
        }
    }

    /* ============================================================
       Gallery
       ============================================================ */
    async function loadGallery() {
        if (state.gallery.loading) { return; }
        const g = state.gallery;
        g.loading = true;
        const message = $('gallery-message');
        message.hidden = true;
        try {
            if (!state.session) {
                const session = await request('GetNewSession');
                state.session = session.session_id;
                state.userId = session.user_id;
                state.outputAppendUser = !!session.output_append_user;
            }
            const res = await request('ListImages', {
                path: '', depth: 3,
                sortBy: g.sort,
                sortReverse: g.sort === 'Date',
                filter: g.search || null
            });
            if (res.error) { throw new ApiError(res.error, 'api_error'); }
            g.items = (res.files || []).map(f => {
                const src = f.src;
                const name = src.split('/').pop();
                const starred = src.startsWith('Starred/');
                return { src, name, url: viewUrl(src), starred, video: /\.(mp4|webm)$/i.test(name) };
            });
            renderGallery();
        } catch (error) {
            message.textContent = error.message;
            message.classList.add('error');
            message.hidden = false;
            g.items = [];
            renderGallery();
        } finally {
            g.loading = false;
        }
    }

    function renderGallery() {
        const g = state.gallery;
        let items = g.items;
        if (g.filter === 'star') { items = items.filter(i => i.starred); }
        const grid = $('gallery-grid');
        grid.replaceChildren();
        const empty = $('gallery-empty');
        $('gallery-count').textContent = g.items.length;
        if (!items.length) {
            empty.hidden = false;
            empty.querySelector('h3').textContent = g.items.length ? '没有符合条件的作品' : '还没有作品';
            empty.querySelector('p').textContent = g.items.length
                ? '试试切换筛选或清空搜索。'
                : '去创作工作台生成第一张商品图吧。';
            empty.querySelector('button').style.display = g.items.length ? 'none' : '';
            return;
        }
        empty.hidden = true;
        for (const item of items) {
            const card = el('div', `gallery-item${item.starred ? ' starred' : ''}`);
            const img = el('img');
            img.src = item.url; img.alt = item.name; img.loading = 'lazy';
            const star = el('span', 'g-star');
            star.textContent = item.starred ? '★' : '☆';
            const name = el('span', 'g-name', item.name);
            card.append(img, star, name);
            card.addEventListener('click', e => {
                if (e.target === star) { e.stopPropagation(); toggleStar(item); return; }
                openLightbox(item);
            });
            star.addEventListener('click', e => { e.stopPropagation(); toggleStar(item); });
            grid.append(card);
        }
    }

    async function toggleStar(item) {
        try {
            const res = await request('ToggleImageStarred', { path: item.src });
            if (res.error) { throw new Error(res.error); }
            // refresh to reflect Starred/ path changes
            await loadGallery();
        } catch (error) {
            const m = $('gallery-message');
            m.textContent = '收藏失败：' + (error.message || '请重试');
            m.classList.add('error'); m.hidden = false;
        }
    }

    async function deleteItem(item) {
        try {
            const res = await request('DeleteImage', { path: item.src });
            if (res.error) { throw new Error(res.error); }
            closeLightbox();
            await loadGallery();
        } catch (error) {
            closeLightbox();
            const m = $('gallery-message');
            m.textContent = '删除失败：' + (error.message || '请重试');
            m.classList.add('error'); m.hidden = false;
        }
    }

    /* ---------- Lightbox ---------- */
    function openLightbox(item) {
        state.gallery.current = item;
        const media = $('lightbox-media');
        media.replaceChildren();
        const node = el(item.video ? 'video' : 'img');
        node.src = item.url;
        if (item.video) { node.controls = true; node.playsInline = true; }
        media.append(node);
        $('lightbox-name').textContent = item.name;
        $('lb-star').textContent = item.starred ? '★' : '☆';
        $('lb-download').href = item.url;
        $('lb-download').setAttribute('download', item.name);
        $('lightbox').hidden = false;
    }
    function closeLightbox() {
        $('lightbox').hidden = true;
        state.gallery.current = null;
    }

    // Load a gallery image into the image-to-image composer.
    async function editFromGallery(item) {
        try {
            const response = await fetch(item.url, { cache: 'no-store' });
            const blob = await response.blob();
            const data = await new Promise((resolve, reject) => {
                const reader = new FileReader();
                reader.onload = () => resolve(reader.result);
                reader.onerror = () => reject(new Error('无法读取该图片。'));
                reader.readAsDataURL(blob);
            });
            const image = new Image();
            const objectUrl = URL.createObjectURL(blob);
            await new Promise((res, rej) => { image.onload = res; image.onerror = rej; image.src = objectUrl; });
            if (state.reference) { URL.revokeObjectURL(state.reference.url); }
            state.reference = { data, url: objectUrl, name: item.name, width: image.width, height: image.height };
            closeLightbox();
            setType('image');
            setMode('img2img');
            renderReference();
            setView('create');
            location.hash = 'create';
            setStatus('已载入该图，可直接图生图，或切换到局部重绘/放大。');
        } catch (error) {
            alert('无法将该图载入创作：' + (error.message || '请重试'));
        }
    }

    /* ============================================================
       Bindings
       ============================================================ */
    function bind() {
        $('theme-toggle').addEventListener('click', toggleTheme);
        window.addEventListener('hashchange', route);

        $('mode-image').addEventListener('click', () => setType('image'));
        $('mode-video').addEventListener('click', () => setType('video'));
        document.querySelectorAll('.mode-tab').forEach(t =>
            t.addEventListener('click', () => setMode(t.dataset.mode)));

        $('refresh-models').addEventListener('click', refreshCatalog);
        $('refresh-workflows').addEventListener('click', refreshCatalog);
        $('model').addEventListener('change', () => { updateModelMeta(); applyModelSize(); });
        $('workflow-select').addEventListener('change', selectWorkflow);

        $('reference-file').addEventListener('change', readReference);
        $('remove-reference').addEventListener('click', clearReference);
        const dz = $('dropzone');
        ['dragenter','dragover'].forEach(ev => dz.addEventListener(ev, e => {
            e.preventDefault(); dz.classList.add('dragging');
        }));
        ['dragleave','drop'].forEach(ev => dz.addEventListener(ev, e => {
            e.preventDefault(); dz.classList.remove('dragging');
            if (ev === 'drop' && e.dataTransfer.files[0]) {
                $('reference-file').files = e.dataTransfer.files;
                readReference();
            }
        }));

        // Mask canvas events
        const mc = $('mask-canvas');
        mc.addEventListener('pointerdown', e => MaskEditor.start(e));
        mc.addEventListener('pointermove', e => MaskEditor.move(e));
        mc.addEventListener('pointerup', () => MaskEditor.end());
        mc.addEventListener('pointerleave', () => MaskEditor.end());
        $('mask-undo').addEventListener('click', () => MaskEditor.undo());
        $('mask-clear').addEventListener('click', () => MaskEditor.clear());

        // Outpaint
        document.querySelectorAll('.op-cell').forEach(b =>
            b.addEventListener('click', () => toggleOutpaintDir(b)));

        // Upscale slider
        $('upscale').addEventListener('input', () => {
            $('upscale-val').textContent = `${Number($('upscale').value).toFixed(2)}×`;
        });
        $('creativity').addEventListener('input', () => {
            $('creativity-val').textContent = Number($('creativity').value).toFixed(2);
        });

        // LoRA
        $('add-lora').addEventListener('click', () => addLoraRow());

        // Dims
        $('swap-dims').addEventListener('click', () => {
            const w = $('width').value; $('width').value = $('height').value; $('height').value = w;
        });
        $('insert-example').addEventListener('click', () => {
            if ($('prompt').value.trim() && !window.confirm('用示例替换当前描述？')) { return; }
            $('prompt').value = state.type === 'image'
                ? '一瓶透明玻璃精华液，置于浅灰色石台，柔和侧光，突出瓶身质感，画面右侧留白用于排版。'
                : '镜头缓慢环绕一双白色运动鞋，鞋底细节逐渐进入画面，柔和棚拍灯光，背景保持简洁，镜头运动平稳。';
            $('prompt').focus();
            updateControls();
        });

        $('unlock').addEventListener('click', () => {
            if (!window.confirm('请确认已在原生引擎核对队列和个人历史。重新提交可能产生第二个任务，确定解锁吗？')) { return; }
            state.uncertain = false;
            updateControls();
        });

        // Prompt enhancement
        $('enhance-prompt').addEventListener('click', () => enhancePrompt());
        $('enhance-apply').addEventListener('click', applyEnhanced);
        $('enhance-retry').addEventListener('click', () => enhancePrompt());
        $('enhance-dismiss').addEventListener('click', resetEnhanceCard);
        $('enhancer-settings').addEventListener('click', openEnhancerSettings);
        $('open-setup-guide').addEventListener('click', openSetupGuide);
        $('setup-guide-close').addEventListener('click', closeSetupGuide);
        $('setup-guide-backdrop').addEventListener('click', closeSetupGuide);
        $('setup-guide-done').addEventListener('click', closeSetupGuide);
        $('setup-cloud-settings').addEventListener('click', () => {
            if (!state.catalog?.can_manage_settings) { return; }
            closeSetupGuide();
            openEnhancerSettings();
        });
        $('enhancer-modal-close').addEventListener('click', closeEnhancerSettings);
        $('enhancer-modal-backdrop').addEventListener('click', closeEnhancerSettings);
        $('enh-cfg-cancel').addEventListener('click', closeEnhancerSettings);
        $('enh-cfg-save').addEventListener('click', saveEnhancerSettings);
        $('enh-cfg-backend').addEventListener('change', onBackendChange);
        $('enh-cfg-endpoint').addEventListener('input', () => PromptSettingsRequests.invalidate());
        $('enh-cfg-key').addEventListener('input', () => PromptSettingsRequests.invalidate());
        $('enh-cfg-fetch-models').addEventListener('click', fetchMpModels);

        $('creation-form').addEventListener('submit', generate);

        // Gallery
        $('gal-all').addEventListener('click', () => {
            state.gallery.filter = 'all';
            $('gal-all').classList.add('selected'); $('gal-all').setAttribute('aria-pressed', 'true');
            $('gal-star').classList.remove('selected'); $('gal-star').setAttribute('aria-pressed', 'false');
            renderGallery();
        });
        $('gal-star').addEventListener('click', () => {
            state.gallery.filter = 'star';
            $('gal-star').classList.add('selected'); $('gal-star').setAttribute('aria-pressed', 'true');
            $('gal-all').classList.remove('selected'); $('gal-all').setAttribute('aria-pressed', 'false');
            renderGallery();
        });
        $('gallery-search').addEventListener('input', debounce(() => {
            state.gallery.search = $('gallery-search').value.trim();
            loadGallery();
        }, 350));
        $('gallery-sort').addEventListener('change', () => {
            state.gallery.sort = $('gallery-sort').value;
            loadGallery();
        });
        $('gallery-refresh').addEventListener('click', loadGallery);
        $('gallery-gocreate').addEventListener('click', () => { location.hash = 'create'; });
        $('queue-refresh').addEventListener('click', loadQueue);
        setInterval(pollBackendStatus, 5000);
        setInterval(() => WorkflowCatalogRefresh.refresh(), 30000);
        document.addEventListener('visibilitychange', () => {
            if (!document.hidden) { pollBackendStatus(); }
        });
        state.queue.timer = setInterval(() => {
            if (state.view === 'queue') { loadQueue(); }
        }, 5000);

        // Lightbox
        $('lightbox-backdrop').addEventListener('click', closeLightbox);
        $('lb-close').addEventListener('click', closeLightbox);
        $('lb-star').addEventListener('click', () => state.gallery.current && toggleStar(state.gallery.current));
        $('lb-delete').addEventListener('click', () => {
            const item = state.gallery.current;
            if (item && window.confirm(`确定删除「${item.name}」？可在回收站找回（若已启用）。`)) { deleteItem(item); }
        });
        $('lb-edit').addEventListener('click', () => state.gallery.current && editFromGallery(state.gallery.current));
        window.addEventListener('keydown', e => {
            if (!$('lightbox').hidden) {
                if (e.key === 'Escape') { closeLightbox(); }
            }
            if (!$('setup-guide-modal').hidden && e.key === 'Escape') { closeSetupGuide(); }
        });

        // Mobile rail toggle
        $('rail-toggle').addEventListener('click', () => $('.rail').classList.toggle('open'));

        window.addEventListener('beforeunload', e => {
            if (state.busy || state.uncertain) { e.preventDefault(); e.returnValue = ''; }
        });
    }

    /* ============================================================
       Init
       ============================================================ */
    initTheme();
    bind();
    updateControls();
    setType('image');
    setMode('text2img');
    route();
    refreshCatalog();
})();
