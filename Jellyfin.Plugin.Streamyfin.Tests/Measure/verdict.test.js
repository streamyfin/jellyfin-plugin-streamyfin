// The verdict a pull request gets: each folder against its floors, and every mutant that
// escapes on a line the pull request touched.

import { describe, expect, test } from "bun:test";
import { judge, measureFolders, oneDecimal } from "../../scripts/measure/verdict.js";

const DB = "Jellyfin.Plugin.Streamyfin/Db/A.cs";
const OWNED = "Jellyfin.Plugin.Streamyfin/Plugin.cs";

const file = (covered, total) => ({
    lines: new Map(Array.from({ length: total }, (_, at) => [at + 1, at < covered ? 1 : 0])),
    branches: new Map(),
});

const mutant = (status, extra = {}) => ({ file: DB, line: 3, endLine: 3, column: 5, mutator: "Boolean mutation", replacement: "false", status, ...extra });

describe("a folder's measure", () => {
    test("is rounded down to a tenth, so a floor is never met by rounding up", () => {
        expect(oneDecimal(66.666)).toBe(66.6);
        expect(oneDecimal(70.5)).toBe(70.5);
        expect(oneDecimal(99.99)).toBe(99.9);
    });

    test("adds up its files, and leaves out generated code and what the integration tests answer for", () => {
        const coverage = new Map([
            [DB, file(2, 3)],
            ["Jellyfin.Plugin.Streamyfin/Db/B.cs", file(1, 1)],
            ["Jellyfin.Plugin.Streamyfin/obj/Generated.cs", file(0, 50)],
            [OWNED, file(0, 40)],
        ]);
        const mutants = [mutant("Killed"), mutant("Survived"), mutant("Survived", { file: OWNED })];

        expect(measureFolders({ coverage, mutants, owners: new Set([OWNED]) })).toEqual([
            { name: "Db", lines: 75, branches: null, mutation: 50 },
        ]);
    });
});

describe("a folder's measure, further", () => {
    const branched = (...branches) => ({
        lines: new Map([[1, 1]]),
        branches: new Map(branches.map(([covered, total], at) => [at + 1, { covered, total }])),
    });

    test("adds up the branches of its files, and lists the folders by name", () => {
        const coverage = new Map([
            ["Jellyfin.Plugin.Streamyfin/Pages/a.js", branched([1, 2], [2, 2])],
            [DB, branched([0, 4])],
        ]);

        expect(measureFolders({ coverage, mutants: [], owners: new Set() })).toEqual([
            { name: "Db", lines: 100, branches: 0, mutation: null },
            { name: "Pages", lines: 100, branches: 75, mutation: null },
        ]);
    });

    test("measures no line of a folder that only has mutants, and no mutation of one that has none", () => {
        const coverage = new Map([[DB, file(3, 4)]]);
        const mutants = [mutant("Survived", { file: "Jellyfin.Plugin.Streamyfin/Configuration/A.cs" })];

        expect(measureFolders({ coverage, mutants, owners: new Set() })).toEqual([
            { name: "Configuration", lines: null, branches: null, mutation: 0 },
            { name: "Db", lines: 75, branches: null, mutation: null },
        ]);
    });

    test("leaves out what sits in no folder: the tests, the docs", () => {
        const coverage = new Map([[DB, file(1, 1)], ["Jellyfin.Plugin.Streamyfin.Tests/A.cs", file(0, 9)], ["docs/a.js", file(0, 9)]]);
        const mutants = [mutant("Survived", { file: "Jellyfin.Plugin.Streamyfin.Tests/A.cs" })];

        expect(measureFolders({ coverage, mutants, owners: new Set() })).toEqual([{ name: "Db", lines: 100, branches: null, mutation: null }]);
    });
});

const measured = [{ name: "Db", lines: 70.5, branches: 58.3, mutation: 50.7 }];
const FLOORS = { Db: { lines: 70.5, branches: 58.3, mutation: 50.7 } };

const verdict = (overrides = {}) => judge({
    measured,
    floors: FLOORS,
    slack: 1,
    mutants: [],
    accepted: [],
    owners: new Set(),
    changed: new Map(),
    lineText: () => "return true;",
    missingReports: [],
    ...overrides,
});

describe("the floors", () => {
    test("pass a folder that measures what they say", () => {
        const result = verdict();

        expect(result.ok).toBe(true);
        expect(result.folders[0].problems).toEqual([]);
    });

    test("fail a folder that measures under one of them", () => {
        const result = verdict({ measured: [{ ...measured[0], mutation: 50.6 }] });

        expect(result.ok).toBe(false);
        expect(result.folders[0].problems).toEqual(["mutation 50.6 % is under its floor of 50.7 %"]);
    });

    // A floor left behind is a floor the next pull request can sink back to.
    test("ask to be raised once a folder is more than the slack above them", () => {
        expect(verdict({ measured: [{ ...measured[0], lines: 71.5 }] }).ok).toBe(true);

        const result = verdict({ measured: [{ ...measured[0], lines: 71.6 }] });

        expect(result.ok).toBe(false);
        expect(result.folders[0].problems).toEqual([
            "lines 71.6 % is more than 1 point over its floor of 70.5 %: run bun run floors:raise",
        ]);
    });

    test("must exist for every folder measured, and only for those", () => {
        const fresh = verdict({ floors: {} });
        expect(fresh.ok).toBe(false);
        expect(fresh.folders[0].problems).toEqual(["has no floors yet: run bun run floors:raise"]);

        const gone = verdict({ floors: { ...FLOORS, Compat: { lines: 10, branches: null, mutation: null } } });
        expect(gone.ok).toBe(false);
        expect(gone.problems).toEqual(["Compat has floors but nothing is measured in it: run bun run floors:raise"]);
    });

    // bun measures no branches, so the pages and the scripts carry none.
    test("leave alone what is measured nowhere, and catch what stopped being measured", () => {
        const none = { name: "Pages", lines: 89, branches: null, mutation: 40 };
        expect(verdict({ measured: [none], floors: { Pages: { lines: 89, branches: null, mutation: 40 } } }).ok).toBe(true);

        const lost = verdict({ measured: [{ ...measured[0], branches: null }] });
        expect(lost.folders[0].problems).toEqual(["branches is no longer measured, under a floor of 58.3 %"]);
    });

    test("fail a run where a mutation shard left no report, and name the shard", () => {
        const result = verdict({ missingReports: ["cs/notifications"] });

        expect(result.ok).toBe(false);
        expect(result.problems).toEqual(["the mutation shard cs/notifications left no report"]);
    });
});

describe("the floors, further", () => {
    test("stay on each folder they were set for, and a folder without any has none", () => {
        const result = verdict({ measured: [...measured, { name: "New", lines: 10, branches: null, mutation: null }] });

        expect(result.folders.map((folder) => folder.floor)).toEqual([FLOORS.Db, null]);
    });

    test("fail the run when one folder of several is under them", () => {
        const result = verdict({
            measured: [{ ...measured[0], lines: 60 }, { name: "Pages", lines: 50, branches: null, mutation: 40 }],
            floors: { ...FLOORS, Pages: { lines: 50, branches: null, mutation: 40 } },
        });

        expect(result.folders.map((folder) => folder.problems.length)).toEqual([1, 0]);
        expect(result.ok).toBe(false);
    });

    test("ask to be set for a metric measured where they say nothing yet", () => {
        const result = verdict({ floors: { Db: { lines: 70.5, branches: null, mutation: 50.7 } } });

        expect(result.folders[0].problems).toEqual(["branches is now measured, at 58.3 %: run bun run floors:raise"]);
    });
});

describe("a mutant that escapes", () => {
    const changed = new Map([[DB, new Set([3])]]);

    test("on a line the pull request touched fails it", () => {
        const result = verdict({ mutants: [mutant("Survived"), mutant("NoCoverage", { replacement: "true" })], changed });

        expect(result.ok).toBe(false);
        expect(result.escaped).toEqual([
            { file: DB, line: 3, text: "return true;", mutator: "Boolean mutation", replacement: "false", status: "Survived" },
            { file: DB, line: 3, text: "return true;", mutator: "Boolean mutation", replacement: "true", status: "NoCoverage" },
        ]);
    });

    test("elsewhere, or in a file the integration tests answer for, does not", () => {
        expect(verdict({ mutants: [mutant("Survived", { line: 9 })], changed }).ok).toBe(true);
        expect(verdict({
            mutants: [mutant("Survived", { file: OWNED })],
            changed: new Map([[OWNED, new Set([3])]]),
            owners: new Set([OWNED]),
        }).ok).toBe(true);
    });

    // Matched on the line's text rather than its number, so the entry holds when code moves.
    test("that was accepted with a reason passes", () => {
        const accepted = [{ file: DB, line: "return true;", column: 5, mutator: "Boolean mutation", replacement: "false", reason: "an equivalent mutant" }];

        expect(verdict({ mutants: [mutant("Survived")], changed, accepted }).ok).toBe(true);
    });

    test("no longer, leaves its acceptance stale, which fails the run", () => {
        const accepted = [{ file: DB, line: "return true;", column: 5, mutator: "Boolean mutation", replacement: "false", reason: "an equivalent mutant" }];
        const result = verdict({ mutants: [mutant("Killed")], accepted });

        expect(result.ok).toBe(false);
        expect(result.stale).toEqual(accepted);
    });
});

describe("an acceptance", () => {
    test("covers its own mutant only, not another on the same line", () => {
        const changed = new Map([[DB, new Set([3])]]);
        const accepted = [{ file: DB, line: "return true;", column: 5, mutator: "Boolean mutation", replacement: "false", reason: "equivalent" }];
        const result = verdict({ mutants: [mutant("Survived"), mutant("Survived", { replacement: "!true" })], accepted, changed });

        expect(result.escaped.map((escapedMutant) => escapedMutant.replacement)).toEqual(["!true"]);
        expect(result.stale).toEqual([]);
    });
});

describe("an acceptance, further", () => {
    const changed = new Map([[DB, new Set([3])]]);
    const entry = { file: DB, line: "return true;", column: 5, mutator: "Boolean mutation", replacement: "false", reason: "equivalent" };

    test("names its column, so a twin further along the line still escapes", () => {
        const result = verdict({ mutants: [mutant("Survived"), mutant("Survived", { column: 12 })], accepted: [entry], changed });

        expect(result.escaped.map((one) => one.line)).toEqual([3]);
        expect(result.ok).toBe(false);
    });

    test("without a reason fails the run", () => {
        const blank = verdict({ mutants: [mutant("Survived")], accepted: [{ ...entry, reason: "  " }], changed });
        const { reason, ...missing } = entry;

        expect(blank.ok).toBe(false);
        expect(blank.problems).toEqual([`tests/accepted-mutants.json: every entry needs a reason (${DB}: return true;)`]);
        expect(verdict({ mutants: [mutant("Survived")], accepted: [missing], changed }).problems)
            .toEqual([`tests/accepted-mutants.json: every entry needs a reason (${DB}: return true;)`]);
    });
});

describe("the lines a mutant spans", () => {
    test("are all judged, so a change further down a multi-line expression catches it", () => {
        const result = verdict({ mutants: [mutant("Survived", { endLine: 4 })], changed: new Map([[DB, new Set([4])]]) });

        expect(result.ok).toBe(false);
        expect(result.escaped.map((one) => one.line)).toEqual([3]);
    });
});

describe("a Stryker disable comment", () => {
    const lineText = (file, line) => (line === 7 ? "// Stryker disable all" : "return true;");

    test("on a changed line of code fails the run, since it takes mutants out without a reason", () => {
        const result = verdict({ changed: new Map([[DB, new Set([7])]]), lineText });

        expect(result.ok).toBe(false);
        expect(result.problems).toEqual([`a Stryker disable comment on ${DB}:7: accept its mutants in tests/accepted-mutants.json instead`]);
    });

    test("in a file that is not code, or the integration tests answer for, is left alone", () => {
        expect(verdict({ changed: new Map([["scripts/README.md", new Set([7])]]), lineText }).ok).toBe(true);
        expect(verdict({ changed: new Map([["scripts/data.json", new Set([7])]]), lineText }).ok).toBe(true);
        expect(verdict({ changed: new Map([[OWNED, new Set([7])]]), owners: new Set([OWNED]), lineText }).ok).toBe(true);
    });
});
