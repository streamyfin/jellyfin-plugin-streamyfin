export const SCHEMA_URL = window.ApiClient.getUrl('streamyfin/config/schema');
export const YAML_URL = window.ApiClient.getUrl('streamyfin/config/yaml');
export const DEFAULT_URL = window.ApiClient.getUrl('streamyfin/config/default');
export const NOTIFICATION_URL = window.ApiClient.getUrl('streamyfin/notification');
export const tools = {jsYaml: undefined};

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

export const saveConfig = () => {
    Dashboard.showLoadingMsg();
    
    if (config) {
        //todo: potentially just keep it as json? we only need to convert only for editor reasons
        // convert config back to yaml 
        const data = JSON.stringify({
            Value: tools.jsYaml.dump(config),
        });
    
        window.ApiClient.ajax({type: 'POST', url: YAML_URL, data, contentType: 'application/json'})
            .then(async (response) => {
                const {Error, Message} = await response.json();
    
                if (Error) {
                    Dashboard.hideLoadingMsg();
                    Dashboard.alert(Message);
                    return;
                } 
    
                Dashboard.processPluginConfigurationUpdateResult();
            })
            .catch((error) => console.error(error))
            .finally(Dashboard.hideLoadingMsg);
    }

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
        
        //fetch default configuration
        window.ApiClient.ajax({type: 'GET', url: DEFAULT_URL, contentType: 'application/json'})
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
