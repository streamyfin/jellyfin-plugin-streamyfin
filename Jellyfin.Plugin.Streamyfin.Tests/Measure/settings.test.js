// The settings every test layer reads, and the environment variables that override them.

import { afterEach, describe, expect, test } from "bun:test";
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { envName, readSettings } from "../../scripts/measure/settings.js";

const roots = [];

const rootWith = (settings) => {
    const root = mkdtempSync(join(tmpdir(), "measure-settings-"));
    mkdirSync(join(root, "tests"));
    writeFileSync(join(root, "tests", "settings.json"), JSON.stringify(settings));
    roots.push(root);
    return root;
};

afterEach(() => {
    for (const root of roots.splice(0)) rmSync(root, { recursive: true, force: true });
});

const SAMPLE = {
    seed: "",
    iterations: { unit: 100 },
    floors: { slack: 1.0 },
    strict: false,
    mutation: { js: { shards: [{ name: "a", mutate: ["a.js"] }] } },
};

describe("the test settings", () => {
    test("are the file as written when nothing overrides them", () => {
        expect(readSettings({ root: rootWith(SAMPLE), env: {} })).toEqual(SAMPLE);
    });

    test("name each override after its path, in capitals, dots as underscores", () => {
        expect(envName("iterations.unit")).toBe("STREAMYFIN_TEST_ITERATIONS_UNIT");
        expect(envName("mutation.js.shards")).toBe("STREAMYFIN_TEST_MUTATION_JS_SHARDS");
    });

    test("take a number, a string and a boolean as written", () => {
        const settings = readSettings({
            root: rootWith(SAMPLE),
            env: {
                STREAMYFIN_TEST_ITERATIONS_UNIT: "250",
                STREAMYFIN_TEST_SEED: "4291",
                STREAMYFIN_TEST_STRICT: "true",
            },
        });

        expect(settings.iterations.unit).toBe(250);
        expect(settings.seed).toBe("4291");
        expect(settings.strict).toBe(true);
    });

    test("take a list as JSON", () => {
        const settings = readSettings({
            root: rootWith(SAMPLE),
            env: { STREAMYFIN_TEST_MUTATION_JS_SHARDS: '[{"name":"b","mutate":["b.js"]}]' },
        });

        expect(settings.mutation.js.shards).toEqual([{ name: "b", mutate: ["b.js"] }]);
    });

    test("take false as false, and true as true", () => {
        const root = rootWith(SAMPLE);

        expect(readSettings({ root, env: { STREAMYFIN_TEST_STRICT: "false" } }).strict).toBe(false);
        expect(readSettings({ root, env: { STREAMYFIN_TEST_STRICT: "true" } }).strict).toBe(true);
    });

    test("refuse blanks for a number", () => {
        expect(() => readSettings({ root: rootWith(SAMPLE), env: { STREAMYFIN_TEST_ITERATIONS_UNIT: "  " } }))
            .toThrow("STREAMYFIN_TEST_ITERATIONS_UNIT is not a number");
    });

    test("leave a setting written as null alone", () => {
        expect(readSettings({ root: rootWith({ ...SAMPLE, seed: null }), env: {} }).seed).toBeNull();
    });

    // STREAMYFIN_TEST_ alone names no setting: it never replaces the whole file.
    test("take no override for the file as a whole", () => {
        expect(readSettings({ root: rootWith(SAMPLE), env: { STREAMYFIN_TEST_: "{}" } })).toEqual(SAMPLE);
    });

    test("refuse a value that is not of the setting's kind, and say which variable", () => {
        const root = rootWith(SAMPLE);

        expect(() => readSettings({ root, env: { STREAMYFIN_TEST_ITERATIONS_UNIT: "many" } }))
            .toThrow("STREAMYFIN_TEST_ITERATIONS_UNIT is not a number");
        expect(() => readSettings({ root, env: { STREAMYFIN_TEST_ITERATIONS_UNIT: "" } }))
            .toThrow("STREAMYFIN_TEST_ITERATIONS_UNIT is not a number");
        expect(() => readSettings({ root, env: { STREAMYFIN_TEST_STRICT: "yes" } }))
            .toThrow("STREAMYFIN_TEST_STRICT is neither true nor false");
        expect(() => readSettings({ root, env: { STREAMYFIN_TEST_MUTATION_JS_SHARDS: "[" } }))
            .toThrow("STREAMYFIN_TEST_MUTATION_JS_SHARDS is not JSON");
    });

    test("of the repository read, with the values every layer needs", () => {
        const settings = readSettings({ env: {} });

        expect(typeof settings.seed).toBe("string");
        expect(settings.iterations.unit).toBeGreaterThan(0);
        expect(settings.floors.slack).toBe(1);
        expect(settings.mutation.cs.shards.length).toBeGreaterThan(0);
        expect(settings.mutation.js.shards.length).toBeGreaterThan(0);
    });
});
