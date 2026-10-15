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
