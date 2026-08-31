// The Application page is json-editor reading the schema the plugin serves, rather than a
// form written by hand a control at a time. Everything specific to a setting travels in the
// schema (see SerializationHelper.ShapeForGeneratedForm); this file only mounts the editors,
// seeds them with the stored config and writes the admin's edits back.

// json-editor labels the "Max" playback-quality option as the empty string, and the hand
// written page always turned a blank field into null, so the value round-trips blank <-> null:
// a stored null shows as blank in the form, and a blank is saved as null.
const NO_VALUE = "";

const mapSettingValues = (settings, map) => {
    const out = {};
    for (const [key, entry] of Object.entries(settings ?? {})) {
        out[key] = entry && typeof entry === "object" && !Array.isArray(entry) && "value" in entry
            ? { ...entry, value: map(entry.value) }
            : entry;
    }
    return out;
};

const toForm = (settings) => mapSettingValues(settings, (value) => (value === null ? NO_VALUE : value));
const toConfig = (settings) => mapSettingValues(settings, (value) => (value === NO_VALUE ? null : value));

// The form renders every declared setting, filling the ones the config does not carry with a
// schema default. Writing all of them would push defaults the admin never chose and turn every
// unset setting into a set one. So a save keeps the settings that were already in the config and
// adds only the ones the admin actually changed.
//
// The comparison is against the editors' own first value, not against what they were seeded
// with. An editor adds a default for every key the config was missing, so a key absent from the
// config compares against undefined and would count as changed, which is how a save came to
// carry all 92 settings and then be rejected by the server.
const settingsToPersist = (loaded, initial, edited) => {
    const present = new Set(Object.keys(loaded ?? {}));
    const result = {};

    for (const [key, value] of Object.entries(edited)) {
        const changed = JSON.stringify(value) !== JSON.stringify(initial?.[key]);
        if (present.has(key) || changed) {
            result[key] = value;
        }
    }

    return result;
};

// The settings, arranged the way the app arranges them: one section per category, subdivided
// where a category is large enough to need it. Both come from the schema, so the page holds no
// list of its own to drift. Object key order is the order the settings are declared in, which
// is the order the sections come out in.
const sectionsFrom = (schema) => {
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

// A schema of its own for one section, so its editor renders those settings and nothing else.
// The definitions stay at the root, or the references inside the settings stop resolving.
const schemaFor = (schema, keys) => ({
    type: "object",
    title: "",
    properties: Object.fromEntries(keys.map((key) => [key, schema.definitions.Settings.properties[key]])),
    definitions: schema.definitions,
});

const pick = (values, keys) => Object.fromEntries(
    keys.filter((key) => key in (values ?? {})).map((key) => [key, values[key]]));

const EDITOR_OPTIONS = {
    theme: "html",
    iconlib: null,
    disable_edit_json: true,
    disable_properties: true,
    no_additional_properties: true,
    // Every declared setting has to render, whether or not the config already carries it:
    // reaching a setting the admin never set is the whole point. Left optional, json-editor
    // draws only the keys present in the stored config, which on one server was 20 of 92, and
    // disable_properties removes the button that would add the rest. Writing them all back is
    // prevented by settingsToPersist, not here.
    required_by_default: true,
    show_errors: "never",
};

export default function (view) {
    view.addEventListener("viewshow", () => {
        import(window.ApiClient.getUrl("web/configurationpage?name=shared.js")).then(async (shared) => {
            shared.setPage("Application");

            if (!window.JSONEditor) {
                await import(window.ApiClient.getUrl("web/configurationpage?name=json-editor.js"));
            }

            const mount = document.getElementById("settings-editor");
            const editors = [];

            const render = () => {
                const schema = shared.getJsonSchema();
                if (!schema || editors.length) return;

                const seed = toForm(shared.getConfig()?.settings);
                mount.textContent = "";

                for (const [category, groups] of sectionsFrom(schema)) {
                    const section = document.createElement("details");
                    section.className = "sf-section";

                    const heading = document.createElement("summary");
                    const count = [...groups.values()].reduce((total, keys) => total + keys.length, 0);
                    heading.textContent = `${category} (${count})`;
                    section.appendChild(heading);

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
                            ...EDITOR_OPTIONS,
                            schema: schemaFor(schema, keys),
                            startval: pick(seed, keys),
                        });

                        // What the editor holds once it has filled in every setting the config
                        // was missing. A save compares against this, so an untouched default is
                        // not counted as a change.
                        const entry = { editor, initial: null };
                        editor.on("ready", () => {
                            entry.initial = editor.getValue();
                        });
                        editors.push(entry);
                    }

                    mount.appendChild(section);
                }
            };

            const dispose = () => {
                for (const { editor } of editors) {
                    editor.destroy();
                }
                editors.length = 0;
            };

            // Schema and config are loaded once, before this import resolves, so the first
            // render usually runs here. The listeners cover a config that is replaced later.
            render();
            shared.setOnSchemaUpdatedListener("application", render);
            shared.setOnConfigUpdatedListener("application", () => {
                if (!editors.length) render();
            });

            shared.keyedEventListener(document.getElementById("save-settings-btn"), "click", (e) => {
                e.preventDefault();
                if (!editors.length) return;

                const edited = {};
                const initial = {};
                for (const entry of editors) {
                    const value = entry.editor.getValue();
                    Object.assign(edited, value);
                    Object.assign(initial, entry.initial ?? value);
                }

                const config = shared.getConfig() ?? {};
                const settings = toConfig(settingsToPersist(config.settings, initial, edited));

                shared.setConfig({ ...config, settings });
                shared.saveConfig();
            });

            view.addEventListener("viewhide", dispose);
        });
    });
}
