// The json-editor settings form. P3.1 built it for the Application tab and P3.3 reused it
// for a group's overrides. The Application tab now runs on settings-form.js, drawn by the
// plugin itself; this stays only until the Targeting tab moves onto the same renderer, and
// goes with json-editor when it does.
//
// The two pages differ in one thing only, and it is the thing that matters: the
// Application tab answers "what does this server default to", so it renders every
// declared setting; the Targeting tab answers "what does this group change", so it
// renders only the keys that level carries and lets the admin add or drop one. That is
// the difference between required_by_default true and false, passed in by the caller.

// json-editor labels the "Max" playback-quality option as the empty string, and the hand
// written page always turned a blank field into null, so the value round-trips blank <-> null:
// a stored null shows as blank in the form, and a blank is saved as null.
export const NO_VALUE = "";

const mapSettingValues = (settings, map) => {
    const out = {};
    for (const [key, entry] of Object.entries(settings ?? {})) {
        out[key] = entry && typeof entry === "object" && !Array.isArray(entry) && "value" in entry
            ? { ...entry, value: map(entry.value) }
            : entry;
    }
    return out;
};

export const toForm = (settings) => mapSettingValues(settings, (value) => (value === null ? NO_VALUE : value));
export const toConfig = (settings) => mapSettingValues(settings, (value) => (value === NO_VALUE ? null : value));

// The settings, arranged the way the app arranges them: one section per category,
// subdivided where a category is large enough to need it. Both come from the schema, so
// no page holds a list of its own to drift. Object key order is the order the settings
// are declared in, which is the order the sections come out in.
export const sectionsFrom = (schema) => {
    const properties = schema?.definitions?.Settings?.properties ?? {};
    const sections = new Map();

    for (const [key, spec] of Object.entries(properties)) {
        const category = spec["x-category"] ?? "Other";
        const group = spec["x-group"] ?? "";

        if (!sections.has(category)) {
            sections.set(category, new Map());
        }

        const groups = sections.get(category);
        if (!groups.has(group)) {
            groups.set(group, []);
        }

        groups.get(group).push(key);
    }

    return sections;
};

// A schema of its own for one section, so its editor renders those settings and nothing
// else. The definitions stay at the root, or the references inside the settings stop
// resolving.
const schemaFor = (schema, keys) => ({
    type: "object",
    title: "",
    properties: Object.fromEntries(keys.map((key) => [key, schema.definitions.Settings.properties[key]])),
    definitions: schema.definitions,
});

const pick = (values, keys) => Object.fromEntries(
    keys.filter((key) => key in (values ?? {})).map((key) => [key, values[key]]));

// Set on every editor either page builds. What a page decides for itself is
// required_by_default and disable_properties, which together are what makes an editor
// render every setting or only the ones a level carries.
export const BASE_EDITOR_OPTIONS = {
    theme: "html",
    iconlib: null,
    disable_edit_json: true,
    no_additional_properties: true,
    show_errors: "never",
};

// json-editor is 535 KB and both pages need it, so it is fetched once and left on the
// window rather than re-imported per page.
export const loadJsonEditor = async () => {
    if (!window.JSONEditor) {
        await import(window.ApiClient.getUrl("web/configurationpage?name=json-editor.js"));
    }
};

const countHeading = (category, keys) => `${category} (${keys.length})`;

/// One collapsible section per category, one editor per group inside it, seeded from
/// `seed`. Returns an entry per editor carrying the value that editor settled on once
/// ready, which is what a save compares against: an editor fills in a default for every
/// key its start value was missing, so comparing against what it was seeded with counts
/// an untouched default as a change.
export const renderSections = (mount, schema, seed, { editorOptions = {}, heading = countHeading } = {}) => {
    mount.textContent = "";
    const editors = [];

    for (const [category, groups] of sectionsFrom(schema)) {
        const section = document.createElement("details");
        section.className = "sf-section";

        const summary = document.createElement("summary");
        summary.textContent = heading(category, [...groups.values()].flat(), seed);
        section.appendChild(summary);

        for (const [group, keys] of groups) {
            if (group) {
                const label = document.createElement("h3");
                label.className = "sf-group";
                label.textContent = group;
                section.appendChild(label);
            }

            const host = document.createElement("div");
            section.appendChild(host);

            const editor = new window.JSONEditor(host, {
                ...BASE_EDITOR_OPTIONS,
                ...editorOptions,
                schema: schemaFor(schema, keys),
                startval: pick(seed, keys),
            });

            const entry = { editor, initial: null };
            editor.on("ready", () => {
                entry.initial = editor.getValue();
            });
            editors.push(entry);
        }

        mount.appendChild(section);
    }

    return editors;
};

/// The sections' editors read back as one settings object, alongside the first value
/// they held.
export const collect = (editors) => {
    const edited = {};
    const initial = {};

    for (const entry of editors) {
        const value = entry.editor.getValue();
        Object.assign(edited, value);
        Object.assign(initial, entry.initial ?? value);
    }

    return { edited, initial };
};

export const destroy = (editors) => {
    for (const { editor } of editors) {
        editor.destroy();
    }
    editors.length = 0;
};
