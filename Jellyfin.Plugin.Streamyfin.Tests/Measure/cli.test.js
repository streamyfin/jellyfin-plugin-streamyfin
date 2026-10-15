// The verdict from the reports on disk, as the command line reads them, and every command,
// run against a throwaway repository with a run that records the tools instead of starting them.

import { afterEach, describe, expect, test } from "bun:test";
import { existsSync, mkdtempSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { collect, commandsFor, runTool, verdictFor } from "../../scripts/measure/cli.js";
import { coverageCs, coverageJs, mutationCs, mutationJs } from "../../scripts/measure/tools.js";

const roots = [];

const put = (root, path, content) => {
    mkdirSync(dirname(join(root, path)), { recursive: true });
    writeFileSync(join(root, path), typeof content === "string" ? content : JSON.stringify(content));
};

const SETTINGS = {
    floors: { slack: 1 },
    mutation: {
        iterations: 10,
        cs: { shards: [{ name: "data", mutate: ["**/Db/**/*.cs"] }] },
        js: { skippedTests: [], shards: [{ name: "scripts", mutate: ["scripts/**/*.js"], tests: [] }] },
    },
};

const report = (path, ...statuses) => ({
    schemaVersion: "2",
    files: { [path]: { mutants: statuses.map((status, at) => ({ mutatorName: "Boolean mutation", replacement: "false", location: { start: { line: at + 1, column: 1 } }, status })) } },
});

// What each tool leaves in its folder of artifacts/, as the run of a measure would.
const REPORTS = {
    "coverage-cs-jf12": (root) => put(root, "artifacts/coverage-cs-jf12/0f/coverage.info", `SF:${join(root, "Jellyfin.Plugin.Streamyfin/Db/A.cs")}\nDA:1,1\nDA:2,0\nend_of_record\n`),
    "coverage-cs-jf11": (root) => put(root, "artifacts/coverage-cs-jf11/0a/coverage.info", `SF:${join(root, "Jellyfin.Plugin.Streamyfin/Db/A.cs")}\nDA:1,0\nDA:2,0\nend_of_record\n`),
    "coverage-js": (root) => put(root, "artifacts/coverage-js/lcov.info", "SF:scripts/a.js\nDA:1,1\nDA:2,1\nend_of_record\n"),
    "mutation-cs-data": (root) => put(root, "artifacts/mutation-cs-data/reports/mutation-report.json", report(join(root, "Jellyfin.Plugin.Streamyfin/Db/A.cs"), "Killed", "Survived")),
    "mutation-js-scripts": (root) => put(root, "artifacts/mutation-js-scripts/mutation.json", report("scripts/a.js", "Killed")),
};

const repository = () => {
    const root = mkdtempSync(join(tmpdir(), "measure-cli-"));
    roots.push(root);
    put(root, "tests/settings.json", SETTINGS);
    put(root, "tests/owners.json", []);
    put(root, "tests/accepted-mutants.json", []);
    put(root, "tests/floors.json", { Db: { lines: 50, branches: null, mutation: 50 }, scripts: { lines: 66.6, branches: null, mutation: 100 } });
    put(root, "Jellyfin.Plugin.Streamyfin/Db/A.cs", "a\nb\n");
    put(root, "scripts/a.js", "export const a = 1;\nexport const b = 2;\n");
    put(root, "scripts/never.js", "// loaded by nothing\nexport const c = 3;\n");
    for (const write of Object.values(REPORTS)) write(root);
    put(root, "Jellyfin.Plugin.Streamyfin.Tests/stryker-config.json", { "stryker-config": { project: "Jellyfin.Plugin.Streamyfin.csproj" } });
    put(root, "stryker.config.json", { testRunner: "command" });
    return root;
};

afterEach(() => {
    for (const root of roots.splice(0)) rmSync(root, { recursive: true, force: true });
});

describe("the reports on disk", () => {
    test("are read as one, with the JavaScript nothing loads counted at 0 %", () => {
        const root = repository();
        const { coverage, mutants, missingReports } = collect({ root, settings: SETTINGS, owners: new Map() });

        expect([...coverage.keys()].sort()).toEqual(["Jellyfin.Plugin.Streamyfin/Db/A.cs", "scripts/a.js", "scripts/never.js"]);
        expect([...coverage.get("scripts/never.js").lines]).toEqual([[2, 0]]);
        expect(mutants.map((mutant) => `${mutant.file}:${mutant.line}:${mutant.status}`)).toEqual([
            "Jellyfin.Plugin.Streamyfin/Db/A.cs:1:Killed",
            "Jellyfin.Plugin.Streamyfin/Db/A.cs:2:Survived",
            "scripts/a.js:1:Killed",
        ]);
        expect(missingReports).toEqual([]);
    });

    test("name a shard that left no report", () => {
        const root = repository();
        rmSync(join(root, "artifacts/mutation-js-scripts"), { recursive: true });

        expect(collect({ root, settings: SETTINGS, owners: new Map() }).missingReports).toEqual(["js/scripts"]);
    });

    // A coverage run that wrote nothing would read as a folder with no code, and a target
    // left out would quietly measure the other alone.
    test("refuse to judge without the coverage of every run", () => {
        const root = repository();
        rmSync(join(root, "artifacts/coverage-js"), { recursive: true });
        expect(() => collect({ root, settings: SETTINGS, owners: new Map() }))
            .toThrow(new Error("artifacts/coverage-js holds no coverage: run bun scripts/measure/cli.js coverage-js"));

        const other = repository();
        rmSync(join(other, "artifacts/coverage-cs-jf11"), { recursive: true });
        expect(() => collect({ root: other, settings: SETTINGS, owners: new Map() }))
            .toThrow(new Error("artifacts/coverage-cs-jf11 holds no coverage: run bun scripts/measure/cli.js coverage-cs jf11"));
    });
});

describe("the verdict from the reports", () => {
    test("judges, and writes its result and its summary next to them", () => {
        const root = repository();
        const result = verdictFor({ root, settings: SETTINGS, changed: new Map() });

        expect(result.folders.map(({ name, lines, mutation }) => ({ name, lines, mutation }))).toEqual([
            { name: "Db", lines: 50, mutation: 50 },
            { name: "scripts", lines: 66.6, mutation: 100 },
        ]);
        expect(result.ok).toBe(true);
        expect(JSON.parse(readFileSync(join(root, "artifacts/measure/verdict.json"), "utf8")).ok).toBe(true);
        expect(readFileSync(join(root, "artifacts/measure/summary.md"), "utf8")).toContain("**Passed.**");
    });

    test("fails a survivor on a line the pull request changed", () => {
        const root = repository();
        const result = verdictFor({ root, settings: SETTINGS, changed: new Map([["Jellyfin.Plugin.Streamyfin/Db/A.cs", new Set([2])]]) });

        expect(result.ok).toBe(false);
        expect(result.escaped.map(({ file, line, text }) => ({ file, line, text }))).toEqual([
            { file: "Jellyfin.Plugin.Streamyfin/Db/A.cs", line: 2, text: "b" },
        ]);
    });

    // The text is the line as the summary shows it: trimmed, and empty for a line the file no
    // longer has, when the report was written on an older version of it.
    test("quotes each escaped line trimmed, and a line past the end as empty", () => {
        const root = repository();
        put(root, "Jellyfin.Plugin.Streamyfin/Db/A.cs", "a\n    b   \n");
        put(root, "artifacts/mutation-cs-data/reports/mutation-report.json",
            report(join(root, "Jellyfin.Plugin.Streamyfin/Db/A.cs"), "Killed", "Survived", "Survived", "Survived"));
        const result = verdictFor({ root, settings: SETTINGS, changed: new Map([["Jellyfin.Plugin.Streamyfin/Db/A.cs", new Set([2, 4])]]) });

        expect(result.escaped.map(({ line, text }) => ({ line, text }))).toEqual([{ line: 2, text: "b" }, { line: 4, text: "" }]);
    });

    test("can judge again over its own last result", () => {
        const root = repository();
        verdictFor({ root, settings: SETTINGS, changed: new Map() });

        expect(verdictFor({ root, settings: SETTINGS, changed: new Map() }).ok).toBe(true);
    });
});

// A run that records each tool's job, and where it was told to run it, instead of starting it.
const recorder = () => {
    const jobs = [];
    const where = [];
    const printed = [];
    return {
        jobs,
        where,
        printed,
        run: (job, options) => {
            jobs.push(job);
            where.push(options);
        },
        print: (text) => printed.push(text),
    };
};

const listing = (root, path) => (existsSync(join(root, path)) ? readdirSync(join(root, path)).sort() : null);

describe("the coverage commands", () => {
    test.each(["jf11", "jf12"])("run dotnet test on %s, in a folder emptied first", (target) => {
        const root = repository();
        put(root, `artifacts/coverage-cs-${target}/old/stale.txt`, "left by a run before");
        const { jobs, run } = recorder();

        expect(commandsFor({ root, run })["coverage-cs"](SETTINGS, [target])).toBe(0);
        expect(jobs).toEqual([coverageCs({ target })]);
        expect(listing(root, `artifacts/coverage-cs-${target}`)).toEqual([]);
    });

    test.each([["jf13"], [undefined]])("refuse a target that is not a build, here %p", (target) => {
        const root = repository();
        const { jobs, run } = recorder();

        expect(() => commandsFor({ root, run })["coverage-cs"](SETTINGS, [target])).toThrow(new Error("coverage-cs takes jf11 or jf12"));
        expect(jobs).toEqual([]);
    });

    test("of the JavaScript runs bun's, in a folder made from nothing", () => {
        const root = repository();
        rmSync(join(root, "artifacts"), { recursive: true });
        const { jobs, run } = recorder();

        expect(commandsFor({ root, run })["coverage-js"](SETTINGS, [])).toBe(0);
        expect(jobs).toEqual([coverageJs()]);
        expect(listing(root, "artifacts/coverage-js")).toEqual([]);
    });
});

describe("the mutation commands", () => {
    test("of a C# shard write its configuration next to its reports, then run Stryker.NET", () => {
        const root = repository();
        const { jobs, run } = recorder();
        const job = mutationCs({ shard: SETTINGS.mutation.cs.shards[0], settings: SETTINGS, owners: new Map(), base: { "stryker-config": { project: "Jellyfin.Plugin.Streamyfin.csproj" } } });

        expect(commandsFor({ root, run })["mutation-cs"](SETTINGS, ["data"])).toBe(0);
        expect(jobs).toEqual([job]);
        expect(readFileSync(join(root, "artifacts/mutation-cs-data/stryker-config.json"), "utf8")).toBe(`${JSON.stringify(job.config, null, 2)}\n`);
        expect(listing(root, "artifacts/mutation-cs-data")).toEqual(["stryker-config.json"]);
    });

    test("of a JavaScript shard write its configuration next to its reports, then run StrykerJS", () => {
        const root = repository();
        const { jobs, run } = recorder();
        const job = mutationJs({ shard: SETTINGS.mutation.js.shards[0], settings: SETTINGS, base: { testRunner: "command" } });

        expect(commandsFor({ root, run })["mutation-js"](SETTINGS, ["scripts"])).toBe(0);
        expect(jobs).toEqual([job]);
        expect(readFileSync(join(root, "artifacts/mutation-js-scripts/stryker.config.json"), "utf8")).toBe(`${JSON.stringify(job.config, null, 2)}\n`);
        expect(listing(root, "artifacts/mutation-js-scripts")).toEqual(["stryker.config.json"]);
    });

    test.each([["mutation-cs", "cs", "data, rest"], ["mutation-js", "js", "scripts, pages"]])("%s names the shards there are when given another", (command, kind, names) => {
        const root = repository();
        const { jobs, run } = recorder();
        const settings = structuredClone(SETTINGS);
        settings.mutation.cs.shards.push({ name: "rest", mutate: ["**/*.cs"] });
        settings.mutation.js.shards.push({ name: "pages", mutate: ["Pages/**/*.js"], tests: [] });

        expect(() => commandsFor({ root, run })[command](settings, ["nope"])).toThrow(new Error(`no ${kind} shard named nope: ${names}`));
        expect(jobs).toEqual([]);
        expect(listing(root, `artifacts/${command}-nope`)).toBeNull();
    });

    test.each([["cs", '["data"]'], ["js", '["scripts"]']])("list the %s shards for the CI matrix", (kind, printedNames) => {
        const { printed, print } = recorder();

        expect(commandsFor({ root: repository(), print }).shards(SETTINGS, [kind])).toBe(0);
        expect(printed).toEqual([printedNames]);
    });
});

describe("the verdict command", () => {
    const survivorChanged = new Map([["Jellyfin.Plugin.Streamyfin/Db/A.cs", new Set([2])]]);

    test.each([
        [["--base", "abc123"], "abc123"],
        [["--json", "--base", "main"], "main"],
        [[], "origin/develop"],
    ])("compares with the base it is given, given %p", (args, base) => {
        const root = repository();
        const asked = [];
        const commands = commandsFor({ root, print: () => {}, env: {}, changed: (from) => { asked.push(from); return new Map(); } });

        expect(commands.verdict(SETTINGS, args)).toBe(0);
        expect(asked).toEqual([base]);
    });

    // A branch's first push names no commit before it: forty zeros, or nothing at all.
    test.each([[["--base", "0000000000000000000000000000000000000000"]], [["--base"]]])("compares nothing on a first push, given %p", (args) => {
        const asked = [];
        const commands = commandsFor({ root: repository(), print: () => {}, env: {}, changed: (from) => { asked.push(from); return survivorChanged; } });

        expect(commands.verdict(SETTINGS, args)).toBe(0);
        expect(asked).toEqual([]);
    });

    test("fails with exit code 1 when a survivor sits on a changed line", () => {
        const commands = commandsFor({ root: repository(), print: () => {}, env: {}, changed: () => survivorChanged });

        expect(commands.verdict(SETTINGS, [])).toBe(1);
    });

    test("prints its summary, and adds it to the job's summary when there is one", () => {
        const root = repository();
        const stepSummary = join(root, "step-summary.md");
        writeFileSync(stepSummary, "before\n");
        const { printed, print } = recorder();

        commandsFor({ root, print, env: { GITHUB_STEP_SUMMARY: stepSummary }, changed: () => new Map() }).verdict(SETTINGS, []);
        const summary = readFileSync(join(root, "artifacts/measure/summary.md"), "utf8");

        expect(printed).toEqual([summary]);
        expect(readFileSync(stepSummary, "utf8")).toBe(`before\n${summary}`);
    });

    test("writes no job summary outside a CI job", () => {
        const root = repository();

        expect(commandsFor({ root, print: () => {}, env: {}, changed: () => new Map() }).verdict(SETTINGS, [])).toBe(0);
        expect(listing(root, ".").includes("step-summary.md")).toBe(false);
    });
});

describe("the raise command", () => {
    test("lifts the floors to what the reports measure, and prints them", () => {
        const root = repository();
        put(root, "tests/floors.json", {});
        const { printed, print } = recorder();

        expect(commandsFor({ root, print }).raise(SETTINGS)).toBe(0);
        const floors = JSON.parse(readFileSync(join(root, "tests/floors.json"), "utf8"));

        expect(Object.keys(floors).sort()).toEqual(["Db", "scripts"]);
        expect(readFileSync(join(root, "tests/floors.json"), "utf8")).toBe(`${JSON.stringify(floors, null, 2)}\n`);
        expect(printed).toEqual([JSON.stringify(floors, null, 2)]);
    });

    test("refuses while a shard's report is missing", () => {
        const root = repository();
        rmSync(join(root, "artifacts/mutation-cs-data"), { recursive: true });
        rmSync(join(root, "artifacts/mutation-js-scripts"), { recursive: true });
        const before = readFileSync(join(root, "tests/floors.json"), "utf8");

        expect(() => commandsFor({ root, print: () => {} }).raise(SETTINGS)).toThrow(new Error("every report is needed to raise the floors, missing: cs/data, js/scripts"));
        expect(readFileSync(join(root, "tests/floors.json"), "utf8")).toBe(before);
    });
});

describe("the all command", () => {
    // The folder of artifacts/ a job writes into, which says which tool it is.
    const folderOf = (job) => [job.configFile ?? "", ...job.command].join(" ").match(/artifacts\/([a-z0-9-]+)/)[1];
    const tools = (root, jobs) => (job) => {
        jobs.push(folderOf(job));
        REPORTS[folderOf(job)](root);
    };

    test("measures everything in order, then judges", () => {
        const root = repository();
        const jobs = [];
        const commands = commandsFor({ root, run: tools(root, jobs), print: () => {}, env: {}, changed: () => new Map() });

        expect(commands.all(SETTINGS, [])).toBe(0);
        expect(jobs).toEqual(["coverage-cs-jf12", "coverage-cs-jf11", "coverage-js", "mutation-cs-data", "mutation-js-scripts"]);
    });

    test("exits as its verdict does", () => {
        const root = repository();
        const commands = commandsFor({
            root,
            run: tools(root, []),
            print: () => {},
            env: {},
            changed: () => new Map([["Jellyfin.Plugin.Streamyfin/Db/A.cs", new Set([2])]]),
        });

        expect(commands.all(SETTINGS, ["--base", "main"])).toBe(1);
    });
});

describe("every command", () => {
    test("runs its tools in the repository it was given, printing where it was told to", () => {
        const root = repository();
        const { where, run, print } = recorder();

        commandsFor({ root, run, print })["coverage-js"](SETTINGS, []);

        expect(where).toEqual([{ root, print }]);
    });

    test("asks the repository it was given which lines changed", () => {
        const root = repository();
        const asked = [];
        const changed = (base, options) => {
            asked.push([base, options]);
            return new Map();
        };

        commandsFor({ root, print: () => {}, env: {}, changed }).verdict(SETTINGS, ["--base", "main"]);

        expect(asked).toEqual([["main", { cwd: root }]]);
    });

    // The defaults together, on a real repository: git is asked there, not in this one.
    test("left to their defaults, find a changed line in that repository", () => {
        const root = repository();
        const git = (...args) => {
            const child = Bun.spawnSync(["git", "-c", "user.name=measure", "-c", "user.email=measure@example.com", "-c", "commit.gpgsign=false", ...args], { cwd: root, stdout: "pipe", stderr: "pipe" });
            if (child.exitCode !== 0) throw new Error(child.stderr.toString());
        };
        git("init", "-q");
        git("add", "-A");
        git("commit", "-q", "-m", "first");
        put(root, "Jellyfin.Plugin.Streamyfin/Db/A.cs", "a\nchanged\n");

        expect(commandsFor({ root, print: () => {}, env: {} }).verdict(SETTINGS, ["--base", "HEAD"])).toBe(1);
    });
});

describe("a tool run", () => {
    const script = (code) => ["bun", "-e", code];

    test("starts in the folder its job names, with the job's environment on top of this one", () => {
        const root = repository();
        mkdirSync(join(root, "sub"));
        const { printed, print } = recorder();
        const code = "require('fs').writeFileSync('seen.txt', [process.env.MEASURE_ADDED, process.env.HOME === undefined].join(' '))";

        runTool({ command: script(code), cwd: "sub", env: { MEASURE_ADDED: "yes" } }, { root, print });

        expect(readFileSync(join(root, "sub/seen.txt"), "utf8")).toBe("yes false");
        expect(printed).toEqual([`> bun -e ${code}`]);
    });

    test("fails with the tool's name and its exit code", () => {
        expect(() => runTool({ command: script("process.exit(3)"), cwd: "." }, { root: repository(), print: () => {} }))
            .toThrow(new Error("bun -e exited with 3"));
    });

    test("lets the tool's output through as it comes", () => {
        const cli = resolve(import.meta.dir, "../../scripts/measure/cli.js");
        const child = Bun.spawnSync(["bun", "-e", `const { runTool } = await import(${JSON.stringify(cli)}); runTool({ command: ["bun", "-e", "console.log('out'); console.error('err')"], cwd: "." }, { print: () => {} });`], { stdout: "pipe", stderr: "pipe" });

        expect(child.exitCode).toBe(0);
        expect(child.stdout.toString()).toBe("out\n");
        expect(child.stderr.toString()).toBe("err\n");
    });
});

describe("the command line", () => {
    const cli = (...args) => Bun.spawnSync(["bun", resolve(import.meta.dir, "../../scripts/measure/cli.js"), ...args], { stdout: "pipe", stderr: "pipe" });

    test.each([["nope"], ["toString"], []])("shows its usage and exits 2 for an unknown command, given %p", (...args) => {
        const child = cli(...args);

        expect(child.exitCode).toBe(2);
        expect(child.stderr.toString()).toBe("usage: bun scripts/measure/cli.js <coverage-cs|coverage-js|mutation-cs|mutation-js|shards|verdict|raise|all> [arguments]\n");
    });

    test("runs a command with the repository's settings", () => {
        const child = cli("shards", "js");

        expect(child.exitCode).toBe(0);
        expect(JSON.parse(child.stdout.toString())).toContain("scripts-measure");
    });
});
