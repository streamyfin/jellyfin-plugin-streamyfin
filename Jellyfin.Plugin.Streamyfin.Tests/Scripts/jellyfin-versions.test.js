// The watch that says when a Jellyfin line the build does not know about is published.
// It replaced a major number comparison that was blind to everything else, so these
// tests are mostly about the cases that comparison missed.

import { describe, expect, test } from "bun:test";

const { parse, compare, classify, headline, builtVersionsFrom } = require("../../scripts/jellyfin-versions");

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
