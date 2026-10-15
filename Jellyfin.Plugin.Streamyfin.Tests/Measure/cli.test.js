// The verdict from the reports on disk, as the command line reads them.

import { afterEach, describe, expect, test } from "bun:test";
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { collect, verdictFor } from "../../scripts/measure/cli.js";

const roots = [];

const put = (root, path, content) => {
    mkdirSync(dirname(join(root, path)), { recursive: true });
    writeFileSync(join(root, path), typeof content === "string" ? content : JSON.stringify(content));
};

const SETTINGS = {
    floors: { slack: 1 },
    mutation: {
        iterations: 10,
        cs: { shards: [{ name: "data", mutate: ["**/Db/**/*.cs"] }] },
        js: { shards: [{ name: "scripts", mutate: ["scripts/**/*.js"], tests: [] }] },
    },
};

const report = (path, ...statuses) => ({
    schemaVersion: "2",
    files: { [path]: { mutants: statuses.map((status, at) => ({ mutatorName: "Boolean mutation", replacement: "false", location: { start: { line: at + 1, column: 1 } }, status })) } },
});

const repository = () => {
    const root = mkdtempSync(join(tmpdir(), "measure-cli-"));
    roots.push(root);
    put(root, "tests/settings.json", SETTINGS);
    put(root, "tests/owners.json", []);
    put(root, "tests/accepted-mutants.json", []);
    put(root, "tests/floors.json", { Db: { lines: 50, branches: null, mutation: 50 }, scripts: { lines: 66.6, branches: null, mutation: 100 } });
    put(root, "Jellyfin.Plugin.Streamyfin/Db/A.cs", "a\nb\n");
    put(root, "scripts/a.js", "export const a = 1;\nexport const b = 2;\n");
    put(root, "scripts/never.js", "// loaded by nothing\nexport const c = 3;\n");
    put(root, "artifacts/coverage-cs-jf12/0f/coverage.info", `SF:${join(root, "Jellyfin.Plugin.Streamyfin/Db/A.cs")}\nDA:1,1\nDA:2,0\nend_of_record\n`);
    put(root, "artifacts/coverage-cs-jf11/0a/coverage.info", `SF:${join(root, "Jellyfin.Plugin.Streamyfin/Db/A.cs")}\nDA:1,0\nDA:2,0\nend_of_record\n`);
    put(root, "artifacts/coverage-js/lcov.info", "SF:scripts/a.js\nDA:1,1\nDA:2,1\nend_of_record\n");
    put(root, "artifacts/mutation-cs-data/reports/mutation-report.json", report(join(root, "Jellyfin.Plugin.Streamyfin/Db/A.cs"), "Killed", "Survived"));
    put(root, "artifacts/mutation-js-scripts/mutation.json", report("scripts/a.js", "Killed"));
    return root;
};

afterEach(() => {
    for (const root of roots.splice(0)) rmSync(root, { recursive: true, force: true });
});

describe("the reports on disk", () => {
    test("are read as one, with the JavaScript nothing loads counted at 0 %", () => {
        const root = repository();
        const { coverage, mutants, missingReports } = collect({ root, settings: SETTINGS, owners: new Map() });

        expect([...coverage.keys()].sort()).toEqual(["Jellyfin.Plugin.Streamyfin/Db/A.cs", "scripts/a.js", "scripts/never.js"]);
        expect([...coverage.get("scripts/never.js").lines]).toEqual([[2, 0]]);
        expect(mutants.map((mutant) => `${mutant.file}:${mutant.line}:${mutant.status}`)).toEqual([
            "Jellyfin.Plugin.Streamyfin/Db/A.cs:1:Killed",
            "Jellyfin.Plugin.Streamyfin/Db/A.cs:2:Survived",
            "scripts/a.js:1:Killed",
        ]);
        expect(missingReports).toEqual([]);
    });

    test("name a shard that left no report", () => {
        const root = repository();
        rmSync(join(root, "artifacts/mutation-js-scripts"), { recursive: true });

        expect(collect({ root, settings: SETTINGS, owners: new Map() }).missingReports).toEqual(["js/scripts"]);
    });

    // A coverage run that wrote nothing would read as a folder with no code, and a target
    // left out would quietly measure the other alone.
    test("refuse to judge without the coverage of every run", () => {
        const root = repository();
        rmSync(join(root, "artifacts/coverage-js"), { recursive: true });
        expect(() => collect({ root, settings: SETTINGS, owners: new Map() })).toThrow("artifacts/coverage-js holds no coverage");

        const other = repository();
        rmSync(join(other, "artifacts/coverage-cs-jf11"), { recursive: true });
        expect(() => collect({ root: other, settings: SETTINGS, owners: new Map() })).toThrow("artifacts/coverage-cs-jf11 holds no coverage");
    });
});

describe("the verdict from the reports", () => {
    test("judges, and writes its result and its summary next to them", () => {
        const root = repository();
        const result = verdictFor({ root, settings: SETTINGS, changed: new Map() });

        expect(result.folders.map(({ name, lines, mutation }) => ({ name, lines, mutation }))).toEqual([
            { name: "Db", lines: 50, mutation: 50 },
            { name: "scripts", lines: 66.6, mutation: 100 },
        ]);
        expect(result.ok).toBe(true);
        expect(JSON.parse(readFileSync(join(root, "artifacts/measure/verdict.json"), "utf8")).ok).toBe(true);
        expect(readFileSync(join(root, "artifacts/measure/summary.md"), "utf8")).toContain("**Passed.**");
    });

    test("fails a survivor on a line the pull request changed", () => {
        const root = repository();
        const result = verdictFor({ root, settings: SETTINGS, changed: new Map([["Jellyfin.Plugin.Streamyfin/Db/A.cs", new Set([2])]]) });

        expect(result.ok).toBe(false);
        expect(result.escaped.map(({ file, line, text }) => ({ file, line, text }))).toEqual([
            { file: "Jellyfin.Plugin.Streamyfin/Db/A.cs", line: 2, text: "b" },
        ]);
    });
});
