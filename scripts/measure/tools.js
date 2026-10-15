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

// Stryker.NET, one shard, from the test project that holds stryker-config.json. A pattern on
// the command line replaces the file's, so the generated code and the files the integration
// tests answer for are left out here, on every shard.
export const mutationCs = ({ shard, settings, owners }) => {
    const generated = EXCLUDED.filter((pattern) => !pattern.startsWith(PLUGIN)).map((pattern) => `!${pattern}`);
    const owned = [...owners.keys()]
        .filter((file) => file.startsWith(PLUGIN) && file.endsWith(".cs"))
        .map((file) => `!**/${file.slice(PLUGIN.length)}`);

    return {
        command: [
            "dotnet", "stryker",
            "--config-file", "stryker-config.json",
            "--output", `../artifacts/mutation-cs-${shard.name}`,
            "--concurrency", String(settings.mutation.cs.concurrency),
            "--additional-timeout", String(settings.mutation.cs.additionalTimeoutMs),
            ...[...shard.mutate, ...generated, ...owned].flatMap((pattern) => ["--mutate", pattern]),
        ],
        cwd: "Jellyfin.Plugin.Streamyfin.Tests",
        env: draws(settings),
    };
};

// StrykerJS, one shard, with a configuration of its own written next to its reports: the
// shard's files, and the tests its mutants run.
export const mutationJs = ({ shard, settings, base }) => {
    const out = `artifacts/mutation-js-${shard.name}`;
    const libraries = EXCLUDED.filter((pattern) => pattern.startsWith(`${PLUGIN}Pages/`)).map((pattern) => `!${pattern}`);
    const configFile = `${out}/stryker.config.json`;

    return {
        config: {
            ...base,
            mutate: [...shard.mutate, ...libraries],
            commandRunner: { command: ["bun", "test", ...shard.tests].join(" ") },
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
