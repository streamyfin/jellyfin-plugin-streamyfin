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

// The page's own script, run against a dashboard made of stubs: the shared module and the
// two helpers are the real files, the server is a schema and the examples file.
const PAGES = new URL("../../Jellyfin.Plugin.Streamyfin/Pages/", import.meta.url).pathname;
const SCHEMA = {
    definitions: {
        SectionOrientation: { type: "string", enum: ["vertical", "horizontal"] },
        Latest: { properties: { limit: { title: "Page limit", type: ["integer", "null"] } } },
    },
};

window.Streamyfin = { shared: true };
globalThis.LibraryMenu = { setTabs() {} };
window.ApiClient = {
    getUrl: (path) => {
        const page = /configurationpage\?name=(.+)$/.exec(path);
        return page ? `${PAGES}${page[1]}` : `http://server/${path}`;
    },
    ajax: async () => ({ json: async () => SCHEMA }),
};

const shared = await import(`${PAGES}shared.js`);
const { default: homeTab } = await import(`${PAGES}Home/index.js`);

const realFetch = globalThis.fetch;
afterEach(() => {
    globalThis.fetch = realFetch;
});

const serveExamples = (answer) => {
    globalThis.fetch = async (url) => {
        if (!String(url).endsWith("home-examples.json")) return realFetch(url);
        return answer();
    };
};

const open = async (sections) => {
    shared.setConfig({ settings: { home: { locked: false, value: { sections } } } });
    const host = await mount();
    const view = host.querySelector(".sf-page") ?? host;
    homeTab(view);
    view.dispatchEvent(new Event("viewshow"));
    for (let tries = 0; tries < 200 && !view.querySelector("#sf-dock:not([hidden])"); tries += 1) {
        await new Promise((resolve) => setTimeout(resolve, 5));
    }
    return view;
};

const titles = (view) => [...view.querySelectorAll(".sf-card header input.sf-text")].map((input) => input.value);
const cards = (view) => [...view.querySelectorAll(".sf-card")];

// happy-dom drops the dataTransfer a DragEvent is created with, so it is put back.
const dragEvent = (type, data) => {
    const event = new DragEvent(type, { bubbles: true, cancelable: true });
    Object.defineProperty(event, "dataTransfer", { value: data });
    return event;
};

const three = [
    { title: "A", kind: "latest", orientation: "horizontal", latest: {} },
    { title: "B", kind: "latest", orientation: "horizontal", latest: {} },
    { title: "C", kind: "latest", orientation: "horizontal", latest: {} },
];

describe("moving a section", () => {
    test("dragged by its handle, it lands where it is dropped, and the move is said", async () => {
        serveExamples(async () => new Response("[]"));
        const view = await open(structuredClone(three));
        const data = new DataTransfer();
        // Not implemented by happy-dom, which throws rather than leave the picture alone.
        data.setDragImage = () => {};

        cards(view)[0].querySelector(".sf-grip").dispatchEvent(dragEvent("dragstart", data));
        const over = dragEvent("dragover", data);
        cards(view)[2].dispatchEvent(over);
        cards(view)[2].dispatchEvent(dragEvent("drop", data));

        expect(over.defaultPrevented).toBe(true);
        expect(titles(view)).toEqual(["B", "C", "A"]);
        expect(view.querySelector("#sf-moved").textContent).toBe("A moved to place 3 of 3.");
        expect(view.querySelector("#sf-save").disabled).toBe(false);
    });

    // A file dropped on a card read as section 0, and text dropped into a field as the
    // section its number named.
    test("a drop that did not start on a handle is left to the browser", async () => {
        serveExamples(async () => new Response("[]"));
        const view = await open(structuredClone(three));
        const data = new DataTransfer();
        data.setData("text/plain", "1");

        const over = dragEvent("dragover", data);
        const drop = dragEvent("drop", data);
        cards(view)[2].querySelector("input.sf-text").dispatchEvent(over);
        cards(view)[2].querySelector("input.sf-text").dispatchEvent(drop);

        expect(over.defaultPrevented).toBe(false);
        expect(drop.defaultPrevented).toBe(false);
        expect(titles(view)).toEqual(["A", "B", "C"]);
        expect(view.querySelector("#sf-moved").textContent).toBe("");
        expect(view.querySelector("#sf-save").disabled).toBe(true);
    });

    test("an arrow says the move, and the focus stays on the section moved", async () => {
        serveExamples(async () => new Response("[]"));
        const view = await open(structuredClone(three));

        cards(view)[0].querySelector('[data-move="down"]').click();

        expect(titles(view)).toEqual(["B", "A", "C"]);
        expect(view.querySelector("#sf-moved").textContent).toBe("A moved to place 2 of 3.");
        expect(document.activeElement).toBe(cards(view)[1].querySelector('[data-move="down"]'));

        // Each arrow names the section it moves, and follows a new title.
        const title = cards(view)[1].querySelector("header input.sf-text");
        title.value = "Renamed";
        title.dispatchEvent(new Event("change"));
        expect(cards(view)[1].querySelector('[data-move="down"]').getAttribute("aria-label")).toBe("Move down: Renamed");

        // At the bottom the same arrow is off, so the focus takes the other one.
        cards(view)[1].querySelector('[data-move="down"]').click();

        expect(titles(view)).toEqual(["B", "C", "Renamed"]);
        expect(document.activeElement).toBe(cards(view)[2].querySelector('[data-move="up"]'));
    });
});

describe("a section's card", () => {
    // The menu said Wide while the app and the preview drew posters.
    test("one that names no shape shows the posters the app draws", async () => {
        serveExamples(async () => new Response("[]"));
        const view = await open([{ title: "A", kind: "latest", latest: {} }]);

        expect(cards(view)[0].querySelector(".sf-body select").value).toBe("vertical");
        expect(view.querySelector(".sf-pcard").classList.contains("is-portrait")).toBe(true);
    });
});

describe("the examples", () => {
    test("come from the file the plugin serves", async () => {
        serveExamples(async () => new Response(await read("home-examples.json")));
        const view = await open([]);

        const names = [...view.querySelectorAll("#sf-example option")].map((option) => option.textContent);
        expect(names).toEqual(JSON.parse(await read("home-examples.json")).map((example) => example.name));
        expect(view.querySelector(".sf-examples").hidden).toBe(false);
    });

    test("a page that cannot read them still edits, without the picker", async () => {
        const quiet = console.error;
        console.error = () => {};
        serveExamples(async () => {
            throw new Error("offline");
        });
        try {
            const view = await open(structuredClone(three));

            expect(view.querySelector(".sf-examples").hidden).toBe(true);
            expect(titles(view)).toEqual(["A", "B", "C"]);
        } finally {
            console.error = quiet;
        }
    });
});
