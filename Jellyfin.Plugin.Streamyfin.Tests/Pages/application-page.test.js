// The Application tab's own markup, without its script.

import { describe, expect, test } from "bun:test";

const read = (path) => Bun.file(new URL(`../../Jellyfin.Plugin.Streamyfin/Pages/${path}`, import.meta.url)).text();

const mount = async () => {
    const host = document.createElement("div");
    host.innerHTML = await read("Application/index.html");
    return host;
};

// The legend was a loose line under the last card and read as something left over. It is
// the banner at the top, and the Legend switch opens it again once it is closed.
describe("the legend", () => {
    test("is the banner at the top of the tab", async () => {
        const host = await mount();

        expect(host.querySelector("#sf-banner #sf-legend")).not.toBeNull();
        expect(host.querySelectorAll(".sf-legend")).toHaveLength(1);
    });

    test("has a switch in the top row that names the banner it opens", async () => {
        const host = await mount();
        const toggle = host.querySelector(".sf-top #sf-legend-toggle");

        expect(toggle.getAttribute("aria-controls")).toBe("sf-banner");
        expect(toggle.textContent).toContain("Legend");
    });
});
