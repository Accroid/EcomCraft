// Checks that every Chinese text in the EcomCraft workspace has an English translation, so the English mode shows no Chinese.
// Run: node --test src/Extensions/EcomCraft/Tests/
// Set ECOMCRAFT_I18N_MISSING=1 to print the missing source texts as JSON.
'use strict';

const { test } = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const studioDir = path.join(__dirname, '..', 'Studio');
const chinese = /[　-〿㐀-鿿＀-￯]/;
// Workspace scripts whose Chinese literals are shown to users. i18n.js only holds the language names, which are never translated.
const scriptFiles = ['studio.js', 'simple.js'];

/** Same lookup form as I18n.normalize in i18n.js. */
function normalize(text) {
    return String(text).trim().replace(/\s+/g, ' ');
}

/** Loads lang/en.js the way the browser does and returns its table. */
function loadEnglish() {
    let sandbox = { window: {} };
    vm.runInNewContext(fs.readFileSync(path.join(studioDir, 'lang', 'en.js'), 'utf8'), sandbox);
    return sandbox.window.EcomCraftLanguages.en;
}

/** Decodes the escapes used in the workspace's JavaScript string literals. */
function unescapeLiteral(body) {
    return body.replace(/\\(u\{[0-9a-fA-F]+\}|u[0-9a-fA-F]{4}|x[0-9a-fA-F]{2}|.)/g, (match, code) => {
        if (code[0] == 'u' && code.length > 1) {
            return String.fromCodePoint(parseInt(code.replace(/[u{}]/g, ''), 16));
        }
        if (code[0] == 'x' && code.length == 3) {
            return String.fromCharCode(parseInt(code.slice(1), 16));
        }
        return { n: '\n', t: '\t', r: '\r' }[code] ?? code;
    });
}

/** Lists string and template literals of a script, skipping comments and regular expressions well enough for this code base. */
function literals(source) {
    let found = [];
    let i = 0;
    let previous = '';
    while (i < source.length) {
        let c = source[i];
        let next = source[i + 1];
        if (c == '/' && next == '/') {
            i = source.indexOf('\n', i);
            i = i < 0 ? source.length : i;
            continue;
        }
        if (c == '/' && next == '*') {
            i = source.indexOf('*/', i + 2) + 2;
            continue;
        }
        if (c == '/' && /[(,=:[!&|?{};]/.test(previous)) {
            // Regular expression literal.
            let j = i + 1;
            let inClass = false;
            while (j < source.length && (source[j] != '/' || inClass)) {
                if (source[j] == '\\') {
                    j++;
                }
                else if (source[j] == '[') {
                    inClass = true;
                }
                else if (source[j] == ']') {
                    inClass = false;
                }
                j++;
            }
            i = j + 1;
            previous = '/';
            continue;
        }
        if (c == '\'' || c == '"' || c == '`') {
            let j = i + 1;
            let body = '';
            let depth = 0;
            while (j < source.length) {
                if (source[j] == '\\') {
                    body += source[j] + source[j + 1];
                    j += 2;
                    continue;
                }
                if (c == '`' && source[j] == '$' && source[j + 1] == '{') {
                    depth++;
                }
                else if (c == '`' && depth > 0 && source[j] == '}') {
                    depth--;
                }
                else if (source[j] == c && depth == 0) {
                    break;
                }
                body += source[j];
                j++;
            }
            found.push({ quote: c, text: unescapeLiteral(body), line: source.slice(0, i).split('\n').length });
            i = j + 1;
            previous = 'a';
            continue;
        }
        if (!/\s/.test(c)) {
            previous = c;
        }
        i++;
    }
    return found;
}

/** Decodes the HTML entities used in index.html, as the browser does before I18n reads the text. */
function decodeEntities(text) {
    return text.replace(/&(nbsp|amp|lt|gt|quot|#39);/g, (match, name) => ({ nbsp: ' ', amp: '&', lt: '<', gt: '>', quot: '"', '#39': '\'' })[name]);
}

/** Chinese texts of index.html as the page shows them: text nodes and translatable attributes. */
function htmlTexts(html) {
    let texts = [];
    let body = html.replace(/<!--[\s\S]*?-->/g, '').replace(/<(script|style)[^>]*>[\s\S]*?<\/\1>/g, '');
    for (let match of body.matchAll(/>([^<]+)</g)) {
        let text = normalize(decodeEntities(match[1]));
        if (chinese.test(text)) {
            texts.push(text);
        }
    }
    for (let match of body.matchAll(/\s(title|placeholder|aria-label|alt)="([^"]*)"/g)) {
        let text = normalize(decodeEntities(match[2]));
        if (chinese.test(text)) {
            texts.push(text);
        }
    }
    return texts;
}

/** Every Chinese source text the workspace can show, with where it came from. */
function sourceTexts() {
    let texts = new Map();
    let add = (text, where) => {
        if (!texts.has(text)) {
            texts.set(text, where);
        }
    };
    for (let text of htmlTexts(fs.readFileSync(path.join(studioDir, 'index.html'), 'utf8'))) {
        add(text, 'index.html');
    }
    let script = fs.readFileSync(path.join(studioDir, 'studio.js'), 'utf8');
    for (let file of scriptFiles) {
        for (let literal of literals(fs.readFileSync(path.join(studioDir, file), 'utf8'))) {
            if (chinese.test(literal.text) && literal.quote != '`') {
                add(normalize(literal.text), `${file}:${literal.line}`);
            }
        }
    }
    let codes = script.match(/const safeErrors = new Set\(\[([\s\S]*?)\]\)/);
    for (let code of codes[1].match(/'[a-z_]+'/g)) {
        add(`error:${code.slice(1, -1)}`, 'safeErrors');
    }
    return texts;
}

test('workspace scripts have no Chinese template literals; variable text must go through tr()', () => {
    let offending = [];
    for (let file of scriptFiles) {
        let script = fs.readFileSync(path.join(studioDir, file), 'utf8');
        offending.push(...literals(script).filter(literal => literal.quote == '`' && chinese.test(literal.text)).map(literal => `${file}:${literal.line}`));
    }
    assert.deepStrictEqual(offending, []);
});

test('every Chinese text of the workspace has an English translation', () => {
    let english = loadEnglish();
    let missing = [...sourceTexts()].filter(([text]) => typeof english[text] != 'string' || !english[text]).map(([text, where]) => ({ text, where }));
    if (process.env.ECOMCRAFT_I18N_MISSING) {
        console.log(JSON.stringify(missing, null, 1));
    }
    assert.deepStrictEqual(missing, []);
});

test('English translations contain no Chinese and keep every placeholder', () => {
    let english = loadEnglish();
    let problems = [];
    for (let [source, translated] of Object.entries(english)) {
        if (chinese.test(translated)) {
            problems.push(`Chinese in translation of ${source}`);
        }
        let wanted = (source.match(/\{\d+\}/g) || []).sort().join();
        let got = (translated.match(/\{\d+\}/g) || []).sort().join();
        if (wanted != got) {
            problems.push(`placeholders differ for ${source}`);
        }
    }
    assert.deepStrictEqual(problems, []);
});

test('the English table has no entries the workspace no longer uses', () => {
    let used = sourceTexts();
    let unused = Object.keys(loadEnglish()).filter(source => !used.has(source));
    assert.deepStrictEqual(unused, []);
});
