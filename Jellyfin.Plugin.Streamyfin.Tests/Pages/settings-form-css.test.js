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
        expect(rule(".sf-page .is-terse .sf-desc:not(.sf-problem):not(.sf-empty):not(.sf-keep)")?.style.getPropertyValue("display")).toBe("none");
    });

    test("hides the keys on any tab that asks, not only on the settings form", () => {
        expect(rule(".sf-page .is-keyless .sf-key:not(.sf-keep)")?.style.getPropertyValue("display")).toBe("none");
    });
});

describe("the legend's close button", () => {
    // The cross that hides the legend on the Application, Home, Notifications and
    // Targeting tabs was a 1.2rem glyph in a box as small as it, hard to see and to hit.
    test("is a 2rem square with a larger cross, centred in it", () => {
        const close = rule(".sf-page .sf-banner-close")?.style;
        expect([close?.getPropertyValue("width"), close?.getPropertyValue("height")]).toEqual(["2rem", "2rem"]);
        expect(close?.getPropertyValue("font-size")).toBe("1.6rem");
        expect([close?.getPropertyValue("display"), close?.getPropertyValue("align-items"), close?.getPropertyValue("justify-content")])
            .toEqual(["flex", "center", "center"]);
    });

    // The bigger box reaches into the banner's padding, so the cross stays where it was
    // drawn, in the banner's corner, rather than pushing the legend down and inwards.
    test("keeps the cross in the banner's corner", () => {
        const close = rule(".sf-page .sf-banner-close")?.style;
        expect(["top", "right", "bottom", "left"].map((side) => close?.getPropertyValue(`margin-${side}`)))
            .toEqual(["-0.35rem", "-0.45rem", "0px", "auto"]);
    });
});
