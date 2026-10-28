(() => {
    'use strict';

    /* ============================================================
       Helpers
       ============================================================ */
    const $ = id => document.getElementById(id);
    const el = (tag, cls, text) => { const e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; };
    /** Builds a decorative stroke icon from SVG path data, so icons do not depend on emoji fonts. */
    function svgIcon(...paths) {
        let ns = 'http://www.w3.org/2000/svg';
        let svg = document.createElementNS(ns, 'svg');
        for (let [name, value] of [['viewBox', '0 0 24 24'], ['width', '16'], ['height', '16'], ['fill', 'none'], ['stroke', 'currentColor'],
            ['stroke-width', '1.8'], ['stroke-linecap', 'round'], ['stroke-linejoin', 'round'], ['aria-hidden', 'true']]) {
            svg.setAttribute(name, value);
        }
        for (let data of paths) {
            let path = document.createElementNS(ns, 'path');
            path.setAttribute('d', data);
            svg.append(path);
        }
        return svg;
    }
    /** Translates Chinese source text through I18n (see i18n.js); {0}, {1}… are filled with the extra arguments. */
    let tr = (source, ...args) => I18n.t(source, ...args);
    const debounce = (fn, ms) => { let t; return (...a) => { clearTimeout(t); t = setTimeout(() => fn(...a), ms); }; };

    class ApiError extends Error { constructor(message, code) { super(message); this.code = code; } }

    const state = {
        session: null, catalog: null, catalogLoading: false, backendStatusLoading: false,
        userId: null, outputAppendUser: false,
        view: 'create', type: 'image', mode: 'text2img',
        workflowId: '', workflowValues: {},
        busy: false, uncertain: false, submitting: false,
        reference: null, referenceLoading: false, referenceRevision: 0,
        loras: [], results: [], timer: null, pending: new Set(), pendingTimer: null,
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
        'invalid_scheduler','generation_failed','generation_uncertain','task_not_found','workflow_dependencies_missing',
        'no_execution_server','execution_server_unavailable','invalid_execution_server','queue_limit',
        'workflow_state','invalid_review','review_save_failed',
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

    /** Server messages are written in Chinese. In another language a known error code shows its translated message
     * (table key "error:<code>"), which drops the server's specific details; unknown codes keep the server text. */
    function serverErrorText(code, text) {
        let key = `error:${code}`;
        let translated = tr(key);
        if (I18n.current != I18n.sourceLanguage && translated != key) {
            return translated;
        }
        return String(text || '');
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
            throw new ApiError(safeErrors.has(code) ? serverErrorText(code, data.error) : '服务未正常返回，请检查登录状态或联系管理员。', code);
        }
        return data;
    }

    /* ============================================================
       Theme
       ============================================================ */
    /** Applies a theme and keeps the toggle label and browser chrome color in step with it. */
    function applyTheme(theme) {
        document.documentElement.setAttribute('data-theme', theme);
        let label = theme == 'dark' ? '切换到浅色主题' : '切换到深色主题';
        $('theme-toggle').setAttribute('aria-label', label);
        $('theme-toggle').title = label;
        document.querySelector('meta[name="theme-color"]')?.setAttribute('content', theme == 'dark' ? '#1E1F20' : '#E8E8E6');
    }

    /** Reads a per-browser preference; private browsing or blocked storage yields null instead of an error. */
    function readStorage(key) {
        try {
            return globalThis.localStorage?.getItem(key) ?? null;
        }
        catch (error) {
            return null;
        }
    }

    /** Saves a per-browser preference when storage is available; the page keeps working without it. */
    function writeStorage(key, value) {
        try {
            globalThis.localStorage?.setItem(key, value);
        }
        catch (error) {
            // Blocked storage only means the preference is not remembered.
        }
    }

    /** Restores the saved theme, falling back to the system preference. */
    function initTheme() {
        let saved = readStorage('ecomcraft-theme');
        let theme = saved == 'dark' || saved == 'light' ? saved
            : (window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
        applyTheme(theme);
    }

    /** Shows the language the button switches to; its own text is never translated (data-i18n-skip). */
    function updateLanguageToggle() {
        let target = I18n.current == 'en' ? 'zh-CN' : 'en';
        let offer = I18n.switchTo[target];
        let button = $('lang-toggle');
        button.textContent = offer.text;
        button.lang = target;
        button.setAttribute('aria-label', offer.label);
        button.title = offer.label;
    }

    /** After I18n has switched the static page, redraws the text that was built with variable parts. Typed prompts and chosen files stay. */
    function onLanguageChange() {
        updateLanguageToggle();
        setView(state.view);
        $('workflow-description').textContent = workflowDescriptionText(currentWorkflow());
        if (state.catalog) {
            $('connection-message-text').textContent = connectionText(state.catalog, state.catalog.models.length);
            updateModelMeta();
            updateHealth();
        }
        renderResults();
        updateControls();
        ExecutionServerPicker.load(true);
    }

    /** Switches between light and dark themes and remembers the choice in this browser. */
    function toggleTheme() {
        let next = document.documentElement.getAttribute('data-theme') == 'dark' ? 'light' : 'dark';
        applyTheme(next);
        writeStorage('ecomcraft-theme', next);
    }

    /* ============================================================
       View routing
       ============================================================ */
    /** Shows one view, marks its navigation entry as current and loads its data. */
    function setView(view) {
        if (!['create', 'gallery', 'queue', 'workflows'].includes(view)) {
            view = 'create';
        }
        state.view = view;
        $('view-create').hidden = view != 'create';
        $('view-gallery').hidden = view != 'gallery';
        $('view-queue').hidden = view != 'queue';
        $('view-workflows').hidden = view != 'workflows';
        let titles = { create: '创作', gallery: '作品画廊', queue: '任务队列', workflows: WorkflowReviewView.reviewer() ? '工作流审核' : '提交工作流审核' };
        document.title = tr('{0} - EcomCraft 创作工作台', tr(titles[view]));
        for (let item of document.querySelectorAll('.nav-item[data-view]')) {
            if (item.dataset.view == view) {
                item.setAttribute('aria-current', 'page');
            }
            else {
                item.removeAttribute('aria-current');
            }
        }
        if (view == 'gallery') {
            loadGallery();
        }
        if (view == 'queue') {
            loadQueue();
        }
        if (view == 'workflows') {
            WorkflowReviewView.enter();
        }
    }
    function route() {
        const hash = (location.hash || '').replace('#', '');
        setView(['gallery', 'queue', 'workflows'].includes(hash) ? hash : 'create');
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
        let wasHidden = modal.hidden;
        modal.hidden = false;
        if (wasHidden) {
            DialogFocus.enter(modal, document.activeElement, $('setup-guide-done'));
        }
        writeStorage('ecomcraft-setup-guide-seen', '1');
    }

    /** Closes the setup guide and returns focus to whatever opened it. */
    function closeSetupGuide() {
        if ($('setup-guide-modal').hidden) {
            return;
        }
        $('setup-guide-modal').hidden = true;
        DialogFocus.leave($('setup-guide-modal'));
    }

    function updateSetupGuideStatus() {
        let catalog = state.catalog;
        if (!catalog) { return; }
        let comfyReady = Number(catalog.local_backends_observed || 0) > 0;
        let cloudReady = !!catalog.cloud_configured;
        $('setup-status').textContent = comfyReady && cloudReady
            ? 'ComfyUI 执行服务器和云端模型均已配置。'
            : comfyReady ? 'ComfyUI 执行服务器已就绪，云端提示词模型尚未配置。'
            : cloudReady ? '云端提示词模型已配置，ComfyUI 执行服务器尚未就绪。'
            : '尚未检测到可用配置，请按下面步骤完成设置。';
        $('setup-status').classList.toggle('is-ready', comfyReady && cloudReady);
        $('setup-comfy-step').classList.toggle('is-ready', comfyReady);
        $('setup-cloud-step').classList.toggle('is-ready', cloudReady);
        $('setup-cloud-settings').hidden = !catalog.can_manage_settings;
    }

    /** Opens the setup guide once per browser while the servers or the cloud prompt model are not ready. */
    function maybeOpenSetupGuide() {
        if (!state.catalog || readStorage('ecomcraft-setup-guide-seen') == '1') {
            return;
        }
        if (!state.catalog.local_backends_observed || !state.catalog.cloud_configured) {
            openSetupGuide();
        }
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
                $('resolution-hint').textContent = '读取自工作流，可修改';
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

    /** Lists workflows for the current type, grouped by the source server prefix of native names ("B/工作流1": everything before the first '/'). */
    function populateWorkflows() {
        let select = $('workflow-select');
        let previous = state.workflowId;
        let workflows = (state.catalog?.workflows || []).filter(workflow => !workflow.kind || workflow.kind == state.type);
        select.replaceChildren(new Option('标准工作台（当前模式）', ''));
        let groups = new Map();
        for (let workflow of workflows) {
            let name = workflow.name || workflow.id;
            let split = name.indexOf('/');
            let groupName = workflow.native && split > 0 ? name.slice(0, split) : '管理员发布';
            let label = workflow.native && split > 0 ? name.slice(split + 1) : name;
            if (!groups.has(groupName)) {
                let group = document.createElement('optgroup');
                group.label = groupName;
                groups.set(groupName, group);
            }
            groups.get(groupName).append(new Option(label, workflow.id));
        }
        for (let group of groups.values()) {
            select.append(group);
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
        $('workflow-description').textContent = workflowDescriptionText(workflow);
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

    /** The explanation under the workflow selector for the selected workflow, or for the standard workspace when none is selected. */
    function workflowDescriptionText(workflow) {
        if (workflow) {
            return tr('{0} 创作方式由工作流决定；要用图生图、局部重绘，请切回标准工作台。', workflow.description || tr('管理员发布的工作流模板。'));
        }
        return (state.catalog?.workflows || []).length
            ? tr('按来源服务器分组。生成时平台会自动选择能运行它的服务器。')
            : tr('暂时没有可用的工作流。确认执行服务器在线、工作流已保存且模型和节点齐全后，点击“刷新列表”。');
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
                        setStatus(tr('“{0}”需要不超过 5 MB 的 PNG、JPEG 或 WebP 图片。', field.name || field.id), true);
                        return;
                    }
                    const reader = new FileReader();
                    reader.onload = () => {
                        if (typeof reader.result === 'string') {
                            setWorkflowValue(field.id, reader.result);
                            setStatus(tr('已读取工作流图片参数“{0}”，提交生成时才发送。', field.name || field.id));
                        }
                    };
                    reader.onerror = () => setStatus(tr('无法读取工作流图片参数“{0}”。', field.name || field.id), true);
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
        $('resolution-hint').textContent = tr('256–2048 像素，{0} 的倍数', step);
        $('model-help').textContent = model ? tr('模型类型：{0}', model.class_name)
            : '没有可用的模型。请在原生引擎添加模型，或请管理员检查账号的模型权限。';
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

    /** One plain sentence about execution servers and available models for the status line above the form. */
    function connectionText(status, modelCount) {
        if (status.local_backends_observed > 0) {
            return tr('{0} 台执行服务器在线，标准工作台有 {1} 个模型可用。', status.local_backends_observed, modelCount);
        }
        if (status.local_backends_starting > 0) {
            return '执行服务器正在启动，启动完成后即可生成。';
        }
        if (status.local_backends_errored > 0) {
            return '执行服务器启动失败。请管理员查看服务器日志。';
        }
        return '还没有在线的执行服务器。';
    }

    /** Reloads authorized models and workflows while preserving the current selections when available. */
    async function refreshCatalog() {
        if (state.catalogLoading || state.busy) { return; }
        state.catalogLoading = true;
        const msg = $('connection-message-text');
        const previousModel = $('model').value;
        msg.textContent = '正在读取账号、工作流、模型与执行服务器信息…';
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
            ExecutionServerPicker.load(true);
            $('user-name').textContent = catalog.user_name;
            $('avatar').textContent = String(catalog.user_name || 'E').slice(0, 1).toUpperCase();
            $('enhancer-settings').hidden = !catalog.can_manage_settings;
            WorkflowReviewView.updateAccess();
            if (state.view == 'workflows') {
                WorkflowReviewView.load();
            }
            if (catalog.version) {
                $('app-version').textContent = `EcomCraft Studio ${catalog.version}`;
            }
            msg.textContent = connectionText(catalog, catalog.models.length);
            populateModels();
            populateSamplers();
            if ($('model').value !== previousModel) { applyModelSize(); }
            updateHealth();
            updateSetupGuideStatus();
            maybeOpenSetupGuide();
            if (state.view == 'gallery') {
                renderGallery();
            }
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
                $('connection-message-text').textContent = connectionText(status, state.catalog.models.length);
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
                    $('workflow-description').textContent = '工作流列表自动刷新失败，可点击“刷新列表”重试。';
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
        if (state.catalog && count > 0) { h.classList.add('ok'); text.textContent = tr('{0} 台服务器在线', count); }
        else if (state.catalog) {
            h.classList.add('warn');
            text.textContent = state.catalog.local_backends_starting > 0 ? '服务器正在启动'
                : state.catalog.local_backends_errored > 0 ? '服务器启动失败' : '无在线服务器';
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
        $('page-title').textContent = type == 'video' ? '生成商品视频' : '生成商品图片';
        $('empty-title').textContent = type == 'video' ? '下一段商品视频，从这里开始' : '下一张商品图，从这里开始';
        $('empty-desc').textContent = type == 'video' ? '描述镜头和运动后点击“生成视频”，结果会出现在这里。' : '填写描述后点击“生成图片”，结果会出现在这里。';
        $('prompt').placeholder = type == 'video'
            ? '例如：镜头缓慢环绕一双白色运动鞋，鞋底细节逐渐进入画面，柔和棚拍灯光，背景保持简洁…'
            : '例如：一瓶透明玻璃精华液，置于浅灰色石台，柔和侧光，突出瓶身质感，画面右侧留白用于排版…';
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
        for (let tab of document.querySelectorAll('.mode-tab')) {
            tab.classList.toggle('selected', tab.dataset.mode == mode);
            tab.setAttribute('aria-pressed', String(tab.dataset.mode == mode));
        }
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
    /** Shows either the upload area or the chosen initial image; the remove button names the file for screen readers. */
    function renderReference() {
        $('dropzone').style.display = state.reference ? 'none' : '';
        $('reference-preview').hidden = !state.reference;
        if (state.reference) {
            $('reference-image').src = state.reference.url;
            $('reference-name').textContent = state.reference.name;
            $('remove-reference').setAttribute('aria-label', tr('移除初始图 {0}', state.reference.name));
        }
        else {
            $('reference-image').removeAttribute('src');
            $('reference-file').value = '';
            $('remove-reference').setAttribute('aria-label', '移除初始图');
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
        let dir = btn.dataset.dir;
        if (outpaintDirs.has(dir)) {
            outpaintDirs.delete(dir);
        }
        else {
            outpaintDirs.add(dir);
        }
        btn.setAttribute('aria-pressed', String(outpaintDirs.has(dir)));
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
            holder.append(el('p', 'hint', 'LoRA 可以固定商品风格、人物形象等。'));
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
        let position = state.loras.length + 1;
        select.setAttribute('aria-label', tr('第 {0} 个 LoRA', position));
        w.setAttribute('aria-label', tr('第 {0} 个 LoRA 的权重', position));
        w.inputMode = 'decimal';
        let remove = el('button', 'icon-btn icon-btn-sm');
        remove.type = 'button';
        remove.textContent = '✕';
        remove.setAttribute('aria-label', tr('移除第 {0} 个 LoRA', position));
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
            $('enh-cfg-save').textContent = this.saving ? '保存中…' : '保存设置';
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
        setEnhanceBusy(true);
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
            setEnhanceBusy(false);
        }
    }

    /** Disables the enhancement buttons while a request runs so it cannot be sent twice. */
    function setEnhanceBusy(busy) {
        enhanceBusy = busy;
        $('enhance-prompt').disabled = busy;
        $('enhance-prompt').textContent = busy ? '优化中…' : '优化提示词';
        $('enhance-retry').disabled = busy;
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
        let wasHidden = $('enhancer-modal').hidden;
        $('enhancer-modal').hidden = false;
        if (wasHidden) {
            // Form fields are disabled while settings load, so start on the close button.
            DialogFocus.enter($('enhancer-modal'), document.activeElement, $('enhancer-modal-close'));
        }
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
                showEnhCfgMessage(tr('已拉取 {0} 个模型。请在上方“提示词模型”框中选择或输入，点击“保存”才会应用配置。', res.models.length), false);
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
        let wasOpen = !$('enhancer-modal').hidden;
        $('enhancer-modal').hidden = true;
        if (wasOpen) {
            DialogFocus.leave($('enhancer-modal'));
        }
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

    /** Counts advanced parameters the user filled in for the standard workspace; native workflows prefill them, so they show no count. */
    function advancedSummary() {
        if (currentWorkflow()?.native) {
            return '读取自工作流';
        }
        let changed = 0;
        for (let id of ['negative', 'steps', 'cfg', 'seed', 'sampler', 'scheduler']) {
            if (String($(id).value ?? '').trim() != '') {
                changed++;
            }
        }
        return changed ? tr('已填写 {0} 项', changed) : '按模型需要填写';
    }

    /** Syncs every form control, the submit button and its explanation with the current state. */
    function updateControls() {
        $('creation-fields').disabled = state.busy;
        $('refresh-workflows').disabled = state.catalogLoading || state.busy;
        $('refresh-workflows').textContent = state.catalogLoading ? '刷新中…' : '刷新列表';
        $('model').disabled = state.busy || Boolean(currentWorkflow()?.native);
        $('mode-image').disabled = state.busy || state.uncertain;
        $('mode-video').disabled = state.busy || state.uncertain || !state.catalog?.can_generate_video;
        $('mode-video').title = state.catalog && !state.catalog.can_generate_video ? '此账号没有视频生成权限' : '';
        $('add-lora').disabled = state.loras.length >= 5;
        $('add-lora').textContent = state.loras.length >= 5 ? '已达 5 个上限' : '添加 LoRA';
        $('adv-summary-count').textContent = advancedSummary();
        $('mask-undo').disabled = !MaskEditor.history.length;
        let hint = '';
        if (state.busy) { hint = '任务已发送到原生引擎，请勿刷新或重复提交。'; }
        else if (state.uncertain) { hint = '执行结果待核对，请先在右侧确认后再提交。'; }
        else if (state.catalogLoading) { hint = '正在刷新工作流、模型和节点信息…'; }
        else if (!state.session || !state.catalog) { hint = '尚未连接服务，请刷新或先登录。'; }
        else if (!state.catalog.can_generate) { hint = '此账号没有生成权限，请联系管理员。'; }
        else if (state.type === 'video' && !state.catalog.can_generate_video) { hint = '此账号没有视频参数权限，请联系管理员。'; }
        else if (!state.catalog.local_backends_observed) { hint = '暂无在线的 ComfyUI 执行服务器。'; }
        else if (!currentWorkflow()?.native && !currentModel()) { hint = '请先在原生引擎配置此类型的模型。'; }
        else if (!state.workflowId && state.referenceLoading) { hint = '正在读取初始图，请稍候。'; }
        else if (!state.workflowId && state.mode === 'inpaint' && state.reference && !MaskEditor.hasMask()) {
            hint = '请先在图上涂抹要修改的区域。';
        }
        else if (!state.workflowId && state.mode === 'outpaint' && state.reference && !outpaintDirs.size) { hint = '请先选择至少一个扩图方向。'; }
        $('submit-hint').textContent = hint || '将由平台分配到具备所需模型和节点的 ComfyUI 执行服务器；实际耗时与显存取决于模型。';
        $('generate').disabled = Boolean(hint) || state.submitting;
        const isVideo = state.type === 'video';
        $('generate-label').textContent = state.submitting ? '提交中…' : (isVideo ? '生成视频' : '生成图片');
        let promptLength = $('prompt').value.length;
        $('prompt-count').textContent = `${promptLength} / 12000`;
        $('prompt-count').classList.toggle('near-limit', promptLength > 11000);
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
            let segments = url.pathname.split('/').slice(1).map(segment => decodeURIComponent(segment));
            // /View/<user>/<path> and /Output/<path> map back to the gallery path relative to the user's output root.
            let relative = segments[0] == 'View' ? segments.slice(2) : segments.slice(1);
            return { url: url.href, video, src: relative.join('/'), name: relative[relative.length - 1] || '生成结果' };
        } catch { return null; }
    }

    /** Renders the latest results on the backdrop with download, continue and gallery actions. */
    function renderResults() {
        let results = state.results;
        let holder = $('results');
        holder.replaceChildren();
        holder.hidden = !results.length;
        holder.className = `results count-${Math.min(results.length, 4)}`;
        $('empty-state').hidden = Boolean(results.length) || state.busy;
        $('pending-state').hidden = !state.busy;
        for (let i = 0; i < results.length; i++) {
            let media = results[i];
            let card = el('figure', 'result-card');
            let node = el(media.video ? 'video' : 'img');
            node.src = media.url;
            node.addEventListener('error', () => setStatus('服务器返回了结果地址，但当前无法预览。请到原生引擎核对。', true));
            if (media.video) {
                node.controls = true;
                node.preload = 'metadata';
                node.playsInline = true;
                let frame = el('div', 'result-open');
                frame.append(node);
                card.append(frame);
            }
            else {
                node.alt = tr('第 {0} 张生成结果', i + 1);
                let open = el('button', 'result-open');
                open.type = 'button';
                open.setAttribute('aria-label', tr('放大查看第 {0} 张结果', i + 1));
                open.append(node);
                open.addEventListener('click', () => Lightbox.open(results, i, 'result', open));
                card.append(open);
            }
            let actions = el('figcaption', 'result-actions');
            if (results.length > 1) {
                actions.append(el('span', 'result-label', media.video ? tr('第 {0} 条', i + 1) : tr('第 {0} 张', i + 1)));
            }
            let download = el('a', 'btn btn-sm', '下载');
            download.href = media.url;
            download.download = media.name;
            actions.append(download);
            if (!media.video) {
                let again = el('button', 'btn btn-sm', '以此图继续创作');
                again.type = 'button';
                again.addEventListener('click', () => editFromGallery(media));
                actions.append(again);
            }
            if (media.src) {
                let reveal = el('button', 'btn btn-sm', '在画廊查看');
                reveal.type = 'button';
                reveal.addEventListener('click', () => revealInGallery(media.src));
                actions.append(reveal);
            }
            card.append(actions);
            holder.append(card);
        }
    }

    /** Switches to the gallery and opens the given file once the list has loaded. */
    function revealInGallery(src) {
        state.gallery.revealSrc = src;
        setGallerySearch('');
        setGalleryFilter('all');
        if (state.view == 'gallery') {
            loadGallery();
        }
        else {
            location.hash = 'gallery';
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
        let pinnedServer = ExecutionServerPicker.selectedId();
        if (pinnedServer != null) { payload.execution_server_id = pinnedServer; }
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
        if (state.submitting || state.uncertain || $('generate').disabled || !$('creation-form').reportValidity()) { return; }
        let payload;
        try { payload = await buildPayload(); }
        catch (error) { setStatus(error.message || '无法组装请求。', true); return; }
        if (!payload.prompt) { setStatus('请先填写创作描述。', true); $('prompt').focus(); return; }
        if (/[<>]|__/.test(payload.prompt + payload.negativeprompt)) {
            setStatus('本版只支持普通描述。原生 <…> 指令、预设或 __通配符__ 请在原生引擎使用。', true);
            return;
        }
        // 提交后立即返回队列，不锁定创作区；可以继续改参数、切模式或再次提交。
        payload.async = true;
        await submitPayload(payload);
    }

    /** Submits one generation request and tracks its task. A failure on an execution server offers reassignment with the same request (R6.2). */
    async function submitPayload(payload) {
        state.submitting = true;
        updateControls();
        Reassign.hide();
        try {
            let result = await request('EcomCraftStudioGenerate', payload);
            if (result.task_id != null) {
                state.pending.add(result.task_id);
                Reassign.remember(result.task_id, payload);
                startPendingTracker();
            }
            let task = result.task_id != null ? tr('（任务 #{0}）', result.task_id) : '';
            let server = result.execution_server ? tr('，由“{0}”执行', result.execution_server) : '';
            let hint = result.server_hint ? ` ${result.server_hint}` : '';
            setStatus(tr('已加入队列{0}{1}。可以继续编辑、切换图片/视频或再次提交；完成后结果会自动保存到作品画廊。', task, server) + hint);
            ExecutionServerPicker.load(true);
            if (state.view == 'gallery') { loadGallery(); }
        }
        catch (error) {
            state.uncertain = uncertainErrors.has(error.code) || !(error instanceof ApiError);
            setStatus(error.message || '提交失败，请稍后重试。', true);
            if (Reassign.retryableErrors.has(error.code)) {
                Reassign.show(error.message || '任务没有完成。', payload);
            }
            if (error.code === 'invalid_session_id') {
                state.catalog = null;
                let m = $('connection-message-text');
                m.textContent = '会话已失效。请刷新重新连接；不会自动重试任务。';
                $('connection-message').classList.add('error');
            }
        }
        finally {
            state.submitting = false;
            updateControls();
        }
    }

    /** The "reassign to another server" notice (R6.2). Requests are kept in memory only, never in browser storage, so a reload drops them and the notice then shows the reason without the button. */
    class Reassign {
        static payloads = new Map();
        static misses = new Map();
        static current = null;
        static retryableErrors = new Set(['generation_failed', 'execution_server_unavailable']);

        /** Keeps the request of a queued task until it ends. */
        static remember(id, payload) {
            this.payloads.set(id, payload);
        }

        /** Takes the kept request of an ended task, or null. */
        static take(id) {
            let payload = this.payloads.get(id) || null;
            this.payloads.delete(id);
            this.misses.delete(id);
            return payload;
        }

        /** Shows the failure reason; the button appears only when the request can be submitted again. */
        static show(message, payload) {
            this.current = payload || null;
            $('reassign-text').textContent = message;
            $('reassign-btn').hidden = !this.current;
            $('reassign').hidden = false;
        }

        /** Hides the notice and forgets the request it offered. */
        static hide() {
            this.current = null;
            $('reassign').hidden = true;
        }

        /** Submits the failed request again without a server restriction, so the platform chooses a server by Rule 1.
         * Clicking it is the user's explicit decision, so it also clears the "result uncertain" lock that a failed submission sets. */
        static async retry() {
            if (!this.current || state.submitting) {
                return;
            }
            state.uncertain = false;
            let payload = Object.assign({}, this.current);
            delete payload.execution_server_id;
            this.hide();
            await submitPayload(payload);
        }
    }

    /** Polls the queue while this browser has queued tasks, reports finished ones and refreshes the gallery when it is open. */
    function startPendingTracker() {
        if (state.pendingTimer) { return; }
        state.pendingTimer = setInterval(async () => {
            if (!state.pending.size || document.hidden) { return; }
            let mine;
            let outcomes;
            try {
                let res = await request('EcomCraftStudioQueue');
                mine = new Set((res.tasks || []).filter(t => t.mine && t.id != null).map(t => t.id));
                outcomes = new Map((res.finished_tasks || []).map(t => [t.id, t]));
            }
            catch (error) { return; }
            for (let id of Array.from(state.pending)) {
                if (mine.has(id)) {
                    continue;
                }
                let outcome = outcomes.get(id);
                // The result is recorded just after the task leaves the queue; give it a couple of polls before giving up on it.
                if (!outcome) {
                    let misses = (Reassign.misses.get(id) || 0) + 1;
                    if (misses < 3) {
                        Reassign.misses.set(id, misses);
                        continue;
                    }
                }
                state.pending.delete(id);
                let payload = Reassign.take(id);
                let server = outcome?.execution_server ? tr('（{0}）', outcome.execution_server) : '';
                if (outcome?.state == 'failed') {
                    setStatus(tr('任务 #{0} 失败{1}。', id, server), true);
                    let reason = outcome.error_id ? serverErrorText(outcome.error_id, outcome.error) : (outcome.error || tr('执行服务器没有返回结果。'));
                    Reassign.show(tr('任务 #{0} 失败{1}：{2}', id, server, reason), outcome.error_id == 'generation_uncertain' ? null : payload);
                }
                else if (outcome) {
                    setStatus(tr('任务 #{0} 已完成{1}，结果已保存到作品画廊。', id, server));
                }
                else {
                    setStatus(tr('任务 #{0} 已结束；成功的结果已自动保存到作品画廊。', id));
                }
                if (state.view == 'gallery') { loadGallery(); }
            }
            if (!state.pending.size) {
                clearInterval(state.pendingTimer);
                state.pendingTimer = null;
            }
        }, 5000);
    }

    /* ============================================================
       Execution server choice
       ============================================================ */
    /** The optional "only run on this server" choice (D7). Automatic assignment is the default; the server validates every ID it receives. */
    class ExecutionServerPicker {
        static lastLoad = 0;
        static loading = false;

        /** Returns the chosen server ID, or null for automatic assignment. */
        static selectedId() {
            let value = $('execution-server').value;
            if (value === '') {
                return null;
            }
            let id = Number(value);
            return Number.isInteger(id) ? id : null;
        }

        /** Reads the server list from the queue API, at most every few seconds unless forced. Failures keep the current options. */
        static async load(force = false) {
            if (this.loading || !state.session || (!force && Date.now() - this.lastLoad < 5000)) {
                return;
            }
            this.loading = true;
            try {
                let result = await request('EcomCraftStudioQueue');
                this.render(result.backends || []);
            }
            catch (error) {
                // The picker is optional; automatic assignment still works without a fresh list.
            }
            finally {
                this.lastLoad = Date.now();
                this.loading = false;
            }
        }

        /** Fills the options with each server's name and state, keeping the current choice while that server is still registered.
         * The row stays hidden with fewer than two servers, unless a choice is already made. */
        static render(backends) {
            let select = $('execution-server');
            let current = select.value;
            let auto = el('option', '', '自动分配（推荐）');
            auto.value = '';
            let options = [auto];
            for (let backend of backends) {
                let queued = backend.queue ? tr('，排队 {0}', backend.queue) : '';
                let option = el('option', '', tr('{0}（{1}{2}）', backend.name || tr('服务器 #{0}', backend.id), tr(queueStateLabel(backend)), queued));
                option.value = String(backend.id);
                options.push(option);
            }
            select.replaceChildren(...options);
            select.value = options.some(option => option.value === current) ? current : '';
            $('execution-server-row').hidden = backends.length < 2 && select.value === '';
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
            ExecutionServerPicker.render(result.backends || []);
            $('queue-message').hidden = true;
            $('queue-updated').textContent = tr('更新于 {0}，每 5 秒自动刷新', new Intl.DateTimeFormat(I18n.locale(), { timeStyle: 'medium' }).format(new Date()));
        }
        catch (error) {
            $('queue-message').textContent = error.message || '队列读取失败。';
            $('queue-message').hidden = false;
        }
        finally {
            state.queue.loading = false;
        }
    }

    /** Returns the user-facing state of one execution server: 空闲 / 忙碌（有任务在执行但未排队）/ 繁忙（出现排队）. */
    function queueStateLabel(backend) {
        if (backend.status == 'running') {
            if (backend.load == 'congested') { return '繁忙'; }
            if (backend.load == 'working') { return '忙碌'; }
            if (backend.load == 'idle') { return '空闲'; }
            return '无响应';
        }
        if (backend.status == 'unresponsive') {
            return '无响应';
        }
        if (backend.status == 'waiting' || backend.status == 'loading') {
            return '启动中';
        }
        if (backend.status == 'errored') {
            return '启动失败';
        }
        return '离线';
    }

    /** Returns the indicator style for one execution server state; color is always paired with the text label. */
    function queueStateClass(backend) {
        if (backend.status == 'running') {
            if (backend.load == 'congested') { return 'is-full'; }
            if (backend.load == 'working') { return 'is-busy'; }
            if (backend.load == 'idle') { return 'is-idle'; }
            return 'is-down';
        }
        return backend.status == 'waiting' || backend.status == 'loading' ? 'is-starting' : 'is-down';
    }

    /** Renders the server table, native ComfyUI entries and SwarmUI claims without exposing workflow inputs. */
    function renderQueue(result) {
        let backendList = $('queue-backends');
        backendList.replaceChildren();
        for (let backend of result.backends || []) {
            let row = el('tr');
            let name = el('td');
            name.append(el('strong', '', backend.name || tr('服务器 #{0}', backend.id)));
            let status = el('td');
            status.append(el('span', `server-state ${queueStateClass(backend)}`, queueStateLabel(backend)));
            let running = el('td', 'num', backend.running == null ? '—' : String(backend.running));
            // Waiting includes tasks this platform assigned but has not yet handed to ComfyUI (R5.2).
            let pending = el('td', 'num', backend.queue == null || backend.running == null ? '—' : String(Math.max(0, backend.queue - backend.running)));
            row.append(name, status, running, pending);
            backendList.append(row);
        }
        if (!backendList.children.length) {
            let row = el('tr');
            let cell = el('td', 'empty-cell', '还没有登记 ComfyUI 执行服务器。请在原生引擎的 Server / Backends 中添加。');
            cell.colSpan = 4;
            row.append(cell);
            backendList.append(row);
        }

        let tasks = result.tasks || [];
        let nativeTasks = result.native_tasks || [];
        $('queue-count').textContent = nativeTasks.length || tasks.length;
        $('queue-count').hidden = false;
        $('queue-task-count').textContent = tr('服务器上 {0} 项，平台请求 {1} 项', nativeTasks.length, tasks.length);
        let list = $('queue-list');
        list.replaceChildren();
        $('queue-empty').hidden = nativeTasks.length > 0 || tasks.length > 0;
        for (let task of nativeTasks) {
            let row = el('li', 'queue-task');
            let main = el('div', 'queue-task-main');
            main.append(el('strong', '', tr('{0}：{1}', task.backend_name || tr('服务器 #{0}', task.backend_id), task.state == 'running' ? tr('执行中') : tr('等待中，第 {0} 位', task.position))));
            let node = task.current_node
                ? tr('节点 {0}', task.current_node) + (task.node_type ? tr('（{0}）', task.node_type) : '')
                : task.state == 'running' ? '当前节点暂无数据' : tr('共 {0} 个节点', task.total_nodes);
            main.append(el('span', '', node));
            if (task.state == 'running') {
                let overall = Number.isFinite(task.overall_percent) ? Math.round(task.overall_percent) : null;
                let current = Number.isFinite(task.node_percent) ? Math.round(task.node_percent) : null;
                main.append(el('span', '', tr('当前节点 {0}，总体约 {1}', current == null ? tr('暂无数据') : `${current}%`, overall == null ? tr('暂无数据') : `${overall}%`)));
                if (overall != null) {
                    let bar = el('progress', 'queue-progress');
                    bar.max = 100;
                    bar.value = overall;
                    bar.setAttribute('aria-label', tr('总体进度 {0}%', overall));
                    main.append(bar);
                }
            }
            row.append(main);
            list.append(row);
        }
        let labels = { running: '执行中', waiting_backend: '等待服务器', queued: '排队中', stopping: '正在结束' };
        for (let task of tasks) {
            let row = el('li', 'queue-task');
            let main = el('div', 'queue-task-main');
            main.append(el('strong', '', task.mine ? tr('我的请求 #{0}', task.request_id || task.id) : '其他用户的请求'));
            main.append(el('span', '', tr('{0}，{1} 张', tr(labels[task.state] || '处理中'), task.count)));
            let age = el('span', 'queue-task-age', tr('已用 {0} 分 {1} 秒', Math.floor(task.elapsed_seconds / 60), task.elapsed_seconds % 60));
            row.append(main, age);
            if (task.mine && task.state != 'stopping') {
                let stopping = state.queue.stopping.has(task.id);
                let button = el('button', 'btn btn-sm btn-danger', stopping ? '正在结束…' : '结束任务');
                button.type = 'button';
                button.disabled = stopping;
                button.addEventListener('click', () => cancelQueueTask(task.id, button));
                row.append(button);
            }
            list.append(row);
        }
    }

    /** Cancels one task after the server verifies its authenticated owner. */
    async function cancelQueueTask(id, button = null) {
        if (state.queue.stopping.has(id) || !window.confirm(tr('结束这个任务？已经生成的结果会保留在画廊。'))) { return; }
        state.queue.stopping.add(id);
        if (button) {
            button.disabled = true;
            button.textContent = '正在结束…';
        }
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
    /** Reads the workflow and execution server from a work's metadata (R7.2); null for works without EcomCraft data or unreadable metadata. */
    function galleryOrigin(metadata) {
        if (typeof metadata != 'string' || !metadata) {
            return null;
        }
        let parsed;
        try {
            parsed = JSON.parse(metadata);
        }
        catch (error) {
            return null;
        }
        let extra = parsed?.sui_extra_data || {};
        let server = typeof extra.ecomcraft_execution_server_name == 'string' ? extra.ecomcraft_execution_server_name : '';
        if (!server && !extra.ecomcraft_execution_scope) {
            return null;
        }
        let workflow = typeof extra.ecomcraft_published_workflow == 'string' && extra.ecomcraft_published_workflow ? extra.ecomcraft_published_workflow : tr('标准工作台');
        return server ? tr('{0}｜{1}', workflow, server) : workflow;
    }

    async function loadGallery() {
        if (state.gallery.loading) { return; }
        const g = state.gallery;
        g.loading = true;
        $('gallery-refresh').disabled = true;
        $('gallery-refresh').textContent = '刷新中…';
        const message = $('gallery-message');
        message.hidden = true;
        try {
            if (!state.session) {
                const session = await request('GetNewSession');
                state.session = session.session_id;
                state.userId = session.user_id;
                state.outputAppendUser = !!session.output_append_user;
            }
            // 星标副本存放在 Starred/<原路径>/，比原文件多一层目录；depth:3 会漏掉整个星标目录里的文件。
            const res = await request('ListImages', {
                path: '', depth: 4,
                sortBy: g.sort,
                // 服务器 Date 排序本身已是“最新在前”，再传 reverse 会变成最旧在前，与“最新优先”标签相反。
                sortReverse: false,
                filter: g.search || null
            });
            if (res.error) { throw new ApiError(res.error, 'api_error'); }
            g.items = (res.files || []).map(f => {
                const src = f.src;
                const name = src.split('/').pop();
                const starred = src.startsWith('Starred/');
                return { src, name, url: viewUrl(src), starred, video: /\.(mp4|webm)$/i.test(name), time: typeof f.time == 'number' ? f.time : 0, origin: galleryOrigin(f.metadata) };
            });
            sortGalleryItems();
            renderGallery();
            if (g.revealSrc) {
                let items = visibleGalleryItems();
                let index = items.findIndex(item => item.src == g.revealSrc);
                g.revealSrc = null;
                if (index >= 0) {
                    Lightbox.open(items, index, 'gallery', null);
                }
                else {
                    message.textContent = '列表中还没有这个作品，可能仍在保存。请稍后点击“刷新”。';
                    message.classList.remove('error');
                    message.hidden = false;
                }
            }
        } catch (error) {
            message.textContent = error.message;
            message.classList.add('error');
            message.hidden = false;
            g.items = [];
            renderGallery();
        } finally {
            g.loading = false;
            $('gallery-refresh').disabled = false;
            $('gallery-refresh').textContent = '刷新';
        }
    }

    /** Sorts the gallery items locally so time order and date grouping stay correct regardless of how the server merged folders. */
    function sortGalleryItems() {
        let g = state.gallery;
        if (g.sort == 'Date') {
            g.items.sort((a, b) => (b.time || 0) - (a.time || 0) || a.name.localeCompare(b.name, 'zh'));
            return;
        }
        g.items.sort((a, b) => a.name.localeCompare(b.name, 'zh'));
    }

    /** Returns the local YYYY-MM-DD key that decides which date group an item belongs to. */
    function galleryDateKey(time) {
        if (!time || time <= 0) { return 'unknown'; }
        let d = new Date(time * 1000);
        let pad = (v) => String(v).padStart(2, '0');
        return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
    }

    /** Formats a generation time as a friendly group heading: 今天 / 昨天 / 2026年10月25日 / 未知时间. */
    function galleryDateLabel(time) {
        if (!time || time <= 0) { return '未知时间'; }
        let d = new Date(time * 1000);
        let dayOf = (x) => `${x.getFullYear()}-${x.getMonth()}-${x.getDate()}`;
        let now = new Date();
        if (dayOf(d) == dayOf(now)) { return '今天'; }
        let yesterday = new Date(now.getFullYear(), now.getMonth(), now.getDate() - 1);
        if (dayOf(d) == dayOf(yesterday)) { return '昨天'; }
        return new Intl.DateTimeFormat(I18n.locale(), { dateStyle: 'long' }).format(d);
    }

    /** Formats a generation time as a local HH:mm:ss clock string, or a dash when unknown. */
    function galleryClock(time) {
        if (!time || time <= 0) { return '--:--:--'; }
        let d = new Date(time * 1000);
        let pad = (v) => String(v).padStart(2, '0');
        return `${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
    }

    /** Sets the gallery search text everywhere it lives: the field, the state and the clear button. Does not reload. */
    function setGallerySearch(value) {
        $('gallery-search').value = value;
        state.gallery.search = value.trim();
        $('gallery-search-clear').hidden = value == '';
    }

    /** Applies the starred / all filter and its pressed state. */
    function setGalleryFilter(filter) {
        state.gallery.filter = filter;
        $('gal-all').classList.toggle('selected', filter == 'all');
        $('gal-all').setAttribute('aria-pressed', String(filter == 'all'));
        $('gal-star').classList.toggle('selected', filter == 'star');
        $('gal-star').setAttribute('aria-pressed', String(filter == 'star'));
    }

    /** Returns the gallery items that pass the current star filter. */
    function visibleGalleryItems() {
        let g = state.gallery;
        if (g.filter == 'star') {
            return g.items.filter(item => item.starred);
        }
        return g.items;
    }

    /** Renders the contact sheet. Every action is a real button, so cards, stars and deletion work from the keyboard. */
    function renderGallery() {
        let g = state.gallery;
        let items = visibleGalleryItems();
        let grid = $('gallery-grid');
        let focused = document.activeElement?.closest?.('.sheet-item [data-role]');
        let refocus = focused ? { src: focused.dataset.src, role: focused.dataset.role } : null;
        grid.replaceChildren();
        grid.classList.toggle('selecting', GallerySelection.active);
        let empty = $('gallery-empty');
        $('gallery-count').textContent = g.items.length;
        $('gallery-count').hidden = false;
        $('gallery-total').textContent = g.items.length;
        GallerySelection.updateControls();
        if (!items.length) {
            let filtered = g.items.length > 0 || Boolean(g.search) || g.filter == 'star';
            empty.hidden = false;
            empty.querySelector('h2').textContent = filtered ? '没有符合条件的作品' : '还没有作品';
            empty.querySelector('p').textContent = filtered
                ? '换一个关键词，或清空搜索查看全部作品。'
                : '去创作工作台生成第一张商品图。';
            $('gallery-gocreate').hidden = filtered;
            $('gallery-clear-filter').hidden = !filtered;
            return;
        }
        empty.hidden = true;
        let groupCounts = new Map();
        if (g.sort == 'Date') {
            for (let item of items) {
                let key = galleryDateKey(item.time);
                groupCounts.set(key, (groupCounts.get(key) || 0) + 1);
            }
        }
        let currentGroup = null;
        for (let item of items) {
            if (g.sort == 'Date') {
                let key = galleryDateKey(item.time);
                if (key != currentGroup) {
                    currentGroup = key;
                    let head = el('li', 'sheet-group');
                    head.append(el('span', 'sheet-group-date', galleryDateLabel(item.time)));
                    head.append(el('span', 'sheet-group-count', tr('{0} 项', groupCounts.get(key))));
                    grid.append(head);
                }
            }
            let selected = GallerySelection.isSelected(item);
            let card = el('li', `sheet-item${selected ? ' selected' : ''}`);
            let open = el('button', 'sheet-open');
            open.type = 'button';
            open.dataset.src = item.src;
            open.dataset.role = 'open';
            open.title = item.name;
            if (GallerySelection.active) {
                open.setAttribute('aria-pressed', String(selected));
                open.setAttribute('aria-label', tr('选择 {0}', item.name));
            }
            else {
                open.setAttribute('aria-label', tr('查看 {0}', item.name));
            }
            let media;
            if (item.video) {
                media = el('video');
                media.muted = true;
                media.preload = 'metadata';
                media.playsInline = true;
                media.src = `${item.url}#t=0.1`;
            }
            else {
                media = el('img');
                media.src = item.url;
                media.alt = '';
                media.loading = 'lazy';
                media.decoding = 'async';
                media.width = 180;
                media.height = 180;
            }
            media.setAttribute('aria-hidden', 'true');
            open.append(media);
            open.addEventListener('click', () => {
                if (GallerySelection.active) {
                    GallerySelection.toggle(item);
                    return;
                }
                Lightbox.open(visibleGalleryItems(), visibleGalleryItems().indexOf(item), 'gallery', open);
            });
            let meta = el('div', 'sheet-meta');
            meta.append(el('span', 'sheet-name', item.name));
            if (item.video) {
                meta.append(el('span', 'sheet-kind', '视频'));
            }
            let star = el('button', 'sheet-star', item.starred ? '★' : '☆');
            star.type = 'button';
            star.dataset.src = item.src;
            star.dataset.role = 'star';
            star.setAttribute('aria-pressed', String(item.starred));
            star.setAttribute('aria-label', item.starred ? tr('取消收藏 {0}', item.name) : tr('收藏 {0}', item.name));
            star.disabled = GallerySelection.active;
            star.addEventListener('click', () => toggleStar(item));
            meta.append(star);
            if (GallerySelection.canDelete()) {
                let remove = el('button', 'sheet-delete');
                remove.append(svgIcon('M4 7h16', 'M10 11v6M14 11v6', 'M6 7l1 13h10l1-13', 'M9 7V4h6v3'));
                remove.type = 'button';
                remove.dataset.src = item.src;
                remove.dataset.role = 'delete';
                remove.setAttribute('aria-label', tr('删除 {0}', item.name));
                remove.title = '删除';
                remove.addEventListener('click', () => GallerySelection.deleteOne(item));
                meta.append(remove);
                card.append(el('span', 'sheet-check', '✓'));
            }
            card.prepend(open);
            card.append(meta);
            let time = el('span', 'sheet-time', galleryClock(item.time));
            if (item.time > 0) {
                time.title = new Date(item.time * 1000).toLocaleString();
            }
            card.append(time);
            if (item.origin) {
                let origin = el('span', 'sheet-origin', item.origin);
                origin.title = item.origin;
                card.append(origin);
            }
            grid.append(card);
        }
        if (refocus) {
            let target = Array.from(grid.querySelectorAll(`[data-role="${refocus.role}"]`)).find(node => node.dataset.src == refocus.src);
            target?.focus();
        }
    }

    /** Single and batch deletion of the current user's gallery files through the native DeleteImage API. */
    class GallerySelection {
        static active = false;
        static selected = new Set();
        static deleting = false;

        /** Delete controls follow the account's user_delete_image permission reported by the catalog. */
        static canDelete() {
            return Boolean(state.catalog?.can_delete_images);
        }

        /** Whether the item is part of the current batch selection. */
        static isSelected(item) {
            return this.active && this.selected.has(item.src);
        }

        /** Enters or leaves batch selection; leaving clears the selection. */
        static setActive(active) {
            if (this.deleting) {
                return;
            }
            this.active = active && this.canDelete();
            this.selected.clear();
            renderGallery();
        }

        /** Adds or removes one item from the batch selection. */
        static toggle(item) {
            if (this.deleting) {
                return;
            }
            if (this.selected.has(item.src)) {
                this.selected.delete(item.src);
            }
            else {
                this.selected.add(item.src);
            }
            renderGallery();
        }

        /** Selects every item in the current filtered list. */
        static selectAllVisible() {
            if (this.deleting) {
                return;
            }
            for (let item of visibleGalleryItems()) {
                this.selected.add(item.src);
            }
            renderGallery();
        }

        /** Drops selections whose files are no longer listed, and syncs toolbar state. */
        static updateControls() {
            let listed = new Set(state.gallery.items.map(item => item.src));
            for (let src of Array.from(this.selected)) {
                if (!listed.has(src)) {
                    this.selected.delete(src);
                }
            }
            let allowed = this.canDelete();
            if (!allowed) {
                this.active = false;
            }
            $('gallery-select').hidden = !allowed || this.active || !state.gallery.items.length;
            $('gallery-select').setAttribute('aria-pressed', String(this.active));
            $('gallery-selection').hidden = !this.active;
            $('gallery-selected-count').textContent = tr('已选 {0} 项', this.selected.size);
            $('gallery-delete-selected').disabled = this.deleting || !this.selected.size;
            $('gallery-delete-selected').textContent = this.deleting ? '正在删除…' : '删除所选';
            $('gallery-select-all').disabled = this.deleting;
            $('gallery-select-cancel').disabled = this.deleting;
        }

        /** Deletes one item after confirmation, for the card button and the lightbox. */
        static async deleteOne(item) {
            if (this.deleting || !window.confirm(tr('确定删除「{0}」？', item.name) + (item.starred ? tr('已收藏的作品会同时删除收藏副本和原始文件。') : '') + tr(this.recycleNote()))) {
                return;
            }
            closeLightbox();
            await this.deleteItems([item]);
        }

        /** Deletes the current batch selection after confirmation. */
        static async deleteSelected() {
            let items = state.gallery.items.filter(item => this.selected.has(item.src));
            let hasStarred = items.some(item => item.starred);
            if (this.deleting || !items.length || !window.confirm(tr('确定删除选中的 {0} 个作品？', items.length) + (hasStarred ? tr('已收藏的作品会同时删除收藏副本和原始文件。') : '') + tr(this.recycleNote()))) {
                return;
            }
            await this.deleteItems(items);
            this.active = false;
            this.selected.clear();
            renderGallery();
        }

        /** Shared confirmation hint; whether files go to the recycle bin depends on the server setting. */
        static recycleNote() {
            return '若服务器启用了回收站，可在回收站找回；否则将永久删除。';
        }

        /** Deletes files one at a time so a failure never hides which items were removed, then refreshes the list. */
        static async deleteItems(items) {
            this.deleting = true;
            this.updateControls();
            let deleted = 0;
            let failures = [];
            try {
                for (let item of items) {
                    try {
                        await request('DeleteImage', { path: item.src });
                        // 已收藏的作品在画廊里是 Starred/ 下的副本；把原始文件一并删除，否则它会继续留在“全部”里。
                        let rawSrc = item.starred ? item.src.slice('Starred/'.length) : null;
                        if (rawSrc) {
                            try {
                                await request('DeleteImage', { path: rawSrc });
                            }
                            catch (rawError) {
                                if (!/does not exist/i.test(rawError.message || '')) { throw rawError; }
                            }
                        }
                        deleted++;
                        state.gallery.items = state.gallery.items.filter(other => other.src != item.src && other.src != rawSrc);
                        this.selected.delete(item.src);
                    }
                    catch (error) {
                        failures.push(item.name);
                        if (error.code == 'invalid_session_id' || error.code == 'bad_permissions' || error.code == 'transport_error') {
                            break;
                        }
                    }
                }
            }
            finally {
                this.deleting = false;
            }
            renderGallery();
            await loadGallery();
            let message = $('gallery-message');
            let skipped = items.length - deleted - failures.length;
            if (!failures.length) {
                message.textContent = tr('已删除 {0} 个作品。', deleted);
                message.classList.remove('error');
            }
            else {
                message.textContent = tr('已删除 {0} 个作品，{1} 个删除失败{2}：', deleted, failures.length, skipped > 0 ? tr('，{0} 个未处理', skipped) : '')
                    + failures.slice(0, 3).join(tr('、')) + (failures.length > 3 ? ' ' + tr('等') : '') + tr('。请刷新后重试，或检查账号的删除权限。');
                message.classList.add('error');
            }
            message.hidden = false;
        }
    }

    /** Stars or unstars one file, reloads the list (paths move into or out of Starred/), and optionally announces the result. */
    async function toggleStar(item, announce = false) {
        try {
            let res = await request('ToggleImageStarred', { path: item.src });
            await loadGallery();
            if (announce) {
                let message = $('gallery-message');
                message.textContent = res.new_state ? tr('已收藏「{0}」。', item.name) : tr('已取消收藏「{0}」。', item.name);
                message.classList.remove('error');
                message.hidden = false;
            }
        }
        catch (error) {
            let message = $('gallery-message');
            message.textContent = tr('收藏没有成功：{0}', error.message || tr('请刷新后重试'));
            message.classList.add('error');
            message.hidden = false;
        }
    }

    /* ============================================================
       Workflow review
       ============================================================ */
    /** Lists discovered workflows for submitters and reviewers and runs the submit and review dialogs; the server checks every permission again. */
    class WorkflowReviewView {
        static items = [];
        static filter = 'todo';
        static loading = false;
        static detail = null;
        static detailRequest = null;
        static controls = new Map();
        static saving = false;
        static submitTarget = null;
        static stateLabels = { pending: '待审核', rereview: '待重新审核', published: '已发布', rejected: '已驳回', unpublished: '已下架', invalid: '无法导入' };
        static filters = { todo: ['pending', 'rereview'], published: ['published', 'rereview'], closed: ['rejected', 'unpublished'], invalid: ['invalid'], all: null };

        /** Whether the account may open this view. */
        static available() {
            return Boolean(state.catalog?.can_review_workflows || state.catalog?.can_submit_workflows);
        }

        /** Whether the account reviews rather than only submits. */
        static reviewer() {
            return Boolean(state.catalog?.can_review_workflows);
        }

        /** Shows the navigation entry and page texts for the account's permission; leaves the view when access was removed. */
        static updateAccess() {
            $('nav-workflows').hidden = !this.available();
            $('workflows-title').textContent = this.reviewer() ? '工作流审核' : '提交工作流审核';
            $('workflows-intro').textContent = this.reviewer()
                ? '执行服务器上保存的 ComfyUI 工作流会自动出现在这里。通过审核时会保存一份快照，用户只能调用已发布的快照；来源文件修改后需要重新审核，通过前用户继续使用旧快照。'
                : '在执行服务器的 ComfyUI 中保存工作流后，可以在这里提交审核。审核通过后，它会出现在创作页的工作流列表中。';
            if (state.catalog && !this.available() && state.view == 'workflows') {
                location.hash = 'create';
            }
        }

        /** Opens the view: waits for the catalog when it is still loading, otherwise loads the list. */
        static enter() {
            if (!state.catalog) {
                return;
            }
            this.updateAccess();
            if (this.available()) {
                this.load();
            }
        }

        /** Reads the list allowed for this account. */
        static async load() {
            if (this.loading || !this.available()) {
                return;
            }
            this.loading = true;
            $('workflows-refresh').disabled = true;
            try {
                let result = await request(this.reviewer() ? 'EcomCraftStudioReviewList' : 'EcomCraftStudioWorkflowSubmissions');
                this.items = Array.isArray(result.workflows) ? result.workflows : [];
                $('workflows-message').hidden = true;
                $('workflows-updated').textContent = tr('共 {0} 个，更新于 {1}', this.items.length, new Intl.DateTimeFormat(I18n.locale(), { timeStyle: 'medium' }).format(new Date()));
                this.render();
            }
            catch (error) {
                this.showBanner(error.message || '工作流列表读取失败，请稍后重试。', true);
            }
            finally {
                this.loading = false;
                $('workflows-refresh').disabled = false;
            }
        }

        /** Refreshes in the background while the view is open and no dialog is in use. */
        static poll() {
            if (state.view == 'workflows' && !document.hidden && $('review-modal').hidden && $('submit-modal').hidden) {
                this.load();
            }
        }

        /** Shows a page-level result or error. */
        static showBanner(text, isError) {
            $('workflows-message').textContent = text;
            $('workflows-message').classList.toggle('error', Boolean(isError));
            $('workflows-message').hidden = false;
        }

        /** Selects a status filter. */
        static setFilter(filter) {
            this.filter = Object.prototype.hasOwnProperty.call(this.filters, filter) ? filter : 'todo';
            this.render();
        }

        /** Renders the filtered list and the count of workflows waiting for review. */
        static render() {
            let list = $('workflows-list');
            list.replaceChildren();
            let wanted = this.filters[this.filter];
            let shown = this.items.filter(item => !wanted || wanted.includes(item.state));
            for (let item of shown) {
                list.append(this.row(item));
            }
            $('workflows-empty').hidden = shown.length > 0;
            $('workflows-empty').textContent = this.items.length
                ? '没有符合筛选条件的工作流。'
                : '还没有发现工作流。请在执行服务器的 ComfyUI 中保存工作流，约 30 秒后点击“刷新”。';
            let todo = this.items.filter(item => ['pending', 'rereview'].includes(item.state)).length;
            $('workflows-count').textContent = todo;
            $('workflows-count').hidden = !this.reviewer() || todo == 0;
            for (let button of $('workflows-filter').querySelectorAll('[data-filter]')) {
                let selected = button.dataset.filter == this.filter;
                button.classList.toggle('selected', selected);
                button.setAttribute('aria-pressed', String(selected));
            }
        }

        /** Formats a Unix millisecond time for display. */
        static time(ms) {
            return ms ? new Intl.DateTimeFormat(I18n.locale(), { dateStyle: 'short', timeStyle: 'short' }).format(new Date(ms)) : '';
        }

        /** Summarizes which servers can run a workflow, e.g. "仅 A（B：缺少节点 X）". */
        static runnableText(runnable) {
            let servers = Array.isArray(runnable) ? runnable : [];
            if (!servers.length) {
                return '可运行服务器：还没有登记执行服务器';
            }
            let ok = servers.filter(server => server.ok).map(server => server.name);
            let bad = servers.filter(server => !server.ok).map(server => tr('{0}：{1}', server.name, (server.problems || []).join(tr('、')) || tr('不可用')));
            if (!ok.length) {
                return tr('没有服务器能运行（{0}）', bad.join(tr('；')));
            }
            return bad.length ? tr('可运行服务器：仅 {0}（{1}）', ok.join(tr('、')), bad.join(tr('；'))) : tr('可运行服务器：{0}', ok.join(tr('、')));
        }

        /** Builds one list row with the actions this account may take. */
        static row(item) {
            let row = el('li', 'review-item');
            let main = el('div', 'review-item-main');
            main.append(el('strong', '', item.name || item.id));
            let source = item.source_missing ? '（来源文件已不存在）' : item.source_online ? '' : '（来源离线）';
            let line = el('span');
            let attention = ['pending', 'rereview', 'invalid'].includes(item.state);
            line.append(el('span', `review-state${attention ? ' is-attention' : ''}`, this.stateLabels[item.state] || '未知状态'),
                document.createTextNode(tr('，来源：{0}{1}', item.server || tr('未知'), tr(source))));
            main.append(line);
            if (item.problem) {
                main.append(el('span', '', item.problem));
            }
            if (this.reviewer()) {
                if (item.state != 'invalid') {
                    main.append(el('span', '', this.runnableText(item.runnable)));
                }
                if (item.submitted_by) {
                    main.append(el('span', '', tr('{0} 于 {1} 提交', item.submitted_by, this.time(item.submitted_at)) + (item.submit_note ? tr('：{0}', item.submit_note) : '')));
                }
                if (item.reject_reason && ['rejected', 'published'].includes(item.state)) {
                    main.append(el('span', '', item.state == 'published' ? tr('新版本驳回原因：{0}', item.reject_reason) : tr('驳回原因：{0}', item.reject_reason)));
                }
                if (item.published_at) {
                    main.append(el('span', '', tr('快照发布于 {0}', this.time(item.published_at))));
                }
            }
            else {
                if (item.mine && item.submitted_at) {
                    main.append(el('span', '', tr('我于 {0} 提交', this.time(item.submitted_at)) + (item.submit_note ? tr('：{0}', item.submit_note) : '')));
                }
                if (item.reject_reason) {
                    main.append(el('span', '', tr('驳回原因：{0}', item.reject_reason)));
                }
            }
            let actions = el('div', 'review-item-actions');
            if (this.reviewer() && item.state != 'invalid') {
                let label = item.state == 'published' ? '查看配置' : item.state == 'rereview' ? '重新审核' : '审核';
                let button = el('button', item.state == 'published' ? 'btn btn-sm' : 'btn btn-sm btn-primary', label);
                button.type = 'button';
                button.addEventListener('click', () => this.openReview(item, button));
                actions.append(button);
            }
            if (!this.reviewer() && item.can_submit) {
                let button = el('button', 'btn btn-sm', item.mine ? '重新提交' : '提交审核');
                button.type = 'button';
                button.addEventListener('click', () => this.openSubmit(item, button));
                actions.append(button);
            }
            row.append(main, actions);
            return row;
        }

        /** Opens the review dialog and loads the parameters of the version under review. */
        static async openReview(item, opener) {
            let modal = $('review-modal');
            this.detail = null;
            this.controls = new Map();
            $('review-title').textContent = tr('审核：{0}', item.name || item.id);
            $('review-meta').textContent = tr('来源 {0}，{1}', item.server || tr('未知'), tr(this.stateLabels[item.state] || ''));
            $('review-loading').textContent = '正在读取工作流参数…';
            $('review-loading').hidden = false;
            $('review-content').hidden = true;
            $('review-message').hidden = true;
            $('review-reason').value = '';
            this.updateReviewButtons();
            modal.hidden = false;
            DialogFocus.enter(modal, opener, $('review-close'));
            this.detailRequest?.abort();
            let controller = new AbortController();
            this.detailRequest = controller;
            try {
                let detail = await request('EcomCraftStudioReviewDetail', { workflow_id: item.id }, controller.signal);
                if (this.detailRequest != controller || modal.hidden) {
                    return;
                }
                this.detail = detail;
                this.renderDetail(detail);
            }
            catch (error) {
                if (!controller.signal.aborted) {
                    $('review-loading').textContent = error.message || '读取失败，请关闭后重试。';
                }
            }
            finally {
                if (this.detailRequest == controller) {
                    this.detailRequest = null;
                }
                this.updateReviewButtons();
            }
        }

        /** Closes the review dialog and drops any request still loading. */
        static closeReview() {
            if ($('review-modal').hidden) {
                return;
            }
            this.detailRequest?.abort();
            this.detailRequest = null;
            $('review-modal').hidden = true;
            DialogFocus.leave($('review-modal'));
        }

        /** Shows a result or error inside the review dialog. */
        static reviewMessage(text, isError) {
            $('review-message').textContent = text;
            $('review-message').classList.toggle('is-error', Boolean(isError));
            $('review-message').hidden = !text;
        }

        /** Enables the actions the current state allows; the server enforces the same rules. */
        static updateReviewButtons() {
            let detail = this.detail;
            let stateName = detail?.state;
            let canReject = ['pending', 'rereview'].includes(stateName);
            $('review-approve').disabled = !detail || this.saving;
            $('review-approve').textContent = stateName == 'published' ? '保存配置' : stateName == 'rereview' ? '通过新版本' : '通过并发布';
            $('review-reject').hidden = !canReject;
            $('review-reject').disabled = !detail || this.saving;
            $('review-reason').closest('.fld').hidden = !canReject;
            $('review-unpublish').hidden = !['published', 'rereview'].includes(stateName);
            $('review-unpublish').disabled = !detail || this.saving;
        }

        /** Fills the dialog from a detail response. */
        static renderDetail(detail) {
            let kind = detail.kind == 'video' ? tr('视频工作流') : tr('图片工作流');
            $('review-meta').textContent = tr('来源 {0}{1}，{2}，{3}', detail.server || tr('未知'), detail.source_online ? '' : tr('（离线）'), tr(this.stateLabels[detail.state] || ''), kind);
            let notes = [];
            if (detail.state == 'rereview') {
                notes.push('来源文件已修改。下面是新版本，已沿用原快照的参数配置；通过前用户继续使用旧快照。');
            }
            if (detail.state == 'published' && !detail.reviewing_source) {
                notes.push('来源文件暂时不可用或新版本已驳回，这里修改的是已发布快照的参数配置。');
            }
            if (detail.submitted_by) {
                notes.push(tr('{0} 提交说明：{1}', detail.submitted_by, detail.submit_note || tr('无')));
            }
            if (detail.reject_reason && detail.state == 'rejected') {
                notes.push(tr('上次驳回原因：{0}', detail.reject_reason));
            }
            $('review-submission').textContent = notes.join(' ');
            $('review-submission').hidden = !notes.length;
            let servers = $('review-servers');
            servers.replaceChildren();
            for (let server of detail.runnable || []) {
                let text = server.ok ? tr('{0}：可以运行', server.name) : tr('{0}：{1}', server.name, (server.problems || []).join(tr('、')) || tr('不可用'));
                servers.append(el('li', `server-state ${server.ok ? 'is-idle' : 'is-down'}`, text));
            }
            if (!servers.children.length) {
                servers.append(el('li', '', '还没有登记执行服务器。'));
            }
            $('review-description').value = detail.description || '';
            let list = $('review-params');
            list.replaceChildren();
            this.controls = new Map();
            let index = 0;
            for (let param of detail.parameters || []) {
                list.append(this.paramRow(param, index++));
            }
            if (!list.children.length) {
                list.append(el('li', 'review-param', '此工作流没有可配置的参数。'));
            }
            $('review-loading').hidden = true;
            $('review-content').hidden = false;
        }

        /** Builds the editors for one parameter: visibility, display name, default and range or options. */
        static paramRow(param, index) {
            let row = el('li', 'review-param');
            let controls = { param };
            let visibleWrap = el('label', 'review-param-visible');
            controls.visible = document.createElement('input');
            controls.visible.type = 'checkbox';
            controls.visible.checked = param.visible != false;
            controls.visible.disabled = Boolean(param.locked);
            visibleWrap.append(controls.visible, document.createTextNode('显示'));
            if (param.locked) {
                visibleWrap.title = '页面固定显示此项';
            }

            let nameWrap = el('div', 'review-param-name');
            controls.name = document.createElement('input');
            controls.name.type = 'text';
            controls.name.maxLength = 40;
            controls.name.value = param.name || param.original_name || param.id;
            controls.name.setAttribute('aria-label', tr('参数显示名：{0}', param.original_name || param.id));
            nameWrap.append(controls.name);
            if (param.name != param.original_name) {
                nameWrap.append(el('p', 'hint', tr('原名：{0}', param.original_name)));
            }
            if (param.form == 'prompt' || param.form == 'negativeprompt') {
                nameWrap.append(el('p', 'hint', '用户每次生成时填写'));
            }

            let valueWrap = el('div', 'review-param-value');
            let valueId = `review-param-${index}`;
            let valueLabel = el('label', 'visually-hidden', tr('默认值：{0}', param.original_name || param.id));
            valueLabel.htmlFor = valueId;
            valueWrap.append(valueLabel);
            let type = String(param.type || 'text');
            let current = param.default == null ? '' : String(param.default);
            if (type == 'dropdown') {
                let all = Array.isArray(param.all_values) ? param.all_values.map(String) : [];
                let chosen = new Set((Array.isArray(param.values) ? param.values : all).map(String));
                controls.value = document.createElement('select');
                for (let option of all) {
                    controls.value.append(new Option(option, option));
                }
                NativeWorkflowForm.setValue(controls.value, current);
                valueWrap.append(controls.value);
                let details = el('details', 'review-options');
                details.append(el('summary', '', tr('可选项（{0}/{1}）', chosen.size, all.length)));
                controls.options = [];
                for (let option of all) {
                    let label = el('label');
                    let box = document.createElement('input');
                    box.type = 'checkbox';
                    box.value = option;
                    box.checked = chosen.has(option);
                    box.addEventListener('change', () => {
                        let count = controls.options.filter(item => item.checked).length;
                        details.querySelector('summary').textContent = tr('可选项（{0}/{1}）', count, all.length);
                    });
                    controls.options.push(box);
                    label.append(box, document.createTextNode(option));
                    details.append(label);
                }
                valueWrap.append(details);
            }
            else if (type == 'integer' || type == 'decimal') {
                controls.value = document.createElement('input');
                controls.value.type = 'number';
                controls.value.step = type == 'integer' ? '1' : 'any';
                controls.value.value = current;
                valueWrap.append(controls.value);
                let range = el('div', 'review-range');
                controls.min = document.createElement('input');
                controls.max = document.createElement('input');
                for (let [input, value, label] of [[controls.min, param.min, '最小值'], [controls.max, param.max, '最大值']]) {
                    input.type = 'number';
                    input.step = controls.value.step;
                    input.value = value == null || (Number(param.min) == 0 && Number(param.max) == 0) ? '' : String(value);
                    input.placeholder = label;
                    input.setAttribute('aria-label', tr('{0}：{1}', tr(label), param.original_name || param.id));
                }
                range.append(document.createTextNode('范围'), controls.min, document.createTextNode('–'), controls.max);
                valueWrap.append(range);
                if (!(Number(param.original_min) == 0 && Number(param.original_max) == 0)) {
                    valueWrap.append(el('p', 'hint', tr('工作流允许 {0}–{1}', param.original_min, param.original_max)));
                }
            }
            else {
                controls.value = document.createElement(type == 'text' ? 'textarea' : 'input');
                if (type == 'text') {
                    controls.value.rows = 2;
                }
                else {
                    controls.value.type = 'text';
                }
                controls.value.value = current;
                valueWrap.append(controls.value);
            }
            controls.value.id = valueId;
            if (param.description) {
                valueWrap.append(el('p', 'hint', param.description));
            }
            row.append(visibleWrap, nameWrap, valueWrap);
            this.controls.set(param.id, controls);
            return row;
        }

        /** Reads the dialog into the configuration the server expects, or returns an error message. */
        static collectConfig() {
            let config = {};
            for (let [id, controls] of this.controls) {
                let param = controls.param;
                let label = param.original_name || id;
                let setting = {};
                if (!param.locked) {
                    setting.visible = controls.visible.checked;
                }
                let name = controls.name.value.trim();
                if (!name) {
                    return tr('请填写参数“{0}”的显示名。', label);
                }
                setting.name = name;
                let value = controls.value.value;
                if (param.type == 'integer' || param.type == 'decimal') {
                    if (value === '' && !param.locked) {
                        return tr('请填写参数“{0}”的默认值。', label);
                    }
                    if (value !== '') {
                        setting.default = Number(value);
                    }
                    let min = controls.min.value;
                    let max = controls.max.value;
                    if ((min === '') != (max === '')) {
                        return tr('参数“{0}”的范围需要同时填写最小值和最大值，或都留空。', label);
                    }
                    if (min !== '') {
                        setting.min = Number(min);
                        setting.max = Number(max);
                    }
                }
                else {
                    setting.default = value;
                }
                if (param.type == 'dropdown') {
                    setting.values = controls.options.filter(box => box.checked).map(box => box.value);
                    if (!setting.values.length) {
                        return tr('参数“{0}”至少要保留一个可选项。', label);
                    }
                    if (!setting.values.includes(value)) {
                        return tr('参数“{0}”的默认值必须是保留的可选项之一。', label);
                    }
                }
                config[id] = setting;
            }
            return config;
        }

        /** Sends one decision; the list and the creation page's workflow list refresh afterwards. */
        static async decide(action) {
            let detail = this.detail;
            if (!detail || this.saving) {
                return;
            }
            let body = { workflow_id: detail.id, action };
            if (action == 'reject') {
                let reason = $('review-reason').value.trim();
                if (!reason) {
                    this.reviewMessage('驳回时请填写原因，提交人会看到它。', true);
                    $('review-reason').focus();
                    return;
                }
                body.reason = reason;
            }
            if (action == 'unpublish' && !window.confirm(tr('下架后用户将不能再调用“{0}”。确定下架吗？', detail.name))) {
                return;
            }
            if (action == 'approve') {
                let config = this.collectConfig();
                if (typeof config == 'string') {
                    this.reviewMessage(config, true);
                    return;
                }
                body.parameters = config;
                body.description = $('review-description').value.trim();
            }
            this.saving = true;
            this.updateReviewButtons();
            this.reviewMessage('正在保存…', false);
            try {
                await request('EcomCraftStudioReviewDecide', body);
                let done = { approve: detail.state == 'published' ? '“{0}”的配置已保存。' : '“{0}”已发布。', reject: '“{0}”已驳回。', unpublish: '“{0}”已下架。' }[action];
                this.closeReview();
                await this.load();
                this.showBanner(tr(done, detail.name), false);
                WorkflowCatalogRefresh.refresh();
            }
            catch (error) {
                this.reviewMessage(error.message || '保存失败，请稍后重试。', true);
            }
            finally {
                this.saving = false;
                this.updateReviewButtons();
            }
        }

        /** Opens the submit dialog for one workflow. */
        static openSubmit(item, opener) {
            this.submitTarget = item;
            $('submit-meta').textContent = (item.server || tr('未知')) + '/' + (item.name || item.id);
            $('submit-note').value = item.submit_note || '';
            $('submit-message').hidden = true;
            $('submit-confirm').disabled = false;
            $('submit-modal').hidden = false;
            DialogFocus.enter($('submit-modal'), opener, $('submit-note'));
        }

        /** Closes the submit dialog. */
        static closeSubmit() {
            if ($('submit-modal').hidden) {
                return;
            }
            $('submit-modal').hidden = true;
            DialogFocus.leave($('submit-modal'));
        }

        /** Submits the workflow with the note. */
        static async submit() {
            let item = this.submitTarget;
            if (!item || $('submit-confirm').disabled) {
                return;
            }
            $('submit-confirm').disabled = true;
            $('submit-message').textContent = '正在提交…';
            $('submit-message').classList.remove('is-error');
            $('submit-message').hidden = false;
            try {
                await request('EcomCraftStudioSubmitWorkflow', { workflow_id: item.id, note: $('submit-note').value.trim() });
                this.closeSubmit();
                await this.load();
                this.showBanner(tr('已提交“{0}”，等待审核。', item.name || item.id), false);
            }
            catch (error) {
                $('submit-message').textContent = error.message || '提交失败，请稍后重试。';
                $('submit-message').classList.add('is-error');
            }
            finally {
                $('submit-confirm').disabled = false;
            }
        }
    }

    /* ---------- Dialog focus ---------- */
    /** Moves focus into an open dialog, keeps Tab inside it and returns focus to the opener when it closes. */
    class DialogFocus {
        static openers = new Map();

        /** Focusable descendants that are currently visible. */
        static focusable(dialog) {
            if (typeof dialog?.querySelectorAll != 'function') {
                return [];
            }
            return Array.from(dialog.querySelectorAll('a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), video[controls]'))
                .filter(node => !node.closest('[hidden]'));
        }

        /** Remembers the opener and focuses the first control, or a given one. */
        static enter(dialog, opener, initial = null) {
            this.openers.set(dialog, opener || document.activeElement);
            let target = initial || this.focusable(dialog)[0];
            if (typeof target?.focus == 'function') {
                target.focus();
            }
        }

        /** Restores focus to the element that opened the dialog, when it is still on the page. */
        static leave(dialog) {
            let opener = this.openers.get(dialog);
            this.openers.delete(dialog);
            if (opener?.isConnected && typeof opener.focus == 'function') {
                opener.focus();
            }
        }

        /** Wraps Tab and Shift+Tab at the dialog edges. */
        static trap(dialog, event) {
            if (event.key != 'Tab') {
                return;
            }
            let nodes = this.focusable(dialog);
            if (!nodes.length) {
                return;
            }
            let first = nodes[0];
            let last = nodes[nodes.length - 1];
            if (event.shiftKey && document.activeElement == first) {
                event.preventDefault();
                last.focus();
            }
            else if (!event.shiftKey && document.activeElement == last) {
                event.preventDefault();
                first.focus();
            }
        }
    }

    /* ---------- Lightbox ---------- */
    /** Full-size preview for gallery items and fresh results, with previous / next navigation. */
    class Lightbox {
        static items = [];
        static index = 0;
        static source = 'gallery';
        /** Zoom limits: 1 = fitted size, maxScale caps how far a preview can be blown up. */
        static minScale = 1;
        static maxScale = 8;
        /** Zoom state of the shown media: scale plus pixel offsets from the fitted center. */
        static zoom = { scale: 1, x: 0, y: 0 };
        /** Live pointers for drag / pinch gestures; drag and pinch hold the active gesture, if any. */
        static pointers = new Map();
        static drag = null;
        static pinch = null;

        /** Opens the item at index within a list; source 'result' hides gallery-only actions. */
        static open(items, index, source, opener) {
            if (!items.length || index < 0) {
                return;
            }
            let wasHidden = $('lightbox').hidden;
            this.items = items;
            this.source = source;
            this.show(index);
            $('lightbox').hidden = false;
            if (wasHidden) {
                DialogFocus.enter($('lightbox'), opener, $('lb-close'));
            }
        }

        /** Renders one item and syncs every action with it. */
        static show(index) {
            this.index = Math.max(0, Math.min(index, this.items.length - 1));
            let item = this.items[this.index];
            let gallery = this.source == 'gallery';
            state.gallery.current = gallery ? item : null;
            let media = $('lightbox-media');
            media.replaceChildren();
            let node = el(item.video ? 'video' : 'img');
            node.src = item.url;
            if (item.video) {
                node.controls = true;
                node.playsInline = true;
            }
            else {
                node.alt = item.name;
                // Native image drag ghosting cancels pointer events and breaks pan-to-drag.
                node.draggable = false;
            }
            media.append(node);
            this.resetZoom();
            let position = this.items.length > 1 ? tr('（{0} / {1}）', this.index + 1, this.items.length) : '';
            $('lightbox-name').textContent = `${item.name}${position}`;
            $('lb-download').href = item.url;
            $('lb-download').setAttribute('download', item.name);
            $('lb-star').hidden = !gallery;
            $('lb-star').textContent = item.starred ? '取消收藏' : '收藏';
            $('lb-star').setAttribute('aria-pressed', String(Boolean(item.starred)));
            $('lb-delete').hidden = !gallery || !GallerySelection.canDelete();
            $('lb-gallery').hidden = gallery || !item.src;
            $('lb-edit').hidden = Boolean(item.video);
            $('lb-prev').disabled = this.index == 0;
            $('lb-next').disabled = this.index >= this.items.length - 1;
            $('lb-prev').hidden = this.items.length < 2;
            $('lb-next').hidden = this.items.length < 2;
        }

        /** Moves by one item; stays put at either end. */
        static step(offset) {
            let next = this.index + offset;
            if (next >= 0 && next < this.items.length) {
                this.show(next);
            }
        }

        /** The img or video element currently on display, if any. */
        static mediaNode() {
            return $('lightbox-media').firstElementChild;
        }

        /** Applies the current zoom to the shown media and refreshes the zoom controls. */
        static applyZoom() {
            let box = $('lightbox-media');
            let node = box.firstElementChild;
            let scale = this.zoom.scale;
            if (node) {
                node.style.transform = scale > 1 ? `translate(${this.zoom.x}px, ${this.zoom.y}px) scale(${scale})` : '';
            }
            box.classList.toggle('zoomed', scale > 1);
            $('lb-zoom-reset').textContent = `${Math.round(scale * 100)}%`;
            $('lb-zoom-in').disabled = scale >= this.maxScale;
            $('lb-zoom-out').disabled = scale <= this.minScale;
            $('lb-zoom-reset').disabled = scale <= this.minScale;
        }

        /** Keeps a zoomed media inside the area it occupied at 100%, so panning cannot push it out of view. */
        static clampPan() {
            let node = this.mediaNode();
            if (!node) {
                return;
            }
            let maxX = node.offsetWidth * (this.zoom.scale - 1) / 2;
            let maxY = node.offsetHeight * (this.zoom.scale - 1) / 2;
            this.zoom.x = Math.max(-maxX, Math.min(maxX, this.zoom.x));
            this.zoom.y = Math.max(-maxY, Math.min(maxY, this.zoom.y));
        }

        /** Zooms to an exact scale while keeping the point at (originX, originY) — offset from the media center — visually anchored. */
        static zoomTo(scale, originX = 0, originY = 0) {
            let old = this.zoom.scale;
            let next = Math.max(this.minScale, Math.min(this.maxScale, scale));
            if (old > 1) {
                let cx = (originX - this.zoom.x) / old;
                let cy = (originY - this.zoom.y) / old;
                this.zoom.x = originX - cx * next;
                this.zoom.y = originY - cy * next;
            }
            else {
                this.zoom.x = originX * (1 - next);
                this.zoom.y = originY * (1 - next);
            }
            this.zoom.scale = next;
            if (next <= 1) {
                this.zoom.x = 0;
                this.zoom.y = 0;
            }
            this.clampPan();
            this.applyZoom();
        }

        /** Zooms in or out by a factor; used by the buttons, keyboard and mouse wheel. */
        static zoomBy(factor, originX = 0, originY = 0) {
            this.zoomTo(this.zoom.scale * factor, originX, originY);
        }

        /** Returns the media to its fitted size and centered position. */
        static resetZoom() {
            this.zoom.scale = 1;
            this.zoom.x = 0;
            this.zoom.y = 0;
            this.applyZoom();
        }

        /** Shifts a zoomed media by (dx, dy) pixels within the reachable range. */
        static panBy(dx, dy) {
            if (this.zoom.scale <= 1) {
                return;
            }
            this.zoom.x += dx;
            this.zoom.y += dy;
            this.clampPan();
            this.applyZoom();
        }

        /** Mouse wheel zooms toward the cursor; the page never scrolls behind the open preview. */
        static wheel(event) {
            if ($('lightbox').hidden) {
                return;
            }
            event.preventDefault();
            let box = $('lightbox-media').getBoundingClientRect();
            let factor = Math.exp(-event.deltaY * (event.deltaMode == 1 ? 0.05 : 0.0015));
            this.zoomBy(factor, event.clientX - box.left - box.width / 2, event.clientY - box.top - box.height / 2);
        }

        /** Double click or double tap toggles between fitted size and a closer look at the click point. */
        static doubleClick(event) {
            if ($('lightbox').hidden) {
                return;
            }
            event.preventDefault();
            let box = $('lightbox-media').getBoundingClientRect();
            let x = event.clientX - box.left - box.width / 2;
            let y = event.clientY - box.top - box.height / 2;
            if (this.zoom.scale > 1) {
                this.resetZoom();
            }
            else {
                this.zoomTo(2.5, x, y);
            }
        }

        /** Tracks a press on the media for later dragging; presses on native video controls pass through untouched. */
        static pointerDown(event) {
            if ($('lightbox').hidden || event.composedPath()[0] !== this.mediaNode()) {
                return;
            }
            this.pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
            if (this.pointers.size == 2 && event.pointerType != 'mouse') {
                let pts = [...this.pointers.values()];
                this.pinch = {
                    distance: Math.hypot(pts[0].x - pts[1].x, pts[0].y - pts[1].y),
                    scale: this.zoom.scale
                };
                this.drag = null;
            }
            else if (this.pointers.size == 1 && this.zoom.scale > 1) {
                this.drag = { x: event.clientX, y: event.clientY, baseX: this.zoom.x, baseY: this.zoom.y, moved: false };
            }
        }

        /** Updates pinch zoom or drag panning while a tracked press is moving. */
        static pointerMove(event) {
            if (!this.pointers.has(event.pointerId)) {
                return;
            }
            this.pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
            if (this.pinch && this.pointers.size >= 2) {
                let pts = [...this.pointers.values()];
                let dist = Math.hypot(pts[0].x - pts[1].x, pts[0].y - pts[1].y);
                let box = $('lightbox-media').getBoundingClientRect();
                let midX = (pts[0].x + pts[1].x) / 2 - box.left - box.width / 2;
                let midY = (pts[0].y + pts[1].y) / 2 - box.top - box.height / 2;
                if (this.pinch.distance > 0) {
                    this.zoomTo(this.pinch.scale * dist / this.pinch.distance, midX, midY);
                }
                return;
            }
            if (this.drag) {
                let dx = event.clientX - this.drag.x;
                let dy = event.clientY - this.drag.y;
                if (!this.drag.moved && Math.hypot(dx, dy) > 3) {
                    // Only grab the pointer once this is clearly a drag, so clicks and video controls keep working.
                    this.drag.moved = true;
                    $('lightbox-media').classList.add('dragging');
                    try {
                        this.mediaNode().setPointerCapture(event.pointerId);
                    }
                    catch (error) {
                        // Losing capture only means the drag ends early; the view stays consistent.
                    }
                }
                if (this.drag.moved) {
                    this.zoom.x = this.drag.baseX + dx;
                    this.zoom.y = this.drag.baseY + dy;
                    this.clampPan();
                    this.applyZoom();
                }
            }
        }

        /** Ends drag or pinch tracking for one pointer; a pinch leftover can keep dragging on its own. */
        static pointerEnd(event) {
            if (!this.pointers.delete(event.pointerId)) {
                return;
            }
            this.drag = null;
            $('lightbox-media').classList.remove('dragging');
            if (this.pointers.size < 2) {
                this.pinch = null;
            }
            if (this.pointers.size == 1 && this.zoom.scale > 1) {
                let only = [...this.pointers.values()][0];
                this.drag = { x: only.x, y: only.y, baseX: this.zoom.x, baseY: this.zoom.y, moved: false };
            }
        }

        /** The item currently shown, or null when closed. */
        static current() {
            return $('lightbox').hidden ? null : this.items[this.index];
        }

        /** Closes the preview and stops any playing video. */
        static close() {
            if ($('lightbox').hidden) {
                return;
            }
            $('lightbox').hidden = true;
            $('lightbox-media').replaceChildren();
            this.pointers.clear();
            this.drag = null;
            this.pinch = null;
            this.resetZoom();
            state.gallery.current = null;
            DialogFocus.leave($('lightbox'));
        }

        /** Keyboard: arrows move between items (or pan while zoomed), + / − zoom, 0 resets, Escape un-zooms then closes, Tab stays inside. Arrows inside a video or form control stay with that control (seeking, cursor movement). */
        static keydown(event) {
            if ($('lightbox').hidden) {
                return false;
            }
            let arrows = ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key);
            let tag = event.target?.tagName;
            if (arrows && ['INPUT', 'SELECT', 'TEXTAREA'].includes(tag)) {
                return true;
            }
            if (event.key == 'Escape') {
                if (this.zoom.scale > 1) {
                    this.resetZoom();
                }
                else {
                    this.close();
                }
            }
            else if (event.key == '+' || event.key == '=') {
                this.zoomBy(1.25);
            }
            else if (event.key == '-' || event.key == '_') {
                this.zoomBy(0.8);
            }
            else if (event.key == '0') {
                this.resetZoom();
            }
            else if (arrows && this.zoom.scale > 1 && tag != 'VIDEO') {
                // While zoomed the arrows pan the media instead of switching items.
                let dx = event.key == 'ArrowLeft' ? -48 : event.key == 'ArrowRight' ? 48 : 0;
                let dy = event.key == 'ArrowUp' ? -48 : event.key == 'ArrowDown' ? 48 : 0;
                this.panBy(dx, dy);
            }
            else if (arrows && tag == 'VIDEO') {
                return true;
            }
            else if (event.key == 'ArrowLeft') {
                this.step(-1);
            }
            else if (event.key == 'ArrowRight') {
                this.step(1);
            }
            else {
                DialogFocus.trap($('lightbox'), event);
                return true;
            }
            event.preventDefault();
            return true;
        }
    }

    /** Kept for existing callers (deletion, continue-from-image). */
    function closeLightbox() {
        Lightbox.close();
    }

    // Load a gallery image into the image-to-image composer.
    async function editFromGallery(item) {
        if (state.busy || state.uncertain) {
            setStatus('当前任务还没有结束，结束后才能载入新的初始图。', true);
            return;
        }
        try {
            const response = await fetch(item.url, { cache: 'no-store' });
            if (!response.ok) {
                throw new Error(tr('服务器返回 {0}，文件可能已被删除', response.status));
            }
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
            // Workflow templates hide the image modes, so continuing from an image returns to the standard workspace.
            if ($('workflow-select').value != '') {
                $('workflow-select').value = '';
                selectWorkflow();
            }
            setMode('img2img');
            renderReference();
            if (state.view != 'create') {
                location.hash = 'create';
            }
            setStatus('已载入为初始图，可以直接图生图，或切换到局部重绘、扩图、放大。');
            $('prompt').focus();
        } catch (error) {
            closeLightbox();
            if (state.view != 'create') {
                location.hash = 'create';
            }
            setStatus(tr('无法载入这张图：{0}。', error.message || tr('请刷新后重试')), true);
        }
    }

    /* ============================================================
       Bindings
       ============================================================ */
    function bind() {
        $('theme-toggle').addEventListener('click', toggleTheme);
        $('lang-toggle').addEventListener('click', () => I18n.set(I18n.current == 'en' ? 'zh-CN' : 'en'));
        I18n.onChange(onLanguageChange);
        updateLanguageToggle();
        window.addEventListener('hashchange', route);

        $('mode-image').addEventListener('click', () => setType('image'));
        $('mode-video').addEventListener('click', () => setType('video'));
        document.querySelectorAll('.mode-tab').forEach(t =>
            t.addEventListener('click', () => setMode(t.dataset.mode)));

        $('refresh-workflows').addEventListener('click', refreshCatalog);
        $('reassign-btn').addEventListener('click', () => Reassign.retry());
        $('reassign-close').addEventListener('click', () => Reassign.hide());
        $('execution-server').addEventListener('focus', () => ExecutionServerPicker.load());
        for (let id of ['negative', 'steps', 'cfg', 'seed', 'sampler', 'scheduler']) {
            $(id).addEventListener('change', updateControls);
        }
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
            if ($('prompt').value.trim() && !window.confirm(tr('用示例替换当前描述？'))) { return; }
            $('prompt').value = state.type === 'image'
                ? '一瓶透明玻璃精华液，置于浅灰色石台，柔和侧光，突出瓶身质感，画面右侧留白用于排版。'
                : '镜头缓慢环绕一双白色运动鞋，鞋底细节逐渐进入画面，柔和棚拍灯光，背景保持简洁，镜头运动平稳。';
            $('prompt').focus();
            updateControls();
        });

        $('unlock').addEventListener('click', () => {
            if (!window.confirm(tr('请确认已在原生引擎核对队列和个人历史。重新提交可能产生第二个任务，确定解锁吗？'))) { return; }
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
            setGalleryFilter('all');
            renderGallery();
        });
        $('gal-star').addEventListener('click', () => {
            setGalleryFilter('star');
            renderGallery();
        });
        $('gallery-clear-filter').addEventListener('click', () => {
            setGallerySearch('');
            setGalleryFilter('all');
            loadGallery();
        });
        let searchReload = debounce(() => loadGallery(), 350);
        $('gallery-search').addEventListener('input', () => {
            setGallerySearch($('gallery-search').value);
            searchReload();
        });
        $('gallery-search-clear').addEventListener('click', () => {
            setGallerySearch('');
            $('gallery-search').focus();
            loadGallery();
        });
        $('gallery-sort').addEventListener('change', () => {
            state.gallery.sort = $('gallery-sort').value;
            loadGallery();
        });
        $('gallery-refresh').addEventListener('click', loadGallery);
        $('gallery-select').addEventListener('click', () => GallerySelection.setActive(true));
        $('gallery-select-cancel').addEventListener('click', () => GallerySelection.setActive(false));
        $('gallery-select-all').addEventListener('click', () => GallerySelection.selectAllVisible());
        $('gallery-delete-selected').addEventListener('click', () => GallerySelection.deleteSelected());
        $('gallery-gocreate').addEventListener('click', () => { location.hash = 'create'; });
        $('queue-refresh').addEventListener('click', loadQueue);

        // Workflow review
        $('workflows-refresh').addEventListener('click', () => WorkflowReviewView.load());
        for (let button of $('workflows-filter').querySelectorAll('[data-filter]')) {
            button.addEventListener('click', () => WorkflowReviewView.setFilter(button.dataset.filter));
        }
        $('review-close').addEventListener('click', () => WorkflowReviewView.closeReview());
        $('review-backdrop').addEventListener('click', () => WorkflowReviewView.closeReview());
        $('review-cancel').addEventListener('click', () => WorkflowReviewView.closeReview());
        $('review-approve').addEventListener('click', () => WorkflowReviewView.decide('approve'));
        $('review-reject').addEventListener('click', () => WorkflowReviewView.decide('reject'));
        $('review-unpublish').addEventListener('click', () => WorkflowReviewView.decide('unpublish'));
        $('submit-close').addEventListener('click', () => WorkflowReviewView.closeSubmit());
        $('submit-backdrop').addEventListener('click', () => WorkflowReviewView.closeSubmit());
        $('submit-cancel').addEventListener('click', () => WorkflowReviewView.closeSubmit());
        $('submit-confirm').addEventListener('click', () => WorkflowReviewView.submit());
        setInterval(() => WorkflowReviewView.poll(), 30000);
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
        $('lb-prev').addEventListener('click', () => Lightbox.step(-1));
        $('lb-next').addEventListener('click', () => Lightbox.step(1));
        $('lb-zoom-in').addEventListener('click', () => Lightbox.zoomBy(1.25));
        $('lb-zoom-out').addEventListener('click', () => Lightbox.zoomBy(0.8));
        $('lb-zoom-reset').addEventListener('click', () => Lightbox.resetZoom());
        let mediaBox = $('lightbox-media');
        mediaBox.addEventListener('wheel', event => Lightbox.wheel(event), { passive: false });
        mediaBox.addEventListener('dblclick', event => Lightbox.doubleClick(event));
        mediaBox.addEventListener('pointerdown', event => Lightbox.pointerDown(event));
        mediaBox.addEventListener('pointermove', event => Lightbox.pointerMove(event));
        mediaBox.addEventListener('pointerup', event => Lightbox.pointerEnd(event));
        mediaBox.addEventListener('pointercancel', event => Lightbox.pointerEnd(event));
        // Belt-and-braces: even draggable content must never start a browser drag inside the preview.
        mediaBox.addEventListener('dragstart', event => event.preventDefault());
        $('lb-star').addEventListener('click', async () => {
            let item = Lightbox.current();
            if (item && Lightbox.source == 'gallery') {
                // Starring moves the file between folders, so the open item's path is no longer valid afterwards.
                closeLightbox();
                await toggleStar(item, true);
            }
        });
        $('lb-delete').addEventListener('click', () => {
            let item = Lightbox.current();
            if (item && Lightbox.source == 'gallery') {
                GallerySelection.deleteOne(item);
            }
        });
        $('lb-edit').addEventListener('click', () => {
            let item = Lightbox.current();
            if (item) {
                editFromGallery(item);
            }
        });
        $('lb-gallery').addEventListener('click', () => {
            let item = Lightbox.current();
            if (item?.src) {
                closeLightbox();
                revealInGallery(item.src);
            }
        });
        window.addEventListener('keydown', e => {
            if (Lightbox.keydown(e)) {
                return;
            }
            if (!$('enhancer-modal').hidden) {
                if (e.key == 'Escape') {
                    closeEnhancerSettings();
                }
                else {
                    DialogFocus.trap($('enhancer-modal'), e);
                }
                return;
            }
            if (!$('setup-guide-modal').hidden) {
                if (e.key == 'Escape') {
                    closeSetupGuide();
                }
                else {
                    DialogFocus.trap($('setup-guide-modal'), e);
                }
                return;
            }
            if (!$('review-modal').hidden) {
                if (e.key == 'Escape') {
                    WorkflowReviewView.closeReview();
                }
                else {
                    DialogFocus.trap($('review-modal'), e);
                }
                return;
            }
            if (!$('submit-modal').hidden) {
                if (e.key == 'Escape') {
                    WorkflowReviewView.closeSubmit();
                }
                else {
                    DialogFocus.trap($('submit-modal'), e);
                }
            }
        });

        window.addEventListener('beforeunload', e => {
            if (state.submitting || state.uncertain) { e.preventDefault(); e.returnValue = ''; }
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
    // Reaching this line means every button is bound; the HTML fallback notice is no longer needed.
    $('boot-check').hidden = true;
    refreshCatalog();
})();
