// The Targeting page is the screen P1.2 to P1.4 never got. Those parts built the whole
// engine — groups with a priority, memberships, per user overrides, resolution from
// server default to group to user — with seven routes and their tests, and no way for an
// administrator to reach any of it short of hand writing HTTP requests.
//
// The settings a level overrides are rendered by the same generated form the Application
// tab uses, with one option flipped: there, every declared setting renders because the
// page answers "what does this server default to". Here the page answers "what does this
// group change", so an editor starts with the keys the level actually carries and the
// admin adds or drops one through json-editor's own property picker.

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

// The server omits a setting a level does not carry, so what arrives is already the
// overrides and nothing else. The null guard is for a level written by hand through the
// API, where a null can arrive spelled out.
const overrides = (settings) => Object.fromEntries(
    Object.entries(settings ?? {}).filter(([, value]) => value !== null && value !== undefined));

// The count in a section's heading says how much of that category this level changes,
// which is the question an admin has on this page. On the Application tab the same
// heading counts the settings the category holds.
const overriddenOf = (seed) => (category, keys) =>
    `${category} (${keys.filter((key) => key in seed).length} of ${keys.length})`;

const EDITOR_OPTIONS = {
    // A level carries only the settings it means to change, so an editor starts with
    // those and no others, and the property picker is what adds or drops one. This is
    // the one place this page differs from the Application tab, and it is the whole
    // difference between "the server's defaults" and "what this group changes".
    required_by_default: false,
    disable_properties: false
};

// Deleting a group takes everyone's membership of it with it, so it asks first. Older
// dashboards reject a cancelled confirmation rather than resolving false, and a
// rejection here would be reported as a failed delete, so both shapes answer false.
const confirmed = (message) => {
    if (window.Dashboard?.confirm) {
        return Promise.resolve(window.Dashboard.confirm(message, "Streamyfin"))
            .then((answer) => answer !== false, () => false);
    }

    return Promise.resolve(window.confirm(message));
};

export default function (view) {
    let users = [];
    let groups = [];
    let editors = [];
    // null when the list is showing, otherwise the level being edited.
    let editing = null;
    let form = null;
    let shared = null;

    const el = (id) => view.querySelector(`#${id}`);
    const show = (id, visible) => el(id).classList.toggle("sf-hidden", !visible);

    const memberIds = () => [...el("sf-members").querySelectorAll("input:checked")]
        .map((input) => input.value);

    const renderMembers = (selected) => {
        const host = el("sf-members");
        host.textContent = "";

        for (const user of users) {
            const label = document.createElement("label");
            const input = document.createElement("input");
            const name = document.createElement("span");

            input.type = "checkbox";
            input.setAttribute("is", "emby-checkbox");
            input.value = user.Id;
            input.checked = selected.includes(user.Id);
            name.textContent = user.Name;

            label.append(input, name);
            host.appendChild(label);
        }
    };

    const renderOverrides = (settings) => {
        form.destroy(editors);

        const seed = form.toForm(overrides(settings));
        editors = form.renderSections(el("sf-overrides"), shared.getJsonSchema(), seed, {
            editorOptions: EDITOR_OPTIONS,
            heading: overriddenOf(seed)
        });
    };

    const renderList = () => {
        const host = el("sf-groups");
        host.textContent = "";

        for (const group of groups) {
            const row = document.createElement("div");
            const name = document.createElement("span");
            const meta = document.createElement("span");

            row.className = "sf-row";
            name.className = "sf-name";
            meta.className = "sf-meta";
            name.textContent = group.name;

            const members = group.userIds?.length ?? 0;
            const settings = Object.keys(overrides(group.settings)).length;
            meta.textContent = `priority ${group.priority} · `
                + `${members} member${members === 1 ? "" : "s"} · `
                + `${settings} setting${settings === 1 ? "" : "s"}`;

            row.append(name, meta);
            row.addEventListener("click", () => openGroup(group));
            host.appendChild(row);
        }

        show("sf-empty", groups.length === 0);
    };

    const showList = () => {
        editing = null;
        form.destroy(editors);
        show("sf-list", true);
        show("sf-editor", false);
    };

    const openGroup = (group) => {
        editing = { kind: "group", group };

        el("sf-editor-title").textContent = group.id ? group.name : "New group";
        el("sf-scope-word").textContent = "group";
        el("sf-group-name").value = group.name ?? "";
        el("sf-group-priority").value = group.priority ?? 0;
        renderMembers(group.userIds ?? []);
        renderOverrides(group.settings);

        show("sf-group-fields", true);
        show("sf-delete", Boolean(group.id));
        show("sf-list", false);
        show("sf-editor", true);
    };

    const openUser = async () => {
        const userId = el("sf-user").value;
        if (!userId) return;

        const user = users.find((candidate) => candidate.Id === userId);
        const stored = await readJson(`users/${userId}/settings`);

        editing = { kind: "user", userId };

        el("sf-editor-title").textContent = user?.Name ?? "User";
        el("sf-scope-word").textContent = "user";
        renderOverrides(stored?.settings);

        show("sf-group-fields", false);
        show("sf-delete", true);
        show("sf-list", false);
        show("sf-editor", true);
    };

    const load = async () => {
        groups = await readJson("groups");
        renderList();
    };

    const save = async () => {
        const { edited } = form.collect(editors);
        const settings = form.toConfig(edited);

        if (editing.kind === "user") {
            await send("PUT", `users/${editing.userId}/settings`, { settings });
            return;
        }

        const name = el("sf-group-name").value.trim();
        if (!name) {
            throw new Error("A group needs a name");
        }

        const priority = Number.parseInt(el("sf-group-priority").value, 10) || 0;
        const members = memberIds();

        if (editing.group.id) {
            await send("PUT", `groups/${editing.group.id}`, { name, priority, settings });
            await send("PUT", `groups/${editing.group.id}/members`, { userIds: members });
        } else {
            await send("POST", "groups", { name, priority, settings, userIds: members });
        }
    };

    const remove = async () => {
        if (editing.kind === "user") {
            if (!await confirmed("Clear the settings targeted at this user?")) return false;
            await send("DELETE", `users/${editing.userId}/settings`);
            return true;
        }

        if (!await confirmed(`Delete the group "${editing.group.name}" and everyone's membership of it?`)) {
            return false;
        }

        await send("DELETE", `groups/${editing.group.id}`);
        return true;
    };

    // A write is followed by a reload rather than by patching the list in place: the
    // server decides the id of a new group and the order the list comes back in. An
    // action that returns false was declined at its confirmation, so the editor stays.
    const commit = (action) => async (event) => {
        event?.preventDefault();
        if (!editing) return;

        window.Dashboard?.showLoadingMsg();

        try {
            if (await action() === false) return;
            await load();
            showList();
        } catch (error) {
            console.error(error);
            window.Dashboard?.alert(error?.message ?? "Streamyfin could not save that. The server log has the reason.");
        } finally {
            window.Dashboard?.hideLoadingMsg();
        }
    };

    view.addEventListener("viewshow", () => {
        import(window.ApiClient.getUrl("web/configurationpage?name=shared.js")).then(async (loaded) => {
            shared = loaded;
            shared.setPage("Targeting");

            form = await import(window.ApiClient.getUrl("web/configurationpage?name=legacy-settings-form.js"));
            await form.loadJsonEditor();

            users = await window.ApiClient.getUsers();
            const picker = el("sf-user");
            picker.textContent = "";
            for (const user of users) {
                const option = document.createElement("option");
                option.value = user.Id;
                option.textContent = user.Name;
                picker.appendChild(option);
            }

            await load();
            showList();

            shared.keyedEventListener(el("sf-new"), "click", (event) => {
                event.preventDefault();
                openGroup({ name: "", priority: 0, settings: {}, userIds: [] });
            });
            shared.keyedEventListener(el("sf-edit-user"), "click", (event) => {
                event.preventDefault();
                openUser().catch((error) => console.error(error));
            });
            shared.keyedEventListener(el("sf-save"), "click", commit(save));
            shared.keyedEventListener(el("sf-delete"), "click", commit(remove));
            shared.keyedEventListener(el("sf-cancel"), "click", (event) => {
                event.preventDefault();
                showList();
            });
        });
    });

    view.addEventListener("viewhide", () => {
        if (form) form.destroy(editors);
    });
}
