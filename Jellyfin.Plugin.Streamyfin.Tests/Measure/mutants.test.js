// Mutants as Stryker.NET and StrykerJS report them, and the score Stryker gives them.

import { describe, expect, test } from "bun:test";
import { escaped, readMutants, score } from "../../scripts/measure/mutants.js";
import { toRepoPath } from "../../scripts/measure/paths.js";

const ROOT = "/repo";

const mutant = (status, line = 4, extra = {}) => ({
    id: "1",
    mutatorName: "Boolean mutation",
    replacement: "false",
    location: { start: { line, column: 3 }, end: { line, column: 7 } },
    status,
    ...extra,
});

describe("a Stryker report", () => {
    test("gives each mutant its file in the repository, its line, what it changed and how it fared", () => {
        const report = {
            schemaVersion: "2",
            files: {
                "/repo/Jellyfin.Plugin.Streamyfin/Db/A.cs": { language: "cs", mutants: [mutant("Survived")] },
                "scripts/a.js": { language: "javascript", mutants: [mutant("Killed", 9, { mutatorName: "StringLiteral", replacement: '""' })] },
            },
        };

        expect(readMutants(report, (path) => toRepoPath(path, ROOT))).toEqual([
            { file: "Jellyfin.Plugin.Streamyfin/Db/A.cs", line: 4, endLine: 4, column: 3, mutator: "Boolean mutation", replacement: "false", status: "Survived" },
            { file: "scripts/a.js", line: 9, endLine: 9, column: 3, mutator: "StringLiteral", replacement: '""', status: "Killed" },
        ]);
    });

    test("keeps where a mutant spanning lines ends", () => {
        const report = { files: { "a.js": { mutants: [mutant("Survived", 4, { location: { start: { line: 4, column: 3 }, end: { line: 6, column: 1 } } })] } } };

        expect(readMutants(report, (path) => path)[0]).toMatchObject({ line: 4, endLine: 6, column: 3 });
    });

    test("with a file that has no mutants gives none for it", () => {
        expect(readMutants({ files: { "scripts/a.js": { source: "" } } }, (path) => path)).toEqual([]);
    });

    test("without a replacement gives an empty one rather than nothing", () => {
        const report = { files: { "a.js": { mutants: [mutant("Survived", 1, { replacement: undefined })] } } };

        expect(readMutants(report, (path) => path)[0].replacement).toBe("");
    });
});

const statuses = (...list) => list.map((status) => ({ status }));

describe("the mutation score", () => {
    // Detected over detected and undetected, as Stryker counts it: a mutant that did not
    // compile, broke the run or was ignored is in neither.
    test("is Stryker's own", () => {
        expect(score(statuses("Killed", "Timeout", "Survived", "NoCoverage", "CompileError", "RuntimeError", "Ignored")))
            .toEqual({ detected: 2, undetected: 2, score: 50 });
    });

    test("is none when no mutant could be judged", () => {
        expect(score(statuses("CompileError", "Ignored")).score).toBeNull();
        expect(score([]).score).toBeNull();
    });

    test("counts as escaping a mutant that survived or that no test reached", () => {
        expect(["Survived", "NoCoverage", "Killed", "Timeout", "CompileError"].map((status) => escaped({ status })))
            .toEqual([true, true, false, false, false]);
    });
});
