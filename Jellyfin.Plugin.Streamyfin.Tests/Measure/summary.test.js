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

    test("adds a folder measured for the first time, and drops one no longer measured", () => {
        const measured = [{ name: "Compat", lines: 50, branches: 40, mutation: null }, { name: "Db", lines: 93.9, branches: 84.8, mutation: 59.8 }];

        expect(Object.keys(raisedFloors(floors, measured))).toEqual(["Compat", "Db"]);
        expect(raisedFloors(floors, measured).Compat).toEqual({ lines: 50, branches: 40, mutation: null });
    });
});
