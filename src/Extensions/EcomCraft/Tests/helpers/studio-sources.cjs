// Finds the workspace scripts through index.html, so tests keep working when a script is split into several files.
'use strict';

const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const studioDir = path.join(__dirname, '..', '..', 'Studio');

/** Local paths of the scripts index.html loads, in load order. */
function scriptFiles() {
    let html = fs.readFileSync(path.join(studioDir, 'index.html'), 'utf8');
    return [...html.matchAll(/<script\b[^>]*\bsrc="\/EcomCraft\/([^"?]+)/g)].map(match => path.join(studioDir, ...match[1].split('/')));
}

/** The workspace's own scripts in load order, without the translation table. */
function workspaceScripts() {
    return scriptFiles().filter(file => path.basename(path.dirname(file)) != 'lang');
}

/** True for the files that make up the simple mode. */
function isSimpleScript(file) {
    return path.basename(file).startsWith('simple');
}

/** The scripts whose text users see: everything except i18n.js, which only holds the language names. */
function translatableScripts() {
    return workspaceScripts().filter(file => path.basename(file) != 'i18n.js');
}

/** Joins the given files in order. */
function readFiles(files) {
    return files.map(file => fs.readFileSync(file, 'utf8')).join('\n');
}

/** Source of all workspace scripts in load order, without lang/en.js. */
function readAllScripts() {
    return readFiles(workspaceScripts());
}

/** Source of the simple mode scripts in load order. */
function readSimpleScripts() {
    return readFiles(workspaceScripts().filter(isSimpleScript));
}

/** Source of the workspace scripts outside the simple mode and i18n.js. */
function readStudioScripts() {
    return readFiles(translatableScripts().filter(file => !isSimpleScript(file)));
}

/** Loads the simple mode scripts the way the browser does and returns the SimpleMode class. */
function loadSimpleMode() {
    let context = vm.createContext({});
    for (let file of workspaceScripts().filter(isSimpleScript)) {
        vm.runInContext(fs.readFileSync(file, 'utf8'), context, { filename: file });
    }
    return vm.runInContext('SimpleMode', context);
}

module.exports = { studioDir, scriptFiles, translatableScripts, readAllScripts, readSimpleScripts, readStudioScripts, loadSimpleMode };
