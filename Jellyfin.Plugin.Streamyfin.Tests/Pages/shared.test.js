// Asking the person before doing something they cannot undo.
//
// Measured on throwaway servers running 10.11.11 and 12.0.0: the dashboard's own
// `Dashboard.confirm(message, title, callback)` returns undefined and answers through the
// callback, `true` for yes and `false` for no. Reading its return value therefore reads
// every cancelled confirmation as a yes, which is what deleted a settings group when the
// administrator clicked Annuler.

import { afterEach, beforeEach, describe, expect, test } from "bun:test";

// shared.js asks the dashboard for its address as it loads, and loads the configuration
// unless a page has already done it, so both are in place before the module is.
window.ApiClient = { getUrl: (path) => `http://server/${path}` };
window.Streamyfin = { shared: true };

const {
    confirmed,
    drawLegend,
    forgetKept,
    KEPT_DESCRIPTIONS,
    paintDisplaySwitches,
    readKept,
    saveConfig,
    setConfig,
    tools,
    warmKept,
    wireDisplaySwitches,
} = await import("../../Jellyfin.Plugin.Streamyfin/Pages/shared.js");

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
        tools.jsYaml = { dump: (value) => JSON.stringify(value) };
        setConfig({ settings: {} });
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
