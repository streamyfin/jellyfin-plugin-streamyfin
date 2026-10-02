// How AppSettingsManifest.json is written from the app. The manifest is only as good as
// what the script reads into it: the first one, written by a script nobody kept, recorded
// downloadQuality as having no default and subtitleMode as 0, and SettingsParityTests
// believed both.

import { afterEach, describe, expect, test } from "bun:test";

const fs = require("fs");
const os = require("os");
const path = require("path");

const { buildManifest, readSettings, readWireNames, Unreadable } = require("../../scripts/app-settings-manifest");

const made = [];

// A checkout: the two files the script reads, plus whatever other files a test gives it.
function checkout(settings, files = {}) {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "app-settings-manifest-"));
    made.push(root);
    const all = {
        "utils/atoms/settings.ts": settings,
        "utils/atoms/settingsOverrides.ts": overrides,
        "node_modules/fake-sdk/package.json": JSON.stringify({ name: "fake-sdk", main: "index.js" }),
        "node_modules/fake-sdk/index.js": 'exports.PlaybackMode = { Default: "Default", Always: "Always" };',
        ...files,
    };
    for (const [file, text] of Object.entries(all)) {
        fs.mkdirSync(path.dirname(path.join(root, file)), { recursive: true });
        fs.writeFileSync(path.join(root, file), text);
    }
    return root;
}

afterEach(() => {
    for (const root of made.splice(0)) fs.rmSync(root, { recursive: true, force: true });
});

const settings = (shape, defaults, rest = "") => `
import { Platform } from "react-native";
import { PlaybackMode } from "fake-sdk";
import { BITRATES } from "@/components/BitrateSelector";
import * as ScreenOrientation from "@/packages/expo-screen-orientation";
${rest}
export type Settings = {
${shape}
};
export const defaultValues: Settings = {
${defaults}
};
`;

// Sorted, like the app's: the first entry has to be worked out, not read off.
const BITRATE_SELECTOR = `
export const BITRATES = [
  { key: "2 Mb/s", value: 2000000 },
  { key: "Max", value: undefined },
].sort((a, b) => (b.value || Number.POSITIVE_INFINITY) - (a.value || Number.POSITIVE_INFINITY));
`;

const normalize = (body) => `const normalizePluginValue = (settingsKey, value) => {\n${body}\n  return value;\n};`;

// The settings alone, before the other names are looked for.
const read = (root) => readSettings(fs.readFileSync(path.join(root, "utils/atoms/settings.ts"), "utf8"), root);
const one = (root, key) => read(root).find((entry) => entry.key === key);

// The shape of the app's own readIntegrationBlocks when this was written.
const LEGACY = [
    ["jellyseerrServerUrl", "seerrServerUrl"],
    ["autoLoginJellyseerr", "autoLoginSeerr"],
];
const readIntegrationBlocks = (plugin) => {
    const { seerr: block, ...read } = plugin;
    const inBlock = block && typeof block === "object" ? block : {};
    for (const [from, to] of [["serverUrl", "seerrServerUrl"], ["autoLogin", "autoLoginSeerr"]]) {
        if (inBlock[from] !== undefined) read[to] = inBlock[from];
    }
    for (const [legacy, current] of LEGACY) {
        if (read[current] === undefined && read[legacy] !== undefined) read[current] = read[legacy];
        delete read[legacy];
    }
    return read;
};
const overrides = `export const LEGACY_SEERR_SETTINGS = ${JSON.stringify(LEGACY)} as const;\n`
    + "const LEGACY = LEGACY_SEERR_SETTINGS;\n"
    + `export const readIntegrationBlocks = ${readIntegrationBlocks.toString()};\n`;

describe("a default", () => {
    test("a literal is read as it is", () => {
        const root = checkout(settings("  rewindSkipTime: number;\n  showTitles: boolean;", "  rewindSkipTime: 10,\n  showTitles: true,"));

        expect(one(root, "rewindSkipTime")).toMatchObject({ default: 10, hasDefault: true, noDefaultReason: null });
        expect(one(root, "showTitles")).toMatchObject({ default: true, hasDefault: true });
    });

    // null reads as nothing chosen, which is what the plugin may leave alone.
    test("undefined, null and a missing key are no default", () => {
        const root = checkout(settings(
            "  home: Home | null;\n  preferedLanguage?: string;\n  videoPlayer?: number;",
            "  home: null,\n  preferedLanguage: undefined,"));

        for (const key of ["home", "preferedLanguage", "videoPlayer"]) {
            expect(one(root, key)).toMatchObject({ default: null, hasDefault: false, noDefaultReason: "none" });
        }
    });

    test("an enum member is its value, numbered the way TypeScript numbers it", () => {
        const root = checkout(settings(
            "  scale: Scale;\n  timeout: Timeout;",
            "  scale: Scale.Large,\n  timeout: Timeout.Second,",
            'enum Scale { Small = "small", Large = "large" }\nenum Timeout { Off, First = 60, Second }'));

        expect(one(root, "scale").default).toBe("large");
        expect(one(root, "timeout").default).toBe(61);
    });

    // What the first manifest got wrong: a constant it could not read became "none".
    test("a constant of the file is followed into", () => {
        const root = checkout(settings(
            "  downloadQuality?: DownloadOption;",
            "  downloadQuality: DownloadOptions[0],",
            'const DownloadOptions = [{ label: "Original quality", value: "original" }];'));

        expect(one(root, "downloadQuality")).toMatchObject({
            default: { label: "Original quality", value: "original" },
            hasDefault: true,
        });
    });

    // What the first manifest got wrong the other way: 0, where the app holds "Default".
    test("a value from a package is read from the checkout's node_modules", () => {
        const root = checkout(settings("  subtitleMode: PlaybackMode;", "  subtitleMode: PlaybackMode.Default,"));

        expect(one(root, "subtitleMode").default).toBe("Default");
    });

    test("a value from the app's own module is read from its source, and run when it is a pure expression", () => {
        const root = checkout(
            settings("  maxBitrate: number;", "  maxBitrate: BITRATES[1].value,"),
            { "components/BitrateSelector.tsx": BITRATE_SELECTOR });

        expect(one(root, "maxBitrate").default).toBe(2000000);
    });

    test("a module of the app the script cannot find stops the run", () => {
        const root = checkout(settings("  maxBitrate: Bitrate;", "  maxBitrate: BITRATES[1],"));

        expect(() => read(root)).toThrow(/cannot find @\/components\/BitrateSelector/);
    });

    test("a value that reads Platform has no single value to declare", () => {
        const root = checkout(settings(
            "  demuxerMaxBytes: number; // MB",
            '  demuxerMaxBytes: Platform.isTV && Platform.OS === "android" ? 75 : 150,'));

        expect(one(root, "demuxerMaxBytes")).toMatchObject({
            type: "number",
            default: null,
            hasDefault: false,
            noDefaultReason: "platform",
        });
    });

    test("an expression that reaches for anything but literals stops the run, naming the setting", () => {
        for (const expression of ["minutes(15)", "Date.now()"]) {
            const root = checkout(settings("  sleepTimer: number;", `  sleepTimer: ${expression},`));

            expect(() => read(root)).toThrow(Unreadable);
            expect(() => read(root)).toThrow(/sleepTimer/);
        }
    });

    test("a value JSON cannot carry stops the run", () => {
        for (const expression of ["Opts.toString", '-"x"', "[1, 2].map"]) {
            const root = checkout(settings("  odd: unknown;", `  odd: ${expression},`, "const Opts = { a: 1 };"));

            expect(() => read(root)).toThrow(/odd/);
        }
    });
});

describe("a value written down by hand", () => {
    const orientation = (phone, tv) => ({
        "packages/expo-screen-orientation.ts": `enum DummyOrientationLock { DEFAULT = ${phone}, ALL = 1 }`,
        "packages/expo-screen-orientation.tv.ts": `export enum OrientationLock { DEFAULT = ${tv}, ALL = 1 }`,
        "node_modules/expo-screen-orientation/package.json": JSON.stringify({ name: "expo-screen-orientation", main: "build/index.js" }),
        "node_modules/expo-screen-orientation/build/ScreenOrientation.types.js": "exports.OrientationLock = { DEFAULT: 0 };",
    });
    const orientationSettings = settings(
        "  defaultVideoOrientation: number;",
        "  defaultVideoOrientation: ScreenOrientation.OrientationLock.DEFAULT,");

    test("is taken once its sources still say it", () => {
        const root = checkout(orientationSettings, orientation(0, 0));

        expect(one(root, "defaultVideoOrientation").default).toBe(0);
    });

    test("stops the run when one of its sources says something else", () => {
        const root = checkout(orientationSettings, orientation(0, 4));

        expect(() => read(root)).toThrow(/APP_VALUES/);
    });
});

describe("a value the plugin sends in another shape", () => {
    test("the wire form is written next to the stored one, once the app's normalizer gives the default back", () => {
        const root = checkout(
            settings(
                "  subtitleSize: number;\n  defaultBitrate?: Bitrate;",
                "  subtitleSize: 1.1,\n  defaultBitrate: BITRATES[0],",
                normalize('  if (settingsKey === "subtitleSize" && value >= 10) return value / 100;\n'
                    + '  if (settingsKey === "defaultBitrate") return BITRATES.find((b) => b.value === value);')),
            { "components/BitrateSelector.tsx": BITRATE_SELECTOR });

        // 110, not the 110.00000000000001 a plain multiplication gives.
        expect(one(root, "subtitleSize")).toMatchObject({ default: 1.1, wireDefault: 110 });
        expect(one(root, "defaultBitrate")).toMatchObject({ default: { key: "Max" }, wireDefault: null });
        expect(one(root, "defaultBitrate").wireNote).toContain("BITRATES");
    });

    test("a normalizer that no longer gives the default back stops the run", () => {
        const root = checkout(settings("  subtitleSize: number;", "  subtitleSize: 1.0,", normalize("")));

        expect(() => read(root)).toThrow(/subtitleSize/);
    });

    // Under 10 the app keeps the number as it is, so 0.05 cannot travel as 5.
    test("a wire form the app would not turn back stops the run", () => {
        const root = checkout(settings(
            "  subtitleSize: number;",
            "  subtitleSize: 0.05,",
            normalize('  if (settingsKey === "subtitleSize" && value >= 10) return value / 100;')));

        expect(() => read(root)).toThrow(/RESHAPED is out of date/);
    });

    // normalizePluginValue rebuilds any { key, value } default from a scalar, so a new one
    // needs its wire form decided rather than assumed.
    test("a new { key, value } default stops the run until its wire form is written down", () => {
        const root = checkout(settings("  maxEpisodes: Count;", '  maxEpisodes: { key: "5", value: 5 },'));

        expect(() => read(root)).toThrow(/RESHAPED/);
    });
});

describe("the names the app also reads a setting under", () => {
    test("the old flat name and the block field are found by running the app's function", () => {
        const names = readWireNames({ LEGACY_SEERR_SETTINGS: LEGACY, readIntegrationBlocks });

        expect(Object.fromEntries(names)).toEqual({
            autoLoginSeerr: ["autoLoginJellyseerr", "seerr.autoLogin"],
            seerrServerUrl: ["jellyseerrServerUrl", "seerr.serverUrl"],
        });
    });

    // The app stops reading the old keys: the block stays, the old names go, and the
    // plugin still declaring them is then what SettingsParityTests fails on.
    test("an app that dropped the old names gives only the block's", () => {
        const blockOnly = (plugin) => {
            const read = readIntegrationBlocks(plugin);
            delete read.seerrServerUrl;
            delete read.autoLoginSeerr;
            const { seerr } = plugin;
            if (seerr) read.seerrServerUrl = seerr.serverUrl;
            return read;
        };

        expect(Object.fromEntries(readWireNames({ readIntegrationBlocks: blockOnly }, ["jellyseerrServerUrl"])))
            .toEqual({ seerrServerUrl: ["seerr.serverUrl"] });
    });

    test("an old name the app's list no longer names is still found while the app reads it", () => {
        const names = readWireNames({ readIntegrationBlocks }, ["jellyseerrServerUrl"]);

        expect(names.get("seerrServerUrl")).toEqual(["jellyseerrServerUrl", "seerr.serverUrl"]);
    });

    test("an app without readIntegrationBlocks stops the run rather than lose every other name", () => {
        expect(() => readWireNames({})).toThrow(/readIntegrationBlocks/);
    });

    test("an old name the app lists that feeds no setting stops the run", () => {
        expect(() => readWireNames({
            LEGACY_SEERR_SETTINGS: [["jellyseerrApiKey", "seerrApiKey"]],
            readIntegrationBlocks,
        })).toThrow(/jellyseerrApiKey/);
    });

    // The script only sees the fields it is asked for by name, so a reader that lists
    // them would otherwise lose some in silence.
    test("a reader that lists the block's fields stops the run", () => {
        const listing = (plugin) => ({ ...plugin.seerr });

        expect(() => readWireNames({ readIntegrationBlocks: listing })).toThrow(/lists the fields/);
    });

    test("they reach the manifest from the checkout's own settingsOverrides.ts", () => {
        const root = checkout(settings("  seerrServerUrl?: string;\n  autoLoginSeerr: boolean;", "  autoLoginSeerr: true,"));

        expect(buildManifest(root).find((entry) => entry.key === "seerrServerUrl").wireNames).toEqual(["jellyseerrServerUrl", "seerr.serverUrl"]);
        expect(buildManifest(root).find((entry) => entry.key === "autoLoginSeerr")).toMatchObject({ default: true, wireNames: ["autoLoginJellyseerr", "seerr.autoLogin"] });
    });

    test("a name feeding a setting the app does not have stops the run", () => {
        const root = checkout(settings("  autoLoginSeerr: boolean;", "  autoLoginSeerr: true,"));

        expect(() => buildManifest(root)).toThrow(/seerrServerUrl/);
    });
});

test("the entries come out sorted by key", () => {
    const root = checkout(settings(
        "  zeta: number;\n  alpha: number;\n  seerrServerUrl?: string;\n  autoLoginSeerr: boolean;",
        "  zeta: 1,\n  alpha: 2,"));

    expect(buildManifest(root).map((entry) => entry.key)).toEqual(["alpha", "autoLoginSeerr", "seerrServerUrl", "zeta"]);
});
