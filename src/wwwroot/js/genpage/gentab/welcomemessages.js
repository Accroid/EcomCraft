/** Removes any current visible image or other content and forcibly redisplays any welcome message. */
function forceShowWelcomeMessage() {
    getRequiredElementById('current_image').innerHTML = '<div id="welcome_message" class="welcome_message"></div>';
    resetWelcomeMessage();
}

/** (Only if a welcome message is displayed) clears the existing welcome message and replaces it automatically. */
function resetWelcomeMessage(override = null) {
    let div = document.getElementById('welcome_message');
    if (div) {
        div.innerHTML = '';
    }
    automaticWelcomeMessage(override);
}

let extraWelcomeDiv = getRequiredElementById('extra_welcome_message');
let extraWelcomeHtml = extraWelcomeDiv.innerHTML.trim();
extraWelcomeDiv.remove();

/** (Only if there is no pre-existing welcome message and the current_image area is empty) automatically chooses a welcome message to display and applies it. */
function automaticWelcomeMessage(override = null) {
    let div = document.getElementById('welcome_message');
    if (!div) {
        return;
    }
    if (div.innerHTML.trim() != '') {
        return;
    }
    let versionHtml = getRequiredElementById('version_display').innerHTML;
    let prefix = `<span class="translate">Welcome to</span> <b>${versionHtml} - ${window.instanceTitle}</b><span class="translate">!</span>\n${extraWelcomeHtml}`;
    let curModelElem = getRequiredElementById('current_model');
    if (!curModelElem.value) {
        if (allModels.length == 0) {
            div.innerHTML = `${prefix}<span class="translate">You don't seem to have downloaded any models yet.</span>\n<span class="translate">Please download a model, or go to the</span> <a href="#Settings-Server" onclick="getRequiredElementById('servertabbutton').click();getRequiredElementById('serverconfigtabbutton').click();"><span class="translate">Server Configuration Tab</span></a><span class="translate">to set your models folder.</span>`;
            applyTranslations(div);
            return;
        }
        div.innerHTML = `${prefix}<span class="translate">Please select a model in the</span> <a href="#models-tab" onclick="getRequiredElementById('modelstabheader').click()"><span class="translate">Models</span></a><span class="translate">tab below to get started.</span>`;
        applyTranslations(div);
        curModelElem.addEventListener('change', () => {
            resetWelcomeMessage(0);
        });
        return;
    }
    let messages = [
        /* Generic welcome messages, order-sensitive, keep at top */
        `<span class="translate">Type your prompt below and hit Generate!</span>`,
        `<span class="translate">Join the SwarmUI</span> <a target="_blank" href="https://discord.gg/q2y38cqjNw"><span class="translate">official Discord!</span></a>`,
        /* Other toss-ins */
        `<span class="translate">Love SwarmUI? You can support development on</span> <a target="_blank" href="https://patreon.com/swarmui"><span class="translate">Patreon!</span></a>`,
        /* "Did you know" facts - interesting things you can do in swarm */
        `<span class="translate">Did you know:\nyou can drag and drop images onto the prompt box to use them as an Image Prompt (eg Edit models, Flux Redux, IP-Adapter, ...).</span>`,
        `<span class="translate">Did you know:\nyou can create multiple variations of one image by locking in your seed, then enabling the</span> <b><span class="translate">Variation Seed</span></b> <span class="translate">parameter.</span>`,
        `<span class="translate">Did you know:\nWant to have live-previews of your generation while you edit the details?\nLock in a seed, then right click the</span> <b><span class="translate">Generate</span></b> <span class="translate">button and select 'Generate Previews'.\nIf you add a</span> <b><span class="translate">Preset</span></b> <span class="translate">and name it exactly</span> <b><span class="translate">Preview</span></b> <span class="translate">that preset will automatically be used for previews (Turbo models, eg LCM, Schnell, etc. recommended in the preset for speed).</span>`,
        `<span class="translate">Did you know:\nWant to compare how different values of a parameter affect your generations?\nHead to the</span> <b><span class="translate">Tools</span></b> <span class="translate">tab below and select the</span> <b><span class="translate">Grid Generator</span></b> <span class="translate">tool.</span>`,
        `<span class="translate">Did you know:\nYou can create a</span> <b><span class="translate">Preset</span></b> <span class="translate">and name it exactly</span> <b><span class="translate">Default</span></b> <span class="translate">\nand that preset will automatically be used to load your default params when you launch Swarm.</span>`,
        `<span class="translate">Did you know:\nSomething going wrong?\nCheck the</span> <b><span class="translate">Server</span></b> <span class="translate">tab for debug logs, system resource usage, etc.</span>`,
        `<span class="translate">Did you know:\nWant to try some fancy prompting?\nJust type a '&lt;' symbol and watch the suggestions for prompt-syntax tools appear! Give the syntax features a try!</span> <a target="_blank" href="https://github.com/mcmonkeyprojects/SwarmUI/blob/master/docs/Features/Prompt%20Syntax.md"><span class="translate">(Documentation Here)</span></a>`,
        `<span class="translate">Did you know:\nSwarm doesn't update itself by default.\nGo to the '</span><b><span class="translate">Server</span></b><span class="translate">' tab and click '</span><b><span class="translate">Update and Restart</span></b><span class="translate">' to get the latest version. There's also a '</span><b><span class="translate">Server Config</span></b><span class="translate">' option to always pull dev updates.</span>`,
        /* Version release notes */
        `<span class="translate">Release notes (2026-02-06): Check out the</span> <a target="_blank" href="https://github.com/mcmonkeyprojects/SwarmUI/releases/tag/0.9.8-Beta"><span class="translate">Release Notes for version 0.9.8 (Beta)</span></a>`
    ];
    let dotnetNotice = document.getElementById('dotnet_missing_message');
    if (dotnetNotice) {
        messages.push(dotnetNotice.innerHTML.trim());
    }
    let gitFailedNotice = document.getElementById('git_failed_message');
    if (gitFailedNotice) {
        messages.push(gitFailedNotice.innerHTML.trim());
    }
    if (override == null) {
        if (dotnetNotice || gitFailedNotice) {
            override = messages.length - 1;
        }
        else {
            override = Math.floor(Math.random() * messages.length);
        }
    }
    override = override % messages.length;
    if (override < 0) {
        override += messages.length;
    }
    div.innerHTML = `${prefix}\n<div class="welcome-message-wrapper">${messages[override]}</div>\n\n<button class="basic-button" onclick="resetWelcomeMessage(${override - 1})">&lt;</button> <button class="basic-button" onclick="resetWelcomeMessage(${override + 1})">&gt;</button>`;
    applyTranslations(div);
    return;
}
