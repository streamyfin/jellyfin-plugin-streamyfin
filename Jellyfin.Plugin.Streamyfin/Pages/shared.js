export const SCHEMA_URL = window.ApiClient.getUrl('streamyfin/config/schema');
export const YAML_URL = window.ApiClient.getUrl('streamyfin/config/yaml');
export const DEFAULT_URL = window.ApiClient.getUrl('streamyfin/config/default');
export const NOTIFICATION_URL = window.ApiClient.getUrl('streamyfin/notification');
export const tools = {jsYaml: undefined};

// Answers that change only when the plugin is updated or the server's libraries move,
// kept a few minutes. Every move between tabs asked for the same descriptions again, one
// round trip after the other: nothing at home, most of a second on a phone or a VPN.
// A failed answer is not kept, a save forgets everything, and each caller gets its own
// copy, so a page that changes what it was handed does not change it for the next one.
const KEPT_FOR = 5 * 60 * 1000;
const kept = new Map();

export const remembered = (key, load, maxAge = KEPT_FOR) => {
    const entry = kept.get(key);
    if (entry && Date.now() - entry.at < maxAge) return entry.answer.then(structuredClone);

    const answer = Promise.resolve().then(load);
    kept.set(key, { answer, at: Date.now() });
    answer.catch(() => {
        if (kept.get(key)?.answer === answer) kept.delete(key);
    });
    return answer.then(structuredClone);
};

// A GET of the server's, by path, kept as above.
export const readKept = (path, maxAge) => remembered(path, () =>
    window.ApiClient.ajax({ type: "GET", url: window.ApiClient.getUrl(path), contentType: "application/json" })
        .then((response) => response.json()), maxAge);

export const forgetKept = () => kept.clear();

// What the other tabs ask for first, fetched once one tab has drawn, so moving to them
// does not wait on the network either.
export const KEPT_DESCRIPTIONS = [
    "streamyfin/v1/settings/form",
    "streamyfin/v1/notifications/form",
    "streamyfin/v1/notifications/events",
];
export const warmKept = () => {
    for (const path of KEPT_DESCRIPTIONS) readKept(path).catch(() => {});
};

// Asking the server to try an address, for whichever page drew the button. Here rather
// than in each page because both tabs draw the same form from the same description, and
// a third hand rolled ApiClient wrapper is a third place to forget when this changes.
// Asking before something that cannot be undone. Jellyfin's own dialog when the
// dashboard offers one, the browser's otherwise, and a refusal on anything unexpected
// so a broken dialog never reads as a yes.
export const confirmed = (message) => new Promise((resolve) => {
    if (!window.Dashboard?.confirm) {
        resolve(window.confirm(message));
        return;
    }

    let answered = false;
    const answer = (yes) => {
        if (!answered) {
            answered = true;
            resolve(yes);
        }
    };

    // Both dashboards this plugin supports, 10.11.11 and 12.0.0, hand back undefined and
    // answer through the callback, true for yes and false for no. Reading the return value
    // therefore read every cancelled question as a yes: deleting a settings group went
    // ahead when the administrator clicked Cancel. The promise shape is still handled, for
    // a dashboard that has one and ignores the callback.
    const returned = window.Dashboard.confirm(message, "Streamyfin", (yes) => answer(yes !== false));

    if (typeof returned?.then === "function") {
        returned.then(() => answer(true), () => answer(false));
    }

    // A dialog that answers neither way leaves this pending, so nothing that cannot be
    // undone happens. Saying yes on an answer that never came is the one outcome worth
    // avoiding.
});

// The dock's way out of a page that opens already refusing to save. Here rather than in
// each page because both tabs draw the same form and the same dock, and writing it twice
// is what let one copy leak a listener per tab switch.
export const wireFindProblem = (button, form, signal, goTo) => {
    if (!button) return;

    button.addEventListener(
        "click",
        () => {
            const found = form()?.firstProblem();
            if (found) goTo(found);
        },
        signal ? { signal } : undefined);
};

// The Descriptions and Keys switches in the top row of every tab that lists settings.
// One choice for the whole dashboard, kept in the browser: help text switched off on one
// tab is off on the others, since it was turned off for the settings and not for a tab.
const DESCRIPTIONS_KEY = "streamyfin.admin.descriptions";
const KEYS_KEY = "streamyfin.admin.keys";
const LEGEND_KEY = "streamyfin.admin.legend";

const recall = (key) => {
    try {
        return window.localStorage.getItem(key);
    } catch {
        return null;
    }
};

const keep = (key, value) => {
    try {
        window.localStorage.setItem(key, value);
    } catch {
        // A dashboard that blocks storage just forgets the choice.
    }
};

export const showsDescriptions = () => recall(DESCRIPTIONS_KEY) !== "off";
// The YAML keys are for the hands that live in the Yaml tab; everyone else sees names.
export const showsKeys = () => recall(KEYS_KEY) === "on";

// Each tab's legend is shown until it is closed on that tab, which leaves the others as
// they were: an administrator who knows the boxes on one tab may not know the states on
// another. The banner names its tab.
const legendKey = (view) => `${LEGEND_KEY}.${view?.querySelector?.("[data-sf-legend-banner]")?.dataset.sfLegendBanner ?? ""}`;
export const showsLegend = (view) => recall(legendKey(view)) !== "off";

const DISPLAY_SWITCHES = [
    ["sf-terse", () => showsDescriptions(), (view, on) => keep(DESCRIPTIONS_KEY, on ? "on" : "off")],
    ["sf-keys", () => showsKeys(), (view, on) => keep(KEYS_KEY, on ? "on" : "off")],
    ["sf-legend-toggle", (view) => showsLegend(view), (view, on) => keep(legendKey(view), on ? "on" : "off")],
];

const paintSwitch = (toggle, on) => {
    toggle.setAttribute("aria-pressed", String(on));
    const pip = toggle.querySelector(".sf-pip");
    if (pip) pip.textContent = on ? "ON" : "OFF";
};

// A card a page draws outside its form follows the switches by carrying this attribute:
// the Targeting tab's events card kept its help text with Descriptions off.
// The legend's banner shows with its tab's Legend switch.
const applyToFollowers = (view, choice) => {
    for (const node of view.querySelectorAll("[data-sf-follows-display]")) {
        node.classList.toggle("is-terse", !choice.descriptions);
        node.classList.toggle("is-keyless", !choice.keys);
    }
    for (const banner of view.querySelectorAll("[data-sf-legend-banner]")) {
        banner.hidden = !choice.legend;
    }
};

// Both choices as the view's switches show them, or as remembered for one it lacks.
const shown = (view) => {
    const pressed = (id, read) => {
        const toggle = view.querySelector(`#${id}`);
        return toggle ? toggle.getAttribute("aria-pressed") === "true" : read();
    };
    return {
        descriptions: pressed("sf-terse", showsDescriptions),
        keys: pressed("sf-keys", showsKeys),
        legend: pressed("sf-legend-toggle", () => showsLegend(view)),
    };
};

// Shows the remembered choices on whichever switches the view has, and hands them to
// apply as { descriptions, keys }. For a view shown again, after another tab changed them.
export const paintDisplaySwitches = (view, apply) => {
    for (const [id, read] of DISPLAY_SWITCHES) {
        const toggle = view.querySelector(`#${id}`);
        if (toggle) paintSwitch(toggle, read(view));
    }
    const choice = { descriptions: showsDescriptions(), keys: showsKeys(), legend: showsLegend(view) };
    applyToFollowers(view, choice);
    apply(choice);
};

// Wires the switches for one showing of the view, the listeners dropped with signal. A
// click applies what was clicked: storage only remembers it, and a dashboard that
// refuses to store would otherwise show a switch saying one thing and a page another.
export const wireDisplaySwitches = (view, signal, apply) => {
    for (const [id, , write] of DISPLAY_SWITCHES) {
        const toggle = view.querySelector(`#${id}`);
        if (!toggle) continue;
        toggle.addEventListener("click", () => {
            const on = toggle.getAttribute("aria-pressed") !== "true";
            write(view, on);
            paintSwitch(toggle, on);
            // Both as the switches show them now, not as they were when this was wired: a
            // view wired once is repainted when another tab changes the other switch.
            const choice = shown(view);
            applyToFollowers(view, choice);
            apply(choice);
        }, { signal });
    }
    // The banner's own cross is its tab's Legend switch turned off.
    for (const close of view.querySelectorAll("[data-sf-legend-close]")) {
        close.addEventListener("click", () => {
            keep(legendKey(view), "off");
            const toggle = view.querySelector("#sf-legend-toggle");
            if (toggle) paintSwitch(toggle, false);
            const choice = { ...shown(view), legend: false };
            applyToFollowers(view, choice);
            apply(choice);
        }, { signal });
    }
    paintDisplaySwitches(view, apply);
};

// What the states and the boxes mean, in the shapes the rows draw them in, as a banner
// at the top of every tab that has either: a setting's three states and three boxes on
// the Application tab, a level's on the Targeting tab, and the two boxes alone where
// nothing is free or locked. Its cross and the Legend switch close it on every tab.
const BOXES = [
    ["box", "on", "On", null],
    ["box", "off", "Off", null],
];
const LEGENDS = {
    settings: {
        states: ["How a setting reaches users", [
            ["edge", "free", "Free", "each user decides"],
            ["edge", "suggested", "Suggested", "your value, set once as each user's starting point"],
            ["edge", "locked", "Locked", "your value, and users cannot change it"],
        ]],
        boxes: ["What a box says", [...BOXES, ["box", "unset", "Not set", "the app uses its own default"]]],
        note: "Only what you set here travels. Everything else stays the app's own default.",
    },
    // A level has no Free and no unset box: a setting is overridden there or falls through.
    level: {
        states: ["How this level reaches its users", [
            ["edge", "suggested", "Suggested", "this level's value, set once as each user's starting point"],
            ["edge", "locked", "Locked", "this level's value, and users cannot change it"],
            ["edge", "free", "Not listed", "the level above decides"],
        ]],
        boxes: ["What a box says", BOXES],
        note: null,
    },
    boxes: {
        states: null,
        boxes: ["What a box says", BOXES],
        note: null,
    },
};

const legendMark = (shape, state) => {
    if (shape === "box") {
        const box = document.createElement("input");
        box.type = "checkbox";
        box.className = "sf-check";
        box.disabled = true;
        box.tabIndex = -1;
        box.checked = state === "on";
        box.indeterminate = state === "unset";
        box.setAttribute("aria-hidden", "true");
        return box;
    }
    const edge = document.createElement("i");
    edge.className = `sf-edge is-${state}`;
    return edge;
};

export const drawLegend = (mount, { kind = "settings" } = {}) => {
    if (!mount) return;
    const legend = LEGENDS[kind] ?? LEGENDS.settings;
    mount.replaceChildren();
    mount.classList.toggle("is-single", !legend.states);
    mount.setAttribute("aria-label", legend.states ? "What the boxes and states mean" : "What the boxes mean");
    for (const [heading, items] of [legend.states, legend.boxes].filter(Boolean)) {
        const column = document.createElement("div");
        column.className = "sf-legend-col";
        const title = document.createElement("p");
        title.className = "sf-legend-h";
        title.textContent = heading;
        column.appendChild(title);
        for (const [shape, state, name, text] of items) {
            const item = document.createElement("div");
            item.className = "sf-legend-item";
            const words = document.createElement("span");
            const bold = document.createElement("b");
            bold.textContent = name;
            words.appendChild(bold);
            if (text) words.appendChild(document.createTextNode(` ${text}`));
            item.append(legendMark(shape, state), words);
            column.appendChild(item);
        }
        mount.appendChild(column);
    }
    if (legend.note) {
        const note = document.createElement("p");
        note.className = "sf-legend-note";
        note.textContent = legend.note;
        mount.appendChild(note);
    }
};

export const probeIntegration = (kind, address) =>
    window.ApiClient.ajax({
        type: "POST",
        url: window.ApiClient.getUrl("streamyfin/v1/integrations/probe"),
        contentType: "application/json",
        data: JSON.stringify({ kind, url: address }),
    }).then((response) => response.json())
        // ApiClient rejects with the Response itself, not with something wrapping one.
        // The route refuses some requests with a sentence of its own, and losing it
        // behind "the server could not be asked" hides which of several things to fix.
        .catch(async (rejected) => {
            const response = typeof rejected?.text === "function" ? rejected : rejected?.response;
            const body = await response?.text?.().catch(() => null);
            const error = rejected instanceof Error ? rejected : new Error("probe failed");
            throw Object.assign(error, { body, status: response?.status });
        });

// region private variables
let schema = undefined;
let config = undefined;
let defaultConfig = undefined;
// The settings the server is known to hold: read with the configuration, then what each answer
// shows it stored. None until the configuration is read.
let onServer = undefined;
// endregion private variables

// region listeners
const onSchemaLoadedListeners = {};
const onConfigLoadedListeners = {};

export const setOnSchemaUpdatedListener = (key, listener) => {
    onSchemaLoadedListeners[key] = listener;
}

export const setOnConfigUpdatedListener = (key, listener) => {
    onConfigLoadedListeners[key] = listener;
}

// One tab that throws on what it is handed stops neither the others nor the save that told
// them: a configuration the server stored has to reach every tab.
const triggerConfigListeners = (value, raw, cause) => {
    Object.values(onConfigLoadedListeners).forEach((listener) => {
        try {
            listener?.(config, raw, cause);
        } catch (error) {
            console.error(error);
        }
    });
}

// What a listener is told when the configuration was read back from the server after a
// save whose answer never came: an editor keeps what the administrator wrote.
export const REREAD = "reread";
// endregion listeners

// region getters/setters
export const getJsonSchema = () => schema;
const setSchema = (value) => {
    schema = value
    Object.values(onSchemaLoadedListeners).forEach(listener => listener?.(schema, value));
}

export const getDefaultConfig = () => defaultConfig;
export const getConfig = () => config;
export const setConfig = (value) => {
    config = value
    // Set to nothing, nothing is known of what the server holds either.
    if (value == null) onServer = undefined;
    triggerConfigListeners(config)
}

export const setYamlConfig = (value) => {
    config = tools.jsYaml.load(value)
    onServer = structuredClone(config?.settings ?? {});
    triggerConfigListeners(config, value)
}
// endregion getters/setters

// region helpers
export const setPage = (resource) => {
    const tabs = StreamyfinTabs();
    
    const index = tabs.findIndex(tab => tab.resource === resource);

    if (index === -1) {
        console.error(`Failed to find tab for ${resource}`);
        return;
    }

    console.log(`${tabs[index].name} loaded`)

    LibraryMenu.setTabs(tabs[index].resource, index, StreamyfinTabs)
}

const STORED = "stored";
const REFUSED = "refused";
const FAILED = "failed";

// Nothing is saved until the configuration was read from the server: a tab drawn without it
// starts from nothing, the Notifications and Other tabs make one from their first change, and a
// save from either would post a document holding only that tab's part, which the server stores
// over everything else.
const notRead = () => {
    Dashboard.alert("Nothing was saved: the configuration could not be read from the server. Reload the page to try again.");
    return false;
};

// A value as the server writes it back, for comparing two of them: its keys in order, since the
// forms write a setting as { value, locked } and the server's document reads back as
// { locked, value }; without a member that is null or empty, an empty string, list or map, since
// YamlDotNet writes the document without them (SerializationHelper's OmitNull and
// OmitEmptyCollections) and the forms write an empty setting as one; and a number or a boolean as
// its text, since a string such as 5.1 or true is written without quotes and read back as one.
// Members first, so a map left empty by its members is left out too.
const empty = (value) => value === undefined || value === null || value === ""
    || (typeof value === "object" && Object.keys(value).length === 0);
const written = (value) => {
    if (Array.isArray(value)) return value.map(written);
    if (typeof value === "number" || typeof value === "boolean") return String(value);
    if (!value || typeof value !== "object") return value;

    const out = {};
    for (const key of Object.keys(value).sort()) {
        const member = written(value[key]);
        if (!empty(member)) out[key] = member;
    }
    return out;
};

// Whether two values hold the same as the server writes them back.
export const same = (a, b) => {
    const text = (value) => {
        const shown = written(value);
        return JSON.stringify(empty(shown) && !Array.isArray(shown) ? null : shown);
    };
    return text(a) === text(b);
};

// The keys whose value differs between two `settings`, a key on one side only included.
const changedKeys = (before, after) => [...new Set([...Object.keys(before), ...Object.keys(after)])]
    .filter((key) => !same(before[key], after[key]));

// `settings` with each of `keys` as `from` has it, and without the ones `from` has none of.
const withKeys = (settings, keys, from) => {
    const out = { ...settings };
    for (const key of keys) {
        if (from[key] === undefined) delete out[key];
        else out[key] = structuredClone(from[key]);
    }
    return out;
};

// The server now holds `next`. A setting the tabs still hold as the server had it becomes what
// it holds now; one changed in a tab and not answered yet stays, for its own answer to settle.
const rebase = (next) => {
    const now = config?.settings ?? {};
    const keys = [...new Set([...Object.keys(now), ...Object.keys(onServer ?? {}), ...Object.keys(next)])];
    config = { ...config, settings: withKeys(now, keys.filter((key) => same(now[key], onServer?.[key])), next) };
    onServer = structuredClone(next);
};

// A save answered without storing, or whose answer was lost: each setting it changed becomes what
// the server holds of it, `held`, unless an edit made since changed it again.
const settle = (posted, changed, held) => {
    const now = config?.settings ?? {};
    config = { ...config, settings: withKeys(now, changed.filter((key) => same(now[key], posted[key])), held) };
};

// The Notifications and Other tabs' save, which keep their edits in the shared configuration as
// they are made. Resolves to true once the server has stored the configuration, and to false
// when it refused it or the request failed, so a page can keep its unsaved state on a refusal.
export const saveConfig = () => {
    if (!config || onServer === undefined) return Promise.resolve(notRead());

    const posted = structuredClone(config.settings ?? {});
    return store(config).then((outcome) => {
        if (outcome === STORED) rebase(posted);
        return outcome === STORED;
    });
};

// The Application and Home tabs' save. The edit goes into the configuration every tab shares
// at once, as the other tabs keep theirs there until they save, so a tab shown meanwhile shows
// it. A refusal puts back what the server holds of the settings the edit changed, and only
// those: the whole configuration from before took along what another tab changed meanwhile
// (#262). A request whose answer never came may have been stored, so the server is read: the
// settings take what it holds, and the save counts as stored when that is the edit. When it
// cannot be read either, they go back to what it was last known to hold.
export const saveConfigWith = (edit) => {
    if (!config || onServer === undefined) return Promise.resolve(notRead());

    const before = config.settings ?? {};
    setConfig(edit(config));
    const posted = structuredClone(config.settings ?? {});
    const changed = changedKeys(before, posted);

    return store(config).then(async (outcome) => {
        if (outcome === STORED) {
            rebase(posted);
            return true;
        }

        const held = outcome === REFUSED ? undefined : await readStored();
        if (held === undefined) {
            settle(posted, changed, onServer);
            triggerConfigListeners(config);
            return false;
        }

        // Told as any change is: the Yaml editor keeps its own text after a read back of its own
        // document, and one it drew while hidden kept an edit the server never stored.
        const settings = held?.settings ?? {};
        settle(posted, changed, settings);
        rebase(settings);
        triggerConfigListeners(config);
        // A save that changed nothing of its own, sent again while the same edit was on its way,
        // cannot tell from the read back whether that edit was stored.
        return changed.length > 0 && changed.every((key) => same(settings[key], posted[key]));
    });
};

// The Yaml tab's save. The configuration every tab saves changes only once the server has
// stored the document: set before, a refused document stayed there, and the next save from
// another tab sent it again with that tab's change. A document with nothing in it is sent
// all the same, so the server says why it is not saved.
export const saveYamlConfig = (text) => {
    let value;
    try {
        value = tools.jsYaml.load(text);
    } catch (error) {
        Dashboard.alert(unreadable(error));
        return Promise.resolve(false);
    }

    return store(value).then(async (outcome) => {
        if (outcome === REFUSED) return false;

        // What the server holds is read back rather than assumed. It folds Seerr's block
        // and the old spellings into the keys, so the document as written is not what the
        // other tabs should save next. And a request that failed on its way back may have
        // been stored, which the next save from another tab would otherwise undo.
        const held = await readStored();
        const now = outcome === STORED ? held ?? value : held;
        if (now === undefined) return false;

        // The settings an edit still on its way changed stay for its answer to settle; the
        // rest of what the tabs share is what the server holds.
        rebase(now?.settings ?? {});
        const { settings } = config;
        config = { ...(now ?? {}) };
        if (now?.settings !== undefined || Object.keys(settings).length) config.settings = settings;

        if (outcome === STORED) {
            triggerConfigListeners(config, text);
        } else {
            triggerConfigListeners(config, undefined, REREAD);
        }
        return outcome === STORED;
    });
}

// The configuration the server holds, or undefined when it cannot be asked.
const readStored = () => window.ApiClient.ajax({type: 'GET', url: YAML_URL, contentType: 'application/json'})
    .then(async (response) => tools.jsYaml.load((await response.json()).Value))
    .catch((error) => {
        console.error(error);
        return undefined;
    });

// js-yaml says where it stopped as a line and a column counted from 0.
const unreadable = (error) => error?.mark
    ? `Nothing was saved: the Yaml cannot be read at line ${error.mark.line + 1}, column ${error.mark.column + 1} (${error.reason}).`
    : `Nothing was saved: the Yaml cannot be read (${error?.message ?? error}).`;

// The server says where a document it refuses goes wrong, counted in the document the page
// wrote from the configuration, which nobody sees: even the Yaml tab's text is read and written
// again before it is sent. The sentence after it is what can be acted on.
const withoutPlace = (message) => String(message ?? "").replace(/^Line \d+, column \d+: /, "");

const store = (value) => {
    Dashboard.showLoadingMsg();

    //todo: potentially just keep it as json? we only need to convert only for editor reasons
    // convert config back to yaml
    const data = JSON.stringify({
        Value: tools.jsYaml.dump(value),
    });

    return window.ApiClient.ajax({type: 'POST', url: YAML_URL, data, contentType: 'application/json'})
        .then(async (response) => {
            const {Error, Message} = await response.json();

            if (Error) {
                Dashboard.alert(withoutPlace(Message));
                return REFUSED;
            } 

            Dashboard.processPluginConfigurationUpdateResult();
            forgetKept();
            return STORED;
        })
        .catch((error) => {
            console.error(error);
            return FAILED;
        })
        .finally(Dashboard.hideLoadingMsg);
}

export const getElValue = (el) => {
    const isArray = el.getAttribute('data-is-array') === "true";

    const valueKey = el.type === 'checkbox' ? 'checked' : el.type === 'number' ? 'valueAsNumber' : 'value';

    // Check any rules set on number input
    if (el.type === "number" && !el.checkValidity?.()) {
        return null
    }

    let value = el[valueKey];

    if (isArray) {
        if (value !== undefined && value !== '') {
            value = value.split(',').map(v => v.trim());
        } else {
            // For array fields, preserve empty arrays instead of converting to null
            value = [];
        }
    } else {
        // For non-array fields, convert empty strings to null
        if (value === '' || value === 'null') {
            value = null
        }
    }

    if (typeof value === 'number' && isNaN(value)) {
        value = null
    }

    return value ?? null
}

export const setDomValues = (dom, obj) => {
    dom.querySelectorAll('[data-key-name][data-prop-name]').forEach(el => {
        const key = el.getAttribute('data-key-name');
        const prop = el.getAttribute('data-prop-name');

        el[el.type === 'checkbox' ? 'checked' : 'value'] = obj?.[key]?.[prop] ?? null;
    })
}

// Once per element and event: a tab wires its controls each time it is shown. Kept per id, a
// view Jellyfin drew again, new elements under the same ids, was never wired, and its controls
// did nothing until the page was reloaded.
const registeredEventListeners = new WeakMap();
export const keyedEventListener = (el, type, listener) => {
    const types = registeredEventListeners.get(el) ?? new Set();
    if (types.has(type)) return;

    types.add(type);
    registeredEventListeners.set(el, types);
    el.addEventListener(type, listener);
}
// endregion helpers

export const StreamyfinTabs = () => [
    {
        href: "configurationpage?name=Application",
        resource: "Application",
        name: "Application"
    },
    {
        href: "configurationpage?name=Home",
        resource: "Home",
        name: "Home"
    },
    {
        href: "configurationpage?name=Targeting",
        resource: "Targeting",
        name: "Targeting"
    },
    {
        href: "configurationpage?name=Notifications",
        resource: "Notifications",
        name: "Notifications"
    },
    {
        href: "configurationpage?name=Other",
        resource: "Other",
        name: "Other"
    },
    {
        href: "configurationpage?name=Yaml",
        resource: "Yaml",
        name: "Yaml Editor"
    },
];

// region on Shared init
if (!window.Streamyfin?.shared) {
    // import json-yaml library
    await import(window.ApiClient.getUrl("web/configurationpage?name=js-yaml.js")).then(async (jsYaml) => {
        tools.jsYaml = jsYaml;
        
        // The default configuration is awaited with the rest, so a page that imports this
        // module can read getDefaultConfig() as soon as the import resolves. The Application
        // page shows a free setting's default from it.
        await window.ApiClient.ajax({type: 'GET', url: DEFAULT_URL, contentType: 'application/json'})
            .then(async function (response) {
                const {Value} = await response.json();
                defaultConfig = jsYaml.load(Value)
            })
            .catch((error) => console.error(error))

        // fetch schema
        // We want to define any pages first before setting any values
        await fetch(SCHEMA_URL)
            .then(async (response) => setSchema(await response.json()))
            .then(async () => {

                // fetch configuration
                await window.ApiClient.ajax({type: 'GET', url: YAML_URL, contentType: 'application/json'})
                    .then(async function (response) {
                        console.log("Getting actual config")
                        const {Value} = await response.json();
                        setYamlConfig(Value)
                    })
                    .catch((error) => console.error(error))
            });
    })
    
    // For developers when reviewing in console
    window.Streamyfin = {
        shared: {
            setOnSchemaUpdatedListener,
            setOnConfigUpdatedListener,
            setYamlConfig,
            setPage,
            saveConfig,
            saveConfigWith,
            saveYamlConfig,
            REREAD,
            getJsonSchema,
            getDefaultConfig,
            getConfig,
            setConfig,
            StreamyfinTabs,
            keyedEventListener,
            getElValue,
            setDomValues,
            SCHEMA_URL,
            YAML_URL,
            DEFAULT_URL,
            NOTIFICATION_URL,
            tools
        }
    }
}
// endregion on Shared init
