// The measure's comment on a pull request: one, carrying the run it came from, never replaced
// by an older run's, and saying so when the measure could not judge.

import { describe, expect, test } from "bun:test";

const { bodyOf, plan, runOf } = require("../../scripts/measure-comment.js");

const bot = (id, body) => ({ id, body, user: { type: "Bot" } });

describe("the run a comment came from", () => {
    test("is read from its marker", () => {
        expect(runOf("<!-- streamyfin-measure run=42 attempt=2 -->\n## Unit tests")).toEqual({ run: 42, attempt: 2 });
    });

    test("is the oldest there is for the bare marker", () => {
        expect(runOf("<!-- streamyfin-measure -->\n## Unit tests")).toEqual({ run: 0, attempt: 0 });
    });

    test("is none for any other comment", () => {
        expect(runOf("<!-- streamyfin-build -->")).toBeNull();
        expect(runOf(undefined)).toBeNull();
    });
});

describe("what a run does with its comment", () => {
    test("writes one when there is none", () => {
        expect(plan({ comments: [{ id: 1, body: "looks good", user: { type: "User" } }], run: 10, attempt: 1 })).toEqual({ action: "create" });
    });

    test("replaces the one an older run wrote, or an earlier attempt of the same run", () => {
        expect(plan({ comments: [bot(5, "<!-- streamyfin-measure run=9 attempt=3 -->")], run: 10, attempt: 1 })).toEqual({ action: "update", id: 5 });
        expect(plan({ comments: [bot(5, "<!-- streamyfin-measure run=10 attempt=1 -->")], run: 10, attempt: 2 })).toEqual({ action: "update", id: 5 });
    });

    test("leaves alone the one a newer run wrote", () => {
        expect(plan({ comments: [bot(5, "<!-- streamyfin-measure run=11 attempt=1 -->")], run: 10, attempt: 4 })).toEqual({ action: "skip" });
        expect(plan({ comments: [bot(5, "<!-- streamyfin-measure run=10 attempt=2 -->")], run: 10, attempt: 1 })).toEqual({ action: "skip" });
    });

    test("only takes a bot's comment for its own", () => {
        const person = { id: 3, body: "<!-- streamyfin-measure run=99 attempt=1 -->", user: { type: "User" } };

        expect(plan({ comments: [person], run: 10, attempt: 1 })).toEqual({ action: "create" });
    });
});

describe("the comment's text", () => {
    test("is the summary, marked with its run", () => {
        expect(bodyOf({ summary: "<!-- streamyfin-measure -->\n## Unit tests, measured\n", run: 7, attempt: 2, needs: {} }))
            .toBe("<!-- streamyfin-measure run=7 attempt=2 -->\n## Unit tests, measured\n");
    });

    test("says which measures failed when there is no summary", () => {
        const needs = { "unit-cs": { result: "failure" }, "unit-js": { result: "success" }, "mutation-js": { result: "cancelled" } };

        expect(bodyOf({ summary: null, run: 7, attempt: 1, needs })).toBe([
            "<!-- streamyfin-measure run=7 attempt=1 -->",
            "## Unit tests, measured",
            "",
            "**Failed.** This push could not be judged: unit-cs failed, mutation-js was cancelled.",
            "",
        ].join("\n"));
    });
});
