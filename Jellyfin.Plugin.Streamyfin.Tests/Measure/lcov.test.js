// Coverage as coverlet and bun write it, brought to the repository's paths.

import { describe, expect, test } from "bun:test";
import { counts, mergeCoverage, parseLcov } from "../../scripts/measure/lcov.js";
import { slashed, toRepoPath } from "../../scripts/measure/paths.js";

const ROOT = "/home/runner/work/plugin";

describe("a path from a report", () => {
    test("is the repository's own, with forward slashes", () => {
        expect(toRepoPath(`${ROOT}/Jellyfin.Plugin.Streamyfin/Db/A.cs`, ROOT)).toBe("Jellyfin.Plugin.Streamyfin/Db/A.cs");
        expect(toRepoPath("./scripts/next-version.js", ROOT)).toBe("scripts/next-version.js");
        expect(toRepoPath("scripts\\next-version.js", ROOT)).toBe("scripts/next-version.js");
    });

    // coverlet on a Windows runner, where the drive letter may come back in either case.
    test("from Windows loses its drive and its backslashes", () => {
        expect(toRepoPath("c:\\a\\plugin\\Jellyfin.Plugin.Streamyfin\\Db\\A.cs", "C:\\a\\plugin"))
            .toBe("Jellyfin.Plugin.Streamyfin/Db/A.cs");
    });

    test("outside the repository stays as it was, so nothing counts it", () => {
        expect(toRepoPath("/usr/share/dotnet/x.cs", ROOT)).toBe("/usr/share/dotnet/x.cs");
        expect(toRepoPath("D:\\other\\x.cs", "C:\\a\\plugin")).toBe("D:/other/x.cs");
    });

    test("finds the root whatever slashes end it", () => {
        expect(toRepoPath(`${ROOT}/scripts/a.js`, `${ROOT}//`)).toBe("scripts/a.js");
        expect(toRepoPath(`${ROOT}/scripts/a.js`, `${ROOT}/`)).toBe("scripts/a.js");
    });

    test("only drops a leading ./, and takes a colon inside a relative path as part of a name", () => {
        expect(toRepoPath("scripts/./a.js", ROOT)).toBe("scripts/./a.js");
        expect(toRepoPath("./scripts/a:/b.js", ROOT)).toBe("scripts/a:/b.js");
    });

    test("turn every backslash into a slash", () => {
        expect(slashed("a\\b\\c.js")).toBe("a/b/c.js");
    });
});

const JF12 = [
    `SF:${ROOT}/Jellyfin.Plugin.Streamyfin/Db/A.cs`,
    "DA:1,3",
    "DA:2,0",
    "DA:3,0",
    "BRDA:2,0,0,1",
    "BRDA:2,0,1,-",
    "end_of_record",
].join("\r\n");

const JF11 = [
    `SF:${ROOT}/Jellyfin.Plugin.Streamyfin/Db/A.cs`,
    "DA:1,1",
    "DA:2,2",
    "DA:4,0",
    "BRDA:2,0,0,0",
    "BRDA:2,0,1,4",
    "BRDA:2,1,0,0",
    "end_of_record",
].join("\n");

const read = (text) => parseLcov(text, (path) => toRepoPath(path, ROOT));

describe("an LCOV report, line by line", () => {
    test("keeps the paths as written when nothing maps them", () => {
        expect([...parseLcov("SF:/abs/a.js\nDA:1,1\nend_of_record\n").keys()]).toEqual(["/abs/a.js"]);
    });

    test("reads a line with spaces around it", () => {
        expect([...parseLcov("  SF:scripts/a.js  \n DA:1,1 \nend_of_record \n").keys()]).toEqual(["scripts/a.js"]);
    });

    test("ends a file at end_of_record, and not before", () => {
        const files = parseLcov(["SF:scripts/a.js", "FNF:0", "DA:1,1", "LF:2", "DA:2,0", "end_of_record", "DA:3,1", ""].join("\n"));

        expect([...files.get("scripts/a.js").lines]).toEqual([[1, 1], [2, 0]]);
    });

    test("counts a branch taken once or more as covered, and one never reached as not", () => {
        const files = parseLcov(["SF:a.cs", "BRDA:4,0,0,2", "BRDA:4,0,1,0", "BRDA:4,0,2,-", "end_of_record", ""].join("\n"));

        expect(files.get("a.cs").branches.get(4)).toEqual({ covered: 1, total: 3 });
    });
});

describe("an LCOV report", () => {
    test("gives each file's lines and, line by line, its branches", () => {
        const file = read(JF12).get("Jellyfin.Plugin.Streamyfin/Db/A.cs");

        expect([...file.lines]).toEqual([[1, 3], [2, 0], [3, 0]]);
        expect([...file.branches]).toEqual([[2, { covered: 1, total: 2 }]]);
        expect(counts(file)).toEqual({ lines: { covered: 1, total: 3 }, branches: { covered: 1, total: 2 } });
    });

    // A line counts when either target runs it, and a line only one target compiled counts
    // once. Branches are taken line by line, as many as the target that has the most.
    test("adds the two targets up rather than averaging them", () => {
        const both = mergeCoverage(read(JF12), read(JF11)).get("Jellyfin.Plugin.Streamyfin/Db/A.cs");

        expect(counts(both)).toEqual({ lines: { covered: 2, total: 4 }, branches: { covered: 1, total: 3 } });
    });

    test("without branches, as bun writes it, measures none", () => {
        const file = read("SF:scripts/a.js\nDA:1,1\nDA:2,0\nend_of_record\n").get("scripts/a.js");

        expect(counts(file)).toEqual({ lines: { covered: 1, total: 2 }, branches: { covered: 0, total: 0 } });
    });
});
