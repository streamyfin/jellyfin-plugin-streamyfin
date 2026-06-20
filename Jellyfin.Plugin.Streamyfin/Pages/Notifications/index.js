const saveBtn = document.getElementById('save-notification-btn');
const libraryContainer = document.getElementById('item-library-container');
const hiddenLibraryInput = document.getElementById('hidden-library-input');

const getValues = () => ({
    notifications: Array.from(document.querySelectorAll('[data-key-name][data-prop-name]')).reduce((acc, el) => {
        if (el.offsetParent === null) return acc;
        
        const notification = el.getAttribute('data-key-name');
        const property = el.getAttribute('data-prop-name');
        
        
        console.log("Notification", notification, el.offsetParent)

        const value = window.Streamyfin.shared.getElValue(el);
        acc[notification] = acc[notification] ?? {}

        if (value != null) {
            acc[notification][property] = value;
        }
        else delete acc[notification]

        return acc
    }, {})
})

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
            
            document.getElementById("notification-endpoint").innerText = shared.NOTIFICATION_URL

            // Set Jellyseerr endpoint URL
            const jellyseerrEndpoint = shared.NOTIFICATION_URL.replace('/notification', '/notification/seerr');
            document.getElementById("jellyseerr-endpoint").innerText = jellyseerrEndpoint

            shared.setDomValues(document, shared.getConfig()?.notifications)
            shared.setOnConfigUpdatedListener('notifications', (config) => {
                console.log("updating dom for notifications")

                const {notifications} = config;
                shared.setDomValues(document, notifications);
            })

            const folders = await window.ApiClient.get("/Library/VirtualFolders")
                .then((response) => response.json())

            if (folders.length === 0) {
                libraryContainer.append("No libraries available")
            }

            folders.forEach(folder => {
                if (!document.getElementById(folder.ItemId)) {
                    const checkboxContainer = document.createElement("label")
                    const checkboxInput = document.createElement("input")
                    const checkboxLabel = document.createElement("span")

                    checkboxContainer.className = "emby-checkbox-label"

                    checkboxInput.setAttribute("id", folder.ItemId)
                    checkboxInput.setAttribute("type", "checkbox")
                    checkboxInput.setAttribute("is", "emby-checkbox")
                    
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
                    })

                    checkboxLabel.className = "checkboxLabel"
                    checkboxLabel.innerText = folder.Name

                    checkboxContainer.append(
                        checkboxInput,
                        checkboxLabel
                    )

                    libraryContainer.append(checkboxContainer)
                }
            })

            document.querySelectorAll('[data-key-name][data-prop-name]').forEach(el => {
                shared.keyedEventListener(el, 'change', function () {
                    shared.setConfig(updateNotificationConfig(
                        el.getAttribute('data-key-name'),
                        shared.getConfig(),
                        el.getAttribute('data-prop-name'),
                        shared.getElValue(el)
                    ));
                })
            })

            shared.keyedEventListener(saveBtn, 'click', function (e) {
                e.preventDefault();
                shared.saveConfig()
            })
        })
    });
}