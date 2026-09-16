// The dashboard keeps the views it has already shown, so more than one page can carry
// an element with the same id. Everything here is looked up inside this view.
const dock = (view) => {
    const summary = view.querySelector('#sf-dock-summary');
    const dot = view.querySelector('#sf-dot');
    const button = view.querySelector('#save-notification-btn');

    // The dock says what a click will do, so it has to know whether anything changed.
    // Counting the edits rather than diffing the configuration: every control here
    // writes through one path, and a count is enough to say how many.
    let edits = 0;

    return {
        button,
        edited: () => {
            edits += 1;
            dot.hidden = false;
            summary.textContent = edits === 1 ? '1 change to save' : `${edits} changes to save`;
            button.disabled = false;
        },
        saved: () => {
            edits = 0;
            dot.hidden = true;
            summary.textContent = 'Nothing to save';
            button.disabled = true;
        },
    };
};

// region helpers
const updateNotificationConfig = (name, config, valueName, value) => ({
    ...(config ?? {}),
    notifications: {
        ...(config?.notifications ?? {}),
        [name]: {
            ...(config?.notifications?.[name] ?? {}),
            [valueName]: value,
        }
    }
})
// endregion helpers

export default function (view, params) {

    // init code here
    view.addEventListener('viewshow', (e) => {
        import(window.ApiClient.getUrl("web/configurationpage?name=shared.js")).then(async (shared) => {
            shared.setPage("Notifications");

            const libraryContainer = view.querySelector('#item-library-container');
            const hiddenLibraryInput = view.querySelector('#hidden-library-input');
            const { button: saveBtn, edited, saved } = dock(view);

            // The dashboard's theme is a user choice and its stylesheet only sets a
            // background, so the page reads that rather than guessing.
            const renderer = await import(window.ApiClient.getUrl("web/configurationpage?name=settings-form.js"));
            renderer.applyTheme(view.querySelector("#sf-app"));
            
            view.querySelector("#notification-endpoint").innerText = shared.NOTIFICATION_URL

            shared.setDomValues(view, shared.getConfig()?.notifications)
            shared.setOnConfigUpdatedListener('notifications', (config) => {
                console.log("updating dom for notifications")

                const {notifications} = config;
                shared.setDomValues(view, notifications);
            })

            const folders = await window.ApiClient.get("/Library/VirtualFolders")
                .then((response) => response.json())

            if (folders.length === 0) {
                libraryContainer.append("No libraries available")
            }

            folders.forEach(folder => {
                if (!view.querySelector(`#${CSS.escape(folder.ItemId)}`)) {
                    const checkboxContainer = document.createElement("label")
                    const checkboxInput = document.createElement("input")
                    const checkboxLabel = document.createElement("span")

                    checkboxContainer.className = "sf-checkline"

                    checkboxInput.setAttribute("id", folder.ItemId)
                    checkboxInput.setAttribute("type", "checkbox")
                    checkboxInput.className = "sf-check"
                    
                    const libraries = shared.getConfig()?.notifications?.['itemAdded']?.['enabledLibraries'] ?? []
                    checkboxInput.checked = libraries.includes(folder.ItemId) === true

                    shared.keyedEventListener(checkboxInput, 'change', function () {
                        const isEnabled = checkboxInput.checked
                        let currentList = hiddenLibraryInput.value.split(",").filter(Boolean)

                        if (isEnabled)
                            currentList = [...new Set(currentList.concat(folder.ItemId))]
                        else
                            currentList = currentList.filter(id => id !== folder.ItemId)

                        hiddenLibraryInput.value = currentList.join(",")

                        shared.setConfig(updateNotificationConfig(
                            "itemAdded",
                            shared.getConfig(),
                            "enabledLibraries",
                            shared.getElValue(hiddenLibraryInput)
                        ));
                        edited();
                    })

                    checkboxLabel.innerText = folder.Name

                    checkboxContainer.append(
                        checkboxInput,
                        checkboxLabel
                    )

                    libraryContainer.append(checkboxContainer)
                }
            })

            view.querySelectorAll('[data-key-name][data-prop-name]').forEach(el => {
                shared.keyedEventListener(el, 'change', function () {
                    shared.setConfig(updateNotificationConfig(
                        el.getAttribute('data-key-name'),
                        shared.getConfig(),
                        el.getAttribute('data-prop-name'),
                        shared.getElValue(el)
                    ));
                    edited();
                })
            })

            shared.keyedEventListener(saveBtn, 'click', function (e) {
                e.preventDefault();
                shared.saveConfig().then((stored) => {
                    if (stored) saved();
                })
            })
        })
    });
}