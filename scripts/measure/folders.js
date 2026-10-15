// Which floor a file counts toward, which files no measure counts, the files the integration
// tests answer for instead, and the JavaScript the tests never load.

import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { slashed } from "./paths.js";

const PLUGIN = "Jellyfin.Plugin.Streamyfin/";

// The same patterns as tests/coverage.runsettings, both Stryker configurations and
// bunfig.toml, which a test holds equal.
export const EXCLUDED = [
    "**/obj/**",
    "**/Migrations/*.Designer.cs",
    "**/Migrations/*ModelSnapshot.cs",
    "Jellyfin.Plugin.Streamyfin/Pages/Libraries/**",
];

const excluded = EXCLUDED.map((pattern) => new Bun.Glob(pattern));

export const isExcluded = (path) => excluded.some((glob) => glob.match(path));

export const folderOf = (path) => {
    if (path.startsWith("scripts/")) return "scripts";
    if (!path.startsWith(PLUGIN)) return null;

    const rest = path.slice(PLUGIN.length);
    const slash = rest.indexOf("/");
    return slash === -1 ? "(root)" : rest.slice(0, slash);
};

export const readOwners = (root) => {
    const owners = new Map();
    for (const entry of JSON.parse(readFileSync(join(root, "tests", "owners.json")))) {
        if (!entry.file || !entry.reason?.trim()) {
            throw new Error(`tests/owners.json: every entry needs a file and a reason (${JSON.stringify(entry)})`);
        }
        if (!existsSync(join(root, entry.file))) {
            throw new Error(`tests/owners.json: ${entry.file} does not exist`);
        }
        owners.set(entry.file, entry.reason);
    }
    return owners;
};

// bun only reports the files the tests load, so one they never load is missing from its
// report. It counts at 0 %, over its lines of code.
export const codeLines = (text) => text.split(/\r?\n/).flatMap((line, index) => {
    const code = line.trim();
    const skipped = code === "" || code.startsWith("//") || code.startsWith("/*") || code.startsWith("*");
    return skipped ? [] : [index + 1];
});

export const unloaded = (text) => ({
    lines: new Map(codeLines(text).map((at) => [at, 0])),
    branches: new Map(),
});

export const ownJsFiles = (root) =>
    ["Jellyfin.Plugin.Streamyfin/Pages/**/*.js", "scripts/**/*.js"]
        .flatMap((pattern) => [...new Bun.Glob(pattern).scanSync({ cwd: root })])
        .map(slashed)
        .filter((path) => !isExcluded(path));
