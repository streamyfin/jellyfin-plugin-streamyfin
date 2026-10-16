// What a home section is, and what editing one means, without a DOM.
//
// The Home tab is hand written: the sections are not settings, they are a list whose
// order matters and whose shape depends on what fills each one. What can be decided
// without a browser is decided here, so it can be tested: which fields a kind has, what
// moving a section does to the others, and what adding one starts from.
//
// The fields come from the schema the server already publishes for the Yaml editor,
// rather than a second list written here that would drift the first time a payload
// gained a field.

/// The four kinds, in the order the editor offers them.
export const KINDS = ["items", "nextUp", "latest", "custom"];

/// What each kind fills its row with, said for an administrator rather than a developer.
export const KIND_LABELS = {
    items: "Items you choose",
    nextUp: "Next up",
    latest: "Recently added",
    custom: "An endpoint",
};

export const KIND_HELP = {
    items: "Movies, shows or episodes picked by type, filters and an order, from every library or one.",
    nextUp: "The next episode of each show somebody is watching.",
    latest: "What arrived most recently, newest first.",
    custom: "Whatever an address of the server answers, such as the plugin's My media and For you rows.",
};

/// Every row scrolls sideways in the app; the orientation is the shape of its cards.
export const ORIENTATION_LABELS = {
    vertical: "Posters (portrait)",
    horizontal: "Wide (16:9)",
};

/// The shape the app gives a section that names none.
export const DEFAULT_ORIENTATION = "vertical";

// A field the payload types declare that the app does not send to Jellyfin: Home.tsx asks
// the items query without genres, so offering it read as a filter that never happens.
const NOT_OFFERED = {
    items: ["genres"],
};

// Next up and Recently added are asked without a library as well, but the plugin still
// reads one named on them: it leaves the section out for anyone who cannot open that
// library. The field is offered with what it really does, since hiding it hid a rule the
// server applies. The schema has nothing to say about any of the three.
const VISIBILITY_ONLY = "People who cannot open this library do not see the row. The app does not narrow the row to it.";
const DESCRIBED = {
    items: { parentId: "The library the row is filled from. People who cannot open it do not see the row." },
    nextUp: { parentId: VISIBILITY_ONLY },
    latest: { parentId: VISIBILITY_ONLY },
};

// What leaving the library out means, which is not the same for the three.
const NO_LIBRARY = {
    items: "Every library",
    nextUp: "None, everyone sees the row",
    latest: "None, everyone sees the row",
};

/// The server's libraries as the choices a library field offers: Jellyfin's virtual
/// folders, by name, standing for their id.
export const libraryChoices = (folders) => (Array.isArray(folders) ? folders : [])
    .filter((folder) => folder?.ItemId && folder?.Name)
    .map((folder) => ({ value: folder.ItemId, label: folder.Name }));

const PAYLOAD_TYPES = {
    items: "Items",
    nextUp: "NextUp",
    latest: "Latest",
    custom: "CustomEndpoint",
};

const definitions = (schema) => schema?.definitions ?? schema?.$defs ?? {};

const resolve = (schema, node) => {
    if (!node) return null;
    const reference = node.$ref;
    if (!reference) return node;
    const name = reference.split("/").pop();
    return definitions(schema)[name] ?? null;
};

// A property can be a type, a reference to one, or a nullable pair of both, which is how
// the generator writes `int?`. The editor needs the one that is not "null".
const shapeOf = (schema, property) => {
    if (!property) return null;
    if (Array.isArray(property.oneOf)) {
        const real = property.oneOf.find((option) => option.$ref || (option.type && option.type !== "null"));
        return resolve(schema, real) ?? null;
    }
    if (property.$ref) return resolve(schema, property);
    return property;
};

const controlFor = (schema, property) => {
    const shape = shapeOf(schema, property);
    if (!shape) return null;

    const type = Array.isArray(shape.type) ? shape.type.find((t) => t !== "null") : shape.type;

    if (type === "boolean") return { control: "Toggle" };
    if (type === "integer" || type === "number") return { control: "Number" };
    if (type === "array") {
        const item = shapeOf(schema, shape.items);
        if (item?.enum?.length) return { control: "Choices", options: item.enum };
        return { control: "List" };
    }
    if (type === "object") return { control: "Map" };
    if (shape.enum?.length) return { control: "Select", options: shape.enum };
    if (type === "string") return { control: "Text" };

    return null;
};

/// The fields a kind's payload has, in the order the payload declares them. With the
/// server's libraries, a library is picked by name rather than typed as an id.
export const fieldsFor = (schema, kind, libraries = []) => {
    const payload = definitions(schema)[PAYLOAD_TYPES[kind]];
    if (!payload?.properties) return [];

    return Object.entries(payload.properties).flatMap(([key, property]) => {
        if (NOT_OFFERED[kind]?.includes(key)) return [];
        const control = key === "parentId" && libraries.length
            ? { control: "Library", options: libraries, empty: NO_LIBRARY[kind] ?? "None" }
            : controlFor(schema, property);
        if (!control) return [];
        return [{
            key,
            title: control.control === "Library" ? "Library" : property.title ?? key,
            description: DESCRIBED[kind]?.[key] ?? property.description ?? null,
            ...control,
        }];
    });
};

/// The orientations a section can take, read from the schema rather than repeated here.
export const orientations = (schema) => definitions(schema).SectionOrientation?.enum ?? ["vertical", "horizontal"];

/// A section of this kind, with nothing filled in but what it needs to exist.
export const blank = (kind) => ({
    title: "New section",
    kind,
    orientation: "horizontal",
    [kind]: kind === "custom" ? { endpoint: "" } : {},
});

/// The sections in the order the app draws them, which is the order the server sorts
/// them into: by the number a section declares, and by where it is written when it
/// declares none. The stored file is not in that order, so a tab that read the array as
/// it comes would show a different home screen from the one the app draws.
export const inOrder = (sections) => (sections ?? [])
    .map((section, index) => ({ section, index }))
    .sort((left, right) => {
        const byOrder = (left.section?.order ?? left.index) - (right.section?.order ?? right.index);
        if (byOrder !== 0) return byOrder;
        // A number that was written wins against one taken from a position, the way the
        // server breaks the same tie.
        const declared = (left.section?.order === undefined || left.section?.order === null ? 1 : 0)
            - (right.section?.order === undefined || right.section?.order === null ? 1 : 0);
        return declared !== 0 ? declared : left.index - right.index;
    })
    .map((pair) => pair.section);

/// The same sections, numbered from zero in the order they are in.
export const renumber = (sections) => (sections ?? []).map((section, index) => ({ ...section, order: index }));

/// The sections with the one at `from` put at `to`, as a drag drops it, renumbered.
export const moveTo = (sections, from, to) => {
    const list = [...(sections ?? [])];
    if (from < 0 || from >= list.length || to < 0 || to >= list.length || from === to) return renumber(list);

    const [moved] = list.splice(from, 1);
    list.splice(to, 0, moved);
    return renumber(list);
};

/// The sections with the one at `from` moved by `by` places, renumbered.
export const move = (sections, from, by) => moveTo(sections, from, from + by);

/// The sections with one of this kind added at the end, renumbered.
export const add = (sections, kind) => renumber([...(sections ?? []), blank(kind)]);

/// The sections without the one at this index, renumbered.
export const remove = (sections, index) => renumber((sections ?? []).filter((_, at) => at !== index));

/// The kind a section declares, or the one its payload implies, read the way the server's
/// Sections.KindOf reads it: two payloads and no kind imply nothing.
export const kindOf = (section) => {
    if (section?.kind) return section.kind;
    const carried = KINDS.filter((candidate) => section?.[candidate]);
    return carried.length === 1 ? carried[0] : null;
};

/// What a section's card says under its title: the kind, and what the payload narrows.
export const summarise = (section) => {
    const kind = kindOf(section);
    if (!kind) return "No kind, and nothing to imply one";

    const payload = section?.[kind] ?? {};
    const said = [];

    // Only Recently added stops at its limit. The app asks for the others a page of that
    // many at a time, and asks again while the row is scrolled.
    if (payload.limit) said.push(kind === "latest" ? `${payload.limit} at most` : `${payload.limit} at a time`);
    if (payload.includeItemTypes?.length) said.push(payload.includeItemTypes.join(", "));
    if (payload.filters?.length) said.push(payload.filters.join(", "));
    if (payload.endpoint) said.push(payload.endpoint);

    const named = KIND_LABELS[kind] ?? kind;
    return said.length ? `${named} · ${said.join(" · ")}` : named;
};

/// What the preview draws for each section: its title, the shape of its cards, and what
/// fills it, in the order the app draws them.
export const preview = (sections) => inOrder(sections).map((section) => {
    const kind = kindOf(section);
    return {
        title: section?.title || "Untitled",
        orientation: section?.orientation === "horizontal" ? "horizontal" : DEFAULT_ORIENTATION,
        // A kind this page does not know is named as it is written, as summarise() does.
        filledBy: kind ? (KIND_LABELS[kind] ?? kind) : "Nothing yet",
    };
});

/// Where the Home tab reads the home screens it offers to start from. They live in a file
/// of their own so the server's tests can hold each one to the schema a save is held to.
export const EXAMPLES_PAGE = "home-examples.json";

/// An example's sections, numbered and copied so editing them leaves the example alone.
export const fromExample = (example) => renumber(structuredClone(example?.sections ?? []));
