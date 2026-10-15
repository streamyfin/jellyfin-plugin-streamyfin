// Which floor a file counts toward, what no measure counts, and what the integration tests
// answer for instead.

import { afterEach, describe, expect, test } from "bun:test";
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { codeLines, folderOf, isExcluded, ownJsFiles, readOwners, unloaded } from "../../scripts/measure/folders.js";
import { ROOT } from "../../scripts/measure/settings.js";

describe("the folder a file counts toward", () => {
    test("is the plugin's folder it sits in, or the root, the pages or the scripts", () => {
        expect(folderOf("Jellyfin.Plugin.Streamyfin/Configuration/Settings/Settings.cs")).toBe("Configuration");
        expect(folderOf("Jellyfin.Plugin.Streamyfin/Plugin.cs")).toBe("(root)");
        expect(folderOf("Jellyfin.Plugin.Streamyfin/Pages/shared.js")).toBe("Pages");
        expect(folderOf("Jellyfin.Plugin.Streamyfin/Pages/Home/index.js")).toBe("Pages");
        expect(folderOf("scripts/measure/cli.js")).toBe("scripts");
    });

    test("is none for the tests and anything outside the plugin", () => {
        expect(folderOf("Jellyfin.Plugin.Streamyfin.Tests/DatabaseTests.cs")).toBeNull();
        expect(folderOf("/usr/share/dotnet/x.cs")).toBeNull();
    });
});

describe("what no measure counts", () => {
    test("is generated code and the libraries the pages ship", () => {
        expect(isExcluded("Jellyfin.Plugin.Streamyfin/obj/Release/net10.0/Generated.cs")).toBe(true);
        expect(isExcluded("Jellyfin.Plugin.Streamyfin/Migrations/20260901000000_Init.Designer.cs")).toBe(true);
        expect(isExcluded("Jellyfin.Plugin.Streamyfin/Migrations/PluginDbContextModelSnapshot.cs")).toBe(true);
        expect(isExcluded("Jellyfin.Plugin.Streamyfin/Pages/Libraries/js-yaml.min.js")).toBe(true);
    });

    test("is not a migration written by hand, nor the plugin's own code", () => {
        expect(isExcluded("Jellyfin.Plugin.Streamyfin/Migrations/20260901000000_Init.cs")).toBe(false);
        expect(isExcluded("Jellyfin.Plugin.Streamyfin/Pages/shared.js")).toBe(false);
        expect(isExcluded("Jellyfin.Plugin.Streamyfin/Db/PluginDatabase.cs")).toBe(false);
    });
});

const roots = [];

const rootWith = (owners, files = []) => {
    const root = mkdtempSync(join(tmpdir(), "measure-folders-"));
    mkdirSync(join(root, "tests"));
    writeFileSync(join(root, "tests", "owners.json"), JSON.stringify(owners));
    for (const file of files) {
        mkdirSync(join(root, file, ".."), { recursive: true });
        writeFileSync(join(root, file), "");
    }
    roots.push(root);
    return root;
};

afterEach(() => {
    for (const root of roots.splice(0)) rmSync(root, { recursive: true, force: true });
});

describe("the files the integration tests answer for", () => {
    test("are read with their reason", () => {
        const file = "Jellyfin.Plugin.Streamyfin/Plugin.cs";
        const owners = readOwners(rootWith([{ file, reason: "Jellyfin builds it" }], [file]));

        expect([...owners]).toEqual([[file, "Jellyfin builds it"]]);
    });

    test("each need a reason, and a file that exists", () => {
        const file = "Jellyfin.Plugin.Streamyfin/Plugin.cs";

        expect(() => readOwners(rootWith([{ file, reason: " " }], [file]))).toThrow("every entry needs a file and a reason");
        expect(() => readOwners(rootWith([{ file }], [file]))).toThrow("every entry needs a file and a reason");
        expect(() => readOwners(rootWith([{ reason: "no file" }], [file]))).toThrow("every entry needs a file and a reason");
        expect(() => readOwners(rootWith([{ file, reason: "gone" }]))).toThrow(`${file} does not exist`);
    });
});

describe("a JavaScript file the tests never load", () => {
    test("counts its lines of code at 0 %, not its blank lines or its comments", () => {
        const text = ["// a comment", "", "export const a = 1;", "/**", " * docs", " */", "export const b = a;"].join("\n");

        expect(codeLines(text)).toEqual([3, 7]);
        expect([...unloaded(text).lines]).toEqual([[3, 0], [7, 0]]);
    });

    test("is found among the repository's own, and the shipped libraries are not", () => {
        const own = ownJsFiles(ROOT);

        expect(own).toContain("Jellyfin.Plugin.Streamyfin/Pages/shared.js");
        expect(own).toContain("scripts/next-version.js");
        expect(own.some((path) => path.includes("/Libraries/"))).toBe(false);
        expect(own.some((path) => path.includes(".Tests/"))).toBe(false);
    });
});
