// The settings form the Application page draws. These run under happy-dom, so what a
// browser would render can be held to account without a Jellyfin server: which control a
// setting gets, what the three states write, and what a search hides.

import { beforeEach, describe, expect, test } from "bun:test";
import {
    createForm,
    sections,
    stateOf,
    themeFromBackground,
} from "../../Jellyfin.Plugin.Streamyfin/Pages/settings-form.js";

const field = (key, control, extra = {}) => ({
    key,
    category: "Playback controls",
    group: "Skip and seek",
    title: key,
    description: "",
    control,
    lockable: true,
    minimum: null,
    maximum: null,
    step: null,
    options: [],
    dependsOn: null,
    ...extra,
});

const FIELDS = [
    field("forwardSkipTime", "Number", { title: "Forward skip time", description: "Seconds skipped forward", minimum: 0, maximum: 60, step: 5 }),
    field("enableDoubleTapToSeek", "Toggle", { title: "Double tap to seek" }),
    field("defaultBitrate", "Select", {
        group: "Quality",
        title: "Default playback quality",
        options: [{ value: null, label: "Max" }, { value: "_1MB", label: "1 MB" }, { value: "_2MB", label: "2 MB" }],
    }),
    field("jellyseerrServerUrl", "Text", { category: "Plugins", group: "Jellyseerr", title: "Seerr server" }),
    field("jellyseerrApiKey", "Secret", { category: "Plugins", group: "Jellyseerr", title: "Seerr API key", description: "**Warning** every user can read it" }),
    field("hiddenLibraries", "List", { category: "Home and appearance", group: null, title: "Hidden libraries" }),
    field("defaultAudioLanguage", "Language", { category: "Audio and subtitles", group: "Audio", title: "Default audio language" }),
    field("home", "Composite", { category: "Home and appearance", group: null, title: "Home view" }),
    field("subtitlesOnMute", "Toggle", { category: "Audio and subtitles", group: "Subtitles", title: "Subtitles on mute" }),
    field("subtitlesOnMuteAllowRestart", "Toggle", { category: "Audio and subtitles", group: "Subtitles", title: "Allow restart", dependsOn: "subtitlesOnMute" }),
];

const DEFAULTS = {
    forwardSkipTime: { value: 30, locked: false },
    enableDoubleTapToSeek: { value: false, locked: false },
    subtitlesOnMute: { value: true, locked: false },
};

const CULTURES = [
    { ThreeLetterISOLanguageName: "eng", DisplayName: "English" },
    { ThreeLetterISOLanguageName: "fra", DisplayName: "French" },
];

const mountForm = (values = {}, options = {}) => {
    const mount = document.createElement("div");
    document.body.appendChild(mount);
    const form = createForm(mount, { fields: FIELDS, values, defaults: DEFAULTS, cultures: CULTURES, ...options });
    return { mount, form };
};

const row = (mount, key) => mount.querySelector(`.sf-row[data-key="${key}"]`);
const control = (mount, key) => row(mount, key).querySelector("[data-control]");
const stateButton = (mount, key, state) => row(mount, key).querySelector(`.sf-state button[data-state="${state}"]`);
const pressed = (mount, key) => [...row(mount, key).querySelectorAll(".sf-state button")]
    .find((button) => button.getAttribute("aria-pressed") === "true")?.dataset.state;

const change = (element, apply) => {
    apply(element);
    element.dispatchEvent(new Event("change", { bubbles: true }));
};

beforeEach(() => {
    document.body.textContent = "";
});

describe("stateOf", () => {
    test("a setting the store does not carry is free", () => {
        expect(stateOf(undefined)).toBe("free");
        expect(stateOf(null)).toBe("free");
    });

    test("a stored unlocked setting is suggested, a locked one is locked", () => {
        expect(stateOf({ value: 30, locked: false })).toBe("suggested");
        expect(stateOf({ value: 30 })).toBe("suggested");
        expect(stateOf({ value: 30, locked: true })).toBe("locked");
    });
});

describe("sections", () => {
    test("arranges the fields by category then group, in declaration order", () => {
        const arranged = sections(FIELDS);

        expect(arranged.map((section) => section.category)).toEqual([
            "Playback controls", "Plugins", "Home and appearance", "Audio and subtitles",
        ]);
        expect(arranged[0].groups.map((group) => group.name)).toEqual(["Skip and seek", "Quality"]);
        expect(arranged[0].groups[0].fields.map((f) => f.key)).toEqual(["forwardSkipTime", "enableDoubleTapToSeek"]);
    });

    test("fields with no group share a card named after the category", () => {
        const home = sections(FIELDS).find((section) => section.category === "Home and appearance");

        expect(home.groups).toHaveLength(1);
        expect(home.groups[0].name).toBe("Home and appearance");
        expect(home.groups[0].fields.map((f) => f.key)).toEqual(["hiddenLibraries", "home"]);
    });
});

describe("createForm", () => {
    test("draws one row per field inside its category and group card", () => {
        const { mount } = mountForm();

        expect(mount.querySelectorAll(".sf-row")).toHaveLength(FIELDS.length);
        const card = row(mount, "defaultBitrate").closest(".sf-card");
        expect(card.dataset.category).toBe("Playback controls");
        expect(card.dataset.group).toBe("Quality");
    });

    test("a stored setting opens in the state the store says, showing its value", () => {
        const { mount } = mountForm({ forwardSkipTime: { value: 45, locked: true } });

        expect(pressed(mount, "forwardSkipTime")).toBe("locked");
        expect(row(mount, "forwardSkipTime").classList.contains("is-locked")).toBe(true);
        expect(control(mount, "forwardSkipTime").value).toBe("45");
    });

    test("a free setting shows the app default in its control", () => {
        const { mount } = mountForm();

        expect(pressed(mount, "forwardSkipTime")).toBe("free");
        expect(control(mount, "forwardSkipTime").value).toBe("30");
    });

    test("free drops a setting from what is saved", () => {
        const { mount, form } = mountForm({ forwardSkipTime: { value: 45, locked: true } });

        stateButton(mount, "forwardSkipTime", "free").click();

        expect(form.toSettings()).not.toHaveProperty("forwardSkipTime");
    });

    test("suggested adds a setting with the app default, unlocked", () => {
        const { mount, form } = mountForm();

        stateButton(mount, "forwardSkipTime", "suggested").click();

        expect(form.toSettings().forwardSkipTime).toEqual({ value: 30, locked: false });
        expect(row(mount, "forwardSkipTime").classList.contains("is-suggested")).toBe(true);
    });

    test("locked writes locked true and keeps the value", () => {
        const { mount, form } = mountForm({ forwardSkipTime: { value: 45, locked: false } });

        stateButton(mount, "forwardSkipTime", "locked").click();

        expect(form.toSettings().forwardSkipTime).toEqual({ value: 45, locked: true });
    });

    test("editing a free setting's control makes it suggested with the edited value", () => {
        const { mount, form } = mountForm();

        change(control(mount, "enableDoubleTapToSeek"), (input) => { input.checked = true; });

        expect(pressed(mount, "enableDoubleTapToSeek")).toBe("suggested");
        expect(form.toSettings().enableDoubleTapToSeek).toEqual({ value: true, locked: false });
    });

    test("a select offers the null choice and writes null for it", () => {
        const { mount, form } = mountForm({ defaultBitrate: { value: "_2MB", locked: false } });
        const select = control(mount, "defaultBitrate");

        expect([...select.options].map((option) => option.textContent)).toEqual(["Max", "1 MB", "2 MB"]);
        expect(select.value).toBe("_2MB");

        change(select, (el) => { el.value = ""; });

        expect(form.toSettings().defaultBitrate).toEqual({ value: null, locked: false });
    });

    test("a number carries its bounds and writes a number", () => {
        const { mount, form } = mountForm({ forwardSkipTime: { value: 45, locked: false } });
        const input = control(mount, "forwardSkipTime");

        expect(input.getAttribute("min")).toBe("0");
        expect(input.getAttribute("max")).toBe("60");
        expect(input.getAttribute("step")).toBe("5");

        change(input, (el) => { el.value = "55"; });

        expect(form.toSettings().forwardSkipTime).toEqual({ value: 55, locked: false });
    });

    test("a number left blank is invalid rather than saved as nothing", () => {
        const { mount, form } = mountForm({ forwardSkipTime: { value: 45, locked: false } });

        change(control(mount, "forwardSkipTime"), (el) => { el.value = ""; });

        expect(form.invalid()).toEqual(["forwardSkipTime"]);
        expect(row(mount, "forwardSkipTime").classList.contains("is-invalid")).toBe(true);
        expect(form.toSettings()).not.toHaveProperty("forwardSkipTime");
    });

    test("a list writes one item per non-blank line", () => {
        const { mount, form } = mountForm({ hiddenLibraries: { value: ["a"], locked: false } });
        const area = control(mount, "hiddenLibraries");

        expect(area.tagName).toBe("TEXTAREA");
        expect(area.value).toBe("a");

        change(area, (el) => { el.value = "a\n\n b \n"; });

        expect(form.toSettings().hiddenLibraries).toEqual({ value: ["a", "b"], locked: false });
    });

    // The cultures come from Jellyfin's API with PascalCase names; the config spells the
    // same two fields camelCase, because YamlDotNet reads it under that convention.
    test("a language writes the chosen culture's code and name the way the config spells them", () => {
        const { mount, form } = mountForm();
        const select = control(mount, "defaultAudioLanguage");

        expect([...select.options].map((option) => option.textContent)).toEqual(["Choose a language", "English", "French"]);

        change(select, (el) => { el.value = "fra"; });

        expect(form.toSettings().defaultAudioLanguage).toEqual({
            value: { threeLetterISOLanguageName: "fra", displayName: "French" },
            locked: false,
        });
    });

    test("a language opens on the stored culture, whichever spelling stored it", () => {
        const camel = { value: { threeLetterISOLanguageName: "eng", displayName: "English" }, locked: true };
        const { mount } = mountForm({ defaultAudioLanguage: camel });
        expect(control(mount, "defaultAudioLanguage").value).toBe("eng");

        const pascal = { value: { ThreeLetterISOLanguageName: "fra", DisplayName: "French" }, locked: false };
        const other = mountForm({ defaultAudioLanguage: pascal });
        expect(control(other.mount, "defaultAudioLanguage").value).toBe("fra");
    });

    test("a composite setting has no control and passes its stored value through", () => {
        const stored = { value: { sections: [{ title: "Films" }] }, locked: false };
        const { mount, form } = mountForm({ home: stored });

        expect(control(mount, "home")).toBeNull();
        expect(row(mount, "home").querySelector("a[href*='name=Yaml']")).not.toBeNull();

        stateButton(mount, "home", "locked").click();

        expect(form.toSettings().home).toEqual({ value: stored.value, locked: true });
    });

    test("a composite setting with no stored value cannot be turned on here", () => {
        const { mount } = mountForm();

        expect(stateButton(mount, "home", "suggested").disabled).toBe(true);
        expect(stateButton(mount, "home", "locked").disabled).toBe(true);
    });

    test("a secret is masked", () => {
        const { mount } = mountForm({ jellyseerrApiKey: { value: "abc", locked: true } });

        expect(control(mount, "jellyseerrApiKey").type).toBe("password");
    });

    test("dirty counts the settings that differ from what was loaded, and reset clears it", () => {
        const { mount, form } = mountForm({ forwardSkipTime: { value: 45, locked: false } });

        expect(form.dirtyCount()).toBe(0);

        stateButton(mount, "forwardSkipTime", "locked").click();
        change(control(mount, "enableDoubleTapToSeek"), (el) => { el.checked = true; });
        expect(form.dirtyCount()).toBe(2);

        form.reset();

        expect(form.dirtyCount()).toBe(0);
        expect(pressed(mount, "forwardSkipTime")).toBe("suggested");
        expect(control(mount, "forwardSkipTime").value).toBe("45");
        expect(pressed(mount, "enableDoubleTapToSeek")).toBe("free");
    });

    test("markSaved makes the current values the baseline", () => {
        const { mount, form } = mountForm();

        stateButton(mount, "forwardSkipTime", "locked").click();
        form.markSaved();

        expect(form.dirtyCount()).toBe(0);
        stateButton(mount, "forwardSkipTime", "free").click();
        expect(form.dirtyCount()).toBe(1);
    });

    test("search keeps the rows whose title, key or description match, and hides empty cards", () => {
        const { mount, form } = mountForm();

        form.search("skip");

        expect(row(mount, "forwardSkipTime").hidden).toBe(false);
        expect(row(mount, "enableDoubleTapToSeek").hidden).toBe(true);
        expect(row(mount, "jellyseerrApiKey").closest(".sf-card").hidden).toBe(true);

        form.search("every user");
        expect(row(mount, "jellyseerrApiKey").hidden).toBe(false);

        form.search("");
        expect(mount.querySelectorAll(".sf-row[hidden], .sf-card[hidden]")).toHaveLength(0);
    });

    test("showCategory shows one category's cards, and a search shows every category", () => {
        const { mount, form } = mountForm();

        form.showCategory("Plugins");

        expect(row(mount, "jellyseerrServerUrl").closest(".sf-card").hidden).toBe(false);
        expect(row(mount, "forwardSkipTime").closest(".sf-card").hidden).toBe(true);

        form.search("time");
        expect(row(mount, "forwardSkipTime").closest(".sf-card").hidden).toBe(false);

        form.search("");
        expect(row(mount, "forwardSkipTime").closest(".sf-card").hidden).toBe(true);
    });

    test("categories reports each category with how many settings it holds", () => {
        const { form } = mountForm();

        expect(form.categories()).toEqual([
            { name: "Playback controls", count: 3 },
            { name: "Plugins", count: 2 },
            { name: "Home and appearance", count: 2 },
            { name: "Audio and subtitles", count: 3 },
        ]);
    });

    test("a dependent setting says what it depends on", () => {
        const { mount } = mountForm();

        expect(row(mount, "subtitlesOnMuteAllowRestart").querySelector(".sf-why").textContent).toContain("Subtitles on mute");
        expect(row(mount, "subtitlesOnMuteAllowRestart").classList.contains("is-inert")).toBe(false);
    });

    test("a dependent setting is greyed and disabled while its toggle is locked off", () => {
        const { mount } = mountForm({ subtitlesOnMute: { value: false, locked: true } });
        const dependent = row(mount, "subtitlesOnMuteAllowRestart");

        expect(dependent.classList.contains("is-inert")).toBe(true);
        expect(control(mount, "subtitlesOnMuteAllowRestart").disabled).toBe(true);
        expect(dependent.querySelector(".sf-why").textContent).toContain("locked off");

        stateButton(mount, "subtitlesOnMute", "free").click();

        expect(dependent.classList.contains("is-inert")).toBe(false);
        expect(control(mount, "subtitlesOnMuteAllowRestart").disabled).toBe(false);
    });

    test("an inert setting can always be set free, and is not held invalid", () => {
        const fields = [
            field("enableHoldToSpeed", "Toggle", { title: "Hold to speed up" }),
            field("holdToSpeedRate", "Number", { title: "Hold to speed rate", dependsOn: "enableHoldToSpeed" }),
        ];
        const values = {
            enableHoldToSpeed: { value: false, locked: true },
            holdToSpeedRate: { value: null, locked: false },
        };
        const { mount, form } = mountForm(values, { fields, defaults: {} });

        expect(row(mount, "holdToSpeedRate").classList.contains("is-inert")).toBe(true);
        expect(form.invalid()).toEqual([]);
        expect(stateButton(mount, "holdToSpeedRate", "free").disabled).toBe(false);

        stateButton(mount, "holdToSpeedRate", "free").click();

        expect(pressed(mount, "holdToSpeedRate")).toBe("free");
        expect(form.toSettings()).not.toHaveProperty("holdToSpeedRate");
    });

    test("a whole number refuses a fraction and steps by one", () => {
        const fields = [field("forwardSkipTime", "Number", { title: "Forward skip time", integer: true })];
        const { mount, form } = mountForm({ forwardSkipTime: { value: 30, locked: false } }, { fields, defaults: {} });
        const input = control(mount, "forwardSkipTime");

        expect(input.getAttribute("step")).toBe("1");

        change(input, (el) => { el.value = "2.5"; });

        expect(form.invalid()).toEqual(["forwardSkipTime"]);
        expect(row(mount, "forwardSkipTime").querySelector(".sf-problem").textContent).toBe("Enter a whole number.");

        change(input, (el) => { el.value = "3"; });

        expect(form.invalid()).toEqual([]);
        expect(form.toSettings().forwardSkipTime).toEqual({ value: 3, locked: false });
    });

    test("a description renders its emphasis without the asterisks", () => {
        const { mount } = mountForm();
        const description = row(mount, "jellyseerrApiKey").querySelector(".sf-desc");

        expect(description.querySelector("strong").textContent).toBe("Warning");
        expect(description.textContent).not.toContain("*");
    });

    test("terse hides the descriptions through one class on the form", () => {
        const { mount, form } = mountForm();

        form.setTerse(true);
        expect(mount.querySelector(".sf-form").classList.contains("is-terse")).toBe(true);

        form.setTerse(false);
        expect(mount.querySelector(".sf-form").classList.contains("is-terse")).toBe(false);
    });

    test("onChange fires when a state or a value changes", () => {
        const { mount, form } = mountForm();
        let calls = 0;
        form.onChange(() => { calls += 1; });

        stateButton(mount, "forwardSkipTime", "locked").click();
        change(control(mount, "forwardSkipTime"), (el) => { el.value = "10"; });

        expect(calls).toBe(2);
    });

    test("settings the form does not draw are passed through untouched", () => {
        const { form } = mountForm({ somethingNewer: { value: 1, locked: false } });

        expect(form.toSettings().somethingNewer).toEqual({ value: 1, locked: false });
    });
});

// Settings the plugin declares no default for: the app decides, and the form must not
// pretend to know what it decides.
const UNDECLARED = [
    field("skipIntro", "Select", { category: "Media segment skip", group: null, title: "Skip intro",
        options: [{ value: "none", label: "None" }, { value: "ask", label: "Ask" }, { value: "auto", label: "Auto" }] }),
    field("streamyStatsMovieRecommendations", "Toggle", { category: "Plugins", group: "Streamystats", title: "Movie recommendations" }),
    field("mpvDemuxerMaxBytes", "Number", { group: "mpv", title: "mpv demuxer buffer (MB)" }),
    field("marlinServerUrl", "Text", { category: "Plugins", group: "Marlin search", title: "Marlin server" }),
];

describe("a setting with no declared default", () => {
    test("shows no value while free, rather than an invented one", () => {
        const { mount } = mountForm({}, { fields: UNDECLARED });

        const select = control(mount, "skipIntro");
        expect(select.selectedOptions[0].textContent).toBe("App default");
        expect(select.selectedOptions[0].disabled).toBe(true);
        expect(control(mount, "streamyStatsMovieRecommendations").indeterminate).toBe(true);
        expect(control(mount, "mpvDemuxerMaxBytes").value).toBe("");
        expect(control(mount, "mpvDemuxerMaxBytes").placeholder).toBe("App default");
        expect(control(mount, "marlinServerUrl").placeholder).toBe("App default");
    });

    test("is invalid once set until a choice is made", () => {
        const { mount, form } = mountForm({}, { fields: UNDECLARED });

        stateButton(mount, "skipIntro", "suggested").click();
        stateButton(mount, "mpvDemuxerMaxBytes", "locked").click();
        stateButton(mount, "marlinServerUrl", "locked").click();

        expect(form.invalid().sort()).toEqual(["marlinServerUrl", "mpvDemuxerMaxBytes", "skipIntro"]);
        expect(form.toSettings()).toEqual({});

        change(control(mount, "skipIntro"), (el) => { el.value = "ask"; });
        change(control(mount, "mpvDemuxerMaxBytes"), (el) => { el.value = "75"; });
        change(control(mount, "marlinServerUrl"), (el) => { el.value = "https://search.example"; });

        expect(form.invalid()).toEqual([]);
        expect(form.toSettings()).toEqual({
            skipIntro: { value: "ask", locked: false },
            mpvDemuxerMaxBytes: { value: 75, locked: true },
            marlinServerUrl: { value: "https://search.example", locked: true },
        });
    });

    test("a toggle set with no default starts off, and says so", () => {
        const { mount, form } = mountForm({}, { fields: UNDECLARED });

        stateButton(mount, "streamyStatsMovieRecommendations", "suggested").click();

        const toggle = control(mount, "streamyStatsMovieRecommendations");
        expect(toggle.indeterminate).toBe(false);
        expect(toggle.checked).toBe(false);
        expect(form.toSettings().streamyStatsMovieRecommendations).toEqual({ value: false, locked: false });
    });

    test("back to free, the value is forgotten again", () => {
        const { mount, form } = mountForm({}, { fields: UNDECLARED });

        change(control(mount, "skipIntro"), (el) => { el.value = "auto"; });
        stateButton(mount, "skipIntro", "free").click();

        expect(control(mount, "skipIntro").selectedOptions[0].textContent).toBe("App default");
        expect(form.dirtyCount()).toBe(0);
    });
});

describe("a declared default that is empty", () => {
    test("a list whose default is empty writes an empty list, not null", () => {
        const fields = [field("hiddenLibraries", "List", { title: "Hidden libraries" })];
        const { mount, form } = mountForm({}, { fields, defaults: { hiddenLibraries: { locked: false } } });

        stateButton(mount, "hiddenLibraries", "locked").click();

        expect(form.invalid()).toEqual([]);
        expect(form.toSettings().hiddenLibraries).toEqual({ value: [], locked: true });
    });

    test("a nullable choice whose default is null opens on its null option", () => {
        const fields = [field("defaultBitrate", "Select", { title: "Quality",
            options: [{ value: null, label: "Max" }, { value: "_1MB", label: "1 MB" }] })];
        const { mount, form } = mountForm({}, { fields, defaults: { defaultBitrate: { locked: false } } });

        expect(control(mount, "defaultBitrate").selectedOptions[0].textContent).toBe("Max");
        stateButton(mount, "defaultBitrate", "suggested").click();
        expect(form.invalid()).toEqual([]);
        expect(form.toSettings().defaultBitrate).toEqual({ value: null, locked: false });
    });
});

describe("themeFromBackground", () => {
    test("a dark dashboard background is dark, a light one is light", () => {
        expect(themeFromBackground("rgb(16, 16, 16)")).toBe("dark");
        expect(themeFromBackground("rgb(242, 242, 242)")).toBe("light");
    });

    test("no readable background is treated as the dashboard's default, dark", () => {
        expect(themeFromBackground("")).toBe("dark");
        expect(themeFromBackground("transparent")).toBe("dark");
    });
});
