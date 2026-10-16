import { afterEach, describe, expect, test } from "bun:test";
import otherTab, { restored } from "../../Jellyfin.Plugin.Streamyfin/Pages/Other/index.js";

describe("the sentence after a restore", () => {
    test("names whose own notification choices came back", () => {
        expect(restored({ configuration: true, groups: 2, users: 0, preferences: 3 }))
            .toBe("Restored the configuration, 2 groups, the notification choices of 3 people.");
        expect(restored({ preferences: 1 })).toBe("Restored the notification choices of 1 person.");
    });

    test("an older report, without the choices, reads as before", () => {
        expect(restored({ configuration: true, groups: 1, users: 1 })).toBe("Restored the configuration, 1 group, 1 user.");
    });

    test("names the titles people wait for that came back", () => {
        expect(restored({ groups: 1, awaited: 3 })).toBe("Restored 1 group, the 3 titles people wait for.");
        expect(restored({ awaited: 1 })).toBe("Restored the 1 title people wait for.");
    });
});

// The page's own script against a dashboard made of stubs, as the Home tab's test runs it:
// the shared module is the real file.
const PAGES = new URL("../../Jellyfin.Plugin.Streamyfin/Pages/", import.meta.url).pathname;

window.Streamyfin ??= { shared: true };
globalThis.LibraryMenu ??= { setTabs() {} };
// The browser's constructor for a select's option, which happy-dom does not have.
globalThis.Option ??= function Option(text, value) {
    return Object.assign(document.createElement("option"), { text, value });
};
window.ApiClient = {
    getUrl: (path) => {
        const page = /configurationpage\?name=(.+)$/.exec(path);
        return page ? `${PAGES}${page[1]}` : `http://server/${path}`;
    },
};

const shared = await import(`${PAGES}shared.js`);
const mounted = [];

afterEach(() => {
    for (const node of mounted.splice(0)) node.remove();
    shared.setOnConfigUpdatedListener("after-other", undefined);
});

const open = async () => {
    const host = document.createElement("div");
    host.innerHTML = await Bun.file(`${PAGES}Other/index.html`).text();
    document.body.appendChild(host);
    mounted.push(host);
    const view = host.firstElementChild;
    otherTab(view);
    view.dispatchEvent(new Event("viewshow"));
    for (let tries = 0; tries < 200 && view.querySelector("#home-page").options.length === 0; tries += 1) {
        await new Promise((resolve) => setTimeout(resolve, 5));
    }
    return view;
};

// The Yaml tab can store a configuration with no section for this tab. Its listener read
// the start page out of that missing section and threw, which stopped the tabs listening
// after it from hearing of the new configuration.
describe("a configuration without the Other section", () => {
    test("leaves the start page unchosen, and the tabs after this one still hear of it", async () => {
        shared.setConfig({ other: { homePage: "Notifications" } });
        const view = await open();
        const heard = [];
        shared.setOnConfigUpdatedListener("after-other", (config) => heard.push(config));

        expect(view.querySelector("#home-page").value).toBe("Notifications");
        expect(() => shared.setConfig({ settings: {} })).not.toThrow();

        expect(heard).toEqual([{ settings: {} }]);
        expect(view.querySelector("#home-page").value).toBe("");
    });
});

// The tab makes the configuration from nothing on its first change when the dashboard could not
// read it, and its Save posted a document holding only the start page, which the server stored
// over every other setting.
describe("saving with no configuration read", () => {
    test("sends nothing, and says why", async () => {
        const posted = [];
        const alerts = [];
        const served = { ajax: window.ApiClient.ajax, Dashboard: window.Dashboard };
        window.ApiClient.ajax = async ({ type = "GET", data }) => {
            if (type === "POST") posted.push(data);
            return { json: async () => ({ Error: false }) };
        };
        window.Dashboard = {
            showLoadingMsg() {},
            hideLoadingMsg() {},
            processPluginConfigurationUpdateResult() {},
            alert: (message) => alerts.push(message),
        };

        try {
            shared.setConfig(undefined);
            const view = await open();
            const select = view.querySelector("#home-page");
            select.value = "Home";
            select.dispatchEvent(new Event("change"));
            view.querySelector("#save-other-btn").click();
            await new Promise((resolve) => setTimeout(resolve, 20));
        } finally {
            window.ApiClient.ajax = served.ajax;
            window.Dashboard = served.Dashboard;
        }

        expect(posted).toEqual([]);
        expect(alerts).toEqual(["Nothing was saved: the configuration could not be read from the server. Reload the page to try again."]);
    });
});

// The answer marked the start page saved whatever the select said by then, and Save stayed
// enabled while the save was on its way.
describe("saving the start page", () => {
    test("one changed while its save is on its way is still to save, and Save is held meanwhile", async () => {
        const jsYaml = await import(`${PAGES}Libraries/js-yaml.min.js`);
        const served = { ajax: window.ApiClient.ajax, Dashboard: window.Dashboard, jsYaml: shared.tools.jsYaml };
        let release;
        const gate = new Promise((resolve) => { release = resolve; });
        const posted = [];
        shared.tools.jsYaml = jsYaml;
        window.ApiClient.ajax = async ({ type = "GET", data }) => {
            if (type === "POST") {
                posted.push(JSON.parse(data).Value);
                await gate;
            }
            return { json: async () => ({ Error: false }) };
        };
        window.Dashboard = { showLoadingMsg() {}, hideLoadingMsg() {}, processPluginConfigurationUpdateResult() {}, alert() {} };

        let whileOnItsWay;
        let view;
        try {
            shared.setYamlConfig(jsYaml.dump({ settings: {}, other: { homePage: "Application" } }));
            view = await open();
            const select = view.querySelector("#home-page");
            select.value = "Home";
            select.dispatchEvent(new Event("change"));
            view.querySelector("#save-other-btn").click();
            select.value = "Targeting";
            select.dispatchEvent(new Event("change"));
            whileOnItsWay = view.querySelector("#save-other-btn").disabled;
            release();
            await new Promise((resolve) => setTimeout(resolve, 20));
        } finally {
            window.ApiClient.ajax = served.ajax;
            window.Dashboard = served.Dashboard;
            shared.tools.jsYaml = served.jsYaml;
        }

        expect(posted).toHaveLength(1);
        expect(jsYaml.load(posted[0]).other.homePage).toBe("Home");
        expect(whileOnItsWay).toBe(true);
        expect(view.querySelector("#sf-dock-summary").textContent).toBe("Start page changed");
        expect(view.querySelector("#save-other-btn").disabled).toBe(false);
    });
});
