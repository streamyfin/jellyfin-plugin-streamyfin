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

const { confirmed } = await import("../../Jellyfin.Plugin.Streamyfin/Pages/shared.js");

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
