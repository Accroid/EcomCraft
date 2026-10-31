// Catches variables lost when workspace scripts are split into files. No dependencies: run from the repository root with
// npx --yes eslint@9 -c src/Extensions/EcomCraft/Tools/lint/eslint.config.mjs src/Extensions/EcomCraft/Studio
const shared = { SimpleMode: 'readonly', I18n: 'readonly', StudioCore: 'readonly' };
const browser = {
    AbortController: 'readonly',
    Blob: 'readonly',
    CSS: 'readonly',
    DOMParser: 'readonly',
    Event: 'readonly',
    File: 'readonly',
    FileReader: 'readonly',
    FormData: 'readonly',
    HTMLElement: 'readonly',
    Image: 'readonly',
    MutationObserver: 'readonly',
    Node: 'readonly',
    NodeFilter: 'readonly',
    Option: 'readonly',
    ResizeObserver: 'readonly',
    URL: 'readonly',
    URLSearchParams: 'readonly',
    WebSocket: 'readonly',
    alert: 'readonly',
    atob: 'readonly',
    btoa: 'readonly',
    cancelAnimationFrame: 'readonly',
    clearInterval: 'readonly',
    clearTimeout: 'readonly',
    confirm: 'readonly',
    console: 'readonly',
    document: 'readonly',
    fetch: 'readonly',
    getComputedStyle: 'readonly',
    history: 'readonly',
    localStorage: 'readonly',
    location: 'readonly',
    matchMedia: 'readonly',
    navigator: 'readonly',
    prompt: 'readonly',
    requestAnimationFrame: 'readonly',
    sessionStorage: 'readonly',
    setInterval: 'readonly',
    setTimeout: 'readonly',
    window: 'readonly'
};

export default [
    {
        files: ['**/*.js'],
        languageOptions: {
            ecmaVersion: 'latest',
            sourceType: 'script',
            globals: { ...browser, ...shared }
        },
        rules: {
            'no-undef': 'error',
            'no-redeclare': ['error', { builtinGlobals: false }]
        }
    }
];
