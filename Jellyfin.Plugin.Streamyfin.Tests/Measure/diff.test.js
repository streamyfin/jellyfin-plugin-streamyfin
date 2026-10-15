// The lines a pull request adds or changes, read from git diff with no context.

import { describe, expect, test } from "bun:test";
import { changedLines, diffArgs, parseDiff } from "../../scripts/measure/diff.js";

const lines = (changed, file) => [...(changed.get(file) ?? [])];

describe("a diff with no context", () => {
    test("gives the new side of each hunk", () => {
        const changed = parseDiff([
            "diff --git a/scripts/a.js b/scripts/a.js",
            "--- a/scripts/a.js",
            "+++ b/scripts/a.js",
            "@@ -3,0 +4,2 @@ export const a = 1;",
            "+one",
            "+two",
            "@@ -10 +12 @@",
            "-old",
            "+new",
            "@@ -20,3 +23,0 @@",
            "-gone",
        ].join("\n"));

        expect(lines(changed, "scripts/a.js")).toEqual([4, 5, 12]);
    });

    test("counts every line of a new file, and none of a deleted one", () => {
        const changed = parseDiff([
            "--- /dev/null",
            "+++ b/scripts/measure/new.js",
            "@@ -0,0 +1,3 @@",
            "--- a/scripts/old.js",
            "+++ /dev/null",
            "@@ -1,2 +0,0 @@",
        ].join("\n"));

        expect(lines(changed, "scripts/measure/new.js")).toEqual([1, 2, 3]);
        expect(changed.has("scripts/old.js")).toBe(false);
        expect([...changed.keys()]).toEqual(["scripts/measure/new.js"]);
    });

    // git on Windows may hand the text back with CRLF line endings.
    test("reads the same with CRLF line endings", () => {
        const changed = parseDiff("--- a/scripts/a.js\r\n+++ b/scripts/a.js\r\n@@ -1 +1,2 @@\r\n");

        expect(lines(changed, "scripts/a.js")).toEqual([1, 2]);
    });

    // A moved file is read as a deletion and an addition, so all of its lines count as new.
    test("is asked for without context and without renames", () => {
        expect(diffArgs("abc123")).toEqual(["diff", "--unified=0", "--no-color", "--no-ext-diff", "--no-renames", "abc123"]);
    });
});

describe("the lines a pull request changes", () => {
    test("are diffed from where the branch left its base", () => {
        const asked = [];
        const git = (args) => {
            asked.push(args);
            return args[0] === "merge-base" ? "abc123\n" : "+++ b/scripts/a.js\n@@ -1 +1 @@\n";
        };

        const changed = changedLines("origin/develop", { cwd: "/repo", git });

        expect(asked).toEqual([["merge-base", "origin/develop", "HEAD"], diffArgs("abc123")]);
        expect(lines(changed, "scripts/a.js")).toEqual([1]);
    });
});
