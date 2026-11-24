// Asking the person before doing something they cannot undo.
//
// Measured on throwaway servers running 10.11.11 and 12.0.0: the dashboard's own
// `Dashboard.confirm(message, title, callback)` returns undefined and answers through the
// callback, `true` for yes and `false` for no. Reading its return value therefore reads
// every cancelled confirmation as a yes, which is what deleted a settings group when the
// administrator clicked Annuler.

import { afterEach, beforeEach, describe, expect, spyOn, test } from "bun:test";

// shared.js asks the dashboard for its address as it loads, and loads the configuration
// unless a page has already done it, so both are in place before the module is.
window.ApiClient = { getUrl: (path) => `http://server/${path}` };
window.Streamyfin = { shared: true };

const {
    confirmed,
    drawLegend,
    forgetKept,
    getConfig,
    KEPT_DESCRIPTIONS,
    keyedEventListener,
    paintDisplaySwitches,
    readKept,
    REREAD,
    same,
    saveConfig,
    saveConfigWith,
    saveYamlConfig,
    setConfig,
    setOnConfigUpdatedListener,
    setYamlConfig,
    tools,
    warmKept,
    wireDisplaySwitches,
} = await import("../../Jellyfin.Plugin.Streamyfin/Pages/shared.js");

// The library the dashboard loads as js-yaml.js, so what is read and written here is what
// the pages read and write.
const jsYaml = await import("../../Jellyfin.Plugin.Streamyfin/Pages/Libraries/js-yaml.min.js");

afterEach(() => {
    delete window.Dashboard;
    delete window.confirm;
});

describe("confirmed", () => {
    test("a dashboard that answers through its callback is believed", async () => {
        const asked = [];
        window.Dashboard = {
            confirm(message, title, callback) {
                asked.push({ message, title });
                callback(false);
            },
        };

        expect(await confirmed("Delete the group?")).toBe(false);
        expect(asked).toEqual([{ message: "Delete the group?", title: "Streamyfin" }]);

        window.Dashboard.confirm = (message, title, callback) => callback(true);

        expect(await confirmed("Delete the group?")).toBe(true);
    });

    test("a dashboard that hands back a promise is believed too", async () => {
        window.Dashboard = { confirm: () => Promise.resolve() };
        expect(await confirmed("Go ahead?")).toBe(true);

        window.Dashboard = { confirm: () => Promise.reject(new Error("no")) };
        expect(await confirmed("Go ahead?")).toBe(false);
    });

    test("the first answer is the one, whichever shape it arrives in", async () => {
        window.Dashboard = {
            confirm(message, title, callback) {
                callback(false);
                return Promise.resolve();
            },
        };

        expect(await confirmed("Delete the group?")).toBe(false);
    });

    test("without a dashboard it is the browser's own question", async () => {
        delete window.Dashboard;
        window.confirm = () => false;

        expect(await confirmed("Delete the group?")).toBe(false);

        window.confirm = () => true;

        expect(await confirmed("Delete the group?")).toBe(true);
    });

    test("a dashboard that never answers never says yes", async () => {
        window.Dashboard = { confirm: () => undefined };

        const answer = await Promise.race([
            confirmed("Delete the group?"),
            new Promise((resolve) => setTimeout(() => resolve("still waiting"), 50)),
        ]);

        expect(answer).toBe("still waiting");
    });
});

// The Descriptions and Keys switches were on the Application tab only, Descriptions alone
// on Targeting, and neither on Notifications, so an administrator could hide the keys
// on one tab and find them everywhere else.
describe("the display switches", () => {
    const view = (...ids) => {
        const root = document.createElement("div");
        for (const id of ids) {
            const button = document.createElement("button");
            button.id = id;
            button.innerHTML = '<span class="sf-pip"></span>';
            root.appendChild(button);
        }
        return root;
    };

    afterEach(() => window.localStorage.clear());

    test("show the remembered choice and hand it over at once", () => {
        window.localStorage.setItem("streamyfin.admin.descriptions", "off");
        const root = view("sf-terse", "sf-keys");
        const applied = [];

        wireDisplaySwitches(root, undefined, (choice) => applied.push(choice));

        expect(applied).toEqual([{ descriptions: false, keys: false, legend: true }]);
        expect(root.querySelector("#sf-terse").getAttribute("aria-pressed")).toBe("false");
        expect(root.querySelector("#sf-terse .sf-pip").textContent).toBe("OFF");
    });

    test("a click flips one switch, remembers it, and hands both over", () => {
        const root = view("sf-terse", "sf-keys");
        const applied = [];
        wireDisplaySwitches(root, undefined, (choice) => applied.push(choice));

        root.querySelector("#sf-keys").click();

        expect(applied.at(-1)).toEqual({ descriptions: true, keys: true, legend: true });
        expect(window.localStorage.getItem("streamyfin.admin.keys")).toBe("on");
        expect(root.querySelector("#sf-keys .sf-pip").textContent).toBe("ON");
    });

    test("one tab's choice is the next tab's", () => {
        const first = view("sf-terse");
        wireDisplaySwitches(first, undefined, () => {});
        first.querySelector("#sf-terse").click();

        const applied = [];
        paintDisplaySwitches(view("sf-terse", "sf-keys"), (choice) => applied.push(choice));

        expect(applied).toEqual([{ descriptions: false, keys: false, legend: true }]);
    });

    test("a click applies what was clicked even when storage refuses it", () => {
        const root = view("sf-terse", "sf-keys");
        const applied = [];
        wireDisplaySwitches(root, undefined, (choice) => applied.push(choice));
        const setItem = Storage.prototype.setItem;
        Storage.prototype.setItem = () => { throw new Error("full"); };
        try {
            root.querySelector("#sf-terse").click();
        } finally {
            Storage.prototype.setItem = setItem;
        }

        expect(root.querySelector("#sf-terse").getAttribute("aria-pressed")).toBe("false");
        expect(applied.at(-1)).toEqual({ descriptions: false, keys: false, legend: true });
    });

    // Notifications wires its switches once and repaints them on each showing. A click
    // after another tab had changed the other switch applied that switch's old value.
    test("a click after another tab's change applies both switches as shown", () => {
        const root = view("sf-terse", "sf-keys");
        const applied = [];
        wireDisplaySwitches(root, undefined, (choice) => applied.push(choice));

        window.localStorage.setItem("streamyfin.admin.descriptions", "off");
        paintDisplaySwitches(root, () => {});
        root.querySelector("#sf-keys").click();

        expect(applied.at(-1)).toEqual({ descriptions: false, keys: true, legend: true });
    });

    // The Targeting tab's events card is outside the form, and kept its help text with
    // Descriptions off.
    test("a card that follows the switches hides its help and its keys with them", () => {
        const root = view("sf-terse", "sf-keys");
        const card = document.createElement("section");
        card.setAttribute("data-sf-follows-display", "");
        root.appendChild(card);
        wireDisplaySwitches(root, undefined, () => {});

        expect(card.classList.contains("is-terse")).toBe(false);
        expect(card.classList.contains("is-keyless")).toBe(true);

        root.querySelector("#sf-terse").click();
        root.querySelector("#sf-keys").click();

        expect(card.classList.contains("is-terse")).toBe(true);
        expect(card.classList.contains("is-keyless")).toBe(false);
    });

    // A tab of its own, with the banner naming it.
    const tab = (name, ...ids) => {
        const root = view(...ids);
        const banner = document.createElement("div");
        banner.setAttribute("data-sf-legend-banner", name);
        const cross = document.createElement("button");
        cross.setAttribute("data-sf-legend-close", "");
        banner.appendChild(cross);
        root.appendChild(banner);
        return { root, banner, cross };
    };

    test("the Legend switch shows and hides its own tab's banner", () => {
        const application = tab("Application", "sf-terse", "sf-legend-toggle");
        wireDisplaySwitches(application.root, undefined, () => {});

        expect(application.banner.hidden).toBe(false);
        application.root.querySelector("#sf-legend-toggle").click();
        expect(application.banner.hidden).toBe(true);
        expect(window.localStorage.getItem("streamyfin.admin.legend.Application")).toBe("off");

        application.root.querySelector("#sf-legend-toggle").click();
        expect(application.banner.hidden).toBe(false);
    });

    // Closed on one tab, the legend stays on the others, and stays closed on that one.
    test("the cross closes the banner on its own tab only, and that tab remembers it", () => {
        const application = tab("Application", "sf-terse", "sf-legend-toggle");
        const applied = [];
        wireDisplaySwitches(application.root, undefined, (choice) => applied.push(choice));

        application.cross.click();

        expect(application.banner.hidden).toBe(true);
        expect(application.root.querySelector("#sf-legend-toggle").getAttribute("aria-pressed")).toBe("false");
        expect(applied.at(-1)).toMatchObject({ descriptions: true, legend: false });

        const targeting = tab("Targeting", "sf-legend-toggle");
        paintDisplaySwitches(targeting.root, () => {});
        expect(targeting.banner.hidden).toBe(false);
        expect(targeting.root.querySelector("#sf-legend-toggle .sf-pip").textContent).toBe("ON");

        const again = tab("Application", "sf-legend-toggle");
        paintDisplaySwitches(again.root, () => {});
        expect(again.banner.hidden).toBe(true);
    });

    test("a view without one of the switches is still wired", () => {
        const root = view("sf-terse");
        const applied = [];

        wireDisplaySwitches(root, undefined, (choice) => applied.push(choice));
        root.querySelector("#sf-terse").click();

        expect(applied.at(-1)).toEqual({ descriptions: false, keys: false, legend: true });
    });
});

// The banner that explains the three states can be closed for good, and nothing said what
// a box with a dash meant.
describe("the legend", () => {
    const items = (mount) => [...mount.querySelectorAll(".sf-legend-item")].map((item) => item.textContent);
    const headings = (mount) => [...mount.querySelectorAll(".sf-legend-h")].map((heading) => heading.textContent);

    test("names the three states and the three boxes, each in the shape the rows use", () => {
        const mount = document.createElement("div");

        drawLegend(mount);

        expect(headings(mount)).toEqual(["How a setting reaches users", "What a box says"]);
        expect(items(mount)).toEqual([
            "Free each user decides",
            "Suggested your value, set once as each user's starting point",
            "Locked your value, and users cannot change it",
            "On",
            "Off",
            "Not set the app uses its own default",
        ]);
        expect([...mount.querySelectorAll(".sf-edge")].map((edge) => edge.className)).toEqual([
            "sf-edge is-free",
            "sf-edge is-suggested",
            "sf-edge is-locked",
        ]);
        const boxes = mount.querySelectorAll("input.sf-check");
        expect([...boxes].map((box) => [box.checked, box.indeterminate, box.disabled])).toEqual([
            [true, false, true],
            [false, false, true],
            [false, true, true],
        ]);
        expect(mount.querySelector(".sf-legend-note").textContent).toContain("Only what you set here travels");
    });

    // On a level there is no Free and nothing unset: a setting is listed there or the
    // level above decides it.
    test("on a level, says what a level does", () => {
        const mount = document.createElement("div");

        drawLegend(mount, { kind: "level" });

        expect(items(mount)).toContain("Not listed the level above decides");
        expect(items(mount).some((text) => text.startsWith("Free"))).toBe(false);
        expect(mount.querySelectorAll("input.sf-check")).toHaveLength(2);
        expect(mount.querySelector(".sf-legend-note")).toBeNull();
    });

    // Notifications and Home have boxes and nothing free or locked.
    test("where nothing is free or locked, says what a box says and nothing else", () => {
        const mount = document.createElement("div");

        drawLegend(mount, { kind: "boxes" });

        expect(headings(mount)).toEqual(["What a box says"]);
        expect(items(mount)).toEqual(["On", "Off"]);
        expect(mount.classList.contains("is-single")).toBe(true);
    });

    test("is drawn once however often the page is shown", () => {
        const mount = document.createElement("div");

        drawLegend(mount);
        drawLegend(mount);

        expect(mount.querySelectorAll(".sf-legend-item")).toHaveLength(6);
    });
});

// Moving between tabs asked for the same descriptions again each time, one round trip
// after another, which a phone or a VPN turns into most of a second per tab.
describe("answers kept between tabs", () => {
    let asked;

    beforeEach(() => {
        asked = [];
        forgetKept();
        window.ApiClient.ajax = async ({ type = "GET", url }) => {
            asked.push(`${type} ${url.replace("http://server/", "")}`);
            return { json: async () => (type === "POST" ? { Error: false } : { fields: [{ key: "forwardSkipTime" }] }) };
        };
    });

    afterEach(() => {
        delete window.ApiClient.ajax;
    });

    test("a description asked for twice is fetched once, and each caller gets its own copy", async () => {
        const first = await readKept("streamyfin/v1/settings/form");
        first.fields.push({ key: "changed by the first page" });

        const second = await readKept("streamyfin/v1/settings/form");

        expect(asked).toEqual(["GET streamyfin/v1/settings/form"]);
        expect(second.fields).toEqual([{ key: "forwardSkipTime" }]);
    });

    test("one older than it may be is asked for again", async () => {
        await readKept("streamyfin/v1/settings/form");
        await readKept("streamyfin/v1/settings/form", 0);

        expect(asked).toHaveLength(2);
    });

    test("a failed answer is not kept", async () => {
        window.ApiClient.ajax = async ({ url }) => {
            asked.push(url);
            throw new Error("offline");
        };

        await expect(readKept("streamyfin/v1/settings/form")).rejects.toThrow("offline");
        await expect(readKept("streamyfin/v1/settings/form")).rejects.toThrow("offline");

        expect(asked).toHaveLength(2);
    });

    test("a save forgets them, so nothing read before it is shown after", async () => {
        window.Dashboard = { showLoadingMsg() {}, hideLoadingMsg() {}, processPluginConfigurationUpdateResult() {}, alert() {} };
        tools.jsYaml = { dump: (value) => JSON.stringify(value), load: (text) => JSON.parse(text) };
        setYamlConfig(JSON.stringify({ settings: {} }));
        await readKept("streamyfin/v1/settings/form");

        expect(await saveConfig()).toBe(true);
        await readKept("streamyfin/v1/settings/form");

        expect(asked).toEqual(["GET streamyfin/v1/settings/form", "POST streamyfin/config/yaml", "GET streamyfin/v1/settings/form"]);
    });

    test("warming asks once for what each other tab starts with", async () => {
        warmKept();
        warmKept();
        await Promise.all(KEPT_DESCRIPTIONS.map((path) => readKept(path)));

        expect(asked).toEqual(KEPT_DESCRIPTIONS.map((path) => `GET ${path}`));
    });
});

// The Yaml tab set what its editor held as the configuration every tab saves, and only
// then sent it. A refused document stayed there: the next save from another tab sent it
// again with that tab's change, and a document refused for setting nothing, `settings: {}`,
// was then stored with the start page the Other tab added, which emptied the rest.
describe("saving the Yaml tab", () => {
    const FULL = {
        settings: { forwardSkipTime: { locked: false, value: 30 } },
        notifications: { itemAdded: { enabled: true } },
        other: { homePage: "Application" },
    };
    const EMPTY = "Nothing was saved: the Yaml is empty.";
    let posted;
    let alerts;
    let answer;
    let heard;
    let server;
    let before;

    // A server that stores what it is sent unless it refuses it, and answers a read with
    // what it holds. `keep` is what it makes of a document it stores, `lost` throws the
    // answer away after storing, `down` fails every request, and `gate`, while set, holds
    // an answer back until the test releases it, counting in `held` the answers it held.
    const serve = () => {
        server = { holds: jsYaml.dump(FULL), keep: (yaml) => yaml, lost: false, down: false, reads: 0, gate: null, held: 0 };
        window.ApiClient.ajax = async ({ type = "GET", data }) => {
            if (server.down) throw new Error("offline");
            if (type === "GET") {
                server.reads += 1;
                return { json: async () => ({ Value: server.holds }) };
            }
            posted.push(JSON.parse(data).Value);
            const gate = server.gate;
            if (gate) {
                server.held += 1;
                await gate;
            }
            if (!answer.Error) server.holds = server.keep(JSON.parse(data).Value);
            if (server.lost) throw new Error("the answer was lost");
            return { json: async () => answer };
        };
    };

    beforeEach(() => {
        before = { jsYaml: tools.jsYaml, config: getConfig() };
        posted = [];
        alerts = [];
        heard = [];
        answer = { Error: false };
        tools.jsYaml = jsYaml;
        window.Dashboard = {
            showLoadingMsg() {},
            hideLoadingMsg() {},
            processPluginConfigurationUpdateResult() {},
            alert: (message) => alerts.push(message),
        };
        serve();
        setYamlConfig(jsYaml.dump(FULL));
        setOnConfigUpdatedListener("yaml-save-test", (config, raw, cause) => heard.push({ config, raw, cause }));
    });

    afterEach(() => {
        setOnConfigUpdatedListener("yaml-save-test", undefined);
        setOnConfigUpdatedListener("throws", undefined);
        delete window.ApiClient.ajax;
        tools.jsYaml = before.jsYaml;
        setConfig(before.config);
    });

    test("a refused document leaves the configuration as it was, for the next tab that saves", async () => {
        answer = { Error: true, Message: EMPTY };

        expect(await saveYamlConfig("settings: {}\n")).toBe(false);

        expect(alerts).toEqual([EMPTY]);
        expect(getConfig()).toEqual(FULL);
        expect(heard).toEqual([]);
        expect(server.reads).toBe(0);

        answer = { Error: false };
        expect(await saveConfig()).toBe(true);
        expect(jsYaml.load(posted[1])).toEqual(FULL);
    });

    test("a stored document becomes the configuration, and every tab hears of it", async () => {
        const text = "settings:\n  forwardSkipTime:\n    value: 10\n";
        const stored = { settings: { forwardSkipTime: { value: 10 } } };

        expect(await saveYamlConfig(text)).toBe(true);

        expect(jsYaml.load(posted[0])).toEqual(stored);
        expect(getConfig()).toEqual(stored);
        expect(heard).toEqual([{ config: stored, raw: text, cause: undefined }]);
    });

    // The server folds Seerr's block and the old spellings into the keys. Kept as written,
    // the block made the Application tab show Seerr's address as Free, and a change there
    // sent the block with the new key, which the server refused as two different values.
    test("what the server stored is what the other tabs save next, not the document as written", async () => {
        server.keep = () => "settings:\n  jellyseerrServerUrl:\n    value: http://seerr.example\n";

        expect(await saveYamlConfig("settings:\n  seerr:\n    serverUrl:\n      value: http://seerr.example\n")).toBe(true);

        expect(getConfig()).toEqual({ settings: { jellyseerrServerUrl: { value: "http://seerr.example" } } });
        expect(heard.map((one) => one.config)).toEqual([getConfig()]);
    });

    // A proxy that times out, or a connection dropped on the way back, after the server
    // stored the document: the pages kept the configuration from before, and the next save
    // from another tab put it back over the document without a word.
    test("a request that fails after the server stored the document takes what the server holds", async () => {
        server.lost = true;
        const logged = spyOn(console, "error").mockImplementation(() => {});

        try {
            expect(await saveYamlConfig("settings:\n  forwardSkipTime:\n    value: 10\n")).toBe(false);
        } finally {
            logged.mockRestore();
        }

        const held = { settings: { forwardSkipTime: { value: 10 } } };
        expect(getConfig()).toEqual(held);
        // Told that it was read back, so the editor keeps what the administrator wrote.
        expect(heard).toEqual([{ config: held, raw: undefined, cause: REREAD }]);
    });

    // Kept as the tab held it, the part left the server's, and the next save of that tab posted it
    // over what the document stored. A change made while a document is on its way is lost to it,
    // as on develop; #284 tracks saves that would say so.
    test("a stored document is what the tabs hold of every part but the settings changed meanwhile", async () => {
        let release;
        server.gate = new Promise((resolve) => { release = resolve; });

        const saving = saveYamlConfig(jsYaml.dump({ ...FULL, notifications: { itemAdded: { enabled: false } } }));
        setConfig({ ...getConfig(), notifications: { itemAdded: { enabled: true }, sessionStarted: { enabled: true } } });
        server.gate = null;
        release();

        expect(await saving).toBe(true);
        expect(getConfig().notifications).toEqual({ itemAdded: { enabled: false } });
    });

    test("a request that fails before the server stored anything leaves the configuration as it was", async () => {
        server.keep = () => server.holds;
        server.lost = true;
        const logged = spyOn(console, "error").mockImplementation(() => {});

        try {
            expect(await saveYamlConfig("settings:\n  forwardSkipTime:\n    value: 10\n")).toBe(false);
        } finally {
            logged.mockRestore();
        }

        expect(getConfig()).toEqual(FULL);
    });

    test("a server that cannot be reached leaves the configuration as it was", async () => {
        server.down = true;
        const logged = spyOn(console, "error").mockImplementation(() => {});

        try {
            expect(await saveYamlConfig("settings:\n  forwardSkipTime:\n    value: 10\n")).toBe(false);
        } finally {
            logged.mockRestore();
        }

        expect(getConfig()).toEqual(FULL);
        expect(heard).toEqual([]);
    });

    // A tab whose listener throws on what it was handed, as the wording card did on a
    // `wording` list with an empty item, which the server stores.
    test("a tab that throws on the new configuration stops neither the save nor the tabs after it", async () => {
        setOnConfigUpdatedListener("yaml-save-test", undefined);
        setOnConfigUpdatedListener("throws", () => {
            throw new TypeError("null is not an object (evaluating 'one.key')");
        });
        setOnConfigUpdatedListener("yaml-save-test", (config, raw, cause) => heard.push({ config, raw, cause }));
        const logged = spyOn(console, "error").mockImplementation(() => {});

        let stored;
        let logs;
        try {
            stored = await saveYamlConfig("notifications:\n  wording:\n    -\n");
            logs = logged.mock.calls.length;
        } finally {
            logged.mockRestore();
        }

        expect(stored).toBe(true);
        expect(heard).toHaveLength(1);
        expect(logs).toBe(1);
    });

    // The server judges a document that sets nothing, `{}` as much as a blank one, and
    // says so in the sentence it answers with.
    test("a document with nothing in it is sent, so the server can say why it is not saved", async () => {
        answer = { Error: true, Message: EMPTY };

        for (const text of ["", "# only a comment\n", "---\n"]) {
            expect(await saveYamlConfig(text)).toBe(false);
        }

        expect(posted.map((value) => jsYaml.load(value) ?? null)).toEqual([null, null, null]);
        expect(alerts).toEqual([EMPTY, EMPTY, EMPTY]);
        expect(getConfig()).toEqual(FULL);
    });

    test("a document that cannot be read is not sent, and says where it stops", async () => {
        expect(await saveYamlConfig("settings:\n  forwardSkipTime: [1\n")).toBe(false);

        expect(posted).toEqual([]);
        expect(alerts).toEqual([
            "Nothing was saved: the Yaml cannot be read at line 3, column 1 (unexpected end of the stream within a flow collection).",
        ]);
        expect(getConfig()).toEqual(FULL);
        expect(heard).toEqual([]);
    });

    // js-yaml reads one document, and says so without a place in the file.
    test("two documents in one file are not sent, and say why", async () => {
        expect(await saveYamlConfig("settings: {}\n---\nother: {}\n")).toBe(false);

        expect(posted).toEqual([]);
        expect(alerts).toEqual([
            "Nothing was saved: the Yaml cannot be read (expected a single document in the stream, but found more).",
        ]);
        expect(getConfig()).toEqual(FULL);
    });
});

// The server writes its document back through YamlDotNet without a member that is null or empty,
// an empty string or map included, and writes a string such as 5.1 or true without quotes, which
// js-yaml then reads as a number or a boolean. The page compares a setting as it is written back.
describe("comparing a setting as the server writes it back", () => {
    test("a member that is null, an empty list, an empty string or an empty map is one left out", () => {
        expect(same({ locked: false, value: null }, { locked: false })).toBe(true);
        expect(same({ value: [], locked: false }, { locked: false })).toBe(true);
        expect(same({ value: { custom: { endpoint: "" } } }, { value: { custom: {} } })).toBe(true);
        expect(same({ value: { custom: {} } }, {})).toBe(true);
        expect(same({ locked: false, value: "x" }, { locked: false })).toBe(false);
    });

    test("a string the server writes without quotes is the number or the boolean read back", () => {
        expect(same({ value: "5.1" }, { value: 5.1 })).toBe(true);
        expect(same({ value: "true" }, { value: true })).toBe(true);
        expect(same({ value: "5.1" }, { value: 5 })).toBe(false);
    });

    test("a list keeps its empty and null items, which are not members", () => {
        expect(same(["", null], [])).toBe(false);
        expect(same({ locked: true, value: { b: 1, a: 2 } }, { value: { a: 2, b: 1 }, locked: true })).toBe(true);
    });
});

describe("saving a tab's edit to the settings", () => {
    const FULL = {
        settings: { forwardSkipTime: { locked: false, value: 30 }, enableDoubleTapToSeek: { locked: true, value: true } },
        notifications: { itemAdded: { enabled: true } },
        other: { homePage: "Application" },
    };
    const ANOTHER_TAB = { itemAdded: { enabled: false } };
    let posted;
    let alerts;
    let heard;
    let server;
    let before;

    // A server that stores what it is sent unless it refuses it, and answers a read with what
    // it holds. `answers` are its answers in turn, `keep` what it makes of a document it stores,
    // `lost` throws the answer away after storing, `unread` fails a read, and `gate`, while set,
    // holds an answer back until the test releases it, counting in `held` the answers it held.
    const serve = () => {
        server = { holds: jsYaml.dump(FULL), keep: (yaml) => yaml, lost: false, unread: false, answers: [], gate: null, held: 0 };
        window.ApiClient.ajax = async ({ type = "GET", data }) => {
            if (type === "GET") {
                if (server.unread) throw new Error("offline");
                return { json: async () => ({ Value: server.holds }) };
            }
            const sent = JSON.parse(data).Value;
            posted.push(sent);
            const answer = server.answers.shift() ?? { Error: false };
            const { gate, lost } = server;
            if (gate) {
                server.held += 1;
                await gate;
            }
            if (!answer.Error) server.holds = server.keep(sent);
            if (lost) throw new Error("the answer was lost");
            return { json: async () => answer };
        };
    };
    const hold = () => {
        let release;
        server.gate = new Promise((resolve) => { release = resolve; });
        return () => { server.gate = null; release(); };
    };
    const forward = (value) => (current) => ({ ...current, settings: { ...current.settings, forwardSkipTime: { locked: false, value } } });
    const quietly = async (run) => {
        const logged = spyOn(console, "error").mockImplementation(() => {});
        try {
            return await run();
        } finally {
            logged.mockRestore();
        }
    };

    beforeEach(() => {
        before = { jsYaml: tools.jsYaml, config: getConfig() };
        posted = [];
        alerts = [];
        heard = [];
        tools.jsYaml = jsYaml;
        window.Dashboard = {
            showLoadingMsg() {},
            hideLoadingMsg() {},
            processPluginConfigurationUpdateResult() {},
            alert: (message) => alerts.push(message),
        };
        serve();
        setYamlConfig(jsYaml.dump(FULL));
        setOnConfigUpdatedListener("edit-save-test", (config, raw, cause) => heard.push({ config: structuredClone(config), raw, cause }));
    });

    afterEach(() => {
        setOnConfigUpdatedListener("edit-save-test", undefined);
        delete window.ApiClient.ajax;
        tools.jsYaml = before.jsYaml;
        setConfig(before.config);
    });

    // Every tab draws from the configuration they share, so a tab shown while the save is on
    // its way shows the edit, as one shown after it would, and saves it along with its own.
    test("the edit is in the configuration every tab shares while it is on its way", async () => {
        const release = hold();

        const saving = saveConfigWith(forward(10));
        expect(getConfig().settings.forwardSkipTime).toEqual({ locked: false, value: 10 });
        expect(heard.at(-1).config.settings.forwardSkipTime.value).toBe(10);
        expect(server.held).toBe(1);
        release();

        expect(await saving).toBe(true);
        expect(jsYaml.load(posted[0]).settings.forwardSkipTime.value).toBe(10);
    });

    // The Application and Home tabs put back the whole configuration from before on a
    // refusal, and a change another tab made meanwhile went with it (#262).
    test("a refused edit puts back the settings it changed, and what another tab changed meanwhile stays", async () => {
        server.answers = [{ Error: true, Message: "Forward skip time accepts 0 to 60, and this is 100." }];
        const release = hold();

        const saving = saveConfigWith(forward(100));
        setConfig({ ...getConfig(), notifications: ANOTHER_TAB });
        release();

        expect(await saving).toBe(false);
        expect(server.held).toBe(1);
        expect(getConfig()).toEqual({ ...FULL, notifications: ANOTHER_TAB });
        expect(alerts).toEqual(["Forward skip time accepts 0 to 60, and this is 100."]);
    });

    // The form writes a setting as { value, locked } and the server's document reads back as
    // { locked, value }: compared as text, every setting the form wrote counted as changed, and
    // a refusal put back one the Yaml tab had stored meanwhile.
    test("a refusal leaves alone a setting the edit only wrote with its keys in another order", async () => {
        server.answers = [{ Error: true, Message: "No." }];
        const release = hold();

        const saving = saveConfigWith((current) => ({
            ...current,
            settings: {
                forwardSkipTime: { locked: false, value: 100 },
                enableDoubleTapToSeek: { value: true, locked: true },
            },
        }));
        setConfig({ ...getConfig(), settings: { ...getConfig().settings, enableDoubleTapToSeek: { locked: true, value: false } } });
        release();

        expect(await saving).toBe(false);
        expect(getConfig().settings).toEqual({
            forwardSkipTime: { locked: false, value: 30 },
            enableDoubleTapToSeek: { locked: true, value: false },
        });
    });

    // The Yaml tab stored the setting while this save was on its way: putting back the value from
    // before wrote over what the server now holds, and the next save from any tab undid it.
    test("a refusal leaves a setting the Yaml tab stored meanwhile as it stored it", async () => {
        server.answers = [{ Error: true, Message: "No." }, { Error: false }];
        const release = hold();

        const saving = saveConfigWith(forward(100));
        server.gate = null;
        const document = { ...getConfig(), settings: { ...getConfig().settings, forwardSkipTime: { locked: false, value: 45 } } };
        expect(await saveYamlConfig(jsYaml.dump(document))).toBe(true);
        release();

        expect(await saving).toBe(false);
        expect(getConfig().settings.forwardSkipTime).toEqual({ locked: false, value: 45 });
    });

    // Compared by value, a setting the Yaml tab stored as this save had written it looked
    // untouched, and the refusal put the old value back over the stored one.
    test("a refusal leaves a setting the Yaml tab stored meanwhile, even with the same value", async () => {
        server.answers = [{ Error: true, Message: "Double tap to seek cannot be locked here." }, { Error: false }];
        const release = hold();

        const saving = saveConfigWith((current) => ({
            ...current,
            settings: { forwardSkipTime: { locked: false, value: 10 }, enableDoubleTapToSeek: { locked: false, value: true } },
        }));
        server.gate = null;
        const document = { ...getConfig(), settings: { ...FULL.settings, forwardSkipTime: { locked: false, value: 10 } } };
        expect(await saveYamlConfig(jsYaml.dump(document))).toBe(true);
        release();

        expect(await saving).toBe(false);
        expect(getConfig().settings.forwardSkipTime).toEqual({ locked: false, value: 10 });
    });

    test("a refused save leaves a setting a newer save of the same tab changed", async () => {
        server.answers = [{ Error: true, Message: "No." }, { Error: false }];
        const release = hold();

        const first = saveConfigWith(forward(10));
        const second = saveConfigWith(forward(20));
        release();

        expect(await first).toBe(false);
        expect(await second).toBe(true);
        expect(getConfig().settings.forwardSkipTime.value).toBe(20);
    });

    // Read back while a newer edit of the setting was still on its way, the server's older value
    // took its place, and the newer edit, once stored, was no longer what the tabs held.
    test("a reread after a lost answer leaves a setting a newer save on its way changed", async () => {
        server.lost = true;
        const releaseFirst = hold();
        const first = saveConfigWith(forward(10));
        server.lost = false;
        const releaseSecond = hold();
        const second = saveConfigWith(forward(20));

        releaseFirst();
        expect(await quietly(() => first)).toBe(true);
        expect(getConfig().settings.forwardSkipTime.value).toBe(20);
        releaseSecond();

        expect(await second).toBe(true);
        expect(getConfig().settings.forwardSkipTime.value).toBe(20);
        expect(jsYaml.load(server.holds).settings.forwardSkipTime.value).toBe(20);
    });

    test("a refusal puts back a setting the edit took out, and takes out one it added", async () => {
        server.answers = [{ Error: true, Message: "No." }];

        const saved = await saveConfigWith((current) => ({
            ...current,
            settings: { forwardSkipTime: current.settings.forwardSkipTime, rewindSkipTime: { locked: false, value: 5 } },
        }));

        expect(saved).toBe(false);
        expect(getConfig()).toEqual(FULL);
    });

    // A proxy that times out, or a connection dropped on the way back, after the server stored
    // the edit: the tabs held the configuration from before, and the next save from any tab
    // undid the edit without a word (#262).
    // Read back holding the edit, the save counts as stored: the tab marks it saved, where it
    // stayed to save while the server and the other tabs held it, and Discard then showed the
    // old values without putting them back.
    test("a request that fails after the server stored the edit keeps it, and counts as stored", async () => {
        server.lost = true;
        setConfig({ ...getConfig(), notifications: ANOTHER_TAB });

        expect(await quietly(() => saveConfigWith(forward(10)))).toBe(true);

        expect(getConfig()).toEqual({
            ...FULL,
            settings: { ...FULL.settings, forwardSkipTime: { locked: false, value: 10 } },
            notifications: ANOTHER_TAB,
        });
        // Told as any change is, not as a read back: the Yaml editor keeps its own text after a
        // read back, so one it drew while hidden kept an edit the server never stored.
        expect(heard.at(-1).cause).toBeUndefined();
    });

    // A second Save of the same edit, sent before the first was answered, changed nothing the
    // tabs held, and with no setting of its own to find in the read back it counted as stored.
    test("a save of an edit already on its way, its answer lost and nothing stored, is not counted as stored", async () => {
        server.keep = () => server.holds;
        server.lost = true;
        const first = saveConfigWith(forward(10));
        const second = saveConfigWith(forward(10));

        expect([await quietly(() => first), await quietly(() => second)]).toEqual([false, false]);
        expect(jsYaml.load(server.holds).settings.forwardSkipTime.value).toBe(30);
    });

    test("a request that fails before the server stored anything puts back what the server holds", async () => {
        server.keep = () => server.holds;
        server.lost = true;
        setConfig({ ...getConfig(), notifications: ANOTHER_TAB });

        expect(await quietly(() => saveConfigWith(forward(10)))).toBe(false);

        expect(getConfig()).toEqual({ ...FULL, notifications: ANOTHER_TAB });
    });

    // Left as written, an edit that may never have been stored rode along with the next save of
    // another tab, after the tab had discarded it.
    test("a server that cannot be read after a failed request puts back what it was known to hold", async () => {
        server.keep = () => server.holds;
        server.lost = true;
        server.unread = true;

        expect(await quietly(() => saveConfigWith(forward(10)))).toBe(false);

        expect(getConfig().settings.forwardSkipTime.value).toBe(30);
    });

    // The server writes its document without a member that is null or an empty list, which is
    // how the form writes an empty setting: read back, the save looked unstored, and the tab kept
    // it to save while the server held it.
    test("a lost answer counts as stored when the server writes an empty value the form sent as none", async () => {
        const omitted = (value) => Array.isArray(value)
            ? value.map(omitted)
            : value && typeof value === "object"
                ? Object.fromEntries(Object.entries(value)
                    .filter(([, member]) => member !== null && !(Array.isArray(member) && member.length === 0))
                    .map(([key, member]) => [key, omitted(member)]))
                : value;
        server.keep = (yaml) => jsYaml.dump(omitted(jsYaml.load(yaml)));
        setYamlConfig(jsYaml.dump({ ...FULL, settings: { ...FULL.settings, defaultBitrate: { locked: false }, hiddenLibraries: { locked: false } } }));
        server.holds = jsYaml.dump(omitted(getConfig()));
        server.lost = true;

        const saved = await quietly(() => saveConfigWith((current) => ({
            ...current,
            settings: {
                ...current.settings,
                forwardSkipTime: { locked: false, value: 10 },
                defaultBitrate: { value: null, locked: false },
                hiddenLibraries: { value: [], locked: false },
            },
        })));

        expect(saved).toBe(true);
    });

    // Each refusal put back the settings as they were when it was sent, which held the other's
    // edit, still unanswered: the refused value stayed in what every tab saves.
    for (const [order, first, second] of [["the first", 0, 1], ["the second", 1, 0]]) {
        test(`two refused saves of one setting put back what the server holds, ${order} answered first`, async () => {
            server.answers = [{ Error: true, Message: "No." }, { Error: true, Message: "No." }];
            const releases = [];
            const saves = [];
            for (const value of [100, 200]) {
                releases.push(hold());
                saves.push(saveConfigWith(forward(value)));
            }

            releases[first]();
            expect(await saves[first]).toBe(false);
            releases[second]();
            expect(await saves[second]).toBe(false);

            expect(getConfig().settings.forwardSkipTime.value).toBe(30);
        });
    }

    // The second save carried the first one's edit, still unanswered, and the server stored it
    // with its own: the first one's refusal then put back a value the server no longer holds.
    for (const [order, first, second] of [["refusal", 0, 1], ["stored save", 1, 0]]) {
        test(`a refused edit another stored save carried stays as the server holds it, the ${order} answered first`, async () => {
            server.answers = [{ Error: true, Message: "No." }, { Error: false }];
            const releases = [];
            const saves = [];
            releases.push(hold());
            saves.push(saveConfigWith(forward(10)));
            releases.push(hold());
            saves.push(saveConfigWith((current) => ({ ...current, settings: { ...current.settings, enableDoubleTapToSeek: { locked: false, value: false } } })));

            releases[first]();
            await saves[first];
            releases[second]();
            await saves[second];

            const held = jsYaml.load(server.holds).settings;
            expect(held.forwardSkipTime.value).toBe(10);
            expect(getConfig().settings).toEqual(held);
        });
    }

    // The Yaml tab's document, sent first, was stored while an Application save sent after it was
    // on its way, and that save was then refused: what the tabs held left the server's.
    test("a document stored while an edit is on its way, the edit then refused, leaves what the server holds", async () => {
        server.answers = [{ Error: false }, { Error: true, Message: "No." }];
        const releaseDocument = hold();
        const document = saveYamlConfig(jsYaml.dump({ ...FULL, settings: { ...FULL.settings, enableDoubleTapToSeek: { locked: false, value: false } } }));
        const releaseEdit = hold();
        const edit = saveConfigWith(forward(100));

        releaseDocument();
        expect(await document).toBe(true);
        releaseEdit();
        expect(await edit).toBe(false);

        expect(getConfig().settings).toEqual(jsYaml.load(server.holds).settings);
    });

    test("an edit refused while a document sent after it is on its way, then stored, leaves what the server holds", async () => {
        server.answers = [{ Error: true, Message: "No." }, { Error: false }];
        const releaseEdit = hold();
        const edit = saveConfigWith(forward(100));
        const releaseDocument = hold();
        const document = saveYamlConfig(jsYaml.dump({ ...FULL, settings: { ...FULL.settings, enableDoubleTapToSeek: { locked: false, value: false } } }));

        releaseEdit();
        expect(await edit).toBe(false);
        releaseDocument();
        expect(await document).toBe(true);

        expect(getConfig().settings).toEqual(jsYaml.load(server.holds).settings);
    });

    // The Notifications and Other tabs make the configuration from nothing on their first change
    // when it could not be read, and their save posted a document holding only their part.
    test("a configuration a tab made from nothing, none read from the server, is not sent", async () => {
        setConfig(undefined);
        setConfig({ other: { homePage: "Home" } });

        expect(await saveConfig()).toBe(false);

        expect(posted).toEqual([]);
        expect(alerts).toEqual(["Nothing was saved: the configuration could not be read from the server. Reload the page to try again."]);
    });

    // The Home tab draws without the configuration, from an empty layout, and its save posted
    // a document holding only that layout, which the server stored over every other setting.
    test("with no configuration read from the server, nothing is sent and the page says why", async () => {
        setConfig(undefined);

        expect(await saveConfigWith(forward(10))).toBe(false);
        expect(await saveConfig()).toBe(false);

        expect(posted).toEqual([]);
        expect(alerts).toEqual([
            "Nothing was saved: the configuration could not be read from the server. Reload the page to try again.",
            "Nothing was saved: the configuration could not be read from the server. Reload the page to try again.",
        ]);
    });

    // The Yaml editor sets its text from the configuration when told it changed, so a stored
    // answer that told every tab again replaced what the administrator was typing there.
    test("a stored edit tells the tabs once, as it is made, and not again when its answer comes", async () => {
        const saving = saveConfigWith(forward(10));
        const told = heard.length;

        expect(await saving).toBe(true);
        expect([told, heard.length]).toEqual([1, 1]);
    });

    // The server says where a document it refuses goes wrong in the document the page wrote
    // from the configuration, which nobody sees: the Yaml tab's own text is read and written
    // again before it is sent.
    test("a refusal is shown without the place in the document the page wrote", async () => {
        server.answers = [{ Error: true, Message: "Line 1, column 1: Property 'setings' not found on type 'Config'." }];

        expect(await saveConfigWith(forward(10))).toBe(false);

        expect(alerts).toEqual(["Property 'setings' not found on type 'Config'."]);
    });
});

// A tab wires its controls every time it is shown, through keyedEventListener, which kept one
// listener per id for as long as the dashboard ran. Jellyfin keeps a view while the person moves
// between the plugin's tabs, but draws it again, as new elements under the same ids, after they
// leave for its home screen: the Other tab's select, Save, backup and restore, and the
// Notifications tab's Save, then did nothing until the page was reloaded.
describe("listening to a tab's controls", () => {
    test("a view drawn again listens with its own elements, under the same ids", () => {
        const heard = [];
        const first = document.createElement("button");
        const second = document.createElement("button");
        first.id = "sf-keyed-drawn-again";
        second.id = "sf-keyed-drawn-again";

        keyedEventListener(first, "click", () => heard.push("first"));
        keyedEventListener(second, "click", () => heard.push("second"));
        second.click();

        expect(heard).toEqual(["second"]);
    });

    test("a tab shown twice still listens once", () => {
        const heard = [];
        const button = document.createElement("button");
        button.id = "sf-keyed-shown-twice";

        keyedEventListener(button, "click", () => heard.push("once"));
        keyedEventListener(button, "click", () => heard.push("twice"));
        button.click();

        expect(heard).toEqual(["once"]);
    });
});
