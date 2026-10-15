// The measure's comment on a pull request: one, carrying the run it came from, never replaced
// by an older run's, and saying so when the measure could not judge.

import { describe, expect, test } from "bun:test";

const { bodyOf, plan, post, runOf } = require("../../scripts/measure-comment.js");

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

    test("rewrites its own, on the same attempt", () => {
        expect(plan({ comments: [bot(5, "<!-- streamyfin-measure run=10 attempt=1 -->")], run: 10, attempt: 1 })).toEqual({ action: "update", id: 5 });
    });

    test("reads an attempt or a run of two digits and more", () => {
        expect(runOf("<!-- streamyfin-measure run=123 attempt=12 -->")).toEqual({ run: 123, attempt: 12 });
    });

    test("passes over a comment whose author is gone", () => {
        expect(plan({ comments: [{ id: 1, body: "<!-- streamyfin-measure run=99 attempt=1 -->", user: null }], run: 10, attempt: 1 })).toEqual({ action: "create" });
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

// The step on GitHub, against a client that records what it is asked.
describe("posting from the workflow", () => {
    const SUMMARY = "<!-- streamyfin-measure -->\n## Unit tests, measured\n";
    const context = { repo: { owner: "streamyfin", repo: "plugin" }, issue: { number: 9 }, runId: 50 };

    const client = (comments) => {
        const calls = [];
        return {
            calls,
            paginate: async (method, params) => {
                calls.push(["list", method, params]);
                return comments;
            },
            rest: {
                issues: {
                    listComments: "listComments",
                    createComment: async (args) => calls.push(["create", args]),
                    updateComment: async (args) => calls.push(["update", args]),
                },
            },
        };
    };
    const files = (summary) => ({
        existsSync: (path) => summary !== null && path === "artifacts/measure/summary.md",
        readFileSync: (path, encoding) => (path === "artifacts/measure/summary.md" && encoding === "utf8" ? summary : null),
    });

    const withAttempt = async (attempt, run) => {
        const before = process.env.GITHUB_RUN_ATTEMPT;
        if (attempt === undefined) delete process.env.GITHUB_RUN_ATTEMPT;
        else process.env.GITHUB_RUN_ATTEMPT = attempt;
        try {
            await run();
        } finally {
            if (before === undefined) delete process.env.GITHUB_RUN_ATTEMPT;
            else process.env.GITHUB_RUN_ATTEMPT = before;
        }
    };

    test("writes the summary, marked with the run and its attempt, on a pull request with none", async () => {
        const github = client([]);

        await withAttempt("3", () => post({ github, context, fs: files(SUMMARY), needs: {} }));

        expect(github.calls).toEqual([
            ["list", "listComments", { owner: "streamyfin", repo: "plugin", issue_number: 9, per_page: 100 }],
            ["create", { owner: "streamyfin", repo: "plugin", issue_number: 9, body: "<!-- streamyfin-measure run=50 attempt=3 -->\n## Unit tests, measured\n" }],
        ]);
    });

    test("replaces the measure's comment of an older run, as the first attempt without the variable", async () => {
        const github = client([bot(4, "<!-- streamyfin-measure run=49 attempt=2 -->")]);

        await withAttempt(undefined, () => post({ github, context, fs: files(SUMMARY), needs: {} }));

        expect(github.calls.at(-1)).toEqual(["update", { owner: "streamyfin", repo: "plugin", comment_id: 4, body: "<!-- streamyfin-measure run=50 attempt=1 -->\n## Unit tests, measured\n" }]);
    });

    test("leaves a newer run's comment alone", async () => {
        const github = client([bot(4, "<!-- streamyfin-measure run=51 attempt=1 -->")]);

        await withAttempt("1", () => post({ github, context, fs: files(SUMMARY), needs: {} }));

        expect(github.calls.map(([call]) => call)).toEqual(["list"]);
    });

    test("says what failed when no summary was written", async () => {
        const github = client([]);

        await withAttempt("1", () => post({ github, context, fs: files(null), needs: { "unit-cs": { result: "failure" } } }));

        expect(github.calls.at(-1)[1].body).toContain("This push could not be judged: unit-cs failed.");
    });
});
