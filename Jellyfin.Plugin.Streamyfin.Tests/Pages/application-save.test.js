// The Application tab's own script saving, against a dashboard made of stubs. The shared module
// and the form are the real files; the shared module is a copy of its own, loaded under a query
// so it starts the way the dashboard starts it, reading the defaults a free setting shows,
// rather than the copy the other tests load without them.

import { afterAll, afterEach, beforeAll, describe, expect, spyOn, test } from "bun:test";

const PAGES = new URL("../../Jellyfin.Plugin.Streamyfin/Pages/", import.meta.url).pathname;
const jsYaml = await import(`${PAGES}Libraries/js-yaml.min.js`);

const field = (key, control, title) => ({
    key, category: "Playback controls", group: "Skip and seek", title, description: "", control,
    lockable: true, minimum: null, maximum: null, step: null, options: null, dependsOn: null,
});
const FIELDS = [
    field("forwardSkipTime", "Number", "Forward skip time"),
    field("enableDoubleTapToSeek", "Toggle", "Double tap to seek"),
];
const DEFAULTS = {
    settings: { forwardSkipTime: { locked: false, value: 30 }, enableDoubleTapToSeek: { locked: false, value: false } },
};

// What the server holds as each test starts.
const STORED = () => ({ settings: { forwardSkipTime: { locked: false, value: 30 } }, other: { homePage: "Application" } });
let stored;
let saved;
let answerSave;
const served = {};

const page = (name) => ({
    "shared.js": `${PAGES}shared.js?application-save`,
    "settings-form.js": `${PAGES}settings-form.js`,
    "js-yaml.js": `${PAGES}Libraries/js-yaml.min.js`,
})[name];

const ajax = async ({ type = "GET", url, data }) => {
    const path = String(url);
    if (type === "POST" && path.endsWith("streamyfin/config/yaml")) {
        const sent = JSON.parse(data).Value;
        saved.push(sent);
        const answer = await answerSave();
        if (!answer.Error) stored = jsYaml.load(sent);
        // A proxy that gives up once the server stored it: the answer never reaches the page.
        if (answer.lost) throw new Error("504 Gateway Timeout");
        return { json: async () => answer };
    }
    if (path.endsWith("streamyfin/config/default")) return { json: async () => ({ Value: jsYaml.dump(DEFAULTS) }) };
    if (path.endsWith("streamyfin/config/yaml")) return { json: async () => ({ Value: jsYaml.dump(stored) }) };
    if (path.endsWith("streamyfin/v1/settings/form")) return { json: async () => FIELDS };
    return { json: async () => ({}) };
};

let shared;
let applicationTab;

beforeAll(async () => {
    for (const key of ["Streamyfin", "ApiClient", "Dashboard", "LibraryMenu"]) served[key] = window[key];
    for (const key of ["fetch"]) served[key] = globalThis[key];

    stored = STORED();
    delete window.Streamyfin;
    window.ApiClient = {
        getUrl: (path) => page(/configurationpage\?name=(.+)$/.exec(path)?.[1]) ?? `http://server/${path}`,
        ajax,
        getCultures: async () => [],
        getInstalledPlugins: async () => [],
    };
    window.LibraryMenu = { setTabs() {} };
    globalThis.fetch = async () => new Response("{}");

    shared = await import(page("shared.js"));
    ({ default: applicationTab } = await import(`${PAGES}Application/index.js`));
});

afterAll(() => {
    for (const [key, value] of Object.entries(served)) {
        if (key === "fetch") globalThis.fetch = value;
        else window[key] = value;
    }
});

const mounted = [];
let alerts;

const show = async (view) => {
    view.dispatchEvent(new Event("viewshow"));
    for (let tries = 0; tries < 200 && !view.querySelector('#sf-editor .sf-row[data-key="forwardSkipTime"]'); tries += 1) {
        await new Promise((resolve) => setTimeout(resolve, 5));
    }
};

const open = async () => {
    stored = STORED();
    alerts = [];
    saved = [];
    answerSave = async () => ({ Error: false });
    window.Dashboard = {
        showLoadingMsg() {},
        hideLoadingMsg() {},
        processPluginConfigurationUpdateResult() {},
        alert: (message) => alerts.push(message),
    };
    // As the dashboard does, from what the server sent.
    shared.setYamlConfig(jsYaml.dump(stored));
    const host = document.createElement("div");
    host.innerHTML = await Bun.file(`${PAGES}Application/index.html`).text();
    document.body.appendChild(host);
    mounted.push(host);
    const view = host.querySelector("[data-role='page']") ?? host;
    applicationTab(view);
    await show(view);
    return view;
};

afterEach(() => {
    for (const node of mounted.splice(0)) node.remove();
});

const state = (view, key, to) => view.querySelector(`.sf-row[data-key="${key}"] .sf-state button[data-state="${to}"]`).click();
const summary = (view) => view.querySelector("#sf-dock-summary").textContent;
const held = () => {
    let release;
    const gate = new Promise((resolve) => { release = resolve; });
    answerSave = async () => {
        await gate;
        return { Error: false };
    };
    return release;
};
const settle = () => new Promise((resolve) => setTimeout(resolve, 20));

describe("saving the Application tab", () => {
    // The answer marked the whole form saved, a change made while the save was on its way
    // included, and nothing ever sent that change.
    test("a change made while the save is on its way is still to save once it is stored", async () => {
        const view = await open();
        const release = held();

        state(view, "forwardSkipTime", "locked");
        view.querySelector("#sf-save").click();
        state(view, "enableDoubleTapToSeek", "locked");
        release();
        await settle();

        expect(jsYaml.load(saved[0]).settings.enableDoubleTapToSeek).toBeUndefined();
        expect(summary(view)).toBe("1 unsaved");
    });

    // Shown again before the answer came, the tab was drawn from the configuration before the
    // edit, and its next save sent the old value back over the stored one.
    test("a tab shown again while its save is on its way shows the edit, and saves it again", async () => {
        const view = await open();
        const release = held();

        state(view, "forwardSkipTime", "locked");
        view.querySelector("#sf-save").click();
        view.dispatchEvent(new Event("viewhide"));
        await show(view);
        release();
        await settle();

        state(view, "enableDoubleTapToSeek", "locked");
        view.querySelector("#sf-save").click();
        await settle();

        expect(jsYaml.load(saved[1]).settings.forwardSkipTime.locked).toBe(true);
        expect(stored.settings.forwardSkipTime.locked).toBe(true);
    });

    // A proxy that answers 504 once the server stored the save: the tab kept the change to save,
    // and Discard then showed the old value while the server held the new one.
    test("a save whose answer is lost, which the server stored, is marked saved", async () => {
        const view = await open();
        answerSave = async () => ({ Error: false, lost: true });
        const logged = spyOn(console, "error").mockImplementation(() => {});

        try {
            state(view, "forwardSkipTime", "locked");
            view.querySelector("#sf-save").click();
            await settle();
        } finally {
            logged.mockRestore();
        }

        expect(stored.settings.forwardSkipTime.locked).toBe(true);
        expect(summary(view)).toBe("Nothing to save");
    });

    // The answer marked the form the page held then, and a tab left meanwhile holds none: the
    // click's handler threw, which the dashboard logs.
    test("a tab left before its save is answered takes the answer without an error", async () => {
        const view = await open();
        const release = held();
        const logged = spyOn(console, "error").mockImplementation(() => {});

        let errors;
        try {
            state(view, "forwardSkipTime", "locked");
            view.querySelector("#sf-save").click();
            view.dispatchEvent(new Event("viewhide"));
            release();
            await settle();
            errors = logged.mock.calls.map((call) => String(call[0]));
        } finally {
            logged.mockRestore();
        }

        expect(errors).toEqual([]);
        expect(stored.settings.forwardSkipTime.locked).toBe(true);
    });
});
