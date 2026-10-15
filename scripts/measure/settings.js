// The settings every test layer reads, from tests/settings.json. Each one can be overridden
// for a run by STREAMYFIN_TEST_ and its path in capitals, dots as underscores: iterations.unit
// is STREAMYFIN_TEST_ITERATIONS_UNIT. A list or an object is overridden as JSON.

import { readFileSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

// Read the same way under bun and under node, which runs the StrykerJS configuration.
export const ROOT = fileURLToPath(new URL("../..", import.meta.url));

export const envName = (path) => `STREAMYFIN_TEST_${path.toUpperCase().replaceAll(".", "_")}`;

const parse = (name, raw, written) => {
    if (typeof written === "number") {
        const value = Number(raw);
        if (raw.trim() === "" || !Number.isFinite(value)) {
            throw new Error(`${name} is not a number: ${JSON.stringify(raw)}`);
        }
        return value;
    }
    if (typeof written === "boolean") {
        if (raw !== "true" && raw !== "false") {
            throw new Error(`${name} is neither true nor false: ${JSON.stringify(raw)}`);
        }
        return raw === "true";
    }
    if (typeof written === "string") return raw;
    try {
        return JSON.parse(raw);
    } catch {
        throw new Error(`${name} is not JSON: ${JSON.stringify(raw)}`);
    }
};

const isObject = (value) => value !== null && typeof value === "object" && !Array.isArray(value);

const overridden = (value, path, env) => {
    if (path !== "" && env[envName(path)] !== undefined) {
        return parse(envName(path), env[envName(path)], value);
    }
    if (!isObject(value)) return value;
    return Object.fromEntries(Object.entries(value).map(([key, child]) =>
        [key, overridden(child, path === "" ? key : `${path}.${key}`, env)]));
};

export const readSettings = ({ root = ROOT, env = process.env } = {}) =>
    overridden(JSON.parse(readFileSync(join(root, "tests", "settings.json"))), "", env);
