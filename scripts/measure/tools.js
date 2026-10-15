// The command that runs each measuring tool, as a list of arguments: the same on every
// system, with nothing for a shell to read.

import { EXCLUDED } from "./folders.js";

const PLUGIN = "Jellyfin.Plugin.Streamyfin/";

export const coverageCs = ({ target }) => ({
    command: [
        "dotnet", "test", "Jellyfin.Plugin.Streamyfin.Tests", "--configuration", "Release", `-p:JellyfinTarget=${target}`,
        "--collect:XPlat Code Coverage", "--settings", "tests/coverage.runsettings",
        "--results-directory", `artifacts/coverage-cs-${target}`,
    ],
    cwd: ".",
});

export const coverageJs = () => ({
    command: ["bun", "test", "--coverage", "--coverage-reporter=lcov", "--coverage-dir=artifacts/coverage-js"],
    cwd: ".",
});

// Mutants run few draws of each property, so a run stays short.
const draws = (settings) => ({ STREAMYFIN_TEST_ITERATIONS_UNIT: String(settings.mutation.iterations) });

// Stryker.NET, one shard, run from the test project. A pattern on the command line replaces
// the configuration's, so the generated code and the files the integration tests answer for
// are left out here, on every shard. The timeout has no command line option, so each shard
// gets a configuration of its own, next to its reports: the test project's, with the timeout.
export const mutationCs = ({ shard, settings, owners, base = {} }) => {
    const out = `artifacts/mutation-cs-${shard.name}`;
    const configFile = `${out}/stryker-config.json`;
    const generated = EXCLUDED.filter((pattern) => !pattern.startsWith(PLUGIN)).map((pattern) => `!${pattern}`);
    const owned = [...owners.keys()]
        .filter((file) => file.startsWith(PLUGIN) && file.endsWith(".cs"))
        .map((file) => `!**/${file.slice(PLUGIN.length)}`);

    return {
        config: {
            "stryker-config": { ...base["stryker-config"], "additional-timeout": settings.mutation.cs.additionalTimeoutMs },
        },
        configFile,
        command: [
            "dotnet", "stryker",
            "--config-file", `../${configFile}`,
            "--output", `../${out}`,
            "--concurrency", String(settings.mutation.cs.concurrency),
            ...[...shard.mutate, ...generated, ...owned].flatMap((pattern) => ["--mutate", pattern]),
        ],
        cwd: "Jellyfin.Plugin.Streamyfin.Tests",
        env: draws(settings),
    };
};

// StrykerJS, one shard, with a configuration of its own written next to its reports: the
// shard's files, and the tests its mutants run. The command runner runs every one of them for
// each mutant, so the tests that load none of the mutated files are left out.
export const mutationJs = ({ shard, settings, base }) => {
    const out = `artifacts/mutation-js-${shard.name}`;
    const libraries = EXCLUDED.filter((pattern) => pattern.startsWith(`${PLUGIN}Pages/`)).map((pattern) => `!${pattern}`);
    const skipped = settings.mutation.js.skippedTests.map((path) => `--path-ignore-patterns=${path}`);
    const configFile = `${out}/stryker.config.json`;

    return {
        config: {
            ...base,
            mutate: [...shard.mutate, ...libraries],
            commandRunner: { command: ["bun", "test", ...shard.tests, ...skipped].join(" ") },
            concurrency: settings.mutation.js.concurrency,
            timeoutMS: settings.mutation.js.timeoutMS,
            jsonReporter: { fileName: `${out}/mutation.json` },
            htmlReporter: { fileName: `${out}/mutation.html` },
            tempDirName: `.stryker-tmp/${shard.name}`,
        },
        configFile,
        command: ["node", "node_modules/@stryker-mutator/core/bin/stryker.js", "run", configFile],
        cwd: ".",
        env: draws(settings),
    };
};
