const subtitlePlaybackValue = () => document.getElementById('subtitle-playback-value');
const defaultOrientationValue = () => document.getElementById('default-orientation-value');
const downloadMethodValue = () => document.getElementById('download-method-value');
const defaultBitRateValue = () => document.getElementById('default-bitrate-value');
const remuxConcurrentLimitValue = () => document.getElementById('remux-concurrent-limit-value');
const searchEngineValue = () => document.getElementById('search-engine-value');

const saveBtn = () => document.getElementById('save-settings-btn');

// region helpers
const getValues = () => ({
    settings: Array.from(document.querySelectorAll('[data-key-name][data-prop-name]')).reduce((acc, el) => {
        if (el.offsetParent === null) return acc;

        const setting = el.getAttribute('data-key-name');
        const property = el.getAttribute('data-prop-name');

        const value = window.Streamyfin.shared.getElValue(el);
        acc[setting] = acc[setting] ?? {}

        if (value != null && !(property === 'locked' && acc[setting]['value'] === undefined)) {
            acc[setting][property] = value;
        }
        else delete acc[setting]

        return acc
    }, {})
})

const createOption = (value, title = null) => new Option(title ?? value, value)
const setOptions = (schema) => {
    if (!schema) return;

    const {
        SubtitlePlaybackMode, 
        OrientationLock, 
        DownloadMethod, 
        RemuxConcurrentLimit, 
        SearchEngine, 
        Bitrate
    } = schema.definitions;

    subtitlePlaybackValue().options.length = 0;
    SubtitlePlaybackMode.enum.forEach(value => subtitlePlaybackValue().add(createOption(value)));
    
    defaultOrientationValue().options.length = 0;
    OrientationLock.enum.forEach(value => defaultOrientationValue().add(createOption(value)));
    
    downloadMethodValue().options.length = 0;
    DownloadMethod.enum.forEach(value => downloadMethodValue().add(createOption(value)));

    defaultBitRateValue().options.length = 0;
    defaultBitRateValue().add(new Option("Max", 'null'))
    Bitrate.enum.forEach(value => defaultBitRateValue().add(createOption(value, value.replaceAll("_", ""))));

    remuxConcurrentLimitValue().options.length = 0;
    RemuxConcurrentLimit.enum.forEach(value => remuxConcurrentLimitValue().add(createOption(value)));

    searchEngineValue().options.length = 0;
    SearchEngine.enum.forEach(value => searchEngineValue().add(createOption(value)));
}

const updateSettingConfig = (name, config, valueName, value) => ({
    ...(config ?? {}),
    settings: {
        ...(config?.settings ?? {}),
        [name]: {
            ...(config?.settings?.[name] ?? {}),
            [valueName]: value,
        }
    }
})
// endregion helpers

export default function (view, params) {

    // init code here
    view.addEventListener('viewshow', (e) => {
        import(window.ApiClient.getUrl("web/configurationpage?name=shared.js")).then((shared) => {
            shared.setPage("Application");

            setOptions(shared.getJsonSchema());
            shared.setDomValues(document, shared.getConfig()?.settings)

            shared.setOnSchemaUpdatedListener('application', setOptions)
            shared.setOnConfigUpdatedListener('application', (config) => {
                console.log("updating dom for application settings")
                const {settings} = config;

                shared.setDomValues(document, settings);
            })

            document.querySelectorAll('[data-key-name][data-prop-name]').forEach(el => {
                shared.keyedEventListener(el, 'change', function () {
                    shared.setConfig(updateSettingConfig(
                        el.getAttribute('data-key-name'),
                        shared.getConfig(),
                        el.getAttribute('data-prop-name'),
                        shared.getElValue(el)
                    ));
                })
            })

            shared.keyedEventListener(saveBtn(), 'click', function () {
                e.preventDefault();
                const config = shared.getConfig();

                shared.setConfig({
                    ...config,
                    ...getValues()
                });
                shared.saveConfig()
            })
        })
    });
}