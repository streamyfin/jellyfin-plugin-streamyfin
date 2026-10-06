// The Home tab's own markup, under the stylesheet the plugin serves with it. happy-dom
// computes the cascade but lays nothing out, so what is held here is what the rules say
// about the page, not where a browser ends up drawing it.

import { afterEach, describe, expect, test } from "bun:test";

const read = (path) => Bun.file(new URL(`../../Jellyfin.Plugin.Streamyfin/Pages/${path}`, import.meta.url)).text();

const mounted = [];

const mount = async () => {
    const style = document.createElement("style");
    style.textContent = await read("settings-form.css");
    const host = document.createElement("div");
    host.innerHTML = await read("Home/index.html");
    document.head.appendChild(style);
    document.body.appendChild(host);
    mounted.push(style, host);
    return host;
};

afterEach(() => {
    for (const node of mounted.splice(0)) node.remove();
});

describe("the top row", () => {
    // The row stretched its children, so the "Add" label kept its text at the top of
    // the height the picker and the button give it, level with the small "Home" beside
    // the page title rather than with the two controls it names.
    test("the label, the kind picker and the button are centred on one line", async () => {
        const host = await mount();
        const actions = host.querySelector(".sf-actions");

        expect([...actions.children].map((child) => child.id || child.tagName.toLowerCase()))
            .toEqual(["label", "sf-new-kind", "sf-add"]);
        expect(getComputedStyle(actions).alignItems).toBe("center");
    });
});
