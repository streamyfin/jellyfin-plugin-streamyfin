// The rows behind the Wording card, minus the browser. The sentences are shaped the way
// the server publishes them at v1/notifications/sentences.

import { describe, expect, test } from "bun:test";
import {
    add,
    asks,
    blank,
    change,
    problems,
    remove,
    summarise,
    toConfig,
} from "../../Jellyfin.Plugin.Streamyfin/Pages/wording-editor.js";

const SENTENCES = [
    { key: "TaskFailedTitle", text: "Scheduled task failed", placeholders: 0, describes: null },
    { key: "TaskFailedWithReason", text: "{0} failed: {1}", placeholders: 2, describes: "0 = Task name, 1 = What the task said went wrong" },
    { key: "UserNowOnline", text: "{0} is now online", placeholders: 1, describes: "0 = Username" },
];

describe("asks", () => {
    test("counts the highest placeholder rather than how many there are", () => {
        expect(asks("")).toBe(0);
        expect(asks("nothing here")).toBe(0);
        expect(asks("{0} failed")).toBe(1);
        expect(asks("{1} then {0}")).toBe(2);
        expect(asks("{0} and {0}")).toBe(1);
        expect(asks("{0:D2} episodes")).toBe(1);
    });
});

describe("rows", () => {
    test("a new row starts from what the sentence says today", () => {
        expect(blank(SENTENCES, "TaskFailedWithReason")).toEqual({
            key: "TaskFailedWithReason",
            locale: "",
            text: "{0} failed: {1}",
        });
    });

    test("the same sentence and language is not listed twice", () => {
        const once = add([], SENTENCES, "TaskFailedTitle");

        expect(once).toHaveLength(1);
        expect(add(once, SENTENCES, "TaskFailedTitle")).toBe(once);
    });

    test("a row can be changed and dropped", () => {
        const list = add([], SENTENCES, "TaskFailedTitle");
        const edited = change(list, 0, { text: "Something broke", locale: "fr" });

        expect(edited[0]).toEqual({ key: "TaskFailedTitle", locale: "fr", text: "Something broke" });
        expect(remove(edited, 0)).toEqual([]);
    });
});

describe("problems", () => {
    test("a sentence this server does not write", () => {
        const found = problems([{ key: "TaskFaild", locale: "", text: "typo" }], SENTENCES);

        expect(found.get(0)).toBe("This server does not write that sentence.");
    });

    test("a placeholder the sentence cannot fill", () => {
        const found = problems([
            { key: "TaskFailedTitle", locale: "", text: "{0} broke" },
            { key: "UserNowOnline", locale: "", text: "{0} and {1}" },
        ], SENTENCES);

        expect(found.get(0)).toBe("That sentence names nothing, so it has no placeholders.");
        expect(found.get(1)).toBe("That sentence names 1 thing(s), so {1} has nothing to show.");
    });

    test("an empty wording says so rather than sending an empty notification", () => {
        expect(problems([{ key: "TaskFailedTitle", locale: "", text: "   " }], SENTENCES).get(0))
            .toBe("Empty, so the plugin's own wording is used.");
    });

    test("two rows for the same sentence and language, where the second never wins", () => {
        const found = problems([
            { key: "TaskFailedTitle", locale: "fr", text: "un" },
            { key: "TaskFailedTitle", locale: "fr", text: "deux" },
        ], SENTENCES);

        expect(found.has(0)).toBe(false);
        expect(found.get(1)).toBe("Another row already covers that sentence and language.");
    });

    test("a row that is fine is not reported", () => {
        expect(problems([{ key: "TaskFailedWithReason", locale: "fr-CA", text: "{0} : {1}" }], SENTENCES).size).toBe(0);
    });
});

describe("what is stored", () => {
    test("blank rows and blank languages are left out", () => {
        expect(toConfig([
            { key: "TaskFailedTitle", locale: "  ", text: "Something broke" },
            { key: "UserNowOnline", locale: "fr", text: "  " },
            { key: "", locale: "", text: "orphan" },
        ])).toEqual([{ key: "TaskFailedTitle", text: "Something broke" }]);
    });

    test("a sentence and language listed twice is stored once", () => {
        // The resolver takes the first match, so storing the second is storing something
        // that never wins. Measured on a throwaway: two identical rows reached the
        // configuration and the page then showed a row nobody could make do anything.
        expect(toConfig([
            { key: "TaskFailedTitle", locale: "fr", text: "premier" },
            { key: "TaskFailedTitle", locale: "fr", text: "second" },
            { key: "TaskFailedTitle", locale: "", text: "every language" },
        ])).toEqual([
            { key: "TaskFailedTitle", locale: "fr", text: "premier" },
            { key: "TaskFailedTitle", text: "every language" },
        ]);
    });

    test("the summary counts what would be stored", () => {
        expect(summarise([])).toBe("The plugin's own wording");
        expect(summarise([{ key: "TaskFailedTitle", locale: "", text: "one" }])).toBe("1 sentence written differently");
        expect(summarise([
            { key: "TaskFailedTitle", locale: "", text: "one" },
            { key: "UserNowOnline", locale: "fr", text: "deux" },
        ])).toBe("2 sentences written differently");
    });
});
