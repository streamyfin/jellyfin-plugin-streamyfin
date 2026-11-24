import { describe, expect, test } from "bun:test";
import { eventSaid, refusal, setCount } from "../../Jellyfin.Plugin.Streamyfin/Pages/Targeting/index.js";

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

describe("what a level says about an event", () => {
    const rowWith = ({ state = "", keptWait } = {}) => {
        const row = document.createElement("div");
        const select = document.createElement("select");
        select.className = "sf-event-state";
        for (const value of ["", "on", "off"]) {
            const option = document.createElement("option");
            option.value = value;
            select.appendChild(option);
        }
        select.value = state;
        row.appendChild(select);
        if (keptWait !== undefined) row.dataset.keptWait = keptWait;
        return row;
    };

    // A wait on a level is read by nothing, so the tab offers no box for it. One a level
    // stored before is kept as it was, so saving something else does not drop it.
    test("keeps a wait the level stored, with no box for it", () => {
        expect(eventSaid(rowWith({ state: "on", keptWait: "30" }))).toEqual({ enabled: true, recentEventThreshold: 30 });
        expect(eventSaid(rowWith({ keptWait: "0" }))).toEqual({ recentEventThreshold: 0 });
    });

    test("says only its state where no wait was stored", () => {
        expect(eventSaid(rowWith({ state: "off" }))).toEqual({ enabled: false });
        expect(eventSaid(rowWith())).toEqual({});
    });
});

describe("how many events a level sets", () => {
    // The card's header counts what the rows show. An event whose only entry is a wait the
    // level kept shows "Follows the level above", so counting it said "2 set" over two rows
    // that showed nothing set.
    test("counts the events a level turns on or off, not a wait it kept", () => {
        expect(setCount({ itemAdded: { recentEventThreshold: 30 }, signInFailed: { recentEventThreshold: 60 } })).toBe(0);
        expect(setCount({ itemAdded: { enabled: true, recentEventThreshold: 30 }, sessionStarted: { enabled: false } })).toBe(2);
    });
});
