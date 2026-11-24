// The comment a pull request gets about its own build. What it says depends on the state
// of the two build jobs at the moment it is written, and the same render runs twice, once
// while the builds are starting and once when they are done, so the part that decides
// whether a render may replace what is already there matters as much as the table.

import { describe, expect, test } from "bun:test";
import { createRequire } from "node:module";

const { commentBody, replaces } = createRequire(import.meta.url)("../../scripts/build-comment.js");

const run = {
    owner: "streamyfin",
    repo: "jellyfin-plugin-streamyfin",
    runId: 42,
    runAttempt: 1,
    sha: "0123456789abcdef0123456789abcdef01234567",
};

const job = (name, status, conclusion, minutes) => ({
    name,
    status,
    conclusion,
    html_url: `https://github.com/streamyfin/jellyfin-plugin-streamyfin/actions/runs/42/job/${name}`,
    started_at: status === "queued" ? null : "2026-09-17T18:00:00Z",
    completed_at: conclusion ? new Date(Date.parse("2026-09-17T18:00:00Z") + minutes * 60000).toISOString() : null,
});

const artifact = (name, id, bytes) => ({ name, id, size_in_bytes: bytes, expired: false });

const both = (state) => [job("jf11", ...state), job("jf12", ...state)];

describe("what the comment says about a build", () => {
    test("a finished build links its download, with the size and how long it took", () => {
        const text = commentBody({
            ...run,
            jobs: [job("jf11", "completed", "success", 1), job("jf12", "completed", "success", 1)],
            artifacts: [
                artifact("Jellyfin.Plugin.Streamyfin-jf11", 111, 5 * 1024 * 1024),
                artifact("Jellyfin.Plugin.Streamyfin-jf12", 222, 6 * 1024 * 1024),
            ],
        });

        expect(text).toContain("/actions/runs/42/artifacts/111");
        expect(text).toContain("5.0 MB");
        expect(text).toContain("1m 0s");
        expect(text).toContain("Jellyfin 10.11.9 and later");
        expect(text).toContain("Jellyfin 12");
        expect(text).toContain("final=true");
    });

    test("a build still running says so, and how long it usually takes", () => {
        const text = commentBody({
            ...run,
            jobs: both(["in_progress", null, 0]),
            artifacts: [],
            reference: { jf11: 52000, jf12: 55000 },
        });

        expect(text).toContain("Building");
        expect(text).toContain("0m 52s");
        expect(text).toContain("final=false");
    });

    test("a queued build says it has not started, without inventing a duration", () => {
        const text = commentBody({ ...run, jobs: both(["queued", null, 0]), artifacts: [] });

        expect(text).toContain("Queued");
        expect(text).not.toContain("usually");
        expect(text).toContain("final=false");
    });

    test("a failed build links its log and offers nothing to download", () => {
        const text = commentBody({
            ...run,
            jobs: [job("jf11", "completed", "failure", 2), job("jf12", "completed", "success", 1)],
            artifacts: [artifact("Jellyfin.Plugin.Streamyfin-jf12", 222, 1024)],
        });

        expect(text).toContain("Failed");
        expect(text).toContain("/actions/runs/42/job/jf11");
        expect(text).not.toContain("/artifacts/111");
        expect(text).toContain("final=true");
    });

    test("a cancelled build is named as cancelled", () => {
        const text = commentBody({ ...run, jobs: both(["completed", "cancelled", 1]), artifacts: [] });

        expect(text).toContain("Cancelled");
    });

    test("a build whose artifact has not arrived yet is not the last word", () => {
        const text = commentBody({
            ...run,
            jobs: both(["completed", "success", 1]),
            artifacts: [artifact("Jellyfin.Plugin.Streamyfin-jf11", 111, 1024)],
        });

        expect(text).toContain("Uploading");
        expect(text).toContain("final=false");
    });

    test("a job this run never reported is pending rather than missing", () => {
        const text = commentBody({ ...run, jobs: [], artifacts: [] });

        expect(text).toContain("Pending");
        expect(text).toContain("Jellyfin 12");
        expect(text).toContain("final=false");
    });

    test("it says where the files go and how long they are kept", () => {
        const text = commentBody({ ...run, jobs: both(["completed", "success", 1]), artifacts: [
            artifact("Jellyfin.Plugin.Streamyfin-jf11", 111, 1024),
            artifact("Jellyfin.Plugin.Streamyfin-jf12", 222, 1024),
        ] });

        expect(text).toContain("Streamyfin_");
        expect(text).toContain("meta.json");
        expect(text).toContain("7 days");
    });

    test("the marker carries the run and the attempt, so a re-run can tell itself apart", () => {
        const text = commentBody({ ...run, runAttempt: 3, jobs: [], artifacts: [] });

        expect(text).toContain("<!-- streamyfin-plugin-build run=42 attempt=3 final=false -->");
    });

    test("the commit is named, and linked", () => {
        const text = commentBody({ ...run, jobs: [], artifacts: [] });

        expect(text).toContain("0123456");
        expect(text).toContain("/commit/0123456789abcdef0123456789abcdef01234567");
    });
});

describe("whether a render may replace what is already there", () => {
    const marker = (runId, attempt, final) =>
        `body\n<!-- streamyfin-plugin-build run=${runId} attempt=${attempt} final=${final} -->`;

    test("a newer run replaces an older one's comment", () => {
        expect(replaces(marker(41, 1, true), { runId: 42, runAttempt: 1, final: false })).toBe(true);
    });

    test("an older run leaves a newer one's comment alone", () => {
        expect(replaces(marker(43, 1, false), { runId: 42, runAttempt: 1, final: true })).toBe(false);
    });

    test("the same run's first render does not undo its own last word", () => {
        expect(replaces(marker(42, 1, true), { runId: 42, runAttempt: 1, final: false })).toBe(false);
    });

    test("the same run writes again while nothing is final", () => {
        expect(replaces(marker(42, 1, false), { runId: 42, runAttempt: 1, final: false })).toBe(true);
    });

    test("a re-run replaces the attempt before it", () => {
        expect(replaces(marker(42, 1, true), { runId: 42, runAttempt: 2, final: false })).toBe(true);
    });

    test("a comment without the marker is replaced, which is how the old one goes", () => {
        expect(replaces("**This branch is built.** ...", { runId: 42, runAttempt: 1, final: false })).toBe(true);
    });
});
