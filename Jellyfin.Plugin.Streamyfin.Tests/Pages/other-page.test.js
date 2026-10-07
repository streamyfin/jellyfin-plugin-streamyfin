import { describe, expect, test } from "bun:test";
import { restored } from "../../Jellyfin.Plugin.Streamyfin/Pages/Other/index.js";

describe("the sentence after a restore", () => {
    test("names whose own notification choices came back", () => {
        expect(restored({ configuration: true, groups: 2, users: 0, preferences: 3 }))
            .toBe("Restored the configuration, 2 groups, the notification choices of 3 people.");
        expect(restored({ preferences: 1 })).toBe("Restored the notification choices of 1 person.");
    });

    test("an older report, without the choices, reads as before", () => {
        expect(restored({ configuration: true, groups: 1, users: 1 })).toBe("Restored the configuration, 1 group, 1 user.");
    });

    test("names the titles people wait for that came back", () => {
        expect(restored({ groups: 1, awaited: 3 })).toBe("Restored 1 group, the 3 titles people wait for.");
        expect(restored({ awaited: 1 })).toBe("Restored the 1 title people wait for.");
    });
});
