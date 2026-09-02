// The settings form, drawn by the plugin itself from the description the server serves
// at v1/settings/form. P3.1 had json-editor draw it from the JSON schema, which meant
// reshaping the schema four ways for one library, styling its DOM from the outside, and
// a property picker that never actually added a setting. Drawing it here costs one
// renderer and buys three things the schema route could not say: the three states a
// setting can be in, a dependency between two settings, and a search over all of them.
//
// Every setting is in one of three states, because the app has three behaviours: "free"
// leaves it to the user, "suggested" pushes the value once as a starting point the user
// can still change, and "locked" pins it. Free is the absence of the key; the other two
// are the stored { value, locked } pair. Only what is not free is written back.
//
// No DOM is touched at import time and nothing here reads window.ApiClient, so the
// module runs under a test DOM as it does in the dashboard. The page fetches the data
// and hands it in.

const STATES = ["free", "suggested", "locked"];

const STATE_LABELS = { free: "Free", suggested: "Suggested", locked: "Locked" };

export const stateOf = (entry) => {
    if (entry === null || entry === undefined || typeof entry !== "object" || Array.isArray(entry)) {
        return "free";
    }
    return entry.locked ? "locked" : "suggested";
};

// The fields arranged the way the app arranges them: one section per category, one card
// per group inside it. A field with no group shares a card named after its category.
// Declaration order is kept at every level, so the form reads in the order Settings.cs
// is written in.
export const sections = (fields) => {
    const out = [];
    const byCategory = new Map();

    for (const field of fields) {
        const category = field.category ?? "Other";
        let section = byCategory.get(category);
        if (!section) {
            section = { category, groups: [], byGroup: new Map() };
            byCategory.set(category, section);
            out.push(section);
        }

        const name = field.group || category;
        let group = section.byGroup.get(name);
        if (!group) {
            group = { name, fields: [] };
            section.byGroup.set(name, group);
            section.groups.push(group);
        }
        group.fields.push(field);
    }

    return out.map(({ category, groups }) => ({ category, groups }));
};

// The dashboard's theme is a user choice, not the OS's, and the stylesheet it loads sets
// only a background on <html>. Its luminance says whether the page is light or dark.
export const themeFromBackground = (color) => {
    const channels = String(color ?? "").match(/\d+(\.\d+)?/g);
    if (!channels || channels.length < 3) {
        return "dark";
    }
    const [r, g, b] = channels.map(Number);
    const luminance = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255;
    return luminance > 0.5 ? "light" : "dark";
};

const el = (tag, className, text) => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
};

// Descriptions are written with **emphasis** for the warnings that matter (the Seerr
// key, for one). Everything else is text, so this is the whole markdown the form knows.
const describe = (text) => {
    const p = el("p", "sf-desc");
    const parts = String(text).split(/\*\*(.+?)\*\*/);
    parts.forEach((part, index) => {
        if (!part) return;
        p.appendChild(index % 2 ? el("strong", null, part) : document.createTextNode(part));
    });
    return p;
};

const typeDefault = (field) => {
    switch (field.control) {
        case "Toggle": return false;
        case "Text": case "Secret": return "";
        case "List": return [];
        default: return null;
    }
};

const formatBound = (n) => String(n);

const boundsHint = (field) => {
    const parts = [];
    if (field.minimum !== null && field.minimum !== undefined && field.maximum !== null && field.maximum !== undefined) {
        parts.push(`${formatBound(field.minimum)}–${formatBound(field.maximum)}`);
    } else if (field.minimum !== null && field.minimum !== undefined) {
        parts.push(`min ${formatBound(field.minimum)}`);
    } else if (field.maximum !== null && field.maximum !== undefined) {
        parts.push(`max ${formatBound(field.maximum)}`);
    }
    if (field.step !== null && field.step !== undefined) {
        parts.push(`step ${formatBound(field.step)}`);
    }
    return parts.join(" · ");
};

const buildControl = (field, cultures) => {
    let control;

    switch (field.control) {
        case "Toggle":
            control = el("input", "sf-switch");
            control.type = "checkbox";
            break;
        case "Number":
            control = el("input", "sf-number");
            control.type = "number";
            if (field.minimum !== null && field.minimum !== undefined) control.setAttribute("min", String(field.minimum));
            if (field.maximum !== null && field.maximum !== undefined) control.setAttribute("max", String(field.maximum));
            if (field.step !== null && field.step !== undefined) control.setAttribute("step", String(field.step));
            else if (field.integer) control.setAttribute("step", "1");
            break;
        case "Text":
            control = el("input", "sf-text");
            control.type = "text";
            control.autocomplete = "off";
            break;
        case "Secret":
            control = el("input", "sf-text");
            control.type = "password";
            control.autocomplete = "off";
            break;
        case "Select":
            control = el("select", "sf-select");
            for (const option of field.options ?? []) {
                const node = el("option", null, option.label);
                node.value = option.value ?? "";
                control.appendChild(node);
            }
            break;
        case "List":
            control = el("textarea", "sf-list");
            control.rows = 3;
            control.placeholder = "One per line";
            break;
        case "Language": {
            control = el("select", "sf-select");
            const blank = el("option", null, "Choose a language");
            blank.value = "";
            control.appendChild(blank);
            for (const culture of cultures ?? []) {
                const node = el("option", null, culture.DisplayName);
                node.value = culture.ThreeLetterISOLanguageName;
                control.appendChild(node);
            }
            break;
        }
        default:
            return null;
    }

    control.setAttribute("data-control", field.control);
    control.setAttribute("aria-label", field.title ?? field.key);
    return control;
};

// The option a select shows while its setting is free and the plugin declares no
// default: the app decides, and the form says so instead of picking a value for it.
const APP_DEFAULT = "\u0000app-default";

const placeholderOption = (select) => {
    let option = [...select.options].find((o) => o.value === APP_DEFAULT);
    if (!option) {
        option = el("option", null, "App default");
        option.value = APP_DEFAULT;
        option.disabled = true;
        option.hidden = true;
        select.insertBefore(option, select.firstChild);
    }
    return option;
};

const writeControl = (row) => {
    const { field, control, value } = row;
    if (!control) return;

    // No value at all: the setting is free and nothing declares what the app does.
    if (value === undefined) {
        switch (field.control) {
            case "Toggle":
                control.checked = false;
                control.indeterminate = true;
                break;
            case "Select":
                placeholderOption(control).selected = true;
                break;
            default:
                control.value = "";
                control.placeholder = "App default";
        }
        return;
    }

    switch (field.control) {
        case "Toggle":
            control.indeterminate = false;
            control.checked = value === true;
            break;
        case "Number":
            control.value = value === null || value === undefined ? "" : String(value);
            break;
        case "Select":
            control.value = value === null || value === undefined ? "" : String(value);
            break;
        case "List":
            control.value = Array.isArray(value) ? value.join("\n") : "";
            break;
        case "Language":
            // The config spells the two fields camelCase, the way YamlDotNet reads them; a
            // level written as JSON keeps the CLR names. Both open on the stored culture.
            control.value = value?.threeLetterISOLanguageName ?? value?.ThreeLetterISOLanguageName ?? "";
            break;
        default:
            control.value = value === null || value === undefined ? "" : String(value);
    }
    if (field.control !== "Toggle" && field.control !== "Select") {
        control.placeholder = field.control === "List" ? "One per line" : "";
    }
};

const readControl = (row, cultures) => {
    const { field, control } = row;

    switch (field.control) {
        case "Toggle":
            return control.checked;
        case "Number":
            return control.value.trim() === "" ? null : Number(control.value);
        case "Select":
            return control.value === "" || control.value === APP_DEFAULT ? null : control.value;
        case "List":
            return control.value.split(/\r?\n/).map((line) => line.trim()).filter(Boolean);
        case "Language": {
            const culture = (cultures ?? []).find((c) => c.ThreeLetterISOLanguageName === control.value);
            return culture
                ? { threeLetterISOLanguageName: culture.ThreeLetterISOLanguageName, displayName: culture.DisplayName }
                : null;
        }
        default:
            return control.value;
    }
};

// What stops a set value from being saved. A free setting is never invalid, since nothing
// is written for it, and neither is an inert one: its value changes nothing while the
// toggle it depends on is locked off, and holding it invalid would hold Save hostage.
const problemOf = (row) => {
    if (row.state === "free" || row.inert) return null;
    const { field, value } = row;

    if (field.control === "Number") {
        if (value === null || Number.isNaN(value)) return "Enter a number.";
        if (field.integer && !Number.isInteger(value)) return "Enter a whole number.";
        const low = field.minimum ?? null;
        const high = field.maximum ?? null;
        if ((low !== null && value < low) || (high !== null && value > high)) {
            return low !== null && high !== null
                ? `Enter a number between ${low} and ${high}.`
                : low !== null ? `Enter a number of at least ${low}.` : `Enter a number of at most ${high}.`;
        }
    }

    if (field.control === "Select" && value === null && !(field.options ?? []).some((o) => o.value === null)) {
        return "Choose a value.";
    }

    if ((field.control === "Text" || field.control === "Secret") && !String(value ?? "").trim()) {
        return "Enter a value.";
    }

    if (field.control === "Language" && !value) return "Choose a language.";

    return null;
};

// undefined (no value at all) and null (a chosen null) are different answers, and
// JSON.stringify drops the former, which is what keeps them apart here.
const snapshot = (row) => JSON.stringify({ state: row.state, value: row.value });

export const createForm = (mount, { fields = [], values = {}, defaults = {}, cultures = [], terse = false } = {}) => {
    const rows = new Map();
    const cards = [];
    const listeners = [];
    const arranged = sections(fields);
    let currentCategory = null;
    let query = "";

    const root = el("div", "sf-form");
    root.classList.toggle("is-terse", Boolean(terse));
    const grid = el("div", "sf-grid");
    root.appendChild(grid);

    const drawn = new Set(fields.map((f) => f.key));
    const passthrough = Object.fromEntries(
        Object.entries(values ?? {}).filter(([key]) => !drawn.has(key)));

    const notify = () => {
        for (const listener of listeners) listener();
    };

    const setPressed = (row) => {
        for (const button of row.buttons) {
            button.setAttribute("aria-pressed", String(button.dataset.state === row.state));
        }
        for (const state of STATES) {
            row.el.classList.toggle(`is-${state}`, row.state === state);
        }
    };

    const refreshProblem = (row) => {
        const problem = problemOf(row);
        row.invalid = problem !== null;
        row.el.classList.toggle("is-invalid", row.invalid);
        row.problem.textContent = problem ?? "";
        row.problem.hidden = !row.invalid;
    };

    // A dependent setting is inert while the toggle it depends on is locked off at this
    // level: nobody can turn the toggle on, so the value changes nothing. Suggested off
    // is not inert, since a user can still turn the toggle on and then meet this value.
    const refreshGating = (row) => {
        const parent = row.field.dependsOn ? rows.get(row.field.dependsOn) : undefined;
        const inert = parent !== undefined && parent.state === "locked" && parent.value === false;
        row.inert = inert;

        if (row.field.dependsOn) {
            const title = parent?.field.title ?? row.field.dependsOn;
            row.el.classList.toggle("is-inert", inert);
            row.why.textContent = inert
                ? `“${title}” is locked off here, so this changes nothing.`
                : `Only matters while “${title}” is on.`;
        }
        if (row.control) row.control.disabled = inert;

        // A composite setting has no control here, so it cannot go from free to set:
        // there would be no value to write.
        // Free stays reachable on an inert row, or a setting stuck there could never be
        // released without unlocking its parent first.
        const noValueToSet = row.field.control === "Composite" && row.value === undefined;
        for (const button of row.buttons) {
            const free = button.dataset.state === "free";
            button.disabled = (inert && !free) || (noValueToSet && !free);
        }
    };

    const refreshRow = (row) => {
        setPressed(row);
        writeControl(row);
        refreshGating(row);
        refreshProblem(row);
        for (const dependent of rows.values()) {
            if (dependent.field.dependsOn !== row.field.key) continue;
            refreshGating(dependent);
            refreshProblem(dependent);
        }
    };

    // What the plugin declares as the app's default, or undefined when it declares
    // nothing. A declared entry with no value is a null the YAML writer omitted: for a
    // list that is an empty list, for a nullable choice it is the null option.
    const defaultValue = (field) => {
        const entry = defaults?.[field.key];
        if (!entry || typeof entry !== "object") return undefined;
        const value = entry.value ?? null;
        return field.control === "List" && value === null ? [] : value;
    };

    // The value a setting takes when it is set with nothing to start from.
    const firstValue = (field) => (field.control === "Toggle" ? false : typeDefault(field) === undefined ? null : typeDefault(field));

    const setState = (row, state) => {
        if (row.state === state) return;
        if (row.field.control === "Composite" && state !== "free" && row.value === undefined) return;

        if (state === "free" && row.field.control !== "Composite") {
            row.value = defaultValue(row.field);
        } else if (row.value === undefined && row.field.control !== "Composite") {
            row.value = firstValue(row.field);
        }
        row.state = state;
        refreshRow(row);
        notify();
    };

    const buildRow = (field) => {
        const stored = values?.[field.key];
        const state = stateOf(stored);
        const row = {
            field,
            state,
            value: state === "free"
                ? (field.control === "Composite" ? undefined : defaultValue(field))
                : (field.control === "List" ? (stored.value ?? []) : (stored.value ?? null)),
            invalid: false,
            inert: false,
            el: el("div", "sf-row"),
            control: field.control === "Composite" ? null : buildControl(field, cultures),
            buttons: [],
            why: el("p", "sf-why"),
            problem: el("p", "sf-problem"),
            baseline: null,
        };
        row.el.dataset.key = field.key;

        const head = el("div", "sf-head");
        if (field.control === "Toggle") head.appendChild(row.control);

        const name = el("span", "sf-name", field.title ?? field.key);
        name.appendChild(el("i", "sf-key", field.key));
        head.appendChild(name);

        const states = el("div", "sf-state");
        states.setAttribute("role", "group");
        states.setAttribute("aria-label", "How this setting reaches users");
        const offered = field.lockable === false ? ["free", "suggested"] : STATES;
        for (const state of offered) {
            const button = el("button", null, field.lockable === false && state === "suggested" ? "Set" : STATE_LABELS[state]);
            button.type = "button";
            button.dataset.state = state;
            states.appendChild(button);
            row.buttons.push(button);
        }
        head.appendChild(states);
        row.el.appendChild(head);

        if (field.description) row.el.appendChild(describe(field.description));

        if (field.dependsOn) {
            row.el.appendChild(row.why);
        } else {
            row.why.hidden = true;
        }

        if (field.control === "Composite") {
            const foot = el("div", "sf-foot");
            const note = el("span", "sf-note", "Edited as YAML for now. ");
            const link = el("a", null, "Open the Yaml tab");
            link.href = "#/configurationpage?name=Yaml";
            note.appendChild(link);
            foot.appendChild(note);
            row.el.appendChild(foot);
        } else if (field.control !== "Toggle") {
            const foot = el("div", "sf-foot");
            foot.appendChild(row.control);
            if (field.control === "Secret") {
                const reveal = el("button", "sf-reveal", "Show");
                reveal.type = "button";
                reveal.addEventListener("click", () => {
                    const masked = row.control.type === "password";
                    row.control.type = masked ? "text" : "password";
                    reveal.textContent = masked ? "Hide" : "Show";
                });
                foot.appendChild(reveal);
            }
            if (field.control === "Number") {
                const hint = boundsHint(field);
                if (hint) foot.appendChild(el("span", "sf-bounds", hint));
            }
            row.el.appendChild(foot);
        }

        row.problem.hidden = true;
        row.el.appendChild(row.problem);

        rows.set(field.key, row);
        return row;
    };

    for (const section of arranged) {
        for (const group of section.groups) {
            const card = el("section", "sf-card");
            card.dataset.category = section.category;
            card.dataset.group = group.name;
            if (group.fields.length > 6) card.classList.add("sf-card--wide");

            const header = el("header");
            header.appendChild(el("h2", null, group.name));
            header.appendChild(el("span", "sf-count", String(group.fields.length)));
            card.appendChild(header);

            const body = el("div", "sf-body");
            for (const field of group.fields) body.appendChild(buildRow(field).el);
            card.appendChild(body);

            grid.appendChild(card);
            cards.push(card);
        }
    }

    for (const row of rows.values()) refreshRow(row);
    for (const row of rows.values()) row.baseline = snapshot(row);

    root.addEventListener("click", (event) => {
        const button = event.target.closest?.(".sf-state button");
        if (!button || button.disabled) return;
        const row = rows.get(button.closest(".sf-row").dataset.key);
        setState(row, button.dataset.state);
    });

    root.addEventListener("change", (event) => {
        const control = event.target;
        if (!control?.hasAttribute?.("data-control")) return;
        const row = rows.get(control.closest(".sf-row").dataset.key);
        row.value = readControl(row, cultures);
        if (row.state === "free") row.state = "suggested";
        refreshRow(row);
        notify();
    });

    const applyVisibility = () => {
        const q = query.trim().toLowerCase();
        for (const row of rows.values()) {
            if (!q) {
                row.el.hidden = false;
                continue;
            }
            const { title, key, description } = row.field;
            row.el.hidden = ![title, key, description].some((text) => String(text ?? "").toLowerCase().includes(q));
        }
        for (const card of cards) {
            if (q) {
                card.hidden = [...card.querySelectorAll(".sf-row")].every((r) => r.hidden);
            } else {
                card.hidden = currentCategory !== null && card.dataset.category !== currentCategory;
            }
        }
    };

    mount.textContent = "";
    mount.appendChild(root);

    return {
        root,
        toSettings: () => {
            const out = { ...passthrough };
            for (const row of rows.values()) {
                if (row.state === "free" || row.invalid) continue;
                const value = row.field.control === "List" ? (row.value ?? []) : (row.value ?? null);
                out[row.field.key] = { value, locked: row.state === "locked" };
            }
            return out;
        },
        invalid: () => [...rows.values()].filter((row) => row.invalid).map((row) => row.field.key),
        dirtyCount: () => [...rows.values()].filter((row) => snapshot(row) !== row.baseline).length,
        reset: () => {
            for (const row of rows.values()) {
                const parsed = JSON.parse(row.baseline);
                row.state = parsed.state;
                row.value = "value" in parsed ? parsed.value : undefined;
            }
            for (const row of rows.values()) refreshRow(row);
            notify();
        },
        markSaved: () => {
            for (const row of rows.values()) row.baseline = snapshot(row);
            notify();
        },
        search: (text) => {
            query = text ?? "";
            applyVisibility();
        },
        showCategory: (category) => {
            currentCategory = category ?? null;
            applyVisibility();
        },
        categories: () => arranged.map((section) => ({
            name: section.category,
            count: section.groups.reduce((n, group) => n + group.fields.length, 0),
        })),
        groups: (category) => (arranged.find((section) => section.category === category)?.groups ?? [])
            .map((group) => ({ name: group.name, count: group.fields.length })),
        cardFor: (category, group) => cards.find((card) => card.dataset.category === category && card.dataset.group === group) ?? null,
        setTerse: (on) => root.classList.toggle("is-terse", Boolean(on)),
        onChange: (listener) => listeners.push(listener),
        destroy: () => {
            mount.textContent = "";
            rows.clear();
            cards.length = 0;
            listeners.length = 0;
        },
    };
};
