// The Application page is json-editor reading the schema the plugin serves, rather than a
// form written by hand a control at a time. Everything specific to a setting travels in the
// schema (see SerializationHelper.ShapeForGeneratedForm); this file only mounts the editor,
// seeds it with the stored config and writes the admin's edits back.

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
// The comparison is against the editor's own first value, not against what it was seeded with.
// The editor adds a default for every key the config was missing, so a key absent from the
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

// The Settings subtree of the served Config schema, made a schema in its own right so the
// editor renders the settings and nothing else. Its definitions stay at the root so the
// references inside the settings still resolve.
const buildFormSchema = (schema) => ({
    type: "object",
    title: "",
    properties: schema?.definitions?.Settings?.properties ?? {},
    definitions: schema?.definitions ?? {},
});

export default function (view) {
    view.addEventListener("viewshow", () => {
        import(window.ApiClient.getUrl("web/configurationpage?name=shared.js")).then(async (shared) => {
            shared.setPage("Application");

            if (!window.JSONEditor) {
                await import(window.ApiClient.getUrl("web/configurationpage?name=json-editor.js"));
            }

            const mount = document.getElementById("settings-editor");
            let editor = null;
            let initial = null;

            const render = () => {
                const schema = shared.getJsonSchema();
                if (!schema || editor) return;

                editor = new window.JSONEditor(mount, {
                    schema: buildFormSchema(schema),
                    startval: toForm(shared.getConfig()?.settings),
                    theme: "html",
                    iconlib: null,
                    disable_edit_json: true,
                    disable_properties: true,
                    no_additional_properties: true,
                    // Every declared setting has to render, whether or not the config
                    // already carries it: reaching a setting the admin never set is the
                    // whole point. Left optional, json-editor draws only the keys present
                    // in the stored config, which on this server was 20 of 92, and
                    // disable_properties removes the button that would add the rest.
                    // Writing them all back is prevented by settingsToPersist, not here.
                    required_by_default: true,
                    show_errors: "never",
                });

                // What the editor holds once it has filled in every setting the config was
                // missing. A save compares against this, so an untouched default is not a change.
                editor.on("ready", () => {
                    initial = editor.getValue();
                });
            };

            const dispose = () => {
                editor?.destroy();
                editor = null;
                initial = null;
            };

            // Schema and config are loaded once, before this import resolves, so the first
            // render usually runs here. The listeners cover a config that is replaced later.
            render();
            shared.setOnSchemaUpdatedListener("application", render);
            shared.setOnConfigUpdatedListener("application", () => {
                if (!editor) render();
            });

            shared.keyedEventListener(document.getElementById("save-settings-btn"), "click", (e) => {
                e.preventDefault();
                if (!editor) return;

                const config = shared.getConfig() ?? {};
                const settings = toConfig(
                    settingsToPersist(config.settings, initial ?? editor.getValue(), editor.getValue()));

                shared.setConfig({ ...config, settings });
                shared.saveConfig();
            });

            view.addEventListener("viewhide", dispose);
        });
    });
}
