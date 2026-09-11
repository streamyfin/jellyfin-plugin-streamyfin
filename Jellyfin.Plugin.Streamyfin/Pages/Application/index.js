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
const BANNER_KEY = "streamyfin.admin.banner";
const KEYS_KEY = "streamyfin.admin.keys";

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


const remember = (key, value) => {
    try {
        window.localStorage.setItem(key, value);
    } catch {
        // A dashboard that blocks storage just forgets the choice.
    }
};

const recalled = (key) => {
    try {
        return window.localStorage.getItem(key);
    } catch {
        return null;
    }
};

const readTerse = () => recalled(TERSE_KEY) === "off";
const writeTerse = (terse) => remember(TERSE_KEY, terse ? "off" : "on");
// The YAML keys are for the hands that live in the Yaml tab; everyone else sees names.
const readKeys = () => recalled(KEYS_KEY) === "on";
const writeKeys = (on) => remember(KEYS_KEY, on ? "on" : "off");

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
        // Nothing to say while nothing changed: the dock appears with the first edit.
        dock.hidden = dirty === 0 && invalid === 0;
        el("sf-discard").disabled = dirty === 0;
        el("sf-save").disabled = dirty === 0 || invalid > 0;
        refreshPillCounts();
    };

    // Each pill says how many of its settings are set, so the page answers "what have I
    // touched" before a single card is opened.
    const refreshPillCounts = () => {
        const counts = new Map(form.categories().map((c) => [c.name, c]));
        for (const pill of el("sf-pills").children) {
            const badge = pill.querySelector(".sf-set");
            const set = counts.get(pill.dataset.category)?.set ?? 0;
            badge.textContent = set ? `${set} set` : "";
            badge.hidden = set === 0;
        }
    };

    const buildNavigation = () => {
        const pills = el("sf-pills");
        const chips = el("sf-chips");
        const find = el("sf-find");
        const filter = el("sf-filter");
        pills.textContent = "";

        const categories = form.categories();
        // The category the pills point at, kept while a search or a filter takes over so
        // clearing either comes back to it rather than to the first one.
        let current = null;

        const pressFilter = (state) => {
            for (const button of filter.children) button.setAttribute("aria-pressed", String(button.dataset.filter === state));
        };

        // The dashboard keeps the page's DOM between showings, so the search box and the
        // filter still hold what they held last time while the form starts over. Both
        // are cleared together, here and on a pill.
        const clearNarrowing = () => {
            find.value = "";
            form.search("");
            pressFilter("all");
            form.filter(null);
        };

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
            current = category;
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

            const set = document.createElement("span");
            set.className = "sf-set";
            set.hidden = true;
            pill.appendChild(set);

            pill.addEventListener("click", () => {
                clearNarrowing();
                select(category.name);
            }, { signal: showing.signal });
            pills.appendChild(pill);
        }

        clearNarrowing();
        if (categories.length) select(categories[0].name);

        // A search or a state filter looks across every category, so the pills stand
        // down while either is on and come back when both are cleared.
        const narrowed = () => Boolean(find.value.trim())
            || filter.querySelector("[aria-pressed='true']")?.dataset.filter !== "all";
        const standDown = () => {
            if (narrowed()) {
                for (const pill of pills.children) pill.setAttribute("aria-current", "false");
                chips.textContent = "";
            } else {
                select(current ?? categories[0]?.name ?? null);
            }
        };

        listen("sf-find", "input", (event) => {
            form.search(event.target.value);
            standDown();
        });

        listen("sf-filter", "click", (event) => {
            const button = event.target.closest("button[data-filter]");
            if (!button) return;
            pressFilter(button.dataset.filter);
            form.filter(button.dataset.filter === "all" ? null : button.dataset.filter);
            standDown();
        });
    };

    const wireBanner = () => {
        const banner = el("sf-banner");
        banner.hidden = recalled(BANNER_KEY) === "off";
        listen("sf-banner-close", "click", () => {
            banner.hidden = true;
            remember(BANNER_KEY, "off");
        });
    };

    // The two switches in the top row: descriptions on or off, keys on or off.
    const wireSwitch = (id, read, write, apply) => {
        const toggle = el(id);
        const show = (on) => {
            toggle.setAttribute("aria-pressed", String(on));
            toggle.querySelector(".sf-pip").textContent = on ? "ON" : "OFF";
            apply(on);
        };

        show(read());
        listen(id, "click", () => {
            const on = toggle.getAttribute("aria-pressed") !== "true";
            write(on);
            show(on);
        });
    };

    const wireTerse = () => {
        wireSwitch("sf-terse", () => !readTerse(), (on) => writeTerse(!on), (on) => form.setTerse(!on));
        wireSwitch("sf-keys", readKeys, writeKeys, (on) => form.setKeys(on));
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
    };

    const load = async (shared) => {
        setStatus("Loading the settings…");

        const [fields, cultures, version] = await Promise.all([
            readJson("v1/settings/form"),
            readCultures(),
            readVersion(),
        ]);
        // shared.js logs and swallows a failed fetch of either. Drawing without the config
        // would show every setting as free and let a save post a configuration missing
        // its other sections; without the defaults, every free setting would claim the
        // app decides. The page refuses instead.
        const config = shared.getConfig();
        const defaults = shared.getDefaultConfig();
        if (!config || !defaults) throw new Error("The configuration or the defaults did not load");

        const app = el("sf-app");
        app.dataset.sfTheme = renderer.themeFromBackground(
            window.getComputedStyle(document.documentElement).backgroundColor);

        form = renderer.createForm(el("sf-editor"), {
            fields,
            values: config.settings ?? {},
            // The plugin's declared defaults, which are the app's own. A free setting
            // shows this value, since it is what a user gets when the server says nothing.
            defaults: defaults.settings ?? {},
            cultures,
            terse: readTerse(),
            keys: readKeys(),
            probe: shared.probeIntegration,
        });

        el("sf-meta").textContent = [version, `${fields.length} settings`].filter(Boolean).join(" · ");

        buildNavigation();
        wireTerse();
        wireBanner();
        wireDock(shared);
        form.onChange(updateDock);
        updateDock();
        setStatus(null);
    };

    view.addEventListener("viewshow", () => {
        showing?.abort();
        showing = new AbortController();

        const failed = (error) => {
            console.error(error);
            setStatus("The settings could not be loaded. The server log has the reason.", true);
        };

        import(window.ApiClient.getUrl("web/configurationpage?name=shared.js")).then(async (shared) => {
            shared.setPage("Application");
            renderer = await import(window.ApiClient.getUrl("web/configurationpage?name=settings-form.js"));

            // The tab was left while the modules were still on their way: nothing to draw.
            if (showing.signal.aborted) return;

            try {
                await load(shared);
            } catch (error) {
                failed(error);
            }
        }).catch(failed);
    });

    view.addEventListener("viewhide", () => {
        showing?.abort();
        form?.destroy();
        form = null;
        el("sf-dock").hidden = true;
    });
}
