// The Home tab: the rows of the app's home screen, edited as a list rather than as YAML.
//
// P3.2. The sections are not settings: their order matters, each one is filled by a
// different query, and adding one used to mean writing a block of YAML with the right
// four fields in the right place. What can be decided without a browser lives in
// home-editor.js and is tested there; this draws it.

export default function (view, params) {
    let drawn = false;
    let loading = false;

    view.addEventListener("viewshow", () => {
        import(window.ApiClient.getUrl("web/configurationpage?name=shared.js")).then(async (shared) => {
            shared.setPage("Home");

            const find = (id) => view.querySelector(`#${id}`);
            const status = find("sf-status");
            const editor = find("sf-editor");
            const dock = find("sf-dock");
            const dot = find("sf-dot");
            const summary = find("sf-dock-summary");
            const saveBtn = find("sf-save");
            const discardBtn = find("sf-discard");
            const addBtn = find("sf-add");
            const kindPicker = find("sf-new-kind");

            const renderer = await import(window.ApiClient.getUrl("web/configurationpage?name=settings-form.js"));
            renderer.applyTheme(find("sf-app"));

            // The dashboard keeps this page between tab switches and fires viewshow on
            // each one. Drawn once, and only once it is: a load that failed has to be
            // allowed to try again on the next showing rather than leaving a page that
            // says nothing until the browser is reloaded.
            if (drawn || loading) {
                return;
            }
            loading = true;

            const home = await import(window.ApiClient.getUrl("web/configurationpage?name=home-editor.js"));

            let schema;
            try {
                schema = await window.ApiClient.ajax({
                    type: "GET",
                    url: shared.SCHEMA_URL,
                    contentType: "application/json",
                }).then((response) => response.json());
            } catch (error) {
                console.error(error);
                status.textContent = renderer.askingFailed(error);
                status.classList.add("is-error");
                loading = false;
                return;
            }

            const el = (tag, className, text) => {
                const node = document.createElement(tag);
                if (className) node.className = className;
                if (text !== undefined) node.textContent = text;
                return node;
            };

            const stored = () => shared.getConfig()?.settings?.home?.value?.sections ?? [];

            // What the app draws is the stored list put in order, so the tab starts from
            // the same order rather than from the order the file happens to be written in.
            const asDrawn = () => home.renumber(home.inOrder(structuredClone(stored())));

            // The list a discard goes back to. Not stored(), which is whatever was last
            // written into the shared configuration: a save that the server refused left
            // its own edits there, so discarding restored them.
            let baseline = asDrawn();
            let sections = structuredClone(baseline);
            let dirty = false;

            const said = () => {
                const count = sections.length;
                return count === 1 ? "1 section" : `${count} sections`;
            };

            const touched = () => {
                dirty = true;
                dot.hidden = false;
                summary.textContent = `${said()}, not saved`;
                saveBtn.disabled = false;
                discardBtn.disabled = false;
            };

            const settled = () => {
                dirty = false;
                dot.hidden = true;
                summary.textContent = said();
                saveBtn.disabled = true;
                discardBtn.disabled = true;
            };

            const payloadOf = (section) => {
                const kind = section.kind ?? home.KINDS.find((candidate) => section[candidate]);
                if (!kind) return { kind: null, payload: {} };
                section[kind] = section[kind] ?? {};
                return { kind, payload: section[kind] };
            };

            const control = (field, value, onChange) => {
                switch (field.control) {
                    case "Toggle": {
                        const box = el("input", "sf-check");
                        box.type = "checkbox";
                        box.checked = value === true;
                        box.addEventListener("change", () => onChange(box.checked));
                        return box;
                    }
                    case "Number": {
                        const number = el("input", "sf-number");
                        number.type = "number";
                        number.value = value === null || value === undefined ? "" : String(value);
                        number.addEventListener("change", () => onChange(number.value === "" ? null : Number(number.value)));
                        return number;
                    }
                    case "Text": {
                        const text = el("input", "sf-text");
                        text.type = "text";
                        text.value = value ?? "";
                        text.addEventListener("change", () => onChange(text.value.trim() === "" ? null : text.value.trim()));
                        return text;
                    }
                    case "List": {
                        const list = el("textarea", "sf-list");
                        list.rows = 2;
                        list.placeholder = "One per line";
                        list.value = (value ?? []).join("\n");
                        list.addEventListener("change", () => {
                            const lines = list.value.split("\n").map((line) => line.trim()).filter(Boolean);
                            onChange(lines.length ? lines : null);
                        });
                        return list;
                    }
                    // Thirty seven item kinds is a wall, so the choices stay folded until
                    // they are the question, and the summary says what is picked.
                    case "Choices": {
                        const chosen = new Set(value ?? []);
                        const box = el("details", "sf-people");
                        const head = el("summary", null, chosen.size ? [...chosen].join(", ") : "Nothing chosen");
                        const list = el("div", "sf-checks");

                        for (const option of field.options) {
                            const line = el("label", "sf-checkline");
                            const tick = el("input", "sf-check");
                            tick.type = "checkbox";
                            tick.value = option;
                            tick.checked = chosen.has(option);
                            tick.addEventListener("change", () => {
                                if (tick.checked) chosen.add(option); else chosen.delete(option);
                                head.textContent = chosen.size ? [...chosen].join(", ") : "Nothing chosen";
                                onChange(chosen.size ? [...chosen] : null);
                            });
                            line.append(tick, el("span", null, option));
                            list.appendChild(line);
                        }

                        box.append(head, list);
                        return box;
                    }
                    case "Select": {
                        const select = el("select", "sf-select");
                        const blankOption = el("option", null, "Nothing chosen");
                        blankOption.value = "";
                        select.appendChild(blankOption);
                        for (const option of field.options) {
                            const node = el("option", null, option);
                            node.value = option;
                            select.appendChild(node);
                        }
                        select.value = value ?? "";
                        select.addEventListener("change", () => onChange(select.value === "" ? null : select.value));
                        return select;
                    }
                    default:
                        return el("span", "sf-note", "Written in the Yaml tab");
                }
            };

            const row = (title, node, description) => {
                const line = el("div", "sf-row");
                const head = el("div", "sf-head");
                if (node.classList.contains("sf-check")) head.appendChild(node);
                head.appendChild(el("span", "sf-name", title));
                if (!node.classList.contains("sf-check") && !node.classList.contains("sf-list") && node.tagName !== "DETAILS") {
                    head.appendChild(node);
                }
                line.appendChild(head);
                if (description) line.appendChild(el("p", "sf-desc", description));
                if (node.classList.contains("sf-list") || node.tagName === "DETAILS") line.appendChild(node);
                return line;
            };

            const card = (section, index) => {
                const { kind, payload } = payloadOf(section);
                const box = el("section", "sf-card sf-card--wide");
                const header = el("header");

                const title = el("input", "sf-text");
                title.type = "text";
                title.value = section.title ?? "";
                title.setAttribute("aria-label", "Section title");
                title.addEventListener("change", () => {
                    section.title = title.value;
                    touched();
                });
                header.appendChild(title);

                const pill = el("span", "sf-count", home.summarise(section));
                header.appendChild(pill);

                const up = el("button", "sf-btn", "↑");
                up.type = "button";
                up.title = "Move up";
                up.disabled = index === 0;
                up.addEventListener("click", () => {
                    sections = home.move(sections, index, -1);
                    touched();
                    redraw();
                });

                const down = el("button", "sf-btn", "↓");
                down.type = "button";
                down.title = "Move down";
                down.disabled = index === sections.length - 1;
                down.addEventListener("click", () => {
                    sections = home.move(sections, index, 1);
                    touched();
                    redraw();
                });

                const drop = el("button", "sf-drop", "×");
                drop.type = "button";
                drop.title = "Remove this section";
                drop.addEventListener("click", async () => {
                    const sure = await shared.confirmed(`Remove “${section.title || "this section"}” from the home screen?`);
                    if (!sure) return;
                    sections = home.remove(sections, index);
                    touched();
                    redraw();
                });

                header.append(up, down, drop);
                box.appendChild(header);

                const body = el("div", "sf-body");

                const orientation = el("select", "sf-select");
                for (const option of home.orientations(schema)) {
                    const node = el("option", null, option);
                    node.value = option;
                    orientation.appendChild(node);
                }
                orientation.value = section.orientation ?? "horizontal";
                orientation.addEventListener("change", () => {
                    section.orientation = orientation.value;
                    touched();
                });
                body.appendChild(row("Orientation", orientation, "How the posters in this row are shaped"));

                for (const field of home.fieldsFor(schema, kind)) {
                    const node = control(field, payload[field.key], (value) => {
                        if (value === null || value === undefined) delete payload[field.key];
                        else payload[field.key] = value;
                        pill.textContent = home.summarise(section);
                        touched();
                    });
                    body.appendChild(row(field.title, node, field.description));
                }

                box.appendChild(body);
                return box;
            };

            const redraw = () => {
                const grid = el("div", "sf-grid sf-grid--one");
                sections.forEach((section, index) => grid.appendChild(card(section, index)));
                if (!sections.length) {
                    grid.appendChild(el("p", "sf-status", "No sections yet. The app falls back to its own home screen."));
                }
                editor.replaceChildren(grid);
            };

            for (const kind of home.KINDS) {
                const option = el("option", null, kind);
                option.value = kind;
                kindPicker.appendChild(option);
            }

            addBtn.addEventListener("click", () => {
                sections = home.add(sections, kindPicker.value);
                touched();
                redraw();
            });

            discardBtn.addEventListener("click", () => {
                sections = structuredClone(baseline);
                settled();
                redraw();
            });

            saveBtn.addEventListener("click", () => {
                const config = shared.getConfig() ?? {};
                const settings = config.settings ?? {};
                const before = structuredClone(config);

                shared.setConfig({
                    ...config,
                    settings: {
                        ...settings,
                        home: { ...(settings.home ?? { locked: false }), value: { sections } },
                    },
                });

                shared.saveConfig().then((saved) => {
                    if (saved) {
                        baseline = structuredClone(sections);
                        settled();
                        return;
                    }

                    // The server refused it. What is on screen is still what the
                    // administrator wrote, so it stays, but the shared configuration goes
                    // back to what is stored: it is what every other tab reads.
                    shared.setConfig(before);
                });
            });

            status.remove();
            dock.hidden = false;
            settled();
            redraw();
            drawn = true;
            loading = false;
        });
    });
}
