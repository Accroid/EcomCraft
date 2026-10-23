
let backend_types = {};

let backends_loaded = {};

let backendsRevisedCallbacks = [];

let hasLoadedBackends = false;

/** Scans for ComfyUI installations and places a selected path in a backend editor. */
class ComfyDiscovery {
    constructor() {
        this.scanButton = document.getElementById('comfy-discovery-scan');
        this.pathSelect = document.getElementById('comfy-discovery-path');
        this.targetSelect = document.getElementById('comfy-discovery-target');
        this.useButton = document.getElementById('comfy-discovery-use');
        this.status = document.getElementById('comfy-discovery-status');
        this.scanButton.addEventListener('click', () => this.scan());
        this.pathSelect.addEventListener('change', () => this.updateUseButton());
        this.targetSelect.addEventListener('change', () => this.updateUseButton());
        this.useButton.addEventListener('click', () => this.useSelectedPath());
    }

    updateUseButton() {
        this.useButton.disabled = !this.pathSelect.value || !this.targetSelect.value;
    }

    scan() {
        this.scanButton.disabled = true;
        this.pathSelect.hidden = true;
        this.targetSelect.hidden = true;
        this.useButton.hidden = true;
        this.status.textContent = 'Scanning local installations…';
        genericRequest('ScanComfyInstallations', {}, data => {
            let paths = Array.isArray(data.paths) ? data.paths : [];
            this.pathSelect.replaceChildren();
            if (paths.length == 0) {
                this.status.textContent = 'No ComfyUI installation found. Check its location or enter the main.py path manually.';
                this.scanButton.disabled = false;
                return;
            }
            this.pathSelect.add(new Option('Select a ComfyUI installation', ''));
            for (let path of paths) {
                this.pathSelect.add(new Option(path, path));
            }
            if (paths.length == 1) {
                this.pathSelect.value = paths[0];
            }
            this.targetSelect.replaceChildren();
            for (let backend of Object.values(backends_loaded)) {
                if (backend.type == 'comfyui_selfstart') {
                    this.targetSelect.add(new Option(`ComfyUI backend ${backend.id}`, String(backend.id)));
                }
            }
            if (permissions.hasPermission('add_remove_backends')) {
                this.targetSelect.add(new Option('Add new ComfyUI backend', 'new'));
            }
            this.pathSelect.hidden = false;
            this.targetSelect.hidden = this.targetSelect.options.length == 0;
            this.useButton.hidden = this.targetSelect.hidden;
            this.updateUseButton();
            this.status.textContent = this.targetSelect.hidden
                ? 'A backend administrator must add a ComfyUI backend before this path can be used.'
                : `Found ${paths.length} installation${paths.length == 1 ? '' : 's'}. Choose a path and backend.`;
            this.scanButton.disabled = false;
        }, 0, error => {
            this.status.textContent = `Scan failed: ${error}`;
            this.scanButton.disabled = false;
        });
    }

    fillBackend(id, path) {
        let card = document.getElementById(`backend-card-${id}`);
        if (!card) {
            this.status.textContent = 'Backend list changed. Refresh the page and try again.';
            return;
        }
        let editButton = card.querySelector('.backend-edit-button');
        if (!editButton.disabled) {
            editButton.click();
        }
        let pathInput = card.querySelector('[data-name="StartScript"]');
        pathInput.value = path;
        pathInput.focus();
        card.scrollIntoView({ behavior: 'smooth', block: 'center' });
        this.status.textContent = 'Path filled in. Click Save on the backend card to apply it.';
    }

    useSelectedPath() {
        let path = this.pathSelect.value;
        let target = this.targetSelect.value;
        if (!path || !target) {
            return;
        }
        if (target != 'new') {
            this.fillBackend(target, path);
            return;
        }
        this.useButton.disabled = true;
        this.status.textContent = 'Adding ComfyUI backend…';
        genericRequest('AddNewBackend', { type_id: 'comfyui_selfstart' }, data => {
            backends_loaded[data.id] = data;
            addBackendToHtml(data, false);
            this.fillBackend(data.id, path);
            this.useButton.disabled = false;
        }, 0, error => {
            this.status.textContent = `Could not add backend: ${error}`;
            this.useButton.disabled = false;
        });
    }
}

let comfyDiscovery = new ComfyDiscovery();

function addNewBackend(type_id) {
    if (confirm(`Are you sure you want to add a new backend of type ${backend_types[type_id].name}?`)) {
        if (backend_types[type_id].is_standard || confirm('This backend type is labeled as Advanced users only. This is likely not a backend you want to use. Are you sure you want to continue?')) {
            genericRequest('AddNewBackend', {'type_id': type_id}, data => {
                backends_loaded[data.id] = data;
                addBackendToHtml(data, false);
            });
        }
    }
}

function addBackendToHtml(backend, disable, spot = null) {
    if (spot == null) {
        spot = createDiv(`backend-wrapper-spot-${backend.id}`, 'backend-wrapper-spot');
        document.getElementById('backends_list').appendChild(spot);
    }
    spot.innerHTML = '';
    let type = backend_types[backend.type];
    let cardBase = createDiv(`backend-card-${backend.id}`, `card backend-${backend.status} backend-card`);
    let cardHeader = createDiv(null, 'card-header');
    let togglerSpan = document.createElement('span');
    togglerSpan.className = 'form-check form-switch display-inline-block';
    let toggleSwitch = document.createElement('input');
    toggleSwitch.type = 'checkbox';
    toggleSwitch.className = 'form-check-input backend-toggle-switch';
    toggleSwitch.title = 'Enable/Disable backend';
    toggleSwitch.checked = backend.enabled;
    toggleSwitch.addEventListener('change', () => {
        backend.enabled = toggleSwitch.checked;
        genericRequest('ToggleBackend', {'backend_id': backend.id, 'enabled': toggleSwitch.checked}, data => {
            loadBackendsList();
        });
    });
    togglerSpan.appendChild(toggleSwitch);
    cardHeader.appendChild(togglerSpan);
    let cardTitleSpan = document.createElement('span');
    let cardTitleStatus = document.createElement('span');
    cardTitleStatus.className = 'card-title-status';
    cardTitleStatus.innerText = backend.status;
    cardTitleSpan.appendChild(cardTitleStatus);
    let cardTitleCenter = document.createElement('span');
    cardTitleCenter.innerText = ` backend: (`;
    cardTitleSpan.appendChild(cardTitleCenter);
    let cardTitleCenterID = document.createElement('span');
    cardTitleCenterID.innerText = `${backend.id}`;
    cardTitleSpan.appendChild(cardTitleCenterID);
    let cardTitleCenter3 = document.createElement('span');
    cardTitleCenter3.innerText = `): `;
    cardTitleSpan.appendChild(cardTitleCenter3);
    let actualCardTitle = document.createElement('span');
    actualCardTitle.innerText = backend.title || type.name;
    cardTitleSpan.appendChild(actualCardTitle);
    cardHeader.appendChild(cardTitleSpan);
    let deleteButton = document.createElement('button');
    deleteButton.className = 'backend-delete-button';
    deleteButton.innerText = '✕';
    deleteButton.title = 'Delete';
    let editButton = document.createElement('button');
    editButton.className = 'backend-edit-button';
    editButton.innerText = '✎';
    editButton.title = 'Edit';
    editButton.disabled = !disable;
    let saveButton = document.createElement('button');
    saveButton.className = 'backend-save-button';
    saveButton.innerText = 'Save';
    saveButton.title = 'Save changes';
    saveButton.style.display = disable ? 'none' : 'inline-block';
    cardHeader.appendChild(deleteButton);
    cardHeader.appendChild(editButton);
    cardHeader.appendChild(saveButton);
    deleteButton.addEventListener('click', () => {
        if (confirm(`Are you sure you want to delete backend ${backend.id} (${type.name})?`)) {
            genericRequest('DeleteBackend', {'backend_id': backend.id}, data => {
                cardBase.remove();
            });
        }
    });
    let cardBody = createDiv(null, 'card-body');
    let buttons = document.createElement('div');
    let isLogAvailable = serverLogs.matchIdentifier(`backend-${backend.id}`) != null;
    buttons.innerHTML = `<button class="basic-button backend-restart-button" disabled onclick="restart_backend('${backend.id}')">Restart</button> <button class="basic-button backend-log-view-button"${isLogAvailable ? '' : ' disabled'} onclick="serverLogs.showLogsForIdentifier('backend-${backend.id}')">View Logs</button> <span class="backend-last-used-time">Last used: <code>${backend.time_since_used}</code></span>`;
    cardBody.appendChild(buttons);
    for (let setting of type.settings) {
        let input = document.createElement('div');
        let pop = `<div class="sui-popover" id="popover_setting_${backend.id}_${setting.name}"><b>${escapeHtml(setting.name)}</b> (${setting.type}):<br>&emsp;${escapeHtml(setting.description)}</div>`;
        if (setting.is_secret) {
            input.innerHTML = makeSecretInput(null, `setting_${backend.id}_${setting.name}`, '', setting.name, setting.description, backend.settings[setting.name], setting.placeholder) + pop;
        }
        else if (setting.type == 'text') {
            input.innerHTML = makeTextInput(null, `setting_${backend.id}_${setting.name}`, '', setting.name, setting.description, backend.settings[setting.name], 'normal', setting.placeholder) + pop;
        }
        else if (setting.type == 'dropdown') {
            input.innerHTML = makeDropdownInput(null, `setting_${backend.id}_${setting.name}`, '', setting.name, setting.description, setting.values, backend.settings[setting.name], false, true, setting.value_names) + pop;
        }
        else if (setting.type == 'integer') {
            input.innerHTML = makeNumberInput(null, `setting_${backend.id}_${setting.name}`, '', setting.name, setting.description, backend.settings[setting.name], 0, 1000, 1) + pop;
        }
        else if (setting.type == 'decimal') {
            input.innerHTML = makeNumberInput(null, `setting_${backend.id}_${setting.name}`, '', setting.name, setting.description, backend.settings[setting.name], 0, 1000, 1) + pop;
        }
        else if (setting.type == 'boolean') {
            input.innerHTML = makeCheckboxInput(null, `setting_${backend.id}_${setting.name}`, '', setting.name, setting.description, backend.settings[setting.name]) + pop;
        }
        else {
            console.log(`Cannot create input slot of type ${setting.type}`);
        }
        if (backend.type == 'comfyui_selfstart' && setting.name == 'StartScript') {
            let pathInput = input.querySelector('[data-name="StartScript"]');
            let scanRow = document.createElement('div');
            scanRow.className = 'comfy-scan-row';
            let scanButton = document.createElement('button');
            scanButton.type = 'button';
            scanButton.className = 'basic-button';
            scanButton.textContent = 'Scan ComfyUI';
            scanButton.disabled = disable;
            let pathSelect = document.createElement('select');
            pathSelect.className = 'comfy-scan-select';
            pathSelect.setAttribute('aria-label', 'Select ComfyUI installation');
            pathSelect.hidden = true;
            let scanStatus = document.createElement('span');
            scanStatus.className = 'comfy-scan-status';
            scanStatus.setAttribute('role', 'status');
            scanButton.addEventListener('click', () => {
                scanButton.disabled = true;
                scanStatus.textContent = 'Scanning local installations…';
                pathSelect.hidden = true;
                genericRequest('ScanComfyInstallations', {}, data => {
                    let paths = Array.isArray(data.paths) ? data.paths : [];
                    pathSelect.replaceChildren();
                    if (paths.length == 0) {
                        scanStatus.textContent = 'No ComfyUI installation found. Check its location or enter the main.py path manually.';
                    }
                    else if (paths.length == 1) {
                        pathInput.value = paths[0];
                        scanStatus.textContent = 'Path filled in. Click Save to apply it.';
                    }
                    else {
                        pathSelect.add(new Option('Select a ComfyUI installation', ''));
                        for (let path of paths) {
                            pathSelect.add(new Option(path, path));
                        }
                        pathSelect.hidden = false;
                        scanStatus.textContent = `Found ${paths.length} installations. Select one, then click Save.`;
                    }
                    scanButton.disabled = false;
                }, 0, error => {
                    scanStatus.textContent = `Scan failed: ${error}`;
                    scanButton.disabled = false;
                });
            });
            pathSelect.addEventListener('change', () => {
                if (pathSelect.value) {
                    pathInput.value = pathSelect.value;
                }
            });
            scanRow.append(scanButton, pathSelect, scanStatus);
            input.appendChild(scanRow);
        }
        cardBody.appendChild(input);
    }
    cardBase.appendChild(cardHeader);
    cardBase.appendChild(cardBody);
    spot.appendChild(cardBase);
    for (let entry of cardBody.querySelectorAll('[data-name]')) {
        entry.disabled = disable;
    }
    if (!disable) {
        actualCardTitle.contentEditable = true;
        actualCardTitle.classList.add('backend-title-editable');
        cardTitleCenterID.contentEditable = true;
        cardTitleCenterID.classList.add('backend-title-editable');
    }
    actualCardTitle.addEventListener('keydown', e => {
        if (e.key == 'Enter') {
            e.preventDefault();
        }
    });
    cardTitleCenterID.addEventListener('keydown', e => {
        if (e.key == 'Enter') {
            e.preventDefault();
        }
    });
    editButton.addEventListener('click', () => {
        saveButton.style.display = 'inline-block';
        editButton.disabled = true;
        actualCardTitle.contentEditable = true;
        actualCardTitle.classList.add('backend-title-editable');
        cardTitleCenterID.contentEditable = true;
        cardTitleCenterID.classList.add('backend-title-editable');
        for (let entry of cardBody.querySelectorAll('[data-name]')) {
            entry.disabled = false;
        }
        for (let entry of cardBody.querySelectorAll('.comfy-scan-row button')) {
            entry.disabled = false;
        }
    });
    saveButton.addEventListener('click', () => {
        saveButton.style.display = 'none';
        actualCardTitle.contentEditable = false;
        actualCardTitle.classList.remove('backend-title-editable');
        cardTitleCenterID.contentEditable = false;
        cardTitleCenterID.classList.remove('backend-title-editable');
        for (let entry of cardBody.querySelectorAll('[data-name]')) {
            let name = entry.dataset.name;
            let value = entry.type == 'checkbox' ? entry.checked : entry.value;
            backend.settings[name] = value;
            entry.disabled = true;
        }
        let dataIn = {
            'backend_id': backend.id,
            'title': actualCardTitle.textContent,
            'settings': backend.settings
        };
        if (cardTitleCenterID.textContent && cardTitleCenterID.textContent != backend.id) {
            dataIn['new_id'] = parseInt(cardTitleCenterID.textContent);
        }
        genericRequest('EditBackend', dataIn, data => {
            addBackendToHtml(data, true, spot);
            if (dataIn['new_id']) {
                clearBackendsList();
                loadBackendsList();
            }
        }, 0, e => {
            console.error(e);
            showError(e);
            clearBackendsList();
            loadBackendsList();
        });
    });
}

function clearBackendsList() {
    for (let oldBack of Object.values(backends_loaded)) {
        let spot = document.getElementById(`backend-wrapper-spot-${oldBack.id}`);
        spot.remove();
    }
    backends_loaded = {};
}

function loadBackendsList() {
    reviseStatusBar();
    genericRequest('ListBackends', {}, data => {
        hasLoadedBackends = true;
        for (let oldBack of Object.values(backends_loaded)) {
            let spot = document.getElementById(`backend-wrapper-spot-${oldBack.id}`);
            let newBack = data[oldBack.id];
            if (!newBack) {
                delete backends_loaded[oldBack.id];
                spot.remove();
            }
            else {
                let card = document.getElementById(`backend-card-${oldBack.id}`);
                if (oldBack.status != newBack.status) {
                    card.classList.remove(`backend-${oldBack.status}`);
                    card.classList.add(`backend-${newBack.status}`);
                    card.querySelector('.card-title-status').innerText = newBack.status;
                }
                card.querySelector('.backend-restart-button').disabled = newBack.status != 'errored' && newBack.status != 'running';
                card.querySelector('.backend-log-view-button').disabled = serverLogs.matchIdentifier(`backend-${newBack.id}`) == null;
                card.querySelector('.backend-last-used-time').innerHTML = `Last used: <code>${newBack.time_since_used}</code>`;
                if (newBack.modcount > oldBack.modcount) {
                    addBackendToHtml(newBack, true, spot);
                }
            }
        }
        for (let newBack of Object.values(data)) {
            let oldBack = backends_loaded[newBack.id];
            if (!oldBack) {
                addBackendToHtml(newBack, true);
            }
        }
        backends_loaded = data;
        hideUnsupportableParams();
        for (let callback of backendsRevisedCallbacks) {
            callback();
        }
        reviseStatusBar();
    });
}

function countBackendsByStatus(status) {
    return Object.values(backends_loaded).filter(x => x.enabled && x.status == status).length;
}

function toggleShowAdvancedBackends() {
    let showAdvanced = document.getElementById('backends_show_advanced').checked;
    for (let button of document.querySelectorAll('#backend_add_buttons button')) {
        if (button.dataset.isStandard == 'false') {
            button.style.display = showAdvanced ? 'inline-block' : 'none';
        }
    }
}

function loadBackendTypesMenu() {
    let addButtonsSection = document.getElementById('backend_add_buttons');
    genericRequest('ListBackendTypes', {}, data => {
        backend_types = {};
        addButtonsSection.innerHTML = '';
        for (let type of data.list) {
            backend_types[type.id] = type;
            let button = document.createElement('button');
            button.dataset.isStandard = type.is_standard;
            button.title = type.description;
            button.innerText = type.name;
            if (!type.is_standard) {
                button.style.display = 'none';
            }
            let id = type.id;
            button.addEventListener('click', () => { addNewBackend(id); });
            addButtonsSection.appendChild(button);
        }
        loadBackendsList();
    });
}

let backendsListView = document.getElementById('backends_list');
let backendsCheckRateCounter = 0;

function isVisible(element) {
    // DOM Element visibility isn't supported in all browsers
    // https://caniuse.com/mdn-api_element_checkvisibility
    if (typeof element.checkVisibility != "undefined") {
        return element.checkVisibility();
    } else {
        return !(element.offsetParent === null);
    }
}

function backendLoopUpdate() {
    if (isVisible(backendsListView)) {
        serverLogs.onTabButtonClick();
        if (backendsCheckRateCounter++ % 3 == 0) {
            loadBackendsList();
        }
    }
    else {
        backendsCheckRateCounter = 0;
    }
}

function restart_backend(id) {
    if (confirm(`Are you sure you want to restart backend ${id}?`)) {
        genericRequest('RestartBackends', {backend: `${id}`}, data => {
            loadBackendsList();
        });
    }
}

function restart_all_backends() {
    if (confirm('Are you sure you want to restart all backends?')) {
        genericRequest('RestartBackends', {}, data => {
            loadBackendsList();
        });
    }
}
