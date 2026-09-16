// The Notifications tab, drawn from what the server says the events are.
//
// The four events used to be written twice: as properties on the configuration and as
// four blocks of markup here, so adding one meant editing both and the page could
// disagree with the server about what a field was called. The server describes them at
// v1/notifications/form, the same way it describes the settings, and this draws whatever
// it is handed. Adding an event is a property and its attributes, server side.
//
// The dashboard keeps the views it has already shown, so more than one page can carry an
// element with the same id. Everything here is looked up inside this view.

const el = (tag, className, text) => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
};

// A key is "<event>.<field>", which is how the payload is shaped.
const split = (key) => {
    const cut = key.indexOf(".");
    return { event: key.slice(0, cut), field: key.slice(cut + 1) };
};

const valueOf = (notifications, key) => {
    const { event, field } = split(key);
    return notifications?.[event]?.[field];
};

const write = (notifications, key, value) => {
    const { event, field } = split(key);
    return {
        ...(notifications ?? {}),
        [event]: { ...(notifications?.[event] ?? {}), [field]: value },
    };
};

const control = (field) => {
    switch (field.control) {
        case "Toggle": {
            const box = el("input", "sf-check");
            box.type = "checkbox";
            return box;
        }
        case "Number": {
            const number = el("input", "sf-number");
            number.type = "number";
            if (field.minimum !== null && field.minimum !== undefined) number.min = String(field.minimum);
            if (field.maximum !== null && field.maximum !== undefined) number.max = String(field.maximum);
            return number;
        }
        case "Text": {
            const text = el("input", "sf-text");
            text.type = "text";
            return text;
        }
        // A list the server could enumerate arrives with its choices, and boxes say what
        // there is to choose. One it could not stays a line per entry.
        case "List": {
            if (field.options?.length) return el("div", "sf-checks");
            const list = el("textarea", "sf-list");
            list.rows = 3;
            list.placeholder = "One per line";
            return list;
        }
        default:
            return null;
    }
};

// A number field carries its floor, so the browser knows the value is out of range
// before the server does. An invalid one is not written: the page says so on the row and
// keeps what was there, rather than sending a wait of minus one to the scheduler.
const valid = (node) => typeof node.checkValidity !== "function" || node.checkValidity();

const readControl = (field, node) => {
    switch (field.control) {
        case "Toggle": return node.checked;
        case "Number": return node.value === "" ? null : Number(node.value);
        case "Text": return node.value;
        case "List":
            return field.options?.length
                ? [...node.querySelectorAll("input:checked")].map((box) => box.value)
                : node.value.split("\n").map((line) => line.trim()).filter(Boolean);
        default: return null;
    }
};

const writeControl = (field, node, value) => {
    switch (field.control) {
        case "Toggle":
            node.checked = value === true;
            break;
        case "Number":
            node.value = value === null || value === undefined ? "" : String(value);
            break;
        case "Text":
            node.value = value ?? "";
            break;
        case "List": {
            const chosen = new Set(value ?? []);
            if (field.options?.length) {
                node.replaceChildren(...field.options.map((option) => {
                    const line = el("label", "sf-checkline");
                    const box = el("input", "sf-check");
                    box.type = "checkbox";
                    box.value = option.value;
                    box.checked = chosen.has(option.value);
                    line.append(box, el("span", null, option.label));
                    return line;
                }));
            } else {
                node.value = (value ?? []).join("\n");
            }
            break;
        }
        default:
            break;
    }
};

export default function (view, params) {
    // The dashboard keeps this page between tab switches and fires viewshow again on
    // each one. Drawn once: a second run would find the status line it had already
    // removed and throw on the way past.
    let drawn = null;

    view.addEventListener("viewshow", () => {
        import(window.ApiClient.getUrl("web/configurationpage?name=shared.js")).then(async (shared) => {
            shared.setPage("Notifications");

            if (drawn) {
                drawn(shared.getConfig()?.notifications);
                return;
            }

            const find = (id) => view.querySelector(`#${id}`);
            const status = find("sf-status");
            const editor = find("sf-editor");
            const dock = find("sf-dock");
            const dot = find("sf-dot");
            const summary = find("sf-dock-summary");
            const saveBtn = find("save-notification-btn");

            // The dashboard's theme is a user choice and its stylesheet only sets a
            // background, so the page reads that rather than guessing.
            const renderer = await import(window.ApiClient.getUrl("web/configurationpage?name=settings-form.js"));
            renderer.applyTheme(find("sf-app"));

            find("notification-endpoint").innerText = shared.NOTIFICATION_URL;

            let edits = 0;
            const edited = () => {
                edits += 1;
                dot.hidden = false;
                summary.textContent = edits === 1 ? "1 change to save" : `${edits} changes to save`;
                saveBtn.disabled = false;
            };
            const saved = () => {
                edits = 0;
                dot.hidden = true;
                summary.textContent = "Nothing to save";
                saveBtn.disabled = true;
            };

            let fields;
            try {
                fields = await window.ApiClient.ajax({
                    type: "GET",
                    url: window.ApiClient.getUrl("streamyfin/v1/notifications/form"),
                    contentType: "application/json",
                }).then((response) => response.json());
            } catch (error) {
                console.error(error);
                status.textContent = renderer.askingFailed(error);
                status.classList.add("is-error");
                return;
            }

            const rows = new Map();
            const grid = el("div", "sf-grid");
            let card = null;
            let category = null;

            for (const field of fields) {
                if (field.category !== category) {
                    category = field.category;
                    card = el("section", "sf-card");
                    const header = el("header");
                    header.appendChild(el("h2", null, category));
                    card.append(header, el("div", "sf-body"));
                    grid.appendChild(card);
                }

                const node = control(field);
                if (!node) continue;
                node.setAttribute("data-control", field.control);
                node.setAttribute("aria-label", field.title ?? field.key);

                const row = el("div", "sf-row");
                row.dataset.key = field.key;

                const head = el("div", "sf-head");
                if (field.control === "Toggle") head.appendChild(node);
                const name = el("span", "sf-name", field.title ?? field.key);
                name.appendChild(el("i", "sf-key", field.key));
                head.appendChild(name);
                if (field.control === "Number" || field.control === "Text") head.appendChild(node);
                row.appendChild(head);

                if (field.description) row.appendChild(el("p", "sf-desc", field.description));
                if (field.control === "List") row.appendChild(node);

                card.querySelector(".sf-body").appendChild(row);
                rows.set(field.key, { field, node, el: row });
            }

            // A field only matters while its event is on, and says so instead of
            // accepting a value that changes nothing.
            const applyDependencies = () => {
                for (const row of rows.values()) {
                    if (!row.field.dependsOn) continue;
                    const parent = rows.get(row.field.dependsOn);
                    row.el.classList.toggle("is-inert", parent ? parent.node.checked !== true : false);
                }
            };

            const draw = (notifications) => {
                for (const row of rows.values()) {
                    writeControl(row.field, row.node, valueOf(notifications, row.field.key));
                }
                applyDependencies();
            };

            draw(shared.getConfig()?.notifications);
            drawn = draw;
            shared.setOnConfigUpdatedListener("notifications", (config) => draw(config?.notifications));

            grid.addEventListener("change", (event) => {
                const row = [...rows.values()].find(
                    (candidate) => candidate.node === event.target || candidate.node.contains(event.target));
                if (!row) return;

                const ok = valid(row.node);
                row.el.classList.toggle("is-invalid", !ok);
                if (!ok) {
                    return;
                }

                const config = shared.getConfig() ?? {};
                shared.setConfig({
                    ...config,
                    notifications: write(config.notifications, row.field.key, readControl(row.field, row.node)),
                });
                applyDependencies();
                edited();
            });

            status.remove();
            editor.appendChild(grid);
            dock.hidden = false;

            shared.keyedEventListener(saveBtn, "click", (event) => {
                event.preventDefault();
                shared.saveConfig().then((stored) => {
                    if (stored) saved();
                });
            });
        });
    });
}
