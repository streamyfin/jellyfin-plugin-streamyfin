// The stylesheet the settings form ships, read as the browser parses it. Only rules whose
// absence nobody would notice in a unit test of the page's script are checked here.
import { describe, expect, test } from "bun:test";
import { readFileSync } from "node:fs";
import { join } from "node:path";

const css = readFileSync(join(import.meta.dir, "../../Jellyfin.Plugin.Streamyfin/Pages/settings-form.css"), "utf8");
const sheet = new CSSStyleSheet();
sheet.replaceSync(css);
const rules = [...sheet.cssRules];
const rule = (selector) => rules.find((r) => r.selectorText === selector);

describe("a number field", () => {
    // The browser draws its up and down arrows inside the field, against the
    // right-aligned digits: on "Max auto play episode count" the arrows covered the
    // number, and on a dark page they were a white box.
    test("keeps its arrows clear of the digits", () => {
        expect(rule(".sf-page .sf-number::-webkit-inner-spin-button")?.style.getPropertyValue("margin-left")).toBe("0.6em");
    });

    test("draws its arrows in the page's theme", () => {
        expect(rule('.sf-page[data-sf-theme="dark"] .sf-number')?.style.getPropertyValue("color-scheme")).toBe("dark");
        expect(rule('.sf-page[data-sf-theme="light"] .sf-number')?.style.getPropertyValue("color-scheme")).toBe("light");
    });
});

describe("the help text switched off", () => {
    // Problems are written with the help text's class, so hiding every description hid
    // what was wrong with a value, and the save stayed refused for no visible reason.
    test("leaves what is wrong with a value, and an empty list's line, in view", () => {
        expect(rule(".sf-page .is-terse .sf-desc:not(.sf-problem):not(.sf-empty)")?.style.getPropertyValue("display")).toBe("none");
    });

    test("hides the keys on any tab that asks, not only on the settings form", () => {
        expect(rule(".sf-page .is-keyless .sf-key")?.style.getPropertyValue("display")).toBe("none");
    });
});
