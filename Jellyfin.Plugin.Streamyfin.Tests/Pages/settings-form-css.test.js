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
