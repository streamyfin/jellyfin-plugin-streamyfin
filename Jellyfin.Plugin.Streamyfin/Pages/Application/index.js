// The Application page is json-editor reading the schema the plugin serves, rather than a
// form written by hand a control at a time. Everything specific to a setting travels in the
// schema (see SerializationHelper.ShapeForGeneratedForm); this file only mounts the editors,
// seeds them with the stored config and writes the admin's edits back. The mounting itself
// is in settings-form.js, shared with the Targeting page.

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

const EDITOR_OPTIONS = {
    disable_properties: true,
    // Every declared setting has to render, whether or not the config already carries it:
    // reaching a setting the admin never set is the whole point. Left optional, json-editor
    // draws only the keys present in the stored config, which on one server was 20 of 92, and
    // disable_properties removes the button that would add the rest. Writing them all back is
    // prevented by settingsToPersist, not here.
    required_by_default: true,
};

export default function (view) {
    view.addEventListener("viewshow", () => {
        import(window.ApiClient.getUrl("web/configurationpage?name=shared.js")).then(async (shared) => {
            shared.setPage("Application");

            const form = await import(window.ApiClient.getUrl("web/configurationpage?name=settings-form.js"));
            await form.loadJsonEditor();

            const mount = document.getElementById("settings-editor");
            let editors = [];

            const render = () => {
                const schema = shared.getJsonSchema();
                if (!schema || editors.length) return;

                editors = form.renderSections(
                    mount,
                    schema,
                    form.toForm(shared.getConfig()?.settings),
                    { editorOptions: EDITOR_OPTIONS });
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

                const { edited, initial } = form.collect(editors);
                const config = shared.getConfig() ?? {};
                const settings = form.toConfig(settingsToPersist(config.settings, initial, edited));

                shared.setConfig({ ...config, settings });
                shared.saveConfig();
            });

            view.addEventListener("viewhide", () => form.destroy(editors));
        });
    });
}
