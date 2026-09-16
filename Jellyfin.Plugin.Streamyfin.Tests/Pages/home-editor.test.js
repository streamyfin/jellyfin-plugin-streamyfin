// The home section editor, minus the browser. The shapes in SCHEMA are the ones the
// server actually publishes at config/schema, nullable pairs and refs included, because
// that is what the page reads and getting it wrong draws the wrong control.

import { describe, expect, test } from "bun:test";
import {
    KINDS,
    add,
    blank,
    fieldsFor,
    inOrder,
    move,
    orientations,
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
        Latest: { properties: { groupItems: { title: "Group items", type: ["boolean", "null"] } } },
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
            genres: "List",
            parentId: "Text",
            filters: "Choices",
            includeItemTypes: "Choices",
            limit: "Number",
        });
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
        expect(fieldsFor(SCHEMA, "latest")[0]).toMatchObject({ key: "groupItems", control: "Toggle" });
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
    test("the kind, and what the payload narrows", () => {
        expect(summarise({ kind: "items", items: { limit: 20, includeItemTypes: ["Movie"] } }))
            .toBe("items · 20 at most · Movie");
    });

    test("an endpoint is the useful half of a custom section", () => {
        expect(summarise({ kind: "custom", custom: { endpoint: "/UserItems/Resume" } }))
            .toBe("custom · /UserItems/Resume");
    });

    test("a section written before the kind existed is read by what it carries", () => {
        expect(summarise({ latest: { limit: 10 } })).toBe("latest · 10 at most");
    });

    test("a section carrying nothing says so rather than pretending", () => {
        expect(summarise({ title: "empty" })).toBe("No kind, and nothing to imply one");
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
