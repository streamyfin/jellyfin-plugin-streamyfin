// The lines a pull request adds or changes, read from git diff with no context.

import { afterEach, describe, expect, test } from "bun:test";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
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

    test("counts a hunk of ten lines or more", () => {
        const changed = parseDiff("+++ b/scripts/a.js\n@@ -0,0 +1,12 @@\n");

        expect(lines(changed, "scripts/a.js")).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]);
    });

    test("reads a name past ASCII as git writes it with quotePath off", () => {
        expect([...parseDiff("+++ b/scripts/é.js\n@@ -1 +1 @@\n").keys()]).toEqual(["scripts/é.js"]);
    });

    // git still quotes a name with a quote, a backslash or a control character, with C escapes
    // inside: read as it is, it would match no file and its lines would go unjudged.
    test("refuses a name git had to quote", () => {
        expect(() => parseDiff('+++ "b/scripts/say\\"hi\\".js"\n@@ -1 +1 @@\n'))
            .toThrow(new Error('cannot read the path of +++ "b/scripts/say\\"hi\\".js": git quotes a name with a quote, a backslash or a control character'));
    });

    // A moved file is read as a deletion and an addition, so all of its lines count as new. The
    // prefixes are given, since diff.noprefix and diff.mnemonicPrefix change them otherwise.
    test("is asked for without context, renames, quoting or prefixes of the user's own", () => {
        expect(diffArgs("abc123")).toEqual([
            "-c", "core.quotePath=false",
            "diff", "--unified=0", "--no-color", "--no-ext-diff", "--no-renames", "--src-prefix=a/", "--dst-prefix=b/",
            "abc123",
        ]);
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

describe("git, asked for real", () => {
    const roots = [];
    afterEach(() => {
        for (const root of roots.splice(0)) rmSync(root, { recursive: true, force: true });
    });

    const repository = () => {
        const root = mkdtempSync(join(tmpdir(), "measure-diff-"));
        roots.push(root);
        const git = (...args) => {
            const child = Bun.spawnSync(["git", "-c", "user.name=measure", "-c", "user.email=measure@example.com", "-c", "commit.gpgsign=false", ...args], { cwd: root, stdout: "pipe", stderr: "pipe" });
            if (child.exitCode !== 0) throw new Error(child.stderr.toString());
        };
        git("init", "-q");
        writeFileSync(join(root, "a.js"), "one\ntwo\n");
        git("add", "-A");
        git("commit", "-q", "-m", "first");
        return { root, git };
    };

    test("finds the lines changed since the base, whatever prefixes the repository's diff uses", () => {
        const { root, git } = repository();
        git("config", "diff.mnemonicPrefix", "true");
        writeFileSync(join(root, "a.js"), "one\nchanged\nadded\n");

        expect(changedLines("HEAD", { cwd: root })).toEqual(new Map([["a.js", new Set([2, 3])]]));
    });

    test("fails with git's own words, on one line", () => {
        const { root } = repository();

        expect(() => changedLines("nope", { cwd: root })).toThrow(/^git merge-base nope HEAD failed: fatal: Not a valid object name nope$/);
    });
});
