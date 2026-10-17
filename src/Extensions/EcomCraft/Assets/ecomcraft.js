/** Read-only administrator workspace. Never participates in generation or scheduling. */
class EcomCraftDiagnostics {
    /** Connects to existing markup without fetching before session permissions have loaded. */
    constructor() {
        this.root = getRequiredElementById('ecomcraft_diagnostics');
        this.refreshButton = getRequiredElementById('ecomcraft_refresh');
        this.downloadButton = getRequiredElementById('ecomcraft_download');
        this.message = getRequiredElementById('ecomcraft_message');
        this.results = getRequiredElementById('ecomcraft_results');
        this.navigation = document.getElementById('maintab_ecomcraft')?.closest('.nav-item');
        this.report = null;
        this.loading = false;
        this.root.hidden = true;
        if (this.navigation) {
            this.navigation.hidden = true;
        }
        this.downloadButton.disabled = true;
        this.refreshButton.addEventListener('click', () => this.refresh());
        this.downloadButton.addEventListener('click', () => this.download());
    }

    /** The host's hasPermission returns true before loading, so explicitly gate on hasLoaded. */
    hasAccess() {
        return permissions.hasLoaded && permissions.hasPermission('view_extension_tab_ecomcraft');
    }

    /** Makes the workspace visible only after the real administrator permissions are known. */
    onSessionReady() {
        let allowed = this.hasAccess();
        this.root.hidden = !allowed;
        if (this.navigation) {
            this.navigation.hidden = !allowed;
        }
        if (!allowed) {
            this.clearReport();
            this.setLoading(false);
            this.refreshButton.disabled = true;
            return;
        }
        this.refresh();
    }

    /** Invalidates stale results before a refresh or after permission loss. */
    clearReport() {
        this.report = null;
        this.downloadButton.disabled = true;
        this.results.replaceChildren();
    }

    /** Keeps controls and accessible busy feedback consistent in all completion paths. */
    setLoading(loading) {
        this.loading = loading;
        this.root.setAttribute('aria-busy', String(loading));
        this.refreshButton.disabled = loading || !this.hasAccess();
        this.downloadButton.disabled = loading || !this.report || !this.hasAccess();
    }

    /** Loads only a same-origin diagnostic snapshot through the host's session-aware wrapper. */
    refresh() {
        if (!this.hasAccess()) {
            this.onSessionReady();
            return;
        }
        if (this.loading) {
            return;
        }
        this.clearReport();
        this.setLoading(true);
        this.message.textContent = '正在读取主控内存中的配置与引擎状态；不进行远程连接测试。';
        let failed = () => {
            this.clearReport();
            this.setLoading(false);
            this.message.textContent = '无法读取诊断快照。旧报告已清除；请刷新，或由管理员检查会话权限及宿主状态。';
            if (!this.hasAccess()) {
                this.onSessionReady();
            }
        };
        try {
            genericRequest('EcomCraftGetDiagnostics', {}, data => {
                if (!this.hasAccess()) {
                    this.onSessionReady();
                    return;
                }
                try {
                    if (!data || data.schema_version != 1 || data.scope != 'configuration_snapshot_only'
                        || !['blocked', 'pending_verification'].includes(data.m0_status)
                        || !Array.isArray(data.checks) || !Array.isArray(data.nodes) || !Array.isArray(data.spikes)) {
                        failed();
                        return;
                    }
                    this.render(data);
                    this.report = data;
                    this.setLoading(false);
                    this.message.textContent = `快照时间：${data.sampled_at || '未知'}。此时间不是成功心跳；报告仅用于配置核对。`;
                }
                catch {
                    failed();
                }
            }, 0, failed);
        }
        catch {
            failed();
        }
    }

    /** Creates plain-text content without interpreting backend names or report fields as HTML. */
    text(tag, value, classes = '') {
        let element = document.createElement(tag);
        element.className = classes;
        element.textContent = String(value ?? '未知');
        return element;
    }

    /** Appends a labeled value to a semantic definition list. */
    fact(list, label, value) {
        list.append(this.text('dt', label), this.text('dd', value));
    }

    /** Uses a closed status vocabulary; unknown values never render as a success. */
    status(status) {
        let labels = {
            blocked: '配置阻断', passed: '配置通过（仅此检查）', warning: '需关注', pending_verification: '待实测'
        };
        let known = Object.hasOwn(labels, status) ? status : 'pending_verification';
        return this.text('span', labels[known], `ecomcraft-status ecomcraft-status-${known}`);
    }

    /** Renders one titled, structured report section. */
    section(title) {
        let section = document.createElement('section');
        section.className = 'ecomcraft-section';
        section.appendChild(this.text('h2', title));
        this.results.appendChild(section);
        return section;
    }

    /** Shows separate configuration checks, engine observations and unexecuted live gates. */
    render(data) {
        let summary = createDiv(null, 'ecomcraft-summary');
        summary.append(this.status(data.m0_status), this.text('p', '即使全部配置检查通过，M0 也必须等待六项实测证据，不会自动变为“就绪”。'));
        this.results.appendChild(summary);
        let checks = this.section('配置检查');
        for (let check of data.checks) {
            let row = createDiv(null, 'ecomcraft-check');
            row.append(this.status(check.status), this.text('h3', check.summary), this.text('p', check.detail));
            checks.appendChild(row);
        }
        let nodes = this.section('后端与执行端');
        if (data.nodes.length == 0) {
            nodes.appendChild(this.text('p', '尚未配置后端。管理员可在 Server → Backends 登记测试节点，再刷新本页。'));
        }
        for (let node of data.nodes) {
            this.renderNode(nodes, node);
        }
        let spikes = this.section('M0 实测门槛 · 本页不自动验收');
        for (let spike of data.spikes) {
            let row = createDiv(null, 'ecomcraft-check');
            row.append(this.status(spike.status), this.text('h3', `${spike.id} · ${spike.title}`), this.text('p', spike.evidence));
            spikes.appendChild(row);
        }
    }

    /** Makes temporary host references, unknown health evidence and capacity limits explicit. */
    renderNode(container, node) {
        let card = createDiv(null, 'ecomcraft-node');
        card.appendChild(this.text('h3', `${node.name || '未命名后端'} · 配置 #${node.configured_backend_id}`));
        let facts = document.createElement('dl');
        facts.className = 'ecomcraft-facts';
        this.fact(facts, '类型', node.backend_type);
        this.fact(facts, '引擎报告状态', node.engine?.status);
        this.fact(facts, '启用', node.engine?.enabled ? '是' : '否或未知');
        this.fact(facts, '引擎容量信号', node.engine_reports_capacity ? '引擎报告有空位；不表示允许调度' : '未确认可用空位');
        this.fact(facts, '继续转发', node.allow_forwarding == null ? '未知或不适用' : node.allow_forwarding ? '开启，需核对' : '关闭；仍需验证完整路径');
        this.fact(facts, '成功心跳 / 最近错误', '未提供可靠观测；不可按刷新时间推断');
        this.fact(facts, '版本 / 已批准能力', '未验证；本页不自动批准模型或工作流');
        this.fact(facts, 'ID 边界', '这里是宿主配置引用，不是稳定业务节点 ID');
        card.appendChild(facts);
        if (node.observation_failed) {
            card.appendChild(this.text('p', '节点观测失败：不能将此节点视为可用。请刷新并检查宿主状态。'));
        }
        let executors = node.execution_backends || [];
        if (node.kind == 'remote_swarm' && executors.length == 0) {
            card.appendChild(this.text('p', '未观察到关联执行子后端；控制连接本身不能执行生成。'));
        }
        for (let executor of executors) {
            card.appendChild(this.text('p', `执行端：主控临时 ID #${executor.host_backend_id} / 远端 ID #${executor.remote_backend_id} · ${executor.backend_type} · 引擎状态 ${executor.status} · 占用槽位 ${executor.usages}/${executor.max_usages}（不是物理 GPU 任务数）`, 'ecomcraft-executor'));
        }
        container.appendChild(card);
    }

    /** Exports only the currently displayed response, without storing files on the server. */
    download() {
        if (!this.hasAccess()) {
            this.onSessionReady();
            return;
        }
        if (this.loading || !this.report) {
            return;
        }
        let blob = new Blob([JSON.stringify(this.report, null, 2)], { type: 'application/json' });
        let url = URL.createObjectURL(blob);
        let link = document.createElement('a');
        link.href = url;
        link.download = `ecomcraft-m0-${Date.now()}.json`;
        link.hidden = true;
        document.body.appendChild(link);
        link.click();
        link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
}

/** One workspace instance, activated only after the host session and permissions are ready. */
let ecomCraftDiagnostics = new EcomCraftDiagnostics();
sessionReadyCallbacks.push(() => ecomCraftDiagnostics.onSessionReady());
