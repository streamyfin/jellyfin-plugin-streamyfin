// The number a release goes out under. The script normally reads it from the commits
// since the last tag, which answers "what does this change deserve"; a release is
// sometimes a decision instead, and then the version is handed to it.

import { afterAll, describe, expect, test } from "bun:test";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";

const script = resolve("scripts/next-version.js");

const run = (env, cwd) => {
    const result = Bun.spawnSync(["node", script], {
        cwd,
        env: { ...process.env, ...env },
        stdout: "pipe",
        stderr: "pipe",
    });

    return {
        ok: result.exitCode === 0,
        out: new TextDecoder().decode(result.stdout).trim(),
        err: new TextDecoder().decode(result.stderr),
    };
};

const asVersion = (s) => s.split(".").map(Number);

// Compares two four segment versions the way System.Version does, which is the only
// comparison Jellyfin makes when it decides whether an update is available.
const compare = (a, b) => {
    const [x, y] = [asVersion(a), asVersion(b)];
    for (let i = 0; i < 4; i++) {
        if (x[i] !== y[i]) return x[i] - y[i];
    }
    return 0;
};

// A throwaway repository, so a test about tags never depends on the tags this one
// happens to carry and never creates one in it.
const temporaryRepositories = [];

const repositoryWith = (tags) => {
    const dir = mkdtempSync(join(tmpdir(), "next-version-"));
    temporaryRepositories.push(dir);

    const git = (...args) => {
        const result = Bun.spawnSync(["git", ...args], { cwd: dir, stdout: "pipe", stderr: "pipe" });
        if (result.exitCode !== 0) {
            throw new Error(`git ${args.join(" ")} failed: ${new TextDecoder().decode(result.stderr)}`);
        }
        return result;
    };

    git("init", "-q", "-b", "main");
    git("config", "user.email", "test@example.com");
    git("config", "user.name", "test");
    // A developer with commit signing on globally would otherwise have every commit
    // here go through their agent, which fails without a terminal and leaves the
    // repository with no tags and the failure three assertions away from its cause.
    git("config", "commit.gpgsign", "false");
    git("config", "tag.gpgsign", "false");

    for (const [index, tag] of tags.entries()) {
        writeFileSync(join(dir, `f${index}`), `${index}`);
        git("add", ".");
        git("commit", "-q", "-m", `commit ${index}`);
        if (tag) git("tag", tag);
    }

    return dir;
};

afterAll(() => {
    for (const dir of temporaryRepositories) rmSync(dir, { recursive: true, force: true });
});

describe("next-version", () => {
    test("computes a version from the commits when it is given none", () => {
        const { ok, out } = run({ RELEASE_VERSION: "" });

        expect(ok).toBe(true);
        expect(out).toMatch(/^\d+\.\d+\.\d+\.\d+$/);
    });

    test("an explicit version wins, padded to four segments", () => {
        expect(run({ RELEASE_VERSION: "0.70.0.0" }).out).toBe("0.70.0.0");
        expect(run({ RELEASE_VERSION: "0.70" }).out).toBe("0.70.0.0");
        expect(run({ RELEASE_VERSION: "v1.2.3" }).out).toBe("1.2.3.0");
    });

    test("whitespace around it is not a version of its own", () => {
        expect(run({ RELEASE_VERSION: "  0.70.0.0  " }).out).toBe("0.70.0.0");
    });

    // A typo in a release dialog must stop the release rather than tag something
    // nobody meant.
    test("something that is not a version stops the release", () => {
        for (const bad of ["latest", "0.70.0.0.0", "1..2", "0.70.", "-1.0"]) {
            const { ok, err } = run({ RELEASE_VERSION: bad });

            expect(ok).toBe(false);
            expect(err).toContain("RELEASE_VERSION is not a version");
        }
    });
});

// The unstable channel exists so a build of develop can be installed the ordinary way,
// from a manifest of its own. Its whole contract is the version it computes.
describe("next-version, unstable channel", () => {
    // In its own repository rather than this one. A pull request checkout carries no
    // tags, so reading the real history here passed on a developer's machine and failed
    // on CI with "No release tag to count from".
    test("numbers a build above the last release and below the next one", () => {
        const repository = repositoryWith(["0.68.1.0", null, null]);

        const unstable = run({ CHANNEL: "unstable", RELEASE_VERSION: "" }, repository);
        const stable = run({ CHANNEL: "stable", RELEASE_VERSION: "" }, repository);

        expect(unstable.ok).toBe(true);
        expect(unstable.out).toMatch(/^\d+\.\d+\.\d+\.\d+$/);

        // The revision is the commit count since the last release, so it is never zero:
        // zero is what a release itself carries.
        expect(asVersion(unstable.out)[3]).toBeGreaterThan(0);

        // The property the scheme rests on. Jellyfin updates a plugin by comparing
        // versions and nothing else, so a release must outrank every unstable build that
        // led to it. Get this backwards and a tester is stranded on the unstable channel.
        expect(compare(unstable.out, stable.out)).toBeLessThan(0);
    });

    test("an explicit version is refused rather than written into the unstable manifest", () => {
        const { ok, err } = run({ CHANNEL: "unstable", RELEASE_VERSION: "0.70.0.0" });

        expect(ok).toBe(false);
        expect(err).toContain("RELEASE_VERSION cannot be set on the unstable channel");
    });

    test("a channel that is neither stops the build", () => {
        const { ok, err } = run({ CHANNEL: "nightly", RELEASE_VERSION: "" });

        expect(ok).toBe(false);
        expect(err).toContain("CHANNEL is stable or unstable");
    });

    // The regression this whole naming scheme is for. An unstable build is tagged
    // `unstable-<version>` so that `git describe --match '[0-9]*.[0-9]*'` cannot find it.
    // Tag one as `<version>` instead and it becomes the base for the next count: the
    // revision restarts near zero and published versions go backwards.
    test("an unstable tag is not mistaken for the last release", () => {
        const repository = repositoryWith(["0.68.1.0", null, "unstable-0.68.1.2", null]);

        const unstable = run({ CHANNEL: "unstable", RELEASE_VERSION: "" }, repository);
        const stable = run({ CHANNEL: "stable", RELEASE_VERSION: "" }, repository);

        // Three commits past 0.68.1.0, counted from the release and not from the
        // unstable tag sitting between them. Counted from that tag it would be 1, which
        // is below a build already published, and the manifest would stop offering it.
        expect(unstable.out).toBe("0.68.1.3");

        // And the release those commits are heading for still outranks them. None of
        // these subjects is a feat or a break, so the rule gives it a patch bump.
        expect(stable.out).toBe("0.68.2.0");
        expect(compare(unstable.out, stable.out)).toBeLessThan(0);
    });

    test("there is nothing to publish when the last release is the commit being built", () => {
        const repository = repositoryWith(["0.68.1.0"]);

        const { ok, err } = run({ CHANNEL: "unstable", RELEASE_VERSION: "" }, repository);

        expect(ok).toBe(false);
        expect(err).toContain("Nothing to publish");
    });
});
