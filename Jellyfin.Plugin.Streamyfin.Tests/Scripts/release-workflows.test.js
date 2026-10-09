// The two workflows that publish: release.yml cuts a stable release from main, and
// prerelease.yml puts a build of develop on the unstable channel. Neither runs on a
// pull request, so the first run of a broken one is a publication. A step that fails
// there fails after a public tag, a commit on main and a release already exist.

import { describe, expect, test } from "bun:test";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const workflows = ["release.yml", "prerelease.yml"];

const jobsOf = (name) => Bun.YAML.parse(readFileSync(resolve(".github/workflows", name), "utf8")).jobs;

// The Makefile reads VERSION from the environment and computes one from the commits
// since the last tag when there is none. Once somebody types a version into the
// dispatch form, that computed number is a different one, so a job that leaves make
// to compute it names and stamps its zips with a version nobody released.
const settledVersion = "${{ needs.version.outputs.version }}";

// A VERSION set inside the command wins over the job's: on make's command line, as a
// prefix to the command, or exported first.
const versionAssigned = /(^|[\s;&|(])(export\s+)?VERSION=/m;

const makeStepsWithoutTheVersion = (jobs) => {
    const missing = [];
    for (const [id, job] of Object.entries(jobs)) {
        if (id === "version") continue;
        const needs = [job.needs ?? []].flat();
        for (const step of job.steps ?? []) {
            const run = step.run ?? "";
            if (!/\bmake\b/.test(run)) continue;
            const env = { ...(job.env ?? {}), ...(step.env ?? {}) };
            // Without the dependency the expression reads as an empty string.
            const settled = env.VERSION === settledVersion && needs.includes("version");
            if (!settled || versionAssigned.test(run)) missing.push(`${id}: ${step.name ?? run}`);
        }
    }
    return missing;
};

// The paths a `git commit` names after its options. Git refuses the whole commit when
// one of them is unknown to it, so a file that is only on disk does not count. A
// variable cannot be checked here, so those are left out.
const committedPaths = (run) => {
    const paths = [];
    for (const line of run.split("\n")) {
        const command = line.trim().match(/^git commit\b(.*)$/);
        if (!command) continue;
        const words = command[1].match(/"[^"]*"|'[^']*'|\S+/g) ?? [];
        for (let i = 0; i < words.length; i++) {
            if (["-m", "--message", "-F", "--file", "-C", "-c"].includes(words[i])) {
                i++;
                continue;
            }
            if (words[i].startsWith("-") || words[i].includes("$")) continue;
            paths.push(words[i]);
        }
    }
    return paths;
};

const knownToGit = (path) =>
    Bun.spawnSync(["git", "ls-files", "--error-unmatch", "--", path], { stdout: "ignore", stderr: "ignore" })
        .exitCode === 0;

// The version has to be computed in an assignment of its own. In any other position, an
// argument to echo for one, the step's shell ignores the script's exit code, so a version
// it refuses comes out empty from a step that succeeded.
const versionComputedInPassing = (run) =>
    run
        .split("\n")
        .filter((line) => line.includes("next-version.js"))
        .filter((line) => !/^\s*[A-Za-z_]\w*=\$\(node scripts\/next-version\.js\)\s*$/.test(line));

describe.each(workflows)("%s", (name) => {
    const jobs = jobsOf(name);
    const steps = Object.values(jobs).flatMap((job) => job.steps ?? []);

    test("every step that runs make gets the version the version job settled", () => {
        expect(makeStepsWithoutTheVersion(jobs)).toEqual([]);
    });

    test("every file a step commits by name is known to git", () => {
        const unknown = steps
            .flatMap((step) => committedPaths(step.run ?? ""))
            .filter((path) => !knownToGit(path));

        expect(unknown).toEqual([]);
    });

    test("a version the script refuses fails the step that computes it", () => {
        expect(steps.flatMap((step) => versionComputedInPassing(step.run ?? ""))).toEqual([]);
    });
});

describe("the checks themselves", () => {
    const build = (step, job = {}) => ({ version: { steps: [] }, build: { needs: "version", ...job, steps: [step] } });

    test("a job that leaves the version to make is caught", () => {
        expect(makeStepsWithoutTheVersion(build({ name: "Build", run: "make build" }))).toEqual(["build: Build"]);
    });

    test("the version can come from the job or from the step", () => {
        const fromJob = build({ run: "make update-manifest" }, { env: { VERSION: settledVersion } });
        const fromStep = build({ env: { VERSION: settledVersion }, run: "make zip" });

        expect(makeStepsWithoutTheVersion(fromJob)).toEqual([]);
        expect(makeStepsWithoutTheVersion(fromStep)).toEqual([]);
    });

    test("a job that does not wait for the version job is caught", () => {
        const jobs = build({ name: "Zip", env: { VERSION: settledVersion }, run: "make zip" }, { needs: ["other"] });

        expect(makeStepsWithoutTheVersion(jobs)).toEqual(["build: Zip"]);
    });

    test("a version set inside the command is caught", () => {
        for (const run of ["make zip VERSION=1.0.0.0", "VERSION=1.0.0.0 make zip", "export VERSION=1.0.0.0\nmake zip"]) {
            const jobs = build({ name: "Zip", env: { VERSION: settledVersion }, run });

            expect(makeStepsWithoutTheVersion(jobs)).toEqual(["build: Zip"]);
        }
    });

    test("only the paths of a commit are read, not its message or its options", () => {
        const run = 'git add x\ngit commit -m "new release: ${VERSION} manifests" manifest.json old.json\ngit push';

        expect(committedPaths(run)).toEqual(["manifest.json", "old.json"]);
    });

    test("a file on disk that git does not track is not taken for a known one", () => {
        expect(knownToGit("Makefile")).toBe(true);
        expect(knownToGit("node_modules")).toBe(false);
    });

    test("a version computed inside echo's argument is caught, an assignment is not", () => {
        expect(versionComputedInPassing('echo "version=$(node scripts/next-version.js)" >> "$GITHUB_OUTPUT"')).toHaveLength(1);
        expect(versionComputedInPassing('version=$(node scripts/next-version.js)\necho "version=$version"')).toEqual([]);
    });
});
