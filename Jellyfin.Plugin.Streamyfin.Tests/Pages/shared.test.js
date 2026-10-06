// Asking the person before doing something they cannot undo.
//
// Measured on throwaway servers running 10.11.11 and 12.0.0: the dashboard's own
// `Dashboard.confirm(message, title, callback)` returns undefined and answers through the
// callback, `true` for yes and `false` for no. Reading its return value therefore reads
// every cancelled confirmation as a yes, which is what deleted a settings group when the
// administrator clicked Annuler.

import { afterEach, describe, expect, test } from "bun:test";

// shared.js asks the dashboard for its address as it loads, and loads the configuration
// unless a page has already done it, so both are in place before the module is.
window.ApiClient = { getUrl: (path) => `http://server/${path}` };
window.Streamyfin = { shared: true };

const { confirmed, drawLegend, paintDisplaySwitches, wireDisplaySwitches } = await import("../../Jellyfin.Plugin.Streamyfin/Pages/shared.js");

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

        expect(applied).toEqual([{ descriptions: false, keys: false }]);
        expect(root.querySelector("#sf-terse").getAttribute("aria-pressed")).toBe("false");
        expect(root.querySelector("#sf-terse .sf-pip").textContent).toBe("OFF");
    });

    test("a click flips one switch, remembers it, and hands both over", () => {
        const root = view("sf-terse", "sf-keys");
        const applied = [];
        wireDisplaySwitches(root, undefined, (choice) => applied.push(choice));

        root.querySelector("#sf-keys").click();

        expect(applied.at(-1)).toEqual({ descriptions: true, keys: true });
        expect(window.localStorage.getItem("streamyfin.admin.keys")).toBe("on");
        expect(root.querySelector("#sf-keys .sf-pip").textContent).toBe("ON");
    });

    test("one tab's choice is the next tab's", () => {
        const first = view("sf-terse");
        wireDisplaySwitches(first, undefined, () => {});
        first.querySelector("#sf-terse").click();

        const applied = [];
        paintDisplaySwitches(view("sf-terse", "sf-keys"), (choice) => applied.push(choice));

        expect(applied).toEqual([{ descriptions: false, keys: false }]);
    });

    test("a view without one of the switches is still wired", () => {
        const root = view("sf-terse");
        const applied = [];

        wireDisplaySwitches(root, undefined, (choice) => applied.push(choice));
        root.querySelector("#sf-terse").click();

        expect(applied.at(-1)).toEqual({ descriptions: false, keys: false });
    });
});

// The banner that explains the three states can be closed for good, and nothing said what
// a box with a dash meant.
describe("the legend", () => {
    test("names the three boxes and the three states", () => {
        const mount = document.createElement("div");

        drawLegend(mount);

        const items = [...mount.children];
        expect(items.map((item) => item.textContent)).toEqual([
            "On",
            "Off",
            "Not set: the app uses its own default",
            "Free: each user decides",
            "Suggested: your value, set once as each user's starting point",
            "Locked: your value, and users cannot change it",
        ]);
        const boxes = mount.querySelectorAll("input.sf-check");
        expect([...boxes].map((box) => [box.checked, box.indeterminate, box.disabled])).toEqual([
            [true, false, true],
            [false, false, true],
            [false, true, true],
        ]);
    });

    test("is drawn once however often the page is shown", () => {
        const mount = document.createElement("div");

        drawLegend(mount);
        drawLegend(mount);

        expect(mount.children.length).toBe(6);
    });
});
