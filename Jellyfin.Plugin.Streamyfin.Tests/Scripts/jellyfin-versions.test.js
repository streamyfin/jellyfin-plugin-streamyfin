// The watch that says when a Jellyfin line the build does not know about is published.
// It replaced a major number comparison that was blind to everything else, so these
// tests are mostly about the cases that comparison missed.

import { describe, expect, test } from "bun:test";

const { parse, compare, classify, headline, issueBody, planIssue, builtVersionsFrom } = require("../../scripts/jellyfin-versions");

const order = (a, b) => compare(parse(a), parse(b));

describe("comparing NuGet versions", () => {
    test("numbers are compared left to right, padded to four", () => {
        expect(order("12.0.0", "10.11.11")).toBeGreaterThan(0);
        expect(order("10.11.9", "10.11.11")).toBeLessThan(0);
        expect(order("12.0", "12.0.0.0")).toBe(0);
    });

    test("a prerelease is below the release it leads to", () => {
        expect(order("12.0.0-rc7", "12.0.0")).toBeLessThan(0);
        expect(order("13.0.0-rc1", "12.1.0")).toBeGreaterThan(0);
        expect(order("12.0.0-rc1", "12.0.0-rc2")).toBeLessThan(0);
    });

    test("something that is not a version is ignored rather than coerced to zero", () => {
        for (const bad of ["latest", "", "1..2", "-1.0"]) {
            expect(parse(bad)).toBeNull();
        }
    });
});

describe("reading what the build targets", () => {
    test("every target is read, not only the first", () => {
        const props = `
          <PropertyGroup Condition="'$(JellyfinTarget)' == 'jf11'">
            <JellyfinVersion>10.11.9</JellyfinVersion>
          </PropertyGroup>
          <PropertyGroup Condition="'$(JellyfinTarget)' == 'jf12'">
            <JellyfinVersion>12.0.0</JellyfinVersion>
          </PropertyGroup>`;

        expect(builtVersionsFrom(props)).toEqual(["10.11.9", "12.0.0"]);
    });

    test("a file with none of them stops the watch rather than reporting everything", () => {
        expect(() => classify(["12.0.0"], builtVersionsFrom("<Project></Project>"))).toThrow();
    });

    // Skipping it would drop a line out of the known set, and the report would then
    // announce a line this repository builds against as one that needs a new target.
    test("a declaration that is not a version stops the watch too", () => {
        expect(() => classify(["12.0.0"], ["10.11.9", "not-a-version"])).toThrow();
    });
});

describe("classifying what is published", () => {
    const built = ["10.11.9", "12.0.0"];

    test("nothing to say when the newest published is the newest built", () => {
        const { newLines, newerInLine } = classify(["10.11.11", "12.0.0"], built);

        expect(newLines).toEqual([]);
        expect(newerInLine).toEqual([]);
    });

    // The one the shell version missed. 12.1.0 was published and nothing said anything,
    // because 12 was already a known major.
    test("a newer minor inside a line already built is reported", () => {
        const { newLines, newerInLine } = classify(["12.0.0", "12.1.0"], built);

        expect(newLines).toEqual([]);
        expect(newerInLine).toEqual(["12.1.0"]);
    });

    // Comparing against the single newest built version, 12.0.0, put this below it and
    // discarded it, although it is a new minor of a line this repository builds.
    test("a newer minor of the older line is not hidden by the newer line", () => {
        expect(classify(["10.12.0"], built).newerInLine).toEqual(["10.12.0"]);
    });

    // The floor of a line is deliberately old: 10.11.9 rather than 10.11.11, because
    // that is the oldest server the plugin actually uses. Reporting every patch above it
    // means repeating a decision that was already taken, every day.
    test("a patch above a deliberate floor is not news", () => {
        const { newLines, newerInLine } = classify(["10.11.10", "10.11.11"], built);

        expect(newLines).toEqual([]);
        expect(newerInLine).toEqual([]);
    });

    test("a line nobody builds against is reported apart, since it needs a new target", () => {
        const { newLines, newerInLine } = classify(["12.1.0", "13.0.0"], built);

        expect(newLines).toEqual(["13.0.0"]);
        expect(newerInLine).toEqual(["12.1.0"]);
    });

    // The moment a 13 target becomes possible at all is the first 13 prerelease, not the
    // release, so a prerelease of an unknown line counts as news.
    test("a prerelease of a new line counts", () => {
        expect(classify(["13.0.0-rc1"], built).newLines).toEqual(["13.0.0-rc1"]);
    });

    // A major older than everything built here is a line that was dropped on purpose.
    test("an older line is not news", () => {
        const { newLines, newerInLine } = classify(["10.8.0", "10.9.0", "10.11.11"], built);

        expect(newLines).toEqual([]);
        expect(newerInLine).toEqual([]);
    });

    // The caller hands in the union of the releases feed and the prerelease feed, and a
    // version published to both is one version, not two entries in the issue title.
    test("a version published to both feeds is named once", () => {
        const { newerInLine } = classify(["12.1.0", "12.1.0"], built);

        expect(newerInLine).toEqual(["12.1.0"]);
    });

    test("the report is ordered oldest first, so the issue reads in order", () => {
        const { newLines } = classify(["13.1.0", "13.0.0-rc1", "13.0.0"], built);

        expect(newLines).toEqual(["13.0.0-rc1", "13.0.0", "13.1.0"]);
    });
});

describe("the title an issue about it gets", () => {
    test("one version is named", () => {
        expect(headline({ newLines: ["13.0.0"], newerInLine: [] })).toBe(
            "Jellyfin.Controller 13.0.0 is on NuGet",
        );
    });

    test("several are the newest and a count, so the search stays short", () => {
        const weekly = Array.from(
            { length: 20 },
            (_, i) => `10.12.0-202511${String(i + 1).padStart(2, "0")}051322`,
        );

        const title = headline({ newLines: [], newerInLine: weekly });

        expect(title).toBe("Jellyfin.Controller 10.12.0-20251120051322 and 19 more are on NuGet");
        expect(title.length).toBeLessThan(120);
    });

    test("a new line is named over anything inside a line already built", () => {
        expect(headline({ newLines: ["13.0.0"], newerInLine: ["10.12.0", "10.12.1"] })).toBe(
            "Jellyfin.Controller 13.0.0 is on NuGet",
        );
    });

    test("nothing new has no title", () => {
        expect(headline({ newLines: [], newerInLine: [] })).toBeNull();
        expect(headline({})).toBeNull();
    });
});

describe("the body of that issue", () => {
    // What #202 found on 2026-09-28: three weekly builds of a new line, and the weekly
    // builds of 10.12 from before Jellyfin renamed it 12, next to a release of 12.1.
    const weekly = (line, first, count) =>
        Array.from({ length: count }, (_, i) => {
            const day = new Date(Date.parse(first) + i * 7 * 86_400_000);
            return `${line}-${day.toISOString().slice(0, 10).replaceAll("-", "")}051416`;
        });
    const thirteen = weekly("13.0.0", "2026-09-14", 3);
    const tenTwelve = weekly("10.12.0", "2025-10-27", 28);
    const report = {
        built: ["10.11.9", "12.0.0"],
        newLines: thirteen,
        newerInLine: [...tenTwelve, "12.1.0"],
        prereleaseOnly: [...thirteen, ...tenTwelve],
    };
    const rows = (body) => body.split("\n").filter((line) => line.startsWith("| "));

    test("each line is one row, with how many versions and the newest", () => {
        expect(rows(issueBody(report))).toContain("| 13.x | 3 | 13.0.0-20260928051416 | prerelease feed |");
    });

    test("a line already built names what it is built against", () => {
        const body = rows(issueBody(report));

        expect(body).toContain("| 10.x | 10.11.9 | 28 | 10.12.0-20260504051416 | prerelease feed |");
        expect(body).toContain("| 12.x | 12.0.0 | 1 | 12.1.0 | nuget.org |");
    });

    test("a line with versions on both feeds says so", () => {
        const mixed = { built: ["12.0.0"], newLines: [], newerInLine: ["12.1.0", "12.2.0-rc1"], prereleaseOnly: ["12.2.0-rc1"] };

        expect(rows(issueBody(mixed))).toContain("| 12.x | 12.0.0 | 2 | 12.2.0-rc1 | both |");
    });

    test("every version is still listed, folded away under the tables", () => {
        const body = issueBody(report);
        const folded = body.slice(body.indexOf("<details>"));

        for (const version of [...report.newLines, ...report.newerInLine]) {
            expect(folded).toContain(`| ${version} |`);
        }
        expect(folded).toContain("<summary>Every version found (32)</summary>");
    });

    test("a section with nothing in it is left out", () => {
        const body = issueBody({ ...report, newLines: [] });

        expect(body).not.toContain("A line nothing here builds against");
        expect(body).toContain("### Newer inside a line already built");
    });

    test("the headings no longer carry the versions", () => {
        const headings = issueBody(report).split("\n").filter((line) => line.startsWith("###"));

        expect(headings).toEqual(["### A line nothing here builds against", "### Newer inside a line already built"]);
    });

    test("it keeps saying what the repository builds against and where the procedure is", () => {
        const body = issueBody(report);

        expect(body.startsWith("This repository builds against **10.11.9** and **12.0.0**.")).toBe(true);
        expect(body).toContain("Compat/README.md#adding-a-jellyfin-line-when-the-time-comes");
        expect(body.trimEnd().endsWith("Closing it is the right answer once it has been read.")).toBe(true);
    });
});

describe("which issue says it", () => {
    // One issue, kept up to date, rather than one a week: the title names the newest
    // build, so every weekly build of a new line used to open another (#193, #201, #202).
    const watch = (number, state, title, body = "old") => ({
        number,
        state,
        title,
        body,
        user: { login: "github-actions[bot]" },
    });
    const title = "Jellyfin.Controller 13.0.0-20260928112619 and 2 more are on NuGet";
    const body = "the tables";

    test("with none yet, one is opened", () => {
        expect(planIssue({ title, body, issues: [] })).toEqual({ create: true, close: [] });
    });

    test("an open one is brought up to date rather than a second one opened", () => {
        const issues = [watch(201, "open", "Jellyfin.Controller 13.0.0-20260921102409 and 1 more are on NuGet")];

        expect(planIssue({ title, body, issues })).toEqual({ update: 201, keep: 201, close: [] });
    });

    test("an open one that already says this is left alone", () => {
        const issues = [watch(202, "open", title, "the tables\r\n")];

        expect(planIssue({ title, body, issues })).toEqual({ keep: 202, close: [], reason: "#202 already says this" });
    });

    test("of several open, the newest is kept and the others are closed as superseded by it", () => {
        const issues = [
            watch(201, "open", "Jellyfin.Controller 13.0.0-20260921102409 and 1 more are on NuGet"),
            watch(202, "open", "Jellyfin.Controller 13.0.0-20260928112619 and 2 more are on NuGet"),
        ];

        expect(planIssue({ title, body, issues })).toEqual({ update: 202, keep: 202, close: [201] });
    });

    test("the ones closed as superseded name the one kept, even when it needs no update", () => {
        const issues = [
            watch(201, "open", "Jellyfin.Controller 13.0.0-20260921102409 and 1 more are on NuGet"),
            watch(202, "open", title, body),
        ];

        expect(planIssue({ title, body, issues })).toEqual({ keep: 202, close: [201], reason: "#202 already says this" });
    });

    test("the last one closed with the same news is not said again", () => {
        const issues = [watch(193, "closed", "Jellyfin.Controller 13.0.0-20260914101923 is on NuGet"), watch(202, "closed", title)];

        expect(planIssue({ title, body, issues })).toEqual({ close: [], reason: "#202 said this and was closed" });
    });

    test("news since the last one was closed opens a new one", () => {
        const issues = [watch(193, "closed", "Jellyfin.Controller 13.0.0-20260914101923 is on NuGet")];

        expect(planIssue({ title, body, issues })).toEqual({ create: true, close: [] });
    });

    test("what is not this watch's is left out of it", () => {
        const issues = [
            { ...watch(150, "open", title), pull_request: {} },
            { ...watch(151, "open", title), user: { login: "someone" } },
            watch(152, "open", "Jellyfin.Controller is slow on NuGet today, anyone else?", "a question"),
        ];

        expect(planIssue({ title, body, issues })).toEqual({ create: true, close: [] });
    });
});
