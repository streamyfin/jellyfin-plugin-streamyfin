// The tabs' own markup, without their scripts.

import { afterEach, describe, expect, test } from "bun:test";

const read = (path) => Bun.file(new URL(`../../Jellyfin.Plugin.Streamyfin/Pages/${path}`, import.meta.url)).text();

const mount = async (page) => {
    const host = document.createElement("div");
    host.innerHTML = await read(`${page}/index.html`);
    return host;
};

// The legend was a loose line under the last card, then a block inside the overrides card.
// It is a banner of its own at the top of every tab with boxes or states to explain, and
// one Legend switch opens it again once closed.
describe("the legend", () => {
    for (const page of ["Application", "Targeting", "Notifications"]) {
        test(`is a banner of its own on the ${page} tab, opened by the Legend switch`, async () => {
            const host = await mount(page);
            // Named after its tab, which remembers whether it was closed.
            const banner = host.querySelector(`#sf-legend-banner[data-sf-legend-banner="${page}"]`);

            expect(banner.querySelector("#sf-legend")).not.toBeNull();
            expect(banner.closest(".sf-card")).toBeNull();
            // Not inside another banner either, as it once landed on the Notifications tab.
            expect(banner.parentElement.closest(".sf-banner")).toBeNull();
            expect(banner.querySelector("[data-sf-legend-close]")).not.toBeNull();
            expect(host.querySelectorAll(".sf-legend")).toHaveLength(1);
            expect(host.querySelector(".sf-top #sf-legend-toggle").getAttribute("aria-controls")).toBe("sf-legend-banner");
        });
    }

    // Nothing there is a box or a state.
    for (const page of ["YamlEditor", "Other"]) {
        test(`is not on the ${page} tab`, async () => {
            const host = await mount(page);

            expect(host.querySelector(".sf-legend")).toBeNull();
            expect(host.querySelector("#sf-legend-toggle")).toBeNull();
        });
    }
});

describe("the Targeting tab", () => {
    // Outside the form, so the switches reach it by the attribute rather than through the form.
    test("has its events card follow the Descriptions and Keys switches", async () => {
        const host = await mount("Targeting");

        expect(host.querySelector("#sf-events-card").hasAttribute("data-sf-follows-display")).toBe(true);
    });
});

// The cultures and the plugin's version are kept between tabs. A failed request used to be
// turned into an empty list or no version before the cache saw it, which then kept that
// failure for five minutes as an answer.
describe("the Application tab's answers kept between tabs", () => {
    // As the other page tests do: a dashboard that already loaded the shared module, so
    // importing it does not fetch the configuration.
    window.Streamyfin ??= { shared: true };
    window.ApiClient ??= { getUrl: (path) => `http://server/${path}` };
    const pages = "../../Jellyfin.Plugin.Streamyfin/Pages/";
    const loading = Promise.all([import(`${pages}shared.js`), import(`${pages}Application/index.js`)]);

    afterEach(async () => {
        const [shared] = await loading;
        shared.forgetKept();
        delete window.ApiClient.getCultures;
        delete window.ApiClient.getInstalledPlugins;
    });

    // Fails once, then answers.
    const flaky = (answer) => {
        let calls = 0;
        const request = async () => {
            calls += 1;
            if (calls === 1) throw new Error("offline");
            return answer;
        };
        return { request, calls: () => calls };
    };

    test("a failed request for the cultures falls back to none and is asked again", async () => {
        const [shared, { keptCultures }] = await loading;
        const french = [{ Name: "French", TwoLetterISOLanguageName: "fr" }];
        const cultures = flaky(french);
        window.ApiClient.getCultures = cultures.request;

        expect(await keptCultures(shared)).toEqual([]);
        expect(await keptCultures(shared)).toEqual(french);
        expect(cultures.calls()).toBe(2);
    });

    test("a failed request for the plugins falls back to no version and is asked again", async () => {
        const [shared, { keptVersion }] = await loading;
        const plugins = flaky([{ Id: "1e9e5d38-6e67-4615-8719-e98a5c34f004", Version: "0.70.0.0" }]);
        window.ApiClient.getInstalledPlugins = plugins.request;

        expect(await keptVersion(shared)).toBeNull();
        expect(await keptVersion(shared)).toBe("0.70.0.0");
        expect(plugins.calls()).toBe(2);
    });
});
