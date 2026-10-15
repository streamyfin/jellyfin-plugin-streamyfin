// The measuring tools' configurations agree with each other: every source file is mutated by
// exactly one shard, and every tool leaves out the same generated code.

import { describe, expect, test } from "bun:test";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { EXCLUDED, isExcluded, ownJsFiles, readOwners } from "../../scripts/measure/folders.js";
import { readSettings, ROOT } from "../../scripts/measure/settings.js";
import { mutationCs } from "../../scripts/measure/tools.js";

const PLUGIN = "Jellyfin.Plugin.Streamyfin/";
const settings = readSettings({ env: {} });
const owners = readOwners(ROOT);
const read = (path) => readFileSync(join(ROOT, path), "utf8");

// A file is in a shard when one of its patterns takes it and none of its negations leaves it out.
const inShard = (path, patterns) =>
    patterns.some((pattern) => !pattern.startsWith("!") && new Bun.Glob(pattern).match(path))
    && !patterns.some((pattern) => pattern.startsWith("!") && new Bun.Glob(pattern.slice(1)).match(path));

const csFiles = [...new Bun.Glob(`${PLUGIN}**/*.cs`).scanSync({ cwd: ROOT })]
    .map((path) => path.replaceAll("\\", "/"))
    .filter((path) => !isExcluded(path) && !owners.has(path));

describe("the mutation shards", () => {
    test("mutate every C# file of the plugin exactly once", () => {
        const wrong = csFiles.flatMap((path) => {
            const relative = path.slice(PLUGIN.length);
            const shards = settings.mutation.cs.shards
                .filter((shard) => inShard(relative, mutationCs({ shard, settings, owners }).command
                    .flatMap((arg, at, all) => (all[at - 1] === "--mutate" ? [arg] : []))))
                .map((shard) => shard.name);
            return shards.length === 1 ? [] : [`${path}: ${shards.join(", ") || "no shard"}`];
        });

        expect(csFiles.length).toBeGreaterThan(100);
        expect(wrong).toEqual([]);
    });

    test("mutate every JavaScript file of ours exactly once", () => {
        const wrong = ownJsFiles(ROOT).flatMap((path) => {
            const shards = settings.mutation.js.shards.filter((shard) => inShard(path, shard.mutate)).map((shard) => shard.name);
            return shards.length === 1 ? [] : [`${path}: ${shards.join(", ") || "no shard"}`];
        });

        expect(wrong).toEqual([]);
    });

    test("never mutate a file the integration tests answer for", () => {
        const owned = [...owners.keys()].filter((path) => path.endsWith(".cs"));

        for (const shard of settings.mutation.cs.shards) {
            const patterns = mutationCs({ shard, settings, owners }).command.flatMap((arg, at, all) => (all[at - 1] === "--mutate" ? [arg] : []));
            for (const path of owned) expect(inShard(path.slice(PLUGIN.length), patterns)).toBe(false);
        }
    });
});

describe("every tool", () => {
    const cs = EXCLUDED.filter((pattern) => !pattern.startsWith(PLUGIN));
    const js = EXCLUDED.filter((pattern) => pattern.startsWith(PLUGIN));

    test("leaves out the code the compiler and Entity Framework generate", () => {
        const runsettings = read("tests/coverage.runsettings");
        const stryker = JSON.parse(read("Jellyfin.Plugin.Streamyfin.Tests/stryker-config.json"))["stryker-config"];

        for (const pattern of cs) {
            expect(runsettings).toContain(pattern);
            expect(stryker.mutate).toContain(`!${pattern}`);
        }
    });

    test("leaves out the libraries the pages ship", () => {
        const bunfig = read("bunfig.toml");
        const stryker = JSON.parse(read("stryker.config.json"));

        for (const pattern of js) {
            expect(bunfig).toContain(`"${pattern}"`);
            expect(stryker.mutate).toContain(`!${pattern}`);
        }
    });
});

// StrykerJS copies the repository into a sandbox and leaves .git out, so a test that asks
// git which files it tracks would find none there and fail before any mutant ran.
describe("the JavaScript mutation sandbox", () => {
    test("keeps git's view of the repository", () => {
        const stryker = JSON.parse(read("stryker.config.json"));

        expect(stryker.ignorePatterns).toContain("!.git");
    });
});

// A StrykerJS shard runs all of its tests for every mutant. A test that loads none of the
// files the shards mutate can fail no mutant, so it is left out of every shard.
describe("a test left out of the mutation runs", () => {
    const stem = (path) => path.replace(/\.js$/, "").split("/").slice(-2).join("/");

    test("exists and names no file the shards mutate", () => {
        const mutated = ownJsFiles(ROOT).map(stem);

        expect(settings.mutation.js.skippedTests.length).toBeGreaterThan(0);
        for (const path of settings.mutation.js.skippedTests) {
            const source = read(path);
            expect(mutated.filter((name) => source.includes(name))).toEqual([]);
        }
    });
});

describe("the files the integration tests answer for", () => {
    test("each say why", () => {
        expect(owners.size).toBeGreaterThan(0);
        for (const reason of owners.values()) expect(reason.length).toBeGreaterThan(20);
    });
});
