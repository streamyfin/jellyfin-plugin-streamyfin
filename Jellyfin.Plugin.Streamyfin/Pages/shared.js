export const SCHEMA_URL = window.ApiClient.getUrl('streamyfin/config/schema');
export const YAML_URL = window.ApiClient.getUrl('streamyfin/config/yaml');
export const DEFAULT_URL = window.ApiClient.getUrl('streamyfin/config/default');
export const NOTIFICATION_URL = window.ApiClient.getUrl('streamyfin/notification');
export const tools = {jsYaml: undefined};

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
// The legend is shown until it is closed, on one tab for all of them.
export const showsLegend = () => recall(LEGEND_KEY) !== "off";

const DISPLAY_SWITCHES = [
    ["sf-terse", showsDescriptions, (on) => keep(DESCRIPTIONS_KEY, on ? "on" : "off")],
    ["sf-keys", showsKeys, (on) => keep(KEYS_KEY, on ? "on" : "off")],
    ["sf-legend-toggle", showsLegend, (on) => keep(LEGEND_KEY, on ? "on" : "off")],
];

const paintSwitch = (toggle, on) => {
    toggle.setAttribute("aria-pressed", String(on));
    const pip = toggle.querySelector(".sf-pip");
    if (pip) pip.textContent = on ? "ON" : "OFF";
};

// A card a page draws outside its form follows the switches by carrying this attribute:
// the Targeting tab's events card kept its help text with Descriptions off.
// The legend's banner, on every tab that has one, shows with the Legend switch.
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
        legend: pressed("sf-legend-toggle", showsLegend),
    };
};

// Shows the remembered choices on whichever switches the view has, and hands them to
// apply as { descriptions, keys }. For a view shown again, after another tab changed them.
export const paintDisplaySwitches = (view, apply) => {
    for (const [id, read] of DISPLAY_SWITCHES) {
        const toggle = view.querySelector(`#${id}`);
        if (toggle) paintSwitch(toggle, read());
    }
    const choice = { descriptions: showsDescriptions(), keys: showsKeys(), legend: showsLegend() };
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
            write(on);
            paintSwitch(toggle, on);
            // Both as the switches show them now, not as they were when this was wired: a
            // view wired once is repainted when another tab changes the other switch.
            const choice = shown(view);
            applyToFollowers(view, choice);
            apply(choice);
        }, { signal });
    }
    // The banner's own cross is the Legend switch turned off.
    for (const close of view.querySelectorAll("[data-sf-legend-close]")) {
        close.addEventListener("click", () => {
            keep(LEGEND_KEY, "off");
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
// endregion private variables

// region listeners
const registeredEventListeners = {}
const onSchemaLoadedListeners = {};
const onConfigLoadedListeners = {};

export const setOnSchemaUpdatedListener = (key, listener) => {
    onSchemaLoadedListeners[key] = listener;
}

export const setOnConfigUpdatedListener = (key, listener) => {
    onConfigLoadedListeners[key] = listener;
}

const triggerConfigListeners = (value, raw) => {
    Object.values(onConfigLoadedListeners).forEach(listener => listener?.(config, raw));
}
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
    triggerConfigListeners(config)
}

export const setYamlConfig = (value) => {
    config = tools.jsYaml.load(value)
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

// Resolves to true once the server has stored the configuration, and to false when it
// refused it or the request failed, so a page can keep its unsaved state on a refusal.
export const saveConfig = () => {
    Dashboard.showLoadingMsg();
    
    if (!config) {
        Dashboard.hideLoadingMsg();
        return Promise.resolve(false);
    }

    //todo: potentially just keep it as json? we only need to convert only for editor reasons
    // convert config back to yaml 
    const data = JSON.stringify({
        Value: tools.jsYaml.dump(config),
    });

    return window.ApiClient.ajax({type: 'POST', url: YAML_URL, data, contentType: 'application/json'})
        .then(async (response) => {
            const {Error, Message} = await response.json();

            if (Error) {
                Dashboard.alert(Message);
                return false;
            } 

            Dashboard.processPluginConfigurationUpdateResult();
            return true;
        })
        .catch((error) => {
            console.error(error);
            return false;
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

// prevent duplicate listeners from being created everytime a tab is switched
export const keyedEventListener = (el, type, listener) =>{
    const elId = el.getAttribute("id");
    
    if (!registeredEventListeners[elId]) {
        registeredEventListeners[elId] = {
            type,
            listener,
        };
        el.addEventListener(type, listener);
    }
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
            getJsonSchema,
            getDefaultConfig,
            getConfig,
            setConfig,
            StreamyfinTabs,
            registeredEventListeners,
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
