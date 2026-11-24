import { describe, expect, test } from "bun:test";
import { describeOwnChoices } from "../../Jellyfin.Plugin.Streamyfin/Pages/own-choices.js";

const labels = { itemAdded: "New movies and episodes", seerrPending: "New requests" };
const morning = { locale: "en-GB", timeZone: "UTC", now: new Date("2026-10-07T01:00:00Z") };

describe("a person's own choices, in one line for Targeting", () => {
    test("nothing chosen says nothing", () => {
        expect(describeOwnChoices(null, labels)).toBeNull();
        expect(describeOwnChoices({ events: [], libraries: [], mutedShows: [], pause: null }, labels)).toBeNull();
    });

    test("what was turned off is named", () => {
        const choices = {
            pause: null,
            events: [{ key: "itemAdded", enabled: true }, { key: "seerrPending", enabled: false }],
            libraries: [{ id: "a", name: "Movies", enabled: true }, { id: "b", name: "Music videos", enabled: false }],
            mutedShows: [{ id: "c", name: "The Bear" }],
        };

        expect(describeOwnChoices(choices, labels))
            .toBe("Their own choices: off for New requests, Music videos, The Bear.");
    });

    // The page lists the server's events, and Seerr's two are only known on the person's side.
    test("Seerr's events are named although the page does not list them", () => {
        const choices = { pause: null, events: [{ key: "seerrRequests", enabled: false }], libraries: [], mutedShows: [] };

        expect(describeOwnChoices(choices, {})).toBe("Their own choices: off for Seerr requests they made.");
    });

    test("a pause comes first, with or without an end", () => {
        const until = { pause: { until: "2026-10-07T05:30:00Z" }, events: [], libraries: [], mutedShows: [] };
        const forever = { pause: {}, events: [], libraries: [], mutedShows: [] };

        expect(describeOwnChoices(until, labels, morning)).toBe("Their own choices: paused until 05:30.");
        expect(describeOwnChoices(forever, labels)).toBe("Their own choices: paused until they turn it back on.");
    });

    // A pause can last a week, and a time alone would read as today.
    test("a pause that ends on another day names the day", () => {
        const later = { pause: { until: "2026-10-10T05:30:00Z" }, events: [], libraries: [], mutedShows: [] };

        const line = describeOwnChoices(later, labels, morning);

        expect(line).toContain("Sat");
        expect(line).toContain("05:30");
    });

    // The server keeps a pause after it ends; it no longer holds anything back.
    test("a pause that has ended says nothing", () => {
        const ended = { pause: { until: "2026-10-06T22:00:00Z" }, events: [], libraries: [], mutedShows: [] };

        expect(describeOwnChoices(ended, labels, morning)).toBeNull();
    });
});
