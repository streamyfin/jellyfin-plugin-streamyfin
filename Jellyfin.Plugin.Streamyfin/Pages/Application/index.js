// The Application page: the server's defaults for every setting, drawn by settings-form.js
// from the description the plugin serves at v1/settings/form. This file fetches what the
// form needs, builds the navigation around it, and writes the admin's edits back.
//
// What a save writes is the form's own answer: every setting the admin set, as its
// { value, locked } pair, and nothing for a setting left free. There is no diff against
// what was loaded, because the three states make the intent explicit: free is the key's
// absence. Settings the form cannot draw yet (the home layout, the library options) pass
// through untouched, so a save never loses what the Yaml tab wrote.

const PLUGIN_ID = "1e9e5d38-6e67-4615-8719-e98a5c34f004";
const TERSE_KEY = "streamyfin.admin.descriptions";

// One glyph per category the app uses, as the elements of a 16 by 16 line icon. A
// category without one shows its name alone.
const ICONS = {
    "Playback controls": [["path", "M3 5.5h10M3 8h10M3 10.5h6"]],
    "Home and appearance": [["path", "M2 6.5L8 2l6 4.5V13H2z"]],
    "Audio and subtitles": [["path", "M2 10h3l3 3V3L5 6H2z"]],
    "Media segment skip": [["path", "M4 3v10l8-5z"]],
    "Music": [["circle", { cx: "5", cy: "12", r: "2" }], ["path", "M7 12V4l6-1.5V10"]],
    "Plugins": [["path", "M6 2v3M10 2v3M3 5h10v8H3z"]],
    "Security": [["path", "M8 2l5 2v4c0 3-2 5-5 6-3-1-5-3-5-6V4z"]],
    "Advanced": [["circle", { cx: "8", cy: "8", r: "2" }], ["path", "M8 1v2M8 13v2M1 8h2M13 8h2"]],
};

const SVG = "http://www.w3.org/2000/svg";

const iconFor = (category) => {
    const shapes = ICONS[category];
    if (!shapes) return null;

    const svg = document.createElementNS(SVG, "svg");
    svg.setAttribute("viewBox", "0 0 16 16");
    svg.setAttribute("fill", "none");
    svg.setAttribute("stroke", "currentColor");
    svg.setAttribute("stroke-width", "1.5");
    svg.setAttribute("aria-hidden", "true");

    for (const [tag, attributes] of shapes) {
        const shape = document.createElementNS(SVG, tag);
        const entries = typeof attributes === "string" ? { d: attributes } : attributes;
        for (const [name, value] of Object.entries(entries)) shape.setAttribute(name, value);
        svg.appendChild(shape);
    }
    return svg;
};

const url = (path) => window.ApiClient.getUrl(`streamyfin/${path}`);

const readJson = (path) =>
    window.ApiClient.ajax({ type: "GET", url: url(path), contentType: "application/json" })
        .then((response) => response.json());

const readVersion = async () => {
    try {
        const plugins = await window.ApiClient.getInstalledPlugins();
        return plugins.find((plugin) => plugin.Id?.replace(/-/g, "") === PLUGIN_ID.replace(/-/g, ""))?.Version ?? null;
    } catch {
        return null;
    }
};

const readCultures = async () => {
    try {
        return await window.ApiClient.getCultures();
    } catch {
        return [];
    }
};

const readTerse = () => {
    try {
        return window.localStorage.getItem(TERSE_KEY) === "off";
    } catch {
        return false;
    }
};

const writeTerse = (terse) => {
    try {
        window.localStorage.setItem(TERSE_KEY, terse ? "off" : "on");
    } catch {
        // A dashboard that blocks storage just forgets the choice.
    }
};

export default function (view) {
    let form = null;
    let renderer = null;
    // The dashboard keeps the page's DOM between tab switches and fires viewshow again,
    // so every listener added here is tied to one showing and dropped on viewhide.
    // Without that a second showing would save twice.
    let showing = null;

    const el = (id) => view.querySelector(`#${id}`);
    const listen = (id, type, handler) => el(id).addEventListener(type, handler, { signal: showing.signal });

    const setStatus = (text, error = false) => {
        const status = el("sf-status");
        status.textContent = text ?? "";
        status.hidden = !text;
        status.classList.toggle("is-error", error);
    };

    const updateDock = () => {
        const dirty = form.dirtyCount();
        const invalid = form.invalid().length;
        const dock = el("sf-dock");
        const parts = [];

        if (dirty) parts.push(`${dirty} unsaved`);
        if (invalid) parts.push(`${invalid} need${invalid === 1 ? "s" : ""} a value`);

        el("sf-dock-summary").textContent = parts.join(" · ") || "Nothing to save";
        dock.classList.toggle("is-clean", dirty === 0);
        el("sf-discard").disabled = dirty === 0;
        el("sf-save").disabled = dirty === 0 || invalid > 0;
    };

    const buildNavigation = () => {
        const pills = el("sf-pills");
        const chips = el("sf-chips");
        pills.textContent = "";

        const categories = form.categories();

        const showChips = (category) => {
            chips.textContent = "";
            const groups = form.groups(category);
            if (groups.length < 2) return;

            for (const group of groups) {
                const chip = document.createElement("button");
                chip.type = "button";
                chip.className = "sf-chip";
                chip.textContent = group.name;
                chip.addEventListener("click", () => {
                    form.cardFor(category, group.name)?.scrollIntoView({ behavior: "smooth", block: "start" });
                }, { signal: showing.signal });
                chips.appendChild(chip);
            }
        };

        const select = (category) => {
            for (const pill of pills.children) {
                pill.setAttribute("aria-current", String(pill.dataset.category === category));
            }
            form.showCategory(category);
            showChips(category);
        };

        for (const category of categories) {
            const pill = document.createElement("button");
            pill.type = "button";
            pill.className = "sf-pill";
            pill.setAttribute("role", "tab");
            pill.dataset.category = category.name;

            const icon = iconFor(category.name);
            if (icon) pill.appendChild(icon);
            pill.appendChild(document.createTextNode(category.name));

            const count = document.createElement("span");
            count.className = "sf-n";
            count.textContent = String(category.count);
            pill.appendChild(count);

            pill.addEventListener("click", () => {
                el("sf-find").value = "";
                form.search("");
                select(category.name);
            }, { signal: showing.signal });
            pills.appendChild(pill);
        }

        if (categories.length) select(categories[0].name);

        // A search looks across every category, so the pills stand down while one is
        // typed and come back when it is cleared.
        listen("sf-find", "input", (event) => {
            const text = event.target.value;
            form.search(text);
            if (text.trim()) {
                for (const pill of pills.children) pill.setAttribute("aria-current", "false");
                chips.textContent = "";
            } else {
                select(categories[0]?.name ?? null);
            }
        });
    };

    const wireTerse = () => {
        const toggle = el("sf-terse");
        const apply = (terse) => {
            toggle.setAttribute("aria-pressed", String(!terse));
            toggle.querySelector(".sf-pip").textContent = terse ? "OFF" : "ON";
            form.setTerse(terse);
        };

        apply(readTerse());
        listen("sf-terse", "click", () => {
            const terse = toggle.getAttribute("aria-pressed") === "true";
            writeTerse(terse);
            apply(terse);
        });
    };

    const wireDock = (shared) => {
        listen("sf-discard", "click", () => form.reset());
        listen("sf-save", "click", async () => {
            if (form.invalid().length) return;

            // saveConfig posts what shared holds, so the edit goes in first. A refusal puts
            // the previous config back: otherwise the next showing of the tab would seed
            // the form from an edit the server never accepted, and read it as saved.
            const previous = shared.getConfig() ?? {};
            shared.setConfig({ ...previous, settings: form.toSettings() });

            if (await shared.saveConfig()) {
                form.markSaved();
            } else {
                shared.setConfig(previous);
            }
        });
        el("sf-dock").hidden = false;
    };

    const load = async (shared) => {
        setStatus("Loading the settings…");

        const [fields, cultures, version] = await Promise.all([
            readJson("v1/settings/form"),
            readCultures(),
            readVersion(),
        ]);
        // The plugin's declared defaults, which are the app's own. A free setting shows
        // this value, since it is what a user gets when the server says nothing.
        const defaults = shared.getDefaultConfig()?.settings ?? {};

        const app = el("sf-app");
        app.dataset.sfTheme = renderer.themeFromBackground(
            window.getComputedStyle(document.documentElement).backgroundColor);

        form = renderer.createForm(el("sf-editor"), {
            fields,
            values: shared.getConfig()?.settings ?? {},
            defaults,
            cultures,
            terse: readTerse(),
        });

        el("sf-meta").textContent = [version, `${fields.length} settings`].filter(Boolean).join(" · ");

        buildNavigation();
        wireTerse();
        wireDock(shared);
        form.onChange(updateDock);
        updateDock();
        setStatus(null);
    };

    view.addEventListener("viewshow", () => {
        showing?.abort();
        showing = new AbortController();

        import(window.ApiClient.getUrl("web/configurationpage?name=shared.js")).then(async (shared) => {
            shared.setPage("Application");
            renderer = await import(window.ApiClient.getUrl("web/configurationpage?name=settings-form.js"));

            try {
                await load(shared);
            } catch (error) {
                console.error(error);
                setStatus("The settings could not be loaded. The server log has the reason.", true);
            }
        });
    });

    view.addEventListener("viewhide", () => {
        showing?.abort();
        form?.destroy();
        form = null;
        el("sf-dock").hidden = true;
    });
}
