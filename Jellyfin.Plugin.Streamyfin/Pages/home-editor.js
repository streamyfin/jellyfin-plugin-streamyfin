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

/// The fields a kind's payload has, in the order the payload declares them.
export const fieldsFor = (schema, kind) => {
    const payload = definitions(schema)[PAYLOAD_TYPES[kind]];
    if (!payload?.properties) return [];

    return Object.entries(payload.properties).flatMap(([key, property]) => {
        const control = controlFor(schema, property);
        if (!control) return [];
        return [{
            key,
            title: property.title ?? key,
            description: property.description ?? null,
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

/// The sections with the one at `from` moved by `by` places, renumbered.
export const move = (sections, from, by) => {
    const list = [...(sections ?? [])];
    const to = from + by;
    if (from < 0 || from >= list.length || to < 0 || to >= list.length) return renumber(list);

    const [moved] = list.splice(from, 1);
    list.splice(to, 0, moved);
    return renumber(list);
};

/// The sections with one of this kind added at the end, renumbered.
export const add = (sections, kind) => renumber([...(sections ?? []), blank(kind)]);

/// The sections without the one at this index, renumbered.
export const remove = (sections, index) => renumber((sections ?? []).filter((_, at) => at !== index));

/// What a section's card says under its title: the kind, and what the payload narrows.
export const summarise = (section) => {
    const kind = section?.kind ?? KINDS.find((candidate) => section?.[candidate]) ?? null;
    if (!kind) return "No kind, and nothing to imply one";

    const payload = section?.[kind] ?? {};
    const said = [];

    if (payload.limit) said.push(`${payload.limit} at most`);
    if (payload.includeItemTypes?.length) said.push(payload.includeItemTypes.join(", "));
    if (payload.filters?.length) said.push(payload.filters.join(", "));
    if (payload.endpoint) said.push(payload.endpoint);

    return said.length ? `${kind} · ${said.join(" · ")}` : kind;
};
