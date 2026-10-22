// What a manifest update does to the version list. The rest of
// validate-and-update-manifest.js is a network call or a file write; this is the part
// where a mistake is silent, since a dropped entry looks exactly like one that was never
// published, and a misordered one looks exactly like a correct one until a server picks
// the wrong build.

import { describe, expect, test } from "bun:test";

const { place } = require("../../scripts/manifest-versions");

const JF11 = "10.11.9.0";
const JF12 = "12.0.0.0";

const entry = (version, targetAbi = JF12) => ({ version, targetAbi });
const names = (versions) => versions.map((v) => `${v.version}/${v.targetAbi}`);

describe("placing a version in a manifest", () => {
    test("the list stays newest first", () => {
        const { versions } = place([entry("0.68.1.0"), entry("0.68.0.0")], entry("0.69.0.0"), null);

        expect(versions.map((v) => v.version)).toEqual(["0.69.0.0", "0.68.1.0", "0.68.0.0"]);
    });

    // Sorted rather than pushed to the front. The publish workflow takes any ref, so a
    // build of an older commit carries a lower version, and the order the server reads
    // must not depend on the order builds happened to be published in.
    test("a version lower than one already there does not jump the queue", () => {
        const { versions } = place([entry("0.68.1.9"), entry("0.68.1.0")], entry("0.68.1.4"), null);

        expect(versions.map((v) => v.version)).toEqual(["0.68.1.9", "0.68.1.4", "0.68.1.0"]);
    });

    test("versions are compared as numbers, not as strings", () => {
        const { versions } = place([entry("0.68.1.9")], entry("0.68.1.10"), null);

        expect(versions.map((v) => v.version)).toEqual(["0.68.1.10", "0.68.1.9"]);
    });

    /// The property the shared manifest rests on.
    test("one version keeps its highest targetAbi first", () => {
        const { versions } = place([entry("0.69.0.0", JF11)], entry("0.69.0.0", JF12), null);

        expect(names(versions)).toEqual([`0.69.0.0/${JF12}`, `0.69.0.0/${JF11}`]);
    });

    test("the order holds whichever line is published first", () => {
        const { versions } = place([entry("0.69.0.0", JF12)], entry("0.69.0.0", JF11), null);

        expect(names(versions)).toEqual([`0.69.0.0/${JF12}`, `0.69.0.0/${JF11}`]);
    });

    // Matching on the version alone was right while each line had a file of its own.
    // Here it would have the jf12 build delete the jf11 build of the same release, and
    // every 10.11 server would stop being offered that version.
    test("a second line of the same version is added, not swapped in", () => {
        const { versions, replaced } = place([entry("0.69.0.0", JF11)], entry("0.69.0.0", JF12), null);

        expect(replaced).toBe(0);
        expect(versions).toHaveLength(2);
    });

    test("republishing one line replaces only that line's entry", () => {
        const existing = [entry("0.69.0.0", JF12), entry("0.69.0.0", JF11)];

        const { versions, replaced } = place(existing, entry("0.69.0.0", JF12), null);

        expect(replaced).toBe(1);
        expect(names(versions)).toEqual([`0.69.0.0/${JF12}`, `0.69.0.0/${JF11}`]);
    });

    test("no limit keeps every version the manifest ever published", () => {
        const existing = Array.from({ length: 40 }, (_, i) => entry(`0.1.${i}.0`));

        const { versions, dropped } = place(existing, entry("0.2.0.0"), null);

        expect(dropped).toEqual([]);
        expect(versions).toHaveLength(41);
    });

    // Counted in versions rather than entries. Ten entries would mean five releases
    // today and three the day a third Jellyfin line is added.
    test("a limit counts versions, and drops every entry of the ones it drops", () => {
        const existing = [
            entry("0.68.1.5", JF12), entry("0.68.1.5", JF11),
            entry("0.68.1.4", JF12), entry("0.68.1.4", JF11),
            entry("0.68.1.3", JF12), entry("0.68.1.3", JF11),
        ];

        const { versions, dropped } = place(existing, entry("0.68.1.6", JF12), 3);

        expect(versions.map((v) => v.version)).toEqual([
            "0.68.1.6", "0.68.1.5", "0.68.1.5", "0.68.1.4", "0.68.1.4",
        ]);
        expect(dropped.map((v) => v.version)).toEqual(["0.68.1.3", "0.68.1.3"]);
    });

    test("a list under the limit loses nothing", () => {
        const { versions, dropped } = place([entry("0.68.1.2"), entry("0.68.1.1")], entry("0.68.1.3"), 10);

        expect(versions).toHaveLength(3);
        expect(dropped).toEqual([]);
    });

    // Republishing must not cost a version's slot. Filtering first and trimming
    // afterwards is what makes that hold.
    test("republishing under a limit keeps the same versions", () => {
        const existing = [entry("0.68.1.5"), entry("0.68.1.4"), entry("0.68.1.3")];

        const { versions, replaced, dropped } = place(existing, entry("0.68.1.4"), 3);

        expect(replaced).toBe(1);
        expect(dropped).toEqual([]);
        expect(versions.map((v) => v.version)).toEqual(["0.68.1.5", "0.68.1.4", "0.68.1.3"]);
    });

    test("an empty manifest ends up with the one entry", () => {
        const { versions } = place([], entry("0.68.1.1"), 10);

        expect(names(versions)).toEqual([`0.68.1.1/${JF12}`]);
    });
});
