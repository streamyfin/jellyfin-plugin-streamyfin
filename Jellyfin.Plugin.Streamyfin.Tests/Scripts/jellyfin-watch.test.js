// The script that reads the two feeds. classify is tested next door; what is left here
// is the part that decides what happens when one of the feeds does not answer, which is
// the failure this watch is least able to notice: an unread feed looks exactly like a
// feed with nothing new on it.

import { describe, expect, test } from "bun:test";
import { resolve } from "node:path";

const script = resolve("scripts/jellyfin-watch.js");

// Small enough to inline, and it keeps the test off the network.
const feed = (...versions) =>
    `data:application/json,${encodeURIComponent(JSON.stringify({ versions }))}`;

const run = (env) => {
    const result = Bun.spawnSync(["node", script], {
        env: { ...process.env, GITHUB_TOKEN: "", GH_TOKEN: "", ...env },
        stdout: "pipe",
        stderr: "pipe",
    });

    return {
        ok: result.exitCode === 0,
        report: result.exitCode === 0 ? JSON.parse(new TextDecoder().decode(result.stdout)) : null,
        err: new TextDecoder().decode(result.stderr),
    };
};

describe("reading both Jellyfin feeds", () => {
    test("with no token the prerelease feed is skipped, not silently empty", () => {
        const { ok, report } = run({ NUGET_FEED: feed("12.0.0", "12.1.0") });

        expect(ok).toBe(true);
        expect(report.unreadable).toContain("no token");
        expect(report.newerInLine).toEqual(["12.1.0"]);
    });

    // GitHub Packages refuses anonymous reads even for a public package, so a token that
    // lacks read:packages gets a 403. That must not turn the job red every morning while
    // the releases half still answers.
    test("a prerelease feed that refuses is reported and does not stop the watch", () => {
        const { ok, report } = run({
            NUGET_FEED: feed("12.0.0", "12.1.0"),
            NUGET_PRERELEASE_FEED: "https://nuget.pkg.github.invalid/does-not-resolve",
            GITHUB_TOKEN: "pretend",
        });

        expect(ok).toBe(true);
        expect(report.unreadable).toBeTruthy();
        expect(report.newerInLine).toEqual(["12.1.0"]);
    });

    // The case the watch exists for, and the one it missed: a line that is only on the
    // prerelease feed. 13.0.0-20260914101923 was published on 2026-09-14 and nuget.org
    // has never heard of it.
    test("a line seen only on the prerelease feed is reported, and said to be only there", () => {
        const { ok, report } = run({
            NUGET_FEED: feed("12.0.0"),
            NUGET_PRERELEASE_FEED: feed("12.0.0-20260907095649", "13.0.0-20260914101923"),
            GITHUB_TOKEN: "pretend",
        });

        expect(ok).toBe(true);
        expect(report.unreadable).toBeNull();
        expect(report.newLines).toEqual(["13.0.0-20260914101923"]);
        expect(report.prereleaseOnly).toEqual(["13.0.0-20260914101923"]);
    });

    test("a version on both feeds is not marked as prerelease only", () => {
        const { report } = run({
            NUGET_FEED: feed("12.0.0", "12.1.0"),
            NUGET_PRERELEASE_FEED: feed("12.1.0"),
            GITHUB_TOKEN: "pretend",
        });

        expect(report.newerInLine).toEqual(["12.1.0"]);
        expect(report.prereleaseOnly).toEqual([]);
    });

    // The releases feed is the half that must work. Failing quietly there would mean a
    // green job reporting that nothing was published.
    test("a releases feed that refuses stops the watch", () => {
        const { ok, err } = run({ NUGET_FEED: "https://api.nuget.invalid/does-not-resolve" });

        expect(ok).toBe(false);
        expect(err).toBeTruthy();
    });
});
