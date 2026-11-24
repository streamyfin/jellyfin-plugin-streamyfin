// The Notifications tab's controls, built from the fields the server describes.
import { describe, expect, test } from "bun:test";
import { control } from "../../Jellyfin.Plugin.Streamyfin/Pages/Notifications/index.js";

describe("a wait on the Notifications tab", () => {
    // Left empty, a wait is the event's own, which the server sends with the field. The box
    // said nothing, so an administrator could not tell what an empty one meant.
    test("says the event's own wait while it is empty", () => {
        expect(control({ control: "Number", minimum: 0, serverDefault: 5 }).placeholder).toBe("5 by default");
        expect(control({ control: "Number", minimum: 0, serverDefault: 300 }).placeholder).toBe("300 by default");
    });

    test("says nothing for a number the server gives no default for", () => {
        expect(control({ control: "Number", minimum: 0 }).placeholder).toBe("");
    });
});
