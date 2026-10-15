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
import { folderOf, ownJsFiles, readOwners, unloaded } from "./folders.js";
import { mergeCoverage, parseLcov } from "./lcov.js";
import { readMutants } from "./mutants.js";
import { toRepoPath } from "./paths.js";
import { readSettings, ROOT } from "./settings.js";
import { raisedFloors, summarize } from "./summary.js";
import { coverageCs, coverageJs, mutationCs, mutationJs } from "./tools.js";
import { judge, measureFolders } from "./verdict.js";

const readJson = (path) => JSON.parse(readFileSync(path));

const found = (dir, name) => (existsSync(dir)
    ? [...new Bun.Glob(`**/${name}`).scanSync({ cwd: dir })].map((path) => join(dir, path))
    : []);

const COVERAGE = [["cs-jf11", "coverage.info"], ["cs-jf12", "coverage.info"], ["js", "lcov.info"]];

export const collect = ({ root, settings, owners }) => {
    const toPath = (path) => toRepoPath(path, root);

    const reports = COVERAGE.map(([dir, name]) => {
        const paths = found(join(root, "artifacts", `coverage-${dir}`), name);
        const rerun = `run bun scripts/measure/cli.js ${dir === "js" ? "coverage-js" : `coverage-cs ${dir.slice(3)}`}`;
        if (paths.length === 0) throw new Error(`artifacts/coverage-${dir} holds no coverage: ${rerun}`);
        const files = paths.map((path) => parseLcov(readFileSync(path, "utf8"), toPath));
        // A target whose tests never ran writes a report all the same, with nothing of ours in it.
        if (!files.some((report) => [...report.keys()].some((path) => folderOf(path) !== null))) {
            throw new Error(`artifacts/coverage-${dir} covers no file of the repository: ${rerun}`);
        }
        return files;
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

// One tool, from the folder its job names, with what the job adds to the environment. Its
// output goes straight to this process's, so a long run shows as it goes.
export const runTool = ({ command, cwd, env = {} }, { root = ROOT, print = console.log } = {}) => {
    print(`> ${command.join(" ")}`);
    const child = Bun.spawnSync(command, {
        cwd: join(root, cwd),
        env: { ...process.env, ...env },
        stdout: "inherit",
        stderr: "inherit",
    });
    if (child.exitCode !== 0) throw new Error(`${command.slice(0, 2).join(" ")} exited with ${child.exitCode}`);
};

const shardNamed = (settings, kind, name) => {
    const shard = settings.mutation[kind].shards.find((one) => one.name === name);
    if (!shard) throw new Error(`no ${kind} shard named ${name}: ${settings.mutation[kind].shards.map((one) => one.name).join(", ")}`);
    return shard;
};

// Every command, given what it touches: the repository, where it prints, how a tool runs,
// the environment and how the changed lines are found. Each returns its exit code.
export const commandsFor = ({ root = ROOT, print = console.log, run = runTool, env = process.env, changed = changedLines } = {}) => {
    const tool = (job) => run(job, { root, print });
    // A run starts from an empty folder: coverlet adds a folder per run, and an old report
    // left next to a new one would be read as part of it.
    const fresh = (...parts) => {
        const dir = join(root, ...parts);
        rmSync(dir, { recursive: true, force: true });
        mkdirSync(dir, { recursive: true });
    };
    const runWithConfig = (job) => {
        writeFileSync(join(root, job.configFile), `${JSON.stringify(job.config, null, 2)}\n`);
        tool(job);
    };

    const commands = {
        "coverage-cs": (settings, [target]) => {
            if (target !== "jf11" && target !== "jf12") throw new Error("coverage-cs takes jf11 or jf12");
            fresh("artifacts", `coverage-cs-${target}`);
            tool(coverageCs({ target }));
            return 0;
        },
        "coverage-js": () => {
            fresh("artifacts", "coverage-js");
            tool(coverageJs());
            return 0;
        },
        "mutation-cs": (settings, [name]) => {
            const shard = shardNamed(settings, "cs", name);
            fresh("artifacts", `mutation-cs-${name}`);
            runWithConfig(mutationCs({
                shard,
                settings,
                owners: readOwners(root),
                base: readJson(join(root, "Jellyfin.Plugin.Streamyfin.Tests", "stryker-config.json")),
            }));
            return 0;
        },
        "mutation-js": (settings, [name]) => {
            const shard = shardNamed(settings, "js", name);
            fresh("artifacts", `mutation-js-${name}`);
            runWithConfig(mutationJs({ shard, settings, base: readJson(join(root, "stryker.config.json")) }));
            return 0;
        },
        shards: (settings, [kind]) => {
            print(JSON.stringify(settings.mutation[kind].shards.map((shard) => shard.name)));
            return 0;
        },
        verdict: (settings, args) => {
            const at = args.indexOf("--base");
            const base = at === -1 ? "origin/develop" : args[at + 1];
            if (!base) throw new Error("verdict needs a base to compare with: --base <commit>");
            // A branch's first push names forty zeros as the commit before it: no line to compare.
            const result = verdictFor({ root, settings, changed: /^0{40}$/.test(base) ? new Map() : changed(base, { cwd: root }) });
            const summary = readFileSync(join(root, "artifacts", "measure", "summary.md"), "utf8");
            if (env.GITHUB_STEP_SUMMARY) writeFileSync(env.GITHUB_STEP_SUMMARY, summary, { flag: "a" });
            print(summary);
            return result.ok ? 0 : 1;
        },
        raise: (settings) => {
            const owners = readOwners(root);
            const { coverage, mutants, missingReports } = collect({ root, settings, owners });
            if (missingReports.length > 0) throw new Error(`every report is needed to raise the floors, missing: ${missingReports.join(", ")}`);
            const path = join(root, "tests", "floors.json");
            const floors = raisedFloors(readJson(path), measureFolders({ coverage, mutants, owners }));
            writeFileSync(path, `${JSON.stringify(floors, null, 2)}\n`);
            print(JSON.stringify(floors, null, 2));
            return 0;
        },
        all: (settings, args) => {
            for (const target of ["jf12", "jf11"]) commands["coverage-cs"](settings, [target]);
            commands["coverage-js"](settings);
            for (const shard of settings.mutation.cs.shards) commands["mutation-cs"](settings, [shard.name]);
            for (const shard of settings.mutation.js.shards) commands["mutation-js"](settings, [shard.name]);
            return commands.verdict(settings, args);
        },
    };
    return commands;
};

if (import.meta.main) {
    const [name, ...args] = process.argv.slice(2);
    const commands = commandsFor();
    if (!Object.hasOwn(commands, name)) {
        console.error(`usage: bun scripts/measure/cli.js <${Object.keys(commands).join("|")}> [arguments]`);
        process.exit(2);
    }
    process.exitCode = commands[name](readSettings(), args);
}
