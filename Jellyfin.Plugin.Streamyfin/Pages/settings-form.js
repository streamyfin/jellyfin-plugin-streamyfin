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

// What a level inherits, given the levels above it from the least specific down. The
// server resolves the same way, most specific wins, so the Targeting tab can say what a
// group or a user falls through to without asking for a resolution per setting. Pass the
// app's declared defaults first, then the server's, then the groups a user belongs to in
// ascending priority, since the highest priority is the last to speak.
export const inherited = (...layers) => Object.assign({}, ...layers.map((layer) => layer ?? {}));

// The groups a user belongs to, least specific first, which is ascending priority. Two
// groups of the same priority keep the order the server listed them in.
export const groupsFor = (groups, userId) => (groups ?? [])
    .filter((group) => (group.userIds ?? []).includes(userId))
    .sort((a, b) => (a.priority ?? 0) - (b.priority ?? 0));

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

// Seerr reports a development build as a full commit hash, forty characters of nothing
// anyone reads. Enough of it to tell two builds apart.
const shortVersion = (version) => (version.length > 20 ? `${version.slice(0, 20)}\u2026` : version);

// What a probe answer reads as. The server says what it found and why; this only
// decides the sentence, so a test can hold the wording without a server.
export const probeText = (health) => {
    if (!health) return "The server gave no answer.";
    switch (health.outcome) {
        case "Ok":
            return health.version ? `Answered, running ${shortVersion(health.version)}.` : (health.detail ?? "Answered.");
        case "NotConfigured":
            return "Nothing to try yet.";
        case "NotAUrl":
            return health.detail ?? "That is not an address the server will open.";
        case "WrongService":
            return health.detail ?? "Something answered, but not this service.";
        default:
            return health.detail ?? "Nothing answered at that address.";
    }
};

// An empty field is not a broken one. Three tones rather than a pass and a fail, or
// "Nothing to try yet" arrives in the same red as "nothing answered".
export const probeTone = (health) => {
    switch (health?.outcome) {
        case "Ok": return "sf-said--ok";
        case "NotConfigured": return "sf-said--quiet";
        case "Reachable": return "sf-said--ok";
        default: return "sf-said--no";
    }
};

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
            control = el("input", "sf-check");
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
                control.title = "The app decides until you set it";
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
            control.title = "";
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
        case "Text":
            // Trimmed, because everything else on this path already is: the Test button
            // sends a trimmed address and the server trims before checking one, so an
            // address pasted with a trailing space was verified in a form it was never
            // saved in.
            return control.value.trim();
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

// Absolute, http or https, with a host. The browser's parser alone is looser than the
// server's: "http:/host" with one slash, and "http:host", both parse here and are
// refused by Uri.TryCreate, so the form would pass a value the server then refuses as a
// banner over the whole save, which is what marking the field exists to avoid.
const isWebAddress = (typed) => {
    const trimmed = typed.trim();
    if (!/^https?:\/\/[^/\\?#]+/i.test(trimmed)) return false;

    try {
        const { protocol, hostname } = new URL(trimmed);
        return (protocol === "http:" || protocol === "https:") && hostname.length > 0;
    } catch {
        return false;
    }
};

// What stops a set value from being saved. A free setting is never invalid, since nothing
// is written for it. An inert one is checked like any other: its value is still written,
// and a null would reach the store as a number it never was.
const problemOf = (row) => {
    if (row.state === "free") return null;
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

    // A choice whose value is null is a real answer, the playback quality's "no cap".
    // Jellyfin's JSON options omit a null, so that option arrives with no value key at
    // all: compared strictly it looked absent, and Max was refused as an empty field.
    if (field.control === "Select" && value === null && !(field.options ?? []).some((o) => (o.value ?? null) === null)) {
        return "Choose a value.";
    }

    if ((field.control === "Text" || field.control === "Secret") && !String(value ?? "").trim()) {
        return "Enter a value.";
    }

    // The same rule the server applies, so a setting it will refuse is marked on the
    // field rather than refused as a banner over the whole save. A configuration stored
    // before the rule existed can carry one of these, and finding it in a list of
    // ninety settings is the difference between a correction and a wall.
    if (field.probe && !isWebAddress(String(value ?? ""))) {
        return "Enter a whole address, starting with http:// or https://.";
    }

    if (field.control === "Language" && !value) return "Choose a language.";

    return null;
};

// undefined (no value at all) and null (a chosen null) are different answers, and
// JSON.stringify drops the former, which is what keeps them apart here.
const snapshot = (row) => JSON.stringify({ state: row.state, value: row.value });

// Two modes. "settings" is the Application tab: every setting, in its category and group,
// answering "what does this server default to". "overrides" is a level on the Targeting
// tab, a group or one user: only what the level overrides is listed, in one card, and
// each override says what it falls through to. In that mode `defaults` is what the level
// inherits, which for a group is what everyone gets.
export const createForm = (mount, { fields = [], values = {}, defaults = {}, cultures = [], terse = false, keys = true, mode = "settings", probe = null } = {}) => {
    const rows = new Map();
    const cards = [];
    const listeners = [];
    const overridesOnly = mode === "overrides";
    const arranged = overridesOnly
        ? [{ category: "Current overrides", groups: [{ name: "Current overrides", fields }] }]
        : sections(fields);
    let currentCategory = null;
    let query = "";
    // null, "set" (suggested or locked) or "locked". Like a search, a filter looks across
    // every category: "what have I set" is a question about the whole server.
    let stateFilter = null;

    const root = el("div", "sf-form");
    root.classList.toggle("is-terse", Boolean(terse));
    root.classList.toggle("is-keyless", !keys);
    // A level lists one setting after another, so it reads as a list rather than as a
    // grid of one-row cards. The page around it supplies the heading and the count.
    root.classList.toggle("is-overrides", overridesOnly);
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
    // Inert greys the row and says why; it locks nothing, so the value can still be
    // corrected and validation applies as everywhere else. Disabling the control was
    // how a row could end up both untouchable and invalid, with Save held hostage.
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

        // A composite setting has no control here, so it cannot go from free to set:
        // there would be no value to write.
        const noValueToSet = row.field.control === "Composite" && row.value === undefined;
        for (const button of row.buttons) {
            button.disabled = noValueToSet && button.dataset.state !== "free";
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

    // What a level falls through to, said for a person: a choice by its label, a toggle
    // as on or off, a list as its items, and "the app's default" when the level above
    // declares nothing.
    const inheritedText = (field) => {
        const entry = defaults?.[field.key];
        if (!entry || typeof entry !== "object") return "the app's default";
        const value = entry.value ?? null;
        switch (field.control) {
            case "Toggle": return value ? "on" : "off";
            case "Select": return (field.options ?? []).find((o) => (o.value ?? null) === value)?.label ?? String(value ?? "nothing");
            case "List": return Array.isArray(value) && value.length ? value.join(", ") : "nothing";
            case "Language": return value?.displayName ?? value?.DisplayName ?? "nothing";
            default: return value === null || value === "" ? "nothing" : String(value);
        }
    };

    const overridden = () => [...rows.values()].filter((row) => row.state !== "free").map((row) => row.field.key);

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
        if (overridesOnly) applyVisibility();
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
        // A level has no "free": a setting is overridden here or it is not listed. The
        // way out is the drop button, which is what "free" means on that tab.
        const offered = (field.lockable === false ? ["free", "suggested"] : STATES)
            .filter((state) => !overridesOnly || state !== "free");
        for (const state of offered) {
            const button = el("button", null, field.lockable === false && state === "suggested" ? "Set" : STATE_LABELS[state]);
            button.type = "button";
            button.dataset.state = state;
            states.appendChild(button);
            row.buttons.push(button);
        }
        head.appendChild(states);
        if (overridesOnly) {
            const drop = el("button", "sf-drop", "\u00d7");
            drop.type = "button";
            drop.title = "Stop overriding this setting";
            drop.setAttribute("aria-label", `Stop overriding ${field.title ?? field.key}`);
            drop.addEventListener("click", () => setState(row, "free"));
            head.appendChild(drop);
        }
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
            // An address is the one setting that can be wrong in a way nobody notices:
            // it saves, it looks right, and it shows up as an empty tab days later. The
            // server does the reaching, since it is the one that can see an internal
            // address a phone never will. No button when the page passed no way to ask.
            if (field.probe && probe) {
                const test = el("button", "sf-try", "Test");
                test.type = "button";
                // Three of these on a page, all reading "Test" to anything that lists
                // the buttons, unless each says what it tests.
                test.setAttribute("aria-label", `Test ${field.title ?? field.key}`);
                const said = el("span", "sf-said");
                // Announced, or the answer arrives only for someone who can see it.
                said.setAttribute("role", "status");
                said.hidden = true;

                const clear = () => {
                    said.hidden = true;
                    said.textContent = "";
                    said.className = "sf-said";
                };

                // An answer is about the address that was tried. Leaving it beside an
                // address that has since been edited says a different one was verified.
                row.control?.addEventListener("input", clear);

                test.addEventListener("click", () => {
                    const typed = String(row.control?.value ?? "").trim();
                    test.disabled = true;
                    said.hidden = false;
                    said.className = "sf-said";
                    said.textContent = "Asking the server\u2026";

                    // The call itself inside the chain, not only its result: a helper
                    // that throws before returning a promise would otherwise escape both
                    // catch and finally, and leave the button disabled on "Asking".
                    Promise.resolve()
                        .then(() => probe(field.probe, typed))
                        .then((health) => {
                            said.textContent = probeText(health);
                            said.className = `sf-said ${probeTone(health)}`;
                        })
                        .catch(() => {
                            said.textContent = "The server could not be asked.";
                            said.className = "sf-said sf-said--no";
                        })
                        .finally(() => {
                            test.disabled = false;
                        });
                });

                foot.appendChild(test);
                foot.appendChild(said);
            }
            row.el.appendChild(foot);
        }

        if (overridesOnly && field.control !== "Composite") {
            row.el.appendChild(el("p", "sf-from", `everyone gets ${inheritedText(field)}`));
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
            // A wide card splits its body into two columns, which is right for a
            // category of short toggles and wrong for a level, where each row carries a
            // value and the line saying what it falls through to.
            if (!overridesOnly && group.fields.length > 6) card.classList.add("sf-card--wide");

            if (!overridesOnly) {
                const header = el("header");
                header.appendChild(el("h2", null, group.name));
                header.appendChild(el("span", "sf-count", String(group.fields.length)));
                card.appendChild(header);
            }

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

    const matchesFilter = (row) => stateFilter === null
        || (stateFilter === "set" && row.state !== "free")
        || (stateFilter === "locked" && row.state === "locked");

    const applyVisibility = () => {
        const q = query.trim().toLowerCase();
        const narrowing = Boolean(q) || stateFilter !== null;
        for (const row of rows.values()) {
            const { title, key, description } = row.field;
            const matchesQuery = !q || [title, key, description].some((text) => String(text ?? "").toLowerCase().includes(q));
            const listed = !overridesOnly || row.state !== "free";
            row.el.hidden = !(listed && matchesQuery && matchesFilter(row));
        }
        for (const card of cards) {
            if (overridesOnly) {
                card.hidden = false;
                continue;
            }
            if (narrowing) {
                card.hidden = [...card.querySelectorAll(".sf-row")].every((r) => r.hidden);
            } else {
                card.hidden = currentCategory !== null && card.dataset.category !== currentCategory;
            }
        }
    };

    // A level opens on its overrides alone; the settings tab opens on everything.
    if (overridesOnly) applyVisibility();

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
            // A level lists the rows it overrides, so restoring the states has to put the
            // list back in step: an override added since the load goes, a dropped one
            // comes back.
            if (overridesOnly) applyVisibility();
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
        filter: (state) => {
            stateFilter = state === "set" || state === "locked" ? state : null;
            applyVisibility();
        },
        showCategory: (category) => {
            currentCategory = category ?? null;
            applyVisibility();
        },
        set: (key, state) => {
            const row = rows.get(key);
            if (row) setState(row, state);
        },
        overridden,
        // What a level could still override: every drawable setting it does not yet.
        candidates: () => [...rows.values()]
            .filter((row) => row.state === "free" && row.field.control !== "Composite")
            .map((row) => ({ key: row.field.key, title: row.field.title ?? row.field.key, category: row.field.category ?? "Other" })),
        categories: () => arranged.map((section) => {
            const keys = section.groups.flatMap((group) => group.fields.map((f) => f.key));
            const states = keys.map((key) => rows.get(key)?.state);
            return {
                name: section.category,
                count: keys.length,
                set: states.filter((state) => state !== "free").length,
                locked: states.filter((state) => state === "locked").length,
            };
        }),
        groups: (category) => (arranged.find((section) => section.category === category)?.groups ?? [])
            .map((group) => ({ name: group.name, count: group.fields.length })),
        cardFor: (category, group) => cards.find((card) => card.dataset.category === category && card.dataset.group === group) ?? null,
        setTerse: (on) => root.classList.toggle("is-terse", Boolean(on)),
        setKeys: (on) => root.classList.toggle("is-keyless", !on),
        onChange: (listener) => listeners.push(listener),
        destroy: () => {
            mount.textContent = "";
            rows.clear();
            cards.length = 0;
            listeners.length = 0;
        },
    };
};
