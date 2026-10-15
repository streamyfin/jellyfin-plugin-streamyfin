// What a run says in the job's summary and the pull request's comment, and the floors a
// raise writes.

import { describe, expect, test } from "bun:test";
import { MARKER, raisedFloors, summarize } from "../../scripts/measure/summary.js";

const passed = {
    ok: true,
    folders: [
        { name: "Db", lines: 93.9, branches: 84.8, mutation: 59.8, floor: { lines: 93.9, branches: 84.8, mutation: 59.8 }, problems: [] },
        { name: "Pages", lines: 61.2, branches: null, mutation: 40, floor: { lines: 61.2, branches: null, mutation: 40 }, problems: [] },
    ],
    escaped: [],
    stale: [],
    problems: [],
};

describe("the summary", () => {
    // The CI step finds its own comment on the pull request by this marker.
    test("is marked as the measure's", () => {
        expect(MARKER).toBe("<!-- streamyfin-measure -->");
    });

    test("of a run that passes is the verdict and the table, nothing else", () => {
        const text = summarize({ ...passed, folders: [...passed.folders, { name: "New", lines: 10, branches: null, mutation: 5, problems: [] }] });

        expect(text).toBe([
            "<!-- streamyfin-measure -->",
            "## Unit tests, measured",
            "",
            "**Passed.**",
            "",
            "| Folder | Lines | Branches | Mutation |",
            "| --- | --- | --- | --- |",
            "| Db | 93.9 % (floor 93.9) | 84.8 % (floor 84.8) | 59.8 % (floor 59.8) |",
            "| Pages | 61.2 % (floor 61.2) | not measured | 40 % (floor 40) |",
            "| New | 10 % | not measured | 5 % |",
            "",
        ].join("\n"));
    });

    test("of a run that fails has a section for each kind of failure, in order", () => {
        const text = summarize({
            ...passed,
            ok: false,
            folders: [{ ...passed.folders[0], problems: ["lines 90 % is under its floor of 93.9 %"] }],
            escaped: [{ file: "scripts/a.js", line: 4, text: "`a` | b", mutator: "LogicalOperator", replacement: "a && b", status: "Survived" }],
            stale: [{ file: "scripts/b.js", line: "return x;", mutator: "BlockStatement", replacement: "{}", reason: "old" }],
            problems: ["the mutation shard js/pages-form left no report"],
        });

        expect(text).toBe([
            "<!-- streamyfin-measure -->",
            "## Unit tests, measured",
            "",
            "**Failed.**",
            "",
            "| Folder | Lines | Branches | Mutation |",
            "| --- | --- | --- | --- |",
            "| Db | 93.9 % (floor 93.9) | 84.8 % (floor 84.8) | 59.8 % (floor 59.8) |",
            "",
            "### What fails",
            "",
            "- the mutation shard js/pages-form left no report",
            "- Db: lines 90 % is under its floor of 93.9 %",
            "",
            "### Mutants that escape on lines this pull request changed",
            "",
            "Each one needs a test that kills it, or an entry in `tests/accepted-mutants.json` with the reason.",
            "",
            "| File | Line | Code | Mutation | Status |",
            "| --- | --- | --- | --- | --- |",
            "| scripts/a.js | 4 | `'a' \\| b` | LogicalOperator: `a && b` | Survived |",
            "",
            "### Accepted mutants nothing matches any more",
            "",
            "Remove them from `tests/accepted-mutants.json`.",
            "",
            "- scripts/b.js: `return x;`, BlockStatement `{}`",
            "",
        ].join("\n"));
    });

    // A comment holds 65536 characters, and a pull request that adds a whole file can leave
    // hundreds of mutants on it.
    test("lists a hundred escaped mutants at most, counts the rest, and fits a comment", () => {
        const escaped = Array.from({ length: 130 }, (_, at) => ({
            file: "scripts/a.js", line: at + 1, text: "x".repeat(400), mutator: "M", replacement: "y".repeat(400), status: "Survived",
        }));
        const text = summarize({ ...passed, ok: false, escaped });

        expect(text.split("\n").filter((line) => line.startsWith("| scripts/a.js |")).length).toBe(100);
        expect(text).toContain("\nAnd 30 more.\n");
        expect(text).toContain(`\`${"x".repeat(120)}…\``);
        expect(text.length).toBeLessThan(65536);
    });

    test("carries its marker, the verdict and every folder against its floors", () => {
        const text = summarize(passed);

        expect(text.startsWith(MARKER)).toBe(true);
        expect(text).toContain("**Passed.**");
        expect(text).toContain("| Db | 93.9 % (floor 93.9) | 84.8 % (floor 84.8) | 59.8 % (floor 59.8) |");
        expect(text).toContain("| Pages | 61.2 % (floor 61.2) | not measured | 40 % (floor 40) |");
    });

    test("says what fails, which mutant escapes where, and which acceptance is stale", () => {
        const text = summarize({
            ...passed,
            ok: false,
            folders: [{ ...passed.folders[0], problems: ["mutation 59.7 % is under its floor of 59.8 %"] }],
            escaped: [{ file: "scripts/a.js", line: 4, text: "a || b", mutator: "LogicalOperator", replacement: "a && b", status: "Survived" }],
            stale: [{ file: "scripts/b.js", line: "return x;", mutator: "BlockStatement", replacement: "{}", reason: "old" }],
            problems: ["the mutation shard js/pages-form left no report"],
        });

        expect(text).toContain("**Failed.**");
        expect(text).toContain("- the mutation shard js/pages-form left no report");
        expect(text).toContain("- Db: mutation 59.7 % is under its floor of 59.8 %");
        // A pipe in the code would end the table's cell.
        expect(text).toContain("| scripts/a.js | 4 | `a \\|\\| b` | LogicalOperator: `a && b` | Survived |");
        expect(text).toContain("- scripts/b.js: `return x;`, BlockStatement `{}`");
    });
});

describe("raising the floors", () => {
    const floors = { Db: { lines: 93.9, branches: 84.8, mutation: 59.8 }, Gone: { lines: 1, branches: null, mutation: null } };

    test("lifts each floor to what was measured, and never lowers one", () => {
        const measured = [{ name: "Db", lines: 95.1, branches: 80, mutation: 59.8 }];

        expect(raisedFloors(floors, measured)).toEqual({ Db: { lines: 95.1, branches: 84.8, mutation: 59.8 } });
    });

    test("leaves a value no longer measured unmeasured, whatever its floor was", () => {
        const measured = [{ name: "Db", lines: 95.1, branches: null, mutation: 59.8 }];

        expect(raisedFloors(floors, measured).Db).toEqual({ lines: 95.1, branches: null, mutation: 59.8 });
    });

    test("adds a folder measured for the first time, and drops one no longer measured", () => {
        const measured = [{ name: "Compat", lines: 50, branches: 40, mutation: null }, { name: "Db", lines: 93.9, branches: 84.8, mutation: 59.8 }];

        expect(Object.keys(raisedFloors(floors, measured))).toEqual(["Compat", "Db"]);
        expect(raisedFloors(floors, measured).Compat).toEqual({ lines: 50, branches: 40, mutation: null });
    });
});
