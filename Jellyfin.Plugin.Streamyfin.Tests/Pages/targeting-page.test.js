import { describe, expect, test } from "bun:test";
import { refusal, waitInput } from "../../Jellyfin.Plugin.Streamyfin/Pages/Targeting/index.js";

// ApiClient rejects a failed request with the response itself, so this is what the page
// is handed when the server refuses a save.
const refused = (status, body) => new Response(body, { status, headers: { "Content-Type": "application/json" } });

describe("what the page says when a save is refused", () => {
    test("the server's sentence, written as a JSON string", async () => {
        const body = JSON.stringify('A group named "Family" exists already.');

        expect(await refusal(refused(400, body))).toBe('A group named "Family" exists already.');
    });

    test("a sentence whose quotes the server escaped as unicode", async () => {
        expect(await refusal(refused(400, '"A group named \\u0022Family\\u0022 exists already."')))
            .toBe('A group named "Family" exists already.');
    });

    test("a body that is not a sentence falls back", async () => {
        const problem = JSON.stringify({ title: "One or more validation errors occurred.", status: 400 });

        expect(await refusal(refused(400, problem))).toBe("Streamyfin could not save that. The server log has the reason.");
        expect(await refusal(refused(500, "Error processing request."))).toBe("Streamyfin could not save that. The server log has the reason.");
    });

    test("an error thrown in the page keeps its own message", async () => {
        expect(await refusal(new Error("The group has no name."))).toBe("The group has no name.");
    });
});

describe("the wait a level sets", () => {
    // Only the events that keep two of the same apart read a wait: one set on a new item,
    // an awaited title or a locked account changed nothing.
    test("is not offered on an event that never waits", () => {
        expect(waitInput({ key: "itemAdded", title: "Item added", wait: null }, {})).toBeNull();
        expect(waitInput({ key: "awaitedTitle", title: "Awaited title" }, {})).toBeNull();
    });

    test("is offered on an event that waits, holding what the level said", () => {
        const box = waitInput({ key: "signInFailed", title: "Failed sign in", wait: 300 }, { recentEventThreshold: 60 });

        expect([box.type, box.min, box.value]).toEqual(["number", "0", "60"]);
    });
});
