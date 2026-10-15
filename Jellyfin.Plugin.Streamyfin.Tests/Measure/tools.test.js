// The commands that run each measuring tool, as argument lists nothing needs a shell for.

import { describe, expect, test } from "bun:test";
import { coverageCs, coverageJs, mutationCs, mutationJs } from "../../scripts/measure/tools.js";

const settings = {
    mutation: {
        iterations: 10,
        cs: { concurrency: 4, additionalTimeoutMs: 5000 },
        js: { concurrency: 3, timeoutMS: 10000 },
    },
};

describe("coverage", () => {
    test("of the C# runs dotnet test on one target, with coverlet's settings", () => {
        const { command, cwd } = coverageCs({ target: "jf11" });

        expect(cwd).toBe(".");
        expect(command).toEqual([
            "dotnet", "test", "Jellyfin.Plugin.Streamyfin.Tests", "--configuration", "Release", "-p:JellyfinTarget=jf11",
            "--collect:XPlat Code Coverage", "--settings", "tests/coverage.runsettings",
            "--results-directory", "artifacts/coverage-cs-jf11",
        ]);
    });

    test("of the JavaScript runs bun test with LCOV", () => {
        expect(coverageJs().command).toEqual(["bun", "test", "--coverage", "--coverage-reporter=lcov", "--coverage-dir=artifacts/coverage-js"]);
    });
});

describe("a C# mutation shard", () => {
    const shard = { name: "data", mutate: ["**/Db/**/*.cs"] };
    const owners = new Map([["Jellyfin.Plugin.Streamyfin/Api/StreamyfinController.cs", "every route"], ["scripts/x.js", "not C#"]]);

    test("mutates its files, never generated code nor what the integration tests answer for", () => {
        const { command, cwd } = mutationCs({ shard, settings, owners });
        const mutated = command.flatMap((arg, at) => (command[at - 1] === "--mutate" ? [arg] : []));

        expect(cwd).toBe("Jellyfin.Plugin.Streamyfin.Tests");
        expect(command.slice(0, 2)).toEqual(["dotnet", "stryker"]);
        expect(mutated).toEqual([
            "**/Db/**/*.cs",
            "!**/obj/**",
            "!**/Migrations/*.Designer.cs",
            "!**/Migrations/*ModelSnapshot.cs",
            "!**/Api/StreamyfinController.cs",
        ]);
    });

    // The timeout is a setting, so a mutant that flips between Timeout and Survived can be
    // given more time rather than turn the verdict red.
    test("runs with the concurrency, the timeout and the draws its settings give", () => {
        const { command, env } = mutationCs({ shard, settings, owners });

        expect(command).toContain("--config-file");
        expect(command[command.indexOf("--output") + 1]).toBe("../artifacts/mutation-cs-data");
        expect(command[command.indexOf("--concurrency") + 1]).toBe("4");
        expect(command[command.indexOf("--additional-timeout") + 1]).toBe("5000");
        expect(env).toEqual({ STREAMYFIN_TEST_ITERATIONS_UNIT: "10" });
    });
});

describe("a JavaScript mutation shard", () => {
    const shard = { name: "pages-form", mutate: ["Jellyfin.Plugin.Streamyfin/Pages/settings-form.js"], tests: ["Jellyfin.Plugin.Streamyfin.Tests/Pages"] };
    const base = { testRunner: "command", coverageAnalysis: "off", reporters: ["json", "html", "clear-text"] };

    test("gets a configuration of its own, next to its reports", () => {
        const { config, configFile, command } = mutationJs({ shard, settings, base });

        expect(configFile).toBe("artifacts/mutation-js-pages-form/stryker.config.json");
        expect(command).toEqual(["node", "node_modules/@stryker-mutator/core/bin/stryker.js", "run", configFile]);
        expect(config).toEqual({
            ...base,
            mutate: ["Jellyfin.Plugin.Streamyfin/Pages/settings-form.js", "!Jellyfin.Plugin.Streamyfin/Pages/Libraries/**"],
            commandRunner: { command: "bun test Jellyfin.Plugin.Streamyfin.Tests/Pages" },
            concurrency: 3,
            timeoutMS: 10000,
            jsonReporter: { fileName: "artifacts/mutation-js-pages-form/mutation.json" },
            htmlReporter: { fileName: "artifacts/mutation-js-pages-form/mutation.html" },
            tempDirName: ".stryker-tmp/pages-form",
        });
    });

    test("runs each mutant with the draws its settings give", () => {
        expect(mutationJs({ shard, settings, base }).env).toEqual({ STREAMYFIN_TEST_ITERATIONS_UNIT: "10" });
    });
});
