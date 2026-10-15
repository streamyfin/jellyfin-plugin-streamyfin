// The workflow that measures every pull request: each measure in a job of its own, through
// the command line, and one verdict once they are all in.

import { describe, expect, test } from "bun:test";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { MARKER } from "../../scripts/measure/summary.js";

const workflow = Bun.YAML.parse(readFileSync(resolve(import.meta.dir, "../../.github/workflows/measure.yml"), "utf8"));
const steps = (job) => workflow.jobs[job].steps;
const commands = (job) => steps(job).flatMap((step) => (step.run ? [step.run.trim()] : []));

describe("the measure workflow", () => {
    test("pins every action to a commit", () => {
        const unpinned = Object.values(workflow.jobs)
            .flatMap((job) => job.steps)
            .flatMap((step) => (step.uses ? [step.uses] : []))
            .filter((uses) => !/@[0-9a-f]{40}$/.test(uses));

        expect(unpinned).toEqual([]);
    });

    test("runs each measure through the command line", () => {
        expect(commands("unit-cs")).toContain("bun scripts/measure/cli.js coverage-cs ${{ matrix.target }}");
        expect(commands("unit-js")).toContain("bun scripts/measure/cli.js coverage-js");
        expect(commands("mutation-cs")).toContain("bun scripts/measure/cli.js mutation-cs ${{ matrix.shard }}");
        expect(commands("mutation-js")).toContain("bun scripts/measure/cli.js mutation-js ${{ matrix.shard }}");
        expect(commands("verdict")).toContain('bun scripts/measure/cli.js verdict --base "$BASE"');
    });

    test("covers both targets, and mutates the shards the settings list", () => {
        expect(workflow.jobs["unit-cs"].strategy.matrix.include.map((one) => one.target)).toEqual(["jf11", "jf12"]);
        expect(workflow.jobs["mutation-cs"].strategy.matrix.shard).toBe("${{ fromJSON(needs.shards.outputs.cs) }}");
        expect(workflow.jobs["mutation-js"].strategy.matrix.shard).toBe("${{ fromJSON(needs.shards.outputs.js) }}");
    });

    test("judges once every measure is in, a failed one included", () => {
        expect(workflow.jobs.verdict.needs).toEqual(["unit-cs", "unit-cs-windows", "unit-js", "mutation-cs", "mutation-js"]);
        expect(workflow.jobs.verdict.if).toBe("always()");
    });

    // The verdict reads artifacts/<name>/, where download-artifact puts each one.
    test("uploads each report under the folder the verdict reads", () => {
        const uploads = [
            ["unit-cs", "coverage-cs-${{ matrix.target }}"],
            ["unit-js", "coverage-js"],
            ["mutation-cs", "mutation-cs-${{ matrix.shard }}"],
            ["mutation-js", "mutation-js-${{ matrix.shard }}"],
        ];

        for (const [job, name] of uploads) {
            const upload = steps(job).find((step) => step.uses?.startsWith("actions/upload-artifact@"));
            expect(upload.with).toMatchObject({ name, path: `artifacts/${name}/` });
        }
    });

    test("writes only to a pull request from a branch here, and finds its comment by the marker", () => {
        const say = steps("verdict").find((step) => step.name === "Say it on the pull request");

        expect(workflow.permissions).toEqual({ contents: "read" });
        expect(workflow.jobs.verdict.permissions).toEqual({ contents: "read", "pull-requests": "write" });
        expect(say.if).toContain("github.event.pull_request.head.repo.full_name == github.repository");
        expect(say.with.script).toContain(MARKER);
    });
});
