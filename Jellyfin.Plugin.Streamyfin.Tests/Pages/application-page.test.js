// The tabs' own markup, without their scripts.

import { describe, expect, test } from "bun:test";

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
