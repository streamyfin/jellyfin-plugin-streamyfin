// One command for every measure, the same on every system:
//   bun scripts/measure/cli.js coverage-cs <jf11|jf12>
//   bun scripts/measure/cli.js coverage-js
//   bun scripts/measure/cli.js mutation-cs <shard>
//   bun scripts/measure/cli.js mutation-js <shard>
//   bun scripts/measure/cli.js shards <cs|js>          the CI matrix, as JSON
//   bun scripts/measure/cli.js verdict [--base <ref>]  judges the reports in artifacts/
//   bun scripts/measure/cli.js raise                   lifts tests/floors.json
//   bun scripts/measure/cli.js all [--base <ref>]      every measure, then the verdict

import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { changedLines } from "./diff.js";
import { ownJsFiles, readOwners, unloaded } from "./folders.js";
import { mergeCoverage, parseLcov } from "./lcov.js";
import { readMutants } from "./mutants.js";
import { toRepoPath } from "./paths.js";
import { readSettings, ROOT } from "./settings.js";
import { raisedFloors, summarize } from "./summary.js";
import { coverageCs, coverageJs, mutationCs, mutationJs } from "./tools.js";
import { judge, measureFolders } from "./verdict.js";

const readJson = (path) => JSON.parse(readFileSync(path, "utf8"));

const found = (dir, name) => (existsSync(dir)
    ? [...new Bun.Glob(`**/${name}`).scanSync({ cwd: dir })].map((path) => join(dir, path))
    : []);

const COVERAGE = [["cs-jf11", "coverage.info"], ["cs-jf12", "coverage.info"], ["js", "lcov.info"]];

export const collect = ({ root, settings, owners }) => {
    const toPath = (path) => toRepoPath(path, root);

    const reports = COVERAGE.map(([dir, name]) => {
        const paths = found(join(root, "artifacts", `coverage-${dir}`), name);
        if (paths.length === 0) throw new Error(`artifacts/coverage-${dir} holds no coverage: run bun scripts/measure/cli.js ${dir === "js" ? "coverage-js" : `coverage-cs ${dir.slice(3)}`}`);
        return paths.map((path) => parseLcov(readFileSync(path, "utf8"), toPath));
    });
    const coverage = mergeCoverage(...reports.flat());

    for (const path of ownJsFiles(root)) {
        if (!coverage.has(path)) coverage.set(path, unloaded(readFileSync(join(root, path), "utf8")));
    }

    const mutants = [];
    const missingReports = [];
    const reportOf = {
        cs: (name) => join(root, "artifacts", `mutation-cs-${name}`, "reports", "mutation-report.json"),
        js: (name) => join(root, "artifacts", `mutation-js-${name}`, "mutation.json"),
    };
    for (const kind of ["cs", "js"]) {
        for (const shard of settings.mutation[kind].shards) {
            const path = reportOf[kind](shard.name);
            if (existsSync(path)) mutants.push(...readMutants(readJson(path), toPath));
            else missingReports.push(`${kind}/${shard.name}`);
        }
    }

    return { coverage, mutants, missingReports };
};

export const verdictFor = ({ root, settings, changed }) => {
    const owners = readOwners(root);
    const { coverage, mutants, missingReports } = collect({ root, settings, owners });
    const source = new Map();
    const lineText = (file, line) => {
        if (!source.has(file)) source.set(file, readFileSync(join(root, file), "utf8").split(/\r?\n/));
        return (source.get(file)[line - 1] ?? "").trim();
    };

    const result = judge({
        measured: measureFolders({ coverage, mutants, owners }),
        floors: readJson(join(root, "tests", "floors.json")),
        slack: settings.floors.slack,
        mutants,
        accepted: readJson(join(root, "tests", "accepted-mutants.json")),
        owners,
        changed,
        lineText,
        missingReports,
    });

    const out = join(root, "artifacts", "measure");
    mkdirSync(out, { recursive: true });
    writeFileSync(join(out, "verdict.json"), `${JSON.stringify(result, null, 2)}\n`);
    writeFileSync(join(out, "summary.md"), summarize(result));
    return result;
};

const run = ({ command, cwd, env = {} }) => {
    console.log(`> ${command.join(" ")}`);
    const child = Bun.spawnSync(command, {
        cwd: join(ROOT, cwd),
        env: { ...process.env, ...env },
        stdout: "inherit",
        stderr: "inherit",
    });
    if (child.exitCode !== 0) throw new Error(`${command.slice(0, 2).join(" ")} exited with ${child.exitCode}`);
};

// A run starts from an empty folder: coverlet adds a folder per run, and an old report left
// next to a new one would be read as part of it.
const fresh = (...parts) => {
    const dir = join(ROOT, ...parts);
    rmSync(dir, { recursive: true, force: true });
    mkdirSync(dir, { recursive: true });
};

const shardNamed = (settings, kind, name) => {
    const shard = settings.mutation[kind].shards.find((one) => one.name === name);
    if (!shard) throw new Error(`no ${kind} shard named ${name}: ${settings.mutation[kind].shards.map((one) => one.name).join(", ")}`);
    return shard;
};

const commands = {
    "coverage-cs": (settings, [target]) => {
        if (target !== "jf11" && target !== "jf12") throw new Error("coverage-cs takes jf11 or jf12");
        fresh("artifacts", `coverage-cs-${target}`);
        run(coverageCs({ target }));
    },
    "coverage-js": () => {
        fresh("artifacts", "coverage-js");
        run(coverageJs());
    },
    "mutation-cs": (settings, [name]) => {
        fresh("artifacts", `mutation-cs-${name}`);
        run(mutationCs({ shard: shardNamed(settings, "cs", name), settings, owners: readOwners(ROOT) }));
    },
    "mutation-js": (settings, [name]) => {
        fresh("artifacts", `mutation-js-${name}`);
        const job = mutationJs({ shard: shardNamed(settings, "js", name), settings, base: readJson(join(ROOT, "stryker.config.json")) });
        writeFileSync(join(ROOT, job.configFile), `${JSON.stringify(job.config, null, 2)}\n`);
        run(job);
    },
    shards: (settings, [kind]) => {
        console.log(JSON.stringify(settings.mutation[kind].shards.map((shard) => shard.name)));
    },
    verdict: (settings, args) => {
        const at = args.indexOf("--base");
        const base = at === -1 ? "origin/develop" : args[at + 1];
        // A branch's first push has no commit before it, so no line to compare.
        const changed = /^0*$/.test(base ?? "") ? new Map() : changedLines(base);
        const result = verdictFor({ root: ROOT, settings, changed });
        const summary = readFileSync(join(ROOT, "artifacts", "measure", "summary.md"), "utf8");
        if (process.env.GITHUB_STEP_SUMMARY) writeFileSync(process.env.GITHUB_STEP_SUMMARY, summary, { flag: "a" });
        console.log(summary);
        if (!result.ok) process.exitCode = 1;
    },
    raise: (settings) => {
        const owners = readOwners(ROOT);
        const { coverage, mutants, missingReports } = collect({ root: ROOT, settings, owners });
        if (missingReports.length > 0) throw new Error(`every report is needed to raise the floors, missing: ${missingReports.join(", ")}`);
        const path = join(ROOT, "tests", "floors.json");
        const floors = raisedFloors(readJson(path), measureFolders({ coverage, mutants, owners }));
        writeFileSync(path, `${JSON.stringify(floors, null, 2)}\n`);
        console.log(JSON.stringify(floors, null, 2));
    },
    all: (settings, args) => {
        for (const target of ["jf12", "jf11"]) commands["coverage-cs"](settings, [target]);
        commands["coverage-js"](settings, []);
        for (const shard of settings.mutation.cs.shards) commands["mutation-cs"](settings, [shard.name]);
        for (const shard of settings.mutation.js.shards) commands["mutation-js"](settings, [shard.name]);
        commands.verdict(settings, args);
    },
};

if (import.meta.main) {
    const [name, ...args] = process.argv.slice(2);
    if (!commands[name]) {
        console.error(`usage: bun scripts/measure/cli.js <${Object.keys(commands).join("|")}> [arguments]`);
        process.exit(2);
    }
    commands[name](readSettings(), args);
}
