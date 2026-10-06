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
            const previewBox = find("sf-preview");
            const moved = find("sf-moved");
            const examplePicker = find("sf-example");
            const exampleAbout = find("sf-example-about");
            const loadExample = find("sf-load-example");

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

            // The server's libraries, read from Jellyfin itself, so a library is picked by
            // name. A page that cannot read them still edits, with the id typed.
            const libraries = await window.ApiClient.ajax({
                type: "GET",
                url: window.ApiClient.getUrl("Library/VirtualFolders"),
                contentType: "application/json",
            })
                .then((response) => response.json())
                .then(home.libraryChoices)
                .catch((error) => {
                    console.error(error);
                    return [];
                });

            // The examples are a convenience: a page that cannot read them still edits.
            const examples = await fetch(window.ApiClient.getUrl(`web/configurationpage?name=${home.EXAMPLES_PAGE}`))
                .then((response) => (response.ok ? response.json() : []))
                .catch((error) => {
                    console.error(error);
                    return [];
                });

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

            // The phone on the right: each row as the app draws it, cards shaped as its
            // orientation says, in the order the list is in.
            const drawPreview = () => {
                const rows = home.preview(sections);
                if (!rows.length) {
                    previewBox.replaceChildren(el("p", "sf-preview-empty", "Nothing set here: the app draws its own home screen."));
                    return;
                }
                previewBox.replaceChildren(...rows.map((one) => {
                    const line = el("div", "sf-prow");
                    line.appendChild(el("div", "sf-prow-title", one.title));
                    const cards = el("div", "sf-prow-cards");
                    const shape = one.orientation === "horizontal" ? "is-wide" : "is-portrait";
                    for (let index = 0; index < (shape === "is-wide" ? 3 : 5); index += 1) {
                        cards.appendChild(el("span", `sf-pcard ${shape}`));
                    }
                    line.appendChild(cards);
                    line.appendChild(el("div", "sf-prow-what", one.filledBy));
                    return line;
                }));
            };

            // The preview is drawn by redraw() after a change to the list, and by edited()
            // after a change inside one card, so it is drawn once either way.
            const touched = () => {
                dirty = true;
                dot.hidden = false;
                summary.textContent = `${said()}, not saved`;
                saveBtn.disabled = false;
                discardBtn.disabled = false;
            };

            const edited = () => {
                touched();
                drawPreview();
            };

            const settled = () => {
                dirty = false;
                dot.hidden = true;
                summary.textContent = said();
                saveBtn.disabled = true;
                discardBtn.disabled = true;
            };

            const payloadOf = (section) => {
                const kind = home.kindOf(section);
                if (!kind) return { kind: null, payload: {} };
                section[kind] = section[kind] ?? {};
                return { kind, payload: section[kind] };
            };

            // The section being dragged, by index. Set by its handle and nothing else, so a
            // file, or text dragged into a field, is left to the browser rather than read as
            // a section. Cleared on the drop as well: redrawing removes the handle, and a
            // handle that left the page does not always hear its dragend.
            let dragging = null;

            // A move made with the arrows is said, as a drag's is, and the arrow that made
            // it keeps the focus once the list is drawn again.
            const placed = (to, title, arrow) => {
                moved.textContent = `${title} moved to place ${to + 1} of ${sections.length}.`;
                if (!arrow) return;
                const card = editor.querySelectorAll(".sf-card")[to];
                const again = card?.querySelector(`[data-move="${arrow}"]`);
                const other = card?.querySelector(`[data-move="${arrow === "up" ? "down" : "up"}"]`);
                (again && !again.disabled ? again : other)?.focus();
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
                    // A library by name. One that is no longer on the server is kept, and
                    // shown as its id, rather than dropped the next time this is saved.
                    case "Library": {
                        const select = el("select", "sf-select");
                        const none = el("option", null, field.empty);
                        none.value = "";
                        select.appendChild(none);
                        for (const option of field.options) {
                            const node = el("option", null, option.label);
                            node.value = option.value;
                            select.appendChild(node);
                        }
                        if (value && !field.options.some((option) => option.value === value)) {
                            const other = el("option", null, `Other (${value})`);
                            other.value = value;
                            select.appendChild(other);
                        }
                        select.value = value ?? "";
                        select.addEventListener("change", () => onChange(select.value === "" ? null : select.value));
                        return select;
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
                    // The arrows name the section they move, so they follow its title.
                    for (const button of [up, down]) button.setAttribute("aria-label", named(button.title));
                    edited();
                });
                header.appendChild(title);

                const pill = el("span", "sf-count", home.summarise(section));
                header.appendChild(pill);

                // Drag by the handle, which is the only thing that drags, so the title and
                // the fields keep selecting text. The arrows stay for the keyboard.
                const grip = el("span", "sf-grip", "⋮⋮");
                grip.title = "Drag to move";
                grip.draggable = true;
                grip.setAttribute("aria-hidden", "true");
                grip.addEventListener("dragstart", (event) => {
                    dragging = index;
                    // A type of its own rather than text, which a field would take on a drop.
                    // Firefox starts no drag without some data set.
                    event.dataTransfer.setData("application/x-streamyfin-section", String(index));
                    event.dataTransfer.effectAllowed = "move";
                    event.dataTransfer.setDragImage(box, 24, 24);
                    // After the browser has taken its picture of the card, or the picture
                    // that follows the pointer is faded as well.
                    requestAnimationFrame(() => box.classList.add("is-dragging"));
                });
                grip.addEventListener("dragend", () => {
                    dragging = null;
                    box.classList.remove("is-dragging");
                });
                box.addEventListener("dragover", (event) => {
                    if (dragging === null) return;
                    event.preventDefault();
                    box.classList.add("is-drop-target");
                });
                box.addEventListener("dragleave", () => box.classList.remove("is-drop-target"));
                box.addEventListener("drop", (event) => {
                    if (dragging === null) return;
                    event.preventDefault();
                    box.classList.remove("is-drop-target");
                    const from = dragging;
                    dragging = null;
                    if (from === index) return;
                    const moving = sections[from]?.title || "The section";
                    sections = home.moveTo(sections, from, index);
                    touched();
                    redraw();
                    placed(index, moving);
                });
                header.insertBefore(grip, title);

                const named = (label) => `${label}: ${section.title || "this section"}`;
                const arrow = (direction, label, by) => {
                    const button = el("button", "sf-btn", direction === "up" ? "↑" : "↓");
                    button.type = "button";
                    button.title = label;
                    button.setAttribute("aria-label", named(label));
                    button.dataset.move = direction;
                    button.disabled = direction === "up" ? index === 0 : index === sections.length - 1;
                    button.addEventListener("click", () => {
                        const moving = section.title || "The section";
                        sections = home.move(sections, index, by);
                        touched();
                        redraw();
                        placed(index + by, moving, direction);
                    });
                    return button;
                };
                const up = arrow("up", "Move up", -1);
                const down = arrow("down", "Move down", 1);

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
                    const node = el("option", null, home.ORIENTATION_LABELS[option] ?? option);
                    node.value = option;
                    orientation.appendChild(node);
                }
                orientation.value = section.orientation ?? home.DEFAULT_ORIENTATION;
                orientation.addEventListener("change", () => {
                    section.orientation = orientation.value;
                    edited();
                });
                body.appendChild(row("Shape of the cards", orientation, "The row scrolls sideways either way"));
                if (kind) body.appendChild(el("p", "sf-desc sf-kind-help", `${home.KIND_LABELS[kind]}: ${home.KIND_HELP[kind]}`));

                for (const field of home.fieldsFor(schema, kind, libraries)) {
                    const node = control(field, payload[field.key], (value) => {
                        if (value === null || value === undefined) delete payload[field.key];
                        else payload[field.key] = value;
                        pill.textContent = home.summarise(section);
                        edited();
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
                drawPreview();
            };

            for (const kind of home.KINDS) {
                const option = el("option", null, home.KIND_LABELS[kind]);
                option.value = kind;
                option.title = home.KIND_HELP[kind];
                kindPicker.appendChild(option);
            }

            for (const [at, example] of examples.entries()) {
                const option = el("option", null, example.name);
                option.value = String(at);
                examplePicker.appendChild(option);
            }
            examplePicker.closest(".sf-examples").hidden = examples.length === 0;
            const sayExample = () => {
                exampleAbout.textContent = examples[Number(examplePicker.value)]?.description ?? "";
            };
            sayExample();
            examplePicker.addEventListener("change", sayExample);

            loadExample.addEventListener("click", async () => {
                const example = examples[Number(examplePicker.value)];
                if (!example) return;
                if (sections.length && !await shared.confirmed(`Replace the ${said()} with “${example.name}”? Nothing is saved until you save.`)) return;
                sections = home.fromExample(example);
                touched();
                redraw();
            });

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
