// The number a release goes out under. The script normally reads it from the commits
// since the last tag, which answers "what does this change deserve"; a release is
// sometimes a decision instead, and then the version is handed to it.

import { describe, expect, test } from "bun:test";

const run = (env) => {
    const result = Bun.spawnSync(["node", "scripts/next-version.js"], {
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
