// The home section editor, minus the browser. The shapes in SCHEMA are the ones the
// server actually publishes at config/schema, nullable pairs and refs included, because
// that is what the page reads and getting it wrong draws the wrong control.

import { describe, expect, test } from "bun:test";
import EXAMPLES from "../../Jellyfin.Plugin.Streamyfin/Pages/home-examples.json";
import {
    DEFAULT_ORIENTATION,
    EXAMPLES_PAGE,
    KINDS,
    KIND_LABELS,
    ORIENTATION_LABELS,
    add,
    blank,
    fieldsFor,
    fromExample,
    inOrder,
    kindOf,
    move,
    moveTo,
    orientations,
    preview,
    remove,
    renumber,
    summarise,
} from "../../Jellyfin.Plugin.Streamyfin/Pages/home-editor.js";

const SCHEMA = {
    definitions: {
        SectionOrientation: { type: "string", enum: ["vertical", "horizontal"] },
        BaseItemKind: { type: "string", enum: ["Movie", "Episode", "Series"] },
        ItemFilter: { type: "string", enum: ["IsPlayed", "IsResumable"] },
        ItemSortBy: { type: "string", enum: ["Default", "DateCreated"] },
        SortOrder: { type: "string", enum: ["Ascending", "Descending"] },
        Items: {
            properties: {
                sortBy: { title: "Sort by", type: ["array", "null"], items: { $ref: "#/definitions/ItemSortBy" } },
                genres: { title: "Genres", type: ["array", "null"], items: { type: "string" } },
                parentId: { title: "Parent id", type: ["null", "string"] },
                filters: { title: "Filters", type: ["array", "null"], items: { $ref: "#/definitions/ItemFilter" } },
                includeItemTypes: { title: "Include item types", type: ["array", "null"], items: { $ref: "#/definitions/BaseItemKind" } },
                limit: { title: "Page limit", type: ["integer", "null"] },
            },
        },
        NextUp: {
            properties: {
                parentId: { title: "Parent id", type: ["null", "string"] },
                limit: { title: "Page limit", type: ["integer", "null"] },
                enableResumable: { title: "Enable resumable", type: ["boolean", "null"] },
            },
        },
        Latest: {
            properties: {
                parentId: { title: "Parent id", type: ["null", "string"] },
                limit: { title: "Page limit", type: ["integer", "null"] },
                groupItems: { title: "Group items", type: ["boolean", "null"] },
                includeItemTypes: { title: "Include item types", type: ["array", "null"], items: { $ref: "#/definitions/BaseItemKind" } },
            },
        },
        CustomEndpoint: {
            properties: {
                endpoint: { title: "Endpoint", type: "string" },
                headers: { title: "Request headers", type: ["object", "null"] },
            },
        },
    },
};

describe("fieldsFor", () => {
    test("a value's type decides its control, through the nullable pair the generator writes", () => {
        const fields = Object.fromEntries(fieldsFor(SCHEMA, "items").map((field) => [field.key, field.control]));

        expect(fields).toEqual({
            sortBy: "Choices",
            parentId: "Text",
            filters: "Choices",
            includeItemTypes: "Choices",
            limit: "Number",
        });
    });

    // Home.tsx asks the items query without genres, so offering it read as a filter that
    // never happens.
    test("a field the app does not send to Jellyfin, and nothing else reads, is not offered", () => {
        expect(fieldsFor(SCHEMA, "items").map((field) => field.key)).not.toContain("genres");
    });

    // Next up and Recently added are asked without a library too, but the plugin leaves
    // the section out for anyone who cannot open the one named: hiding the field hid that.
    test("a library the app does not filter by is offered for what it does", () => {
        const library = (kind) => fieldsFor(SCHEMA, kind).find((field) => field.key === "parentId");

        for (const kind of ["nextUp", "latest"]) {
            expect(library(kind).description).toContain("do not see the row");
            expect(library(kind).description).toContain("does not narrow");
        }
        expect(library("items").description).toContain("filled from");
    });

    test("a list of enums carries what there is to choose", () => {
        const kinds = fieldsFor(SCHEMA, "items").find((field) => field.key === "includeItemTypes");

        expect(kinds.options).toEqual(["Movie", "Episode", "Series"]);
    });

    test("the title the server wrote is the label", () => {
        const limit = fieldsFor(SCHEMA, "nextUp").find((field) => field.key === "limit");

        expect(limit.title).toBe("Page limit");
    });

    test("a booleans and an endpoint come out as themselves", () => {
        expect(fieldsFor(SCHEMA, "latest").find((field) => field.key === "groupItems")).toMatchObject({ control: "Toggle" });
        expect(fieldsFor(SCHEMA, "custom")[0]).toMatchObject({ key: "endpoint", control: "Text" });
    });

    test("a map is named rather than drawn, since the Yaml tab is where one is written", () => {
        const headers = fieldsFor(SCHEMA, "custom").find((field) => field.key === "headers");

        expect(headers.control).toBe("Map");
    });

    test("a kind nothing describes has no fields rather than a broken card", () => {
        expect(fieldsFor({}, "items")).toEqual([]);
        expect(fieldsFor(SCHEMA, "nonsense")).toEqual([]);
    });
});

describe("the list", () => {
    const sections = () => [
        { title: "one", kind: "items", items: {} },
        { title: "two", kind: "latest", latest: {} },
        { title: "three", kind: "custom", custom: { endpoint: "/x" } },
    ];

    test("moving a section takes the others with it, and everything is numbered after", () => {
        const moved = move(sections(), 2, -1);

        expect(moved.map((section) => section.title)).toEqual(["one", "three", "two"]);
        expect(moved.map((section) => section.order)).toEqual([0, 1, 2]);
    });

    test("moving past either end changes nothing but the numbering", () => {
        expect(move(sections(), 0, -1).map((section) => section.title)).toEqual(["one", "two", "three"]);
        expect(move(sections(), 2, 1).map((section) => section.title)).toEqual(["one", "two", "three"]);
        expect(move(sections(), 9, -1).map((section) => section.title)).toEqual(["one", "two", "three"]);
    });

    test("adding one puts it last, of the kind asked for, and ready to be stored", () => {
        const added = add(sections(), "nextUp");

        expect(added).toHaveLength(4);
        expect(added[3]).toMatchObject({ kind: "nextUp", order: 3 });
        expect(added[3].nextUp).toEqual({});
    });

    test("a custom section starts with the endpoint it cannot do without", () => {
        expect(blank("custom").custom).toEqual({ endpoint: "" });
    });

    test("removing one renumbers the rest", () => {
        const left = remove(sections(), 0);

        expect(left.map((section) => section.title)).toEqual(["two", "three"]);
        expect(left.map((section) => section.order)).toEqual([0, 1]);
    });

    test("nothing to number is not a failure", () => {
        expect(renumber(null)).toEqual([]);
        expect(move(null, 0, 1)).toEqual([]);
        expect(remove(undefined, 0)).toEqual([]);
        expect(add(null, "items")).toHaveLength(1);
    });
});

describe("inOrder", () => {
    test("a declared number decides, and one that declares nothing keeps its place", () => {
        const sections = [
            { title: "written first" },
            { title: "pinned", order: 0 },
            { title: "written third", order: 1 },
        ];

        expect(inOrder(sections).map((section) => section.title))
            .toEqual(["pinned", "written first", "written third"]);
    });

    test("nothing declared means nothing moves", () => {
        const sections = [{ title: "one" }, { title: "two" }, { title: "three" }];

        expect(inOrder(sections).map((section) => section.title)).toEqual(["one", "two", "three"]);
    });

    test("the same number twice keeps the written order", () => {
        const sections = [{ title: "a", order: 5 }, { title: "b", order: 5 }];

        expect(inOrder(sections).map((section) => section.title)).toEqual(["a", "b"]);
    });

    test("nothing to sort is not a failure", () => {
        expect(inOrder(null)).toEqual([]);
    });
});

describe("what a card says", () => {
    // Named as the kind picker names it, not by the key the configuration stores.
    test("the kind, and what the payload narrows", () => {
        expect(summarise({ kind: "items", items: { limit: 20, includeItemTypes: ["Movie"] } }))
            .toBe("Items you choose · 20 at a time · Movie");
    });

    // The app asks for items and Next up a page of that many at a time and keeps asking
    // while the row scrolls. Only Recently added stops there.
    test("a limit is a page, except on Recently added", () => {
        expect(summarise({ kind: "nextUp", nextUp: { limit: 20 } })).toBe("Next up · 20 at a time");
        expect(summarise({ kind: "latest", latest: { limit: 20 } })).toBe("Recently added · 20 at most");
    });

    test("an endpoint is the useful half of a custom section", () => {
        expect(summarise({ kind: "custom", custom: { endpoint: "/UserItems/Resume" } }))
            .toBe("An endpoint · /UserItems/Resume");
    });

    test("a section written before the kind existed is read by what it carries", () => {
        expect(summarise({ latest: { limit: 10 } })).toBe("Recently added · 10 at most");
    });

    test("a section carrying nothing says so rather than pretending", () => {
        expect(summarise({ title: "empty" })).toBe("No kind, and nothing to imply one");
    });
});

describe("kindOf", () => {
    test("a declared kind is the answer, whatever the payloads", () => {
        expect(kindOf({ kind: "latest", items: {} })).toBe("latest");
    });

    test("one payload implies its kind, two imply nothing, as on the server", () => {
        expect(kindOf({ nextUp: {} })).toBe("nextUp");
        expect(kindOf({ items: {}, latest: {} })).toBeNull();
        expect(kindOf({})).toBeNull();
        expect(kindOf(null)).toBeNull();
    });
});

describe("the kinds", () => {
    test("the four the server knows, in the order the editor offers them", () => {
        expect(KINDS).toEqual(["items", "nextUp", "latest", "custom"]);
    });

    test("the orientations come from the schema, with a fallback that matches it", () => {
        expect(orientations(SCHEMA)).toEqual(["vertical", "horizontal"]);
        expect(orientations({})).toEqual(["vertical", "horizontal"]);
    });
});

// Dragging a section drops it where it lands, rather than a step at a time.
describe("moveTo", () => {
    const three = renumber([{ title: "A" }, { title: "B" }, { title: "C" }]);

    test("puts a section where it was dropped, and renumbers", () => {
        expect(moveTo(three, 2, 0).map((section) => [section.title, section.order])).toEqual([["C", 0], ["A", 1], ["B", 2]]);
        expect(moveTo(three, 0, 2).map((section) => section.title)).toEqual(["B", "C", "A"]);
    });

    test("a drop outside the list, or on itself, changes nothing", () => {
        expect(moveTo(three, 1, 1).map((section) => section.title)).toEqual(["A", "B", "C"]);
        expect(moveTo(three, 0, 5).map((section) => section.title)).toEqual(["A", "B", "C"]);
    });
});

describe("the preview", () => {
    test("draws the sections in the app's order, with the shape and the filling of each", () => {
        const rows = preview([
            { title: "Second", order: 1, kind: "latest", latest: {} },
            { title: "First", order: 0, kind: "nextUp", orientation: "horizontal", nextUp: {} },
        ]);

        expect(rows).toEqual([
            { title: "First", orientation: "horizontal", filledBy: KIND_LABELS.nextUp },
            // A section that says nothing is drawn as the app draws it, in posters.
            { title: "Second", orientation: "vertical", filledBy: KIND_LABELS.latest },
        ]);
    });

    test("a kind this page does not know is named as written rather than left blank", () => {
        expect(preview([{ title: "Later", kind: "watchlist" }])[0].filledBy).toBe("watchlist");
    });

    test("a section with no title or kind still has a row", () => {
        expect(preview([{}])).toEqual([{ title: "Untitled", orientation: DEFAULT_ORIENTATION, filledBy: "Nothing yet" }]);
    });

    // Home.tsx draws `section.orientation || "vertical"`.
    test("the shape a section that names none gets is the app's", () => {
        expect(DEFAULT_ORIENTATION).toBe("vertical");
    });
});

// The server's tests hold each example to the schema a save is held to. What is checked
// here is what the page relies on, and what each one asks the app for.
describe("the examples", () => {
    test("are the file the page asks the plugin for", () => {
        expect(EXAMPLES_PAGE).toBe("home-examples.json");
    });

    // Without it Jellyfin's Next up repeats every episode Continue watching shows above.
    test("a Next up row leaves out what is already being watched", () => {
        const nextUps = EXAMPLES.flatMap((example) => example.sections).filter((section) => section.kind === "nextUp");

        expect(nextUps.length).toBeGreaterThan(0);
        for (const section of nextUps) expect(section.nextUp.enableResumable).toBe(false);
    });

    // The app's own Continue watching asks for films and episodes, not audiobooks.
    test("a resume row asks for what the app's own one does", () => {
        const resumes = EXAMPLES.flatMap((example) => example.sections)
            .filter((section) => section.custom?.endpoint === "/UserItems/Resume");

        expect(resumes.length).toBeGreaterThan(0);
        for (const section of resumes) expect(section.custom.query).toEqual({ includeItemTypes: "Movie,Episode" });
    });

    const KNOWN = {
        includeItemTypes: ["Movie", "Series", "Episode"],
        filters: ["IsUnplayed", "IsPlayed", "IsFavorite", "IsResumable"],
        sortBy: ["DateCreated", "SortName", "PremiereDate", "Random"],
        sortOrder: ["Ascending", "Descending"],
    };

    test("each is a home screen of named sections, each filled by one kind", () => {
        for (const example of EXAMPLES) {
            expect(example.name).toBeTruthy();
            expect(example.description).toBeTruthy();
            expect(example.sections.length).toBeGreaterThan(1);
            for (const section of example.sections) {
                expect(section.title).toBeTruthy();
                expect(KINDS).toContain(section.kind);
                expect(Object.keys(ORIENTATION_LABELS)).toContain(section.orientation);
                expect(section[section.kind]).toBeDefined();
                for (const [key, values] of Object.entries(section[section.kind])) {
                    if (KNOWN[key]) for (const value of values) expect(KNOWN[key]).toContain(value);
                }
            }
        }
    });

    test("loading one numbers its sections and leaves the example as it was", () => {
        const loaded = fromExample(EXAMPLES[0]);
        loaded[0].title = "Changed";

        expect(loaded.map((section) => section.order)).toEqual(EXAMPLES[0].sections.map((_, index) => index));
        expect(EXAMPLES[0].sections[0].title).not.toBe("Changed");
    });
});
