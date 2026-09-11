// The Targeting page: what a group, or one user, gets instead of what everyone gets.
// P1.2 to P1.4 built the whole engine, groups with a priority, memberships, per user
// overrides and the resolution between them, and left it with no screen at all.
//
// It draws with the same renderer as the Application tab, in its overrides mode: a level
// lists only the settings it changes, each saying what it falls through to, and the way
// to stop overriding one is to drop it rather than to set it free. P3.3 rendered this
// through json-editor, whose property picker never actually added a setting, so an
// override could be read and changed but never created. That is what this replaces.

const TERSE_KEY = "streamyfin.admin.descriptions";

const url = (path) => window.ApiClient.getUrl(`streamyfin/v1/${path}`);

const readJson = (path) =>
    window.ApiClient.ajax({ type: "GET", url: url(path), contentType: "application/json" })
        .then((response) => response.json());

const send = (type, path, body) =>
    window.ApiClient.ajax({
        type,
        url: url(path),
        data: body === undefined ? undefined : JSON.stringify(body),
        contentType: "application/json"
    });

const readTerse = () => {
    try {
        return window.localStorage.getItem(TERSE_KEY) === "off";
    } catch {
        return false;
    }
};

const writeTerse = (terse) => {
    try {
        window.localStorage.setItem(TERSE_KEY, terse ? "off" : "on");
    } catch {
        // A dashboard that blocks storage just forgets the choice.
    }
};

// Deleting a group takes everyone's membership of it with it, so it asks first. Older
// dashboards reject a cancelled confirmation rather than resolving false, and a rejection
// here would be reported as a failed delete, so both shapes answer false.
const confirmed = (message) => {
    if (window.Dashboard?.confirm) {
        return Promise.resolve(window.Dashboard.confirm(message, "Streamyfin")).then(
            (answer) => answer !== false,
            () => false);
    }

    return Promise.resolve(window.confirm(message));
};

export default function (view) {
    let renderer = null;
    let shared = null;
    let form = null;
    let fields = [];
    let users = [];
    let groups = [];
    // The level being edited: a group, or one user. Never null once the page has loaded,
    // since a server with no group opens on a new one.
    let level = null;
    let showing = null;

    const el = (id) => view.querySelector(`#${id}`);
    const listen = (id, type, handler) => el(id).addEventListener(type, handler, { signal: showing.signal });

    const setStatus = (text, error = false) => {
        const status = el("sf-status");
        status.textContent = text ?? "";
        status.hidden = !text;
        status.classList.toggle("is-error", error);
        el("sf-level").hidden = Boolean(text);
    };

    const isNewGroup = () => level.kind === "group" && !level.group.id;

    // What this level falls through to. A group falls through to the server, a user to
    // every group they belong to as well, which is the order the server resolves in.
    const inheritedFor = () => {
        const app = shared.getDefaultConfig()?.settings ?? {};
        const server = shared.getConfig()?.settings ?? {};

        if (level.kind === "group") {
            return renderer.inherited(app, server);
        }

        const theirs = renderer.groupsFor(groups, level.userId).map((group) => group.settings ?? {});
        return renderer.inherited(app, server, ...theirs);
    };

    const memberIds = () => [...el("sf-members").querySelectorAll("input:checked")].map((input) => input.value);

    const renderMembers = (selected) => {
        const host = el("sf-members");
        host.textContent = "";

        for (const user of users) {
            const label = document.createElement("label");
            const input = document.createElement("input");

            label.className = "sf-member";
            input.type = "checkbox";
            input.value = user.Id;
            input.checked = selected.includes(user.Id);
            label.classList.toggle("is-in", input.checked);
            input.addEventListener("change", () => {
                label.classList.toggle("is-in", input.checked);
                countMembers();
                updateDock();
            }, { signal: showing.signal });

            label.append(input, document.createTextNode(user.Name));
            host.appendChild(label);
        }

        countMembers();
    };

    const countMembers = () => {
        const n = memberIds().length;
        el("sf-members-count").textContent = n === 0
            ? "No members yet"
            : `${n} member${n === 1 ? "" : "s"} of ${users.length}`;
    };

    const filterMembers = (text) => {
        const q = text.trim().toLowerCase();
        for (const label of el("sf-members").children) {
            label.hidden = Boolean(q) && !label.textContent.toLowerCase().includes(q);
        }
    };

    const renderScope = () => {
        const host = el("sf-groups");
        host.textContent = "";

        for (const group of groups) {
            const button = document.createElement("button");
            const count = document.createElement("span");

            button.type = "button";
            button.className = "sf-lv";
            button.setAttribute("role", "tab");
            button.dataset.groupId = group.id;
            button.appendChild(document.createTextNode(group.name));

            count.className = "sf-n";
            count.textContent = String(Object.keys(group.settings ?? {}).length);
            button.appendChild(count);

            button.addEventListener("click", () => openGroup(group), { signal: showing.signal });
            host.appendChild(button);
        }

        markCurrent();
    };

    const markCurrent = () => {
        const groupId = level?.kind === "group" ? level.group.id : null;
        for (const button of el("sf-groups").children) {
            button.setAttribute("aria-current", String(button.dataset.groupId === groupId));
        }
        el("sf-user").parentElement.classList.toggle("is-current", level?.kind === "user");
    };

    const renderAdder = () => {
        const picker = el("sf-add");
        const candidates = form.candidates();
        picker.textContent = "";

        for (const candidate of candidates) {
            const option = document.createElement("option");
            option.value = candidate.key;
            option.textContent = `${candidate.title} · ${candidate.category}`;
            picker.appendChild(option);
        }

        picker.disabled = candidates.length === 0;
        el("sf-add-go").disabled = candidates.length === 0;
        el("sf-blank").hidden = form.overridden().length > 0;
        el("sf-overrides-count").textContent = `${form.overridden().length} of ${fields.length}`;
    };

    const updateDock = () => {
        const dirty = form.dirtyCount() + changedFields();
        const invalid = form.invalid().length;
        const dock = el("sf-dock");
        const parts = [];

        if (dirty) parts.push(`${dirty} unsaved`);
        if (invalid) parts.push(`${invalid} need${invalid === 1 ? "s" : ""} a value`);

        el("sf-dock-summary").textContent = parts.join(" · ") || "Nothing to save";
        dock.classList.toggle("is-clean", dirty === 0);
        // A new group is always worth saving, even before anything is typed into it.
        dock.hidden = dirty === 0 && invalid === 0 && !isNewGroup();
        el("sf-discard").disabled = dirty === 0;
        el("sf-save").disabled = invalid > 0 || (dirty === 0 && !isNewGroup());
        renderAdder();
    };

    // The group's own fields are outside the form, so the dock counts them itself.
    const changedFields = () => {
        if (level.kind !== "group") return 0;

        const group = level.group;
        const members = memberIds();
        const before = group.userIds ?? [];
        let changed = 0;

        if (el("sf-group-name").value.trim() !== (group.name ?? "")) changed += 1;
        if (Number.parseInt(el("sf-group-priority").value, 10) !== (group.priority ?? 0)) changed += 1;
        if (members.length !== before.length || members.some((id) => !before.includes(id))) changed += 1;

        return changed;
    };

    // The same switch as the Application tab, sharing its remembered choice: an
    // administrator who turned the help text off did so for the settings, not for a tab.
    const wireTerse = () => {
        const toggle = el("sf-terse");
        const show = (on) => {
            toggle.setAttribute("aria-pressed", String(on));
            toggle.querySelector(".sf-pip").textContent = on ? "ON" : "OFF";
            form.setTerse(!on);
        };

        show(!readTerse());
        listen("sf-terse", "click", () => {
            const on = toggle.getAttribute("aria-pressed") !== "true";
            writeTerse(!on);
            show(on);
        });
    };

    const draw = (values) => {
        form?.destroy();
        form = renderer.createForm(el("sf-editor"), {
            fields,
            values,
            defaults: inheritedFor(),
            cultures: level.cultures ?? [],
            terse: readTerse(),
            mode: "overrides",
        });
        form.onChange(updateDock);
        el("sf-find").value = "";
        el("sf-terse").setAttribute("aria-pressed", String(!readTerse()));
        updateDock();
    };

    const openGroup = (group) => {
        level = { kind: "group", group, cultures: level?.cultures };
        el("sf-level-title").textContent = group.id ? group.name : "New group";
        el("sf-group-fields").hidden = false;
        el("sf-level-help").hidden = false;
        el("sf-people").hidden = false;
        el("sf-delete").hidden = !group.id;
        el("sf-group-name").value = group.name ?? "";
        el("sf-group-priority").value = group.priority ?? 0;
        renderMembers(group.userIds ?? []);
        el("sf-people").open = !group.id;
        el("sf-member-find").value = "";
        filterMembers("");
        draw(group.settings ?? {});
        markCurrent();
    };

    const openUser = async (userId) => {
        const stored = await readJson(`users/${userId}/settings`);
        const user = users.find((candidate) => candidate.Id === userId);

        level = { kind: "user", userId, cultures: level?.cultures };
        el("sf-level-title").textContent = user?.Name ?? "This user";
        el("sf-group-fields").hidden = true;
        el("sf-level-help").hidden = true;
        el("sf-people").hidden = true;
        el("sf-delete").hidden = false;
        draw(stored?.settings ?? {});
        markCurrent();
    };

    const save = async () => {
        const settings = form.toSettings();

        if (level.kind === "user") {
            await send("PUT", `users/${level.userId}/settings`, { settings });
            return;
        }

        const name = el("sf-group-name").value.trim();
        if (!name) throw new Error("A group needs a name before it can be saved.");

        const priority = Number.parseInt(el("sf-group-priority").value, 10) || 0;
        const userIds = memberIds();

        if (level.group.id) {
            await send("PUT", `groups/${level.group.id}`, { name, priority, settings });
            await send("PUT", `groups/${level.group.id}/members`, { userIds });
        } else {
            await send("POST", "groups", { name, priority, settings, userIds });
        }
    };

    const remove = async () => {
        if (level.kind === "user") {
            if (!await confirmed("Clear every setting aimed at this user?")) return false;
            await send("DELETE", `users/${level.userId}/settings`);
            return true;
        }

        if (!await confirmed(`Delete the group "${level.group.name}" and everyone's membership of it?`)) return false;
        await send("DELETE", `groups/${level.group.id}`);
        return true;
    };

    // A write is followed by a reload rather than by patching the list in place: the
    // server decides the id of a new group and the order the list comes back in. An
    // action that answers false was declined at its confirmation, so nothing moves.
    const commit = (action) => async () => {
        window.Dashboard?.showLoadingMsg();

        try {
            if (await action() === false) return;

            const was = level.kind === "user" ? level.userId : el("sf-group-name").value.trim();
            groups = await readJson("groups");
            renderScope();

            if (level.kind === "user") {
                await openUser(was);
            } else {
                openGroup(groups.find((group) => group.name === was) ?? groups[0] ?? blankGroup());
            }
        } catch (error) {
            console.error(error);
            window.Dashboard?.alert(error?.message ?? "Streamyfin could not save that. The server log has the reason.");
        } finally {
            window.Dashboard?.hideLoadingMsg();
        }
    };

    const blankGroup = () => ({ name: "", priority: 0, settings: {}, userIds: [] });

    const load = async (loaded) => {
        setStatus("Loading the groups…");

        const [form_, allGroups, cultures] = await Promise.all([
            readJson("settings/form"),
            readJson("groups"),
            window.ApiClient.getCultures().catch(() => []),
        ]);

        // Drawing without the server's own settings would claim every level inherits the
        // app's default, which is what the whole page is about. shared.js logs and
        // swallows a failed fetch, so the page checks rather than guessing.
        if (!shared.getConfig() || !shared.getDefaultConfig()) {
            throw new Error("The server configuration did not load");
        }

        fields = form_;
        groups = allGroups;
        users = await window.ApiClient.getUsers();
        level = { cultures };

        const picker = el("sf-user");
        picker.textContent = "";
        for (const user of users) {
            const option = document.createElement("option");
            option.value = user.Id;
            option.textContent = user.Name;
            picker.appendChild(option);
        }

        el("sf-meta").textContent = `${groups.length} group${groups.length === 1 ? "" : "s"} · ${users.length} users`;
        el("sf-app").dataset.sfTheme = renderer.themeFromBackground(
            window.getComputedStyle(document.documentElement).backgroundColor);

        renderScope();
        setStatus(null);
        openGroup(groups[0] ?? blankGroup());

        listen("sf-new", "click", () => openGroup(blankGroup()));
        listen("sf-user", "change", (event) => openUser(event.target.value).catch((error) => console.error(error)));
        listen("sf-add-go", "click", () => {
            const key = el("sf-add").value;
            if (key) form.set(key, "suggested");
        });
        listen("sf-find", "input", (event) => form.search(event.target.value));
        wireTerse();
        listen("sf-member-find", "input", (event) => filterMembers(event.target.value));
        listen("sf-save", "click", commit(save));
        listen("sf-delete", "click", commit(remove));
        listen("sf-discard", "click", () => {
            form.reset();
            if (level.kind === "group") {
                el("sf-group-name").value = level.group.name ?? "";
                el("sf-group-priority").value = level.group.priority ?? 0;
                renderMembers(level.group.userIds ?? []);
            }
            updateDock();
        });
    };

    view.addEventListener("viewshow", () => {
        showing?.abort();
        // This showing's own controller. `showing` is replaced by the next one, so a run
        // that is still awaiting its imports has to ask the controller it started with
        // whether it was abandoned, not whichever one is current by then.
        const mine = new AbortController();
        showing = mine;

        const failed = (error) => {
            console.error(error);
            setStatus("The targeting screen could not be loaded. The server log has the reason.", true);
        };

        import(window.ApiClient.getUrl("web/configurationpage?name=shared.js")).then(async (loaded) => {
            renderer = await import(window.ApiClient.getUrl("web/configurationpage?name=settings-form.js"));

            if (mine.signal.aborted) return;

            shared = loaded;
            shared.setPage("Targeting");

            try {
                await load(loaded);
            } catch (error) {
                failed(error);
            }
        }).catch(failed);
    });

    view.addEventListener("viewhide", () => {
        showing?.abort();
        form?.destroy();
        form = null;
        el("sf-dock").hidden = true;
    });
}
