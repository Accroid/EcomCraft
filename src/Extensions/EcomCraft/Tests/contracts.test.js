let test = require('node:test');
let assert = require('node:assert/strict');
let fs = require('node:fs');
let path = require('node:path');
let root = path.resolve(__dirname, '..');

/** Reads source text for static integration tripwires, not runtime acceptance evidence. */
function source(name) {
    return fs.readFileSync(path.join(root, name), 'utf8');
}

test('extension follows host discovery and excludes the nested test program from production', () => {
    let project = source('EcomCraft.csproj');
    assert.match(project, /Import Project="\.\.\/\.\.\/SwarmUI\.extension\.props"/);
    assert.match(project, /Compile Remove="Tests\/\*\*\/\*\.cs"/);
    assert.match(source('EcomCraft.cs'), /namespace EcomCraftStudio;/);
    assert.match(source('EcomCraft.cs'), /class EcomCraft : Extension/);
});

test('the exact generated tab permission is preregistered for administrators and reused by the API', () => {
    let extension = source('EcomCraft.cs');
    let permission = extension.match(/DiagnosticsPermissionId = "([^"]+)"/)[1];
    let tab = 'EcomCraft'.toLowerCase().replace(/[^a-z]/g, '');
    assert.equal(permission, `view_extension_tab_${tab}`);
    assert.match(extension, /Permissions\.Register\(new\(DiagnosticsPermissionId,[\s\S]*?PermissionDefault\.ADMINS/);
    assert.match(extension, /RegisterAPICall\(api\.EcomCraftGetDiagnostics, false, permission\)/);
    assert.match(source('Assets/ecomcraft.js'), /permissions\.hasLoaded && permissions\.hasPermission\('view_extension_tab_ecomcraft'\)/);
});

test('direct handler invocation checks session permission before reading host data', () => {
    let api = source('Diagnostics/DiagnosticsApi.cs');
    assert.ok(api.indexOf('session?.User is null') < api.indexOf('SwarmSnapshotReader.Read()'));
    assert.match(api, /!session\.User\.HasPermission\(RequiredPermission\)/);
    assert.match(api, /\["error_id"\] = "bad_permissions"/);
    assert.doesNotMatch(api, /SessionlessRoutes|GenerateText2Image/);
});

test('diagnostic JSON has an explicit allowlist and never serializes host configuration objects', () => {
    let api = source('Diagnostics/DiagnosticsApi.cs');
    let keys = [...api.matchAll(/\["([^"\n]+)"\]\s*=/g)].map(match => match[1]);
    let allowed = new Set([
        'error', 'error_id', 'schema_version', 'extension_version', 'scope', 'sampled_at', 'm0_status', 'checks',
        'id', 'status', 'summary', 'detail', 'nodes', 'spikes', 'title', 'evidence', 'configured_backend_id',
        'node_id', 'name', 'backend_type', 'kind', 'engine_reports_capacity', 'allow_forwarding', 'observation_failed',
        'last_successful_heartbeat_at', 'last_error_summary', 'backend_version', 'approved_capabilities',
        'capability_status', 'environment_status', 'engine', 'execution_backends', 'host_backend_id',
        'remote_backend_id', 'enabled', 'shutdown_reserved', 'reservations', 'model_load_reserved', 'usages',
        'max_usages', 'can_load_models'
    ]);
    assert.deepEqual(new Set(keys), allowed);
    assert.doesNotMatch(api, /(?:JObject|JToken)\.FromObject|SettingsRaw|AuthorizationHeader|OtherHeaders|\.Address/);
    assert.match(api, /\["last_successful_heartbeat_at"\] = null/);
    assert.match(api, /\["node_id"\] = null/);
});

test('host projection uses the actual Swarm parent reference and does not read secrets or perform I/O', () => {
    let reader = source('Diagnostics/SwarmSnapshotReader.cs');
    assert.match(reader, /ReferenceEquals\(childRemote\.Parent, remote\)/);
    assert.match(reader, /!childRemote\.IsReal/);
    assert.match(reader, /Observation|观测失败/);
    assert.doesNotMatch(reader, /\.Address|\.AuthorizationHeader|\.OtherHeaders|SaveAllWithoutSecretValues|\.Session\b/);
    assert.doesNotMatch(reader, /HttpClient|WebSocket|File\.|Directory\.|GetNewSession|ValidateAndBuild|ReviseRemoteDataList|TriggerRefresh/);
});

test('no extension code hooks generation, scheduling, updates or backend mutations', () => {
    let files = ['EcomCraft.cs', 'Diagnostics/DiagnosticsApi.cs', 'Diagnostics/ReadinessEvaluator.cs', 'Diagnostics/SwarmSnapshotReader.cs'];
    for (let file of files) {
        assert.doesNotMatch(source(file), /PreGenerateEvent\s*[+\-]?=|AltBackendValidators\.|CanTryFindNow\.|RegisterBackendType|GetNextT2IBackend\(|GenerateLive\(|GenerateText2Image\(|UpdateAndRestart\(|DoGitUpdate\(|SaveSettingsFile\(|DoShutdownNow\(/);
    }
    assert.doesNotMatch(source('Diagnostics/ReadinessEvaluator.cs'), /SwarmUI\.|Program\.|DateTime|File\.|HttpClient/);
});

test('all six spike gates remain explicitly pending and overall ready is not an output', () => {
    let evaluator = source('Diagnostics/ReadinessEvaluator.cs');
    let gates = [...evaluator.matchAll(/new\("(SPIKE-\d+)",[^\n]*?"pending_verification"/g)].map(match => match[1]);
    assert.deepEqual(gates, ['SPIKE-01', 'SPIKE-02', 'SPIKE-03', 'SPIKE-04', 'SPIKE-05', 'SPIKE-06']);
    assert.doesNotMatch(evaluator, /"ready"|"completed"/);
    assert.match(evaluator, /checks\.Any\(c => c\.Status == "blocked"\) \? "blocked" : "pending_verification"/);
});

test('resources resolve inside the extension and static markup starts hidden with semantic controls', () => {
    let extension = source('EcomCraft.cs');
    for (let match of extension.matchAll(/(?:ScriptFiles|StyleSheetFiles)\.Add\("([^"]+)"\)/g)) {
        assert.ok(fs.existsSync(path.join(root, match[1])), match[1]);
    }
    let html = source('Tabs/Text2Image/EcomCraft.html');
    assert.match(html, /id="ecomcraft_diagnostics"[^>]* hidden/);
    assert.match(html, /role="status" aria-live="polite"/);
    for (let button of html.matchAll(/<button\b([^>]+)>/g)) {
        assert.match(button[1], /type="button"/);
        assert.match(button[1], /disabled/);
    }
    assert.doesNotMatch(html, /onclick=|<script|https?:\/\//);
    let ids = [...html.matchAll(/\bid="([^"]+)"/g)].map(match => match[1]);
    assert.equal(ids.length, new Set(ids).size);
    for (let match of source('Assets/ecomcraft.js').matchAll(/getRequiredElementById\('([^']+)'\)/g)) {
        assert.ok(ids.includes(match[1]), match[1]);
    }
});

test('frontend uses only the same-origin session-aware diagnostic API and theme-scoped styling', () => {
    let js = source('Assets/ecomcraft.js');
    assert.deepEqual([...js.matchAll(/genericRequest\('([^']+)'/g)].map(match => match[1]), ['EcomCraftGetDiagnostics']);
    assert.doesNotMatch(js, /\bfetch\(|XMLHttpRequest|makeWSRequest|\.innerHTML\s*=|\beval\(/);
    assert.doesNotMatch(source('Assets/ecomcraft.css'), /@import|url\(|#[0-9a-fA-F]{3,8}\b/);
});
