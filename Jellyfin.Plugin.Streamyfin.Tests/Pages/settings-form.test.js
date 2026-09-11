// The settings form the Application page draws. These run under happy-dom, so what a
// browser would render can be held to account without a Jellyfin server: which control a
// setting gets, what the three states write, and what a search hides.

import { beforeEach, describe, expect, test } from "bun:test";
import {
    createForm,
    groupsFor,
    inherited,
    askingFailed,
    probeText,
    probeTone,
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
        // The "no cap" choice as the server actually sends it: Jellyfin's JSON options
        // omit a null, so the option arrives with no value key at all rather than with
        // a null one. A fixture that spelled it out would test a shape nothing sends.
        options: [{ label: "Max" }, { value: "_1MB", label: "1 MB" }, { value: "_2MB", label: "2 MB" }],
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
            { name: "Playback controls", count: 3, set: 0, locked: 0 },
            { name: "Plugins", count: 2, set: 0, locked: 0 },
            { name: "Home and appearance", count: 2, set: 0, locked: 0 },
            { name: "Audio and subtitles", count: 3, set: 0, locked: 0 },
        ]);
    });

    test("a dependent setting says what it depends on", () => {
        const { mount } = mountForm();

        expect(row(mount, "subtitlesOnMuteAllowRestart").querySelector(".sf-why").textContent).toContain("Subtitles on mute");
        expect(row(mount, "subtitlesOnMuteAllowRestart").classList.contains("is-inert")).toBe(false);
    });

    test("a dependent setting is greyed, and says why, while its toggle is locked off", () => {
        const { mount } = mountForm({ subtitlesOnMute: { value: false, locked: true } });
        const dependent = row(mount, "subtitlesOnMuteAllowRestart");

        expect(dependent.classList.contains("is-inert")).toBe(true);
        expect(dependent.querySelector(".sf-why").textContent).toContain("locked off");
        // Greyed, not locked: the value can still be corrected.
        expect(control(mount, "subtitlesOnMuteAllowRestart").disabled).toBe(false);

        stateButton(mount, "subtitlesOnMute", "free").click();

        expect(dependent.classList.contains("is-inert")).toBe(false);
        expect(dependent.querySelector(".sf-why").textContent).toContain("Only matters");
    });

    // A null would reach the store as a number it never was, so an inert row is checked
    // like any other. It is never stuck: its control stays editable and Free is reachable.
    test("an inert setting is still validated, and can be corrected or set free", () => {
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
        expect(form.invalid()).toEqual(["holdToSpeedRate"]);
        expect(form.toSettings()).not.toHaveProperty("holdToSpeedRate");

        change(control(mount, "holdToSpeedRate"), (el) => { el.value = "2"; });

        expect(form.invalid()).toEqual([]);
        expect(form.toSettings().holdToSpeedRate).toEqual({ value: 2, locked: false });

        stateButton(mount, "holdToSpeedRate", "free").click();

        expect(pressed(mount, "holdToSpeedRate")).toBe("free");
        expect(form.invalid()).toEqual([]);
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

    test("a state filter keeps the set settings, or the locked ones, across every category", () => {
        const { mount, form } = mountForm({
            forwardSkipTime: { value: 45, locked: true },
            jellyseerrServerUrl: { value: "https://seerr.example", locked: false },
        });
        form.showCategory("Playback controls");

        form.filter("set");
        expect(row(mount, "forwardSkipTime").hidden).toBe(false);
        expect(row(mount, "jellyseerrServerUrl").hidden).toBe(false);
        expect(row(mount, "jellyseerrServerUrl").closest(".sf-card").hidden).toBe(false);
        expect(row(mount, "enableDoubleTapToSeek").hidden).toBe(true);
        expect(row(mount, "home").closest(".sf-card").hidden).toBe(true);

        form.filter("locked");
        expect(row(mount, "forwardSkipTime").hidden).toBe(false);
        expect(row(mount, "jellyseerrServerUrl").closest(".sf-card").hidden).toBe(true);

        form.filter(null);
        expect(row(mount, "enableDoubleTapToSeek").hidden).toBe(false);
        expect(row(mount, "jellyseerrServerUrl").closest(".sf-card").hidden).toBe(true);
    });

    test("categories count what is set and what is locked, live", () => {
        const { mount, form } = mountForm({ forwardSkipTime: { value: 45, locked: true } });

        expect(form.categories()[0]).toEqual({ name: "Playback controls", count: 3, set: 1, locked: 1 });

        stateButton(mount, "enableDoubleTapToSeek", "suggested").click();

        expect(form.categories()[0]).toEqual({ name: "Playback controls", count: 3, set: 2, locked: 1 });
    });

    test("an undecided toggle says the app decides, until it is set", () => {
        const fields = [field("streamyStatsMovieRecommendations", "Toggle", { title: "Movie recommendations" })];
        const { mount } = mountForm({}, { fields, defaults: {} });
        const toggle = control(mount, "streamyStatsMovieRecommendations");

        expect(toggle.title).toBe("The app decides until you set it");

        stateButton(mount, "streamyStatsMovieRecommendations", "suggested").click();

        expect(toggle.title).toBe("");
    });

    // Jellyfin's JSON options omit a null, so the "no cap" choice arrives with no value
    // key. Compared strictly against null it looked like no such choice existed, and a
    // quality set to Max was held invalid with "Choose a value".
    test("the no cap choice is a real answer, not a missing one", () => {
        const { mount, form } = mountForm({ defaultBitrate: { value: "_2MB", locked: false } });

        change(control(mount, "defaultBitrate"), (el) => { el.value = ""; });

        expect(form.invalid()).toEqual([]);
        expect(row(mount, "defaultBitrate").querySelector(".sf-problem").hidden).toBe(true);
        expect(form.toSettings().defaultBitrate).toEqual({ value: null, locked: false });
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

    test("the keys are shown or hidden on their own, apart from the descriptions", () => {
        const { mount, form } = mountForm({}, { keys: false });
        const root = mount.querySelector(".sf-form");

        expect(root.classList.contains("is-keyless")).toBe(true);

        form.setKeys(true);
        expect(root.classList.contains("is-keyless")).toBe(false);
        form.setTerse(true);
        expect(root.classList.contains("is-keyless")).toBe(false);
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
            options: [{ label: "Max" }, { value: "_1MB", label: "1 MB" }] })];
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

// The Targeting tab draws a level, a group or one user, with the same renderer in its
// overrides mode: only what the level changes is listed, everything else falls through
// to the level below, and the value it falls through to is shown beside the override.
describe("overrides mode", () => {
    const EVERYONE = {
        forwardSkipTime: { value: 30, locked: false },
        enableDoubleTapToSeek: { value: false, locked: false },
        defaultBitrate: { value: "_1MB", locked: false },
        subtitlesOnMute: { value: true, locked: true },
    };

    const mountLevel = (values = {}) => {
        const mount = document.createElement("div");
        document.body.appendChild(mount);
        const form = createForm(mount, { fields: FIELDS, values, defaults: EVERYONE, cultures: CULTURES, mode: "overrides" });
        return { mount, form };
    };

    // One list, no categories and no card headings: the page around it says whose level
    // this is and how many of the settings it changes.
    test("lists only what the level overrides, as one list", () => {
        const { mount, form } = mountLevel({ forwardSkipTime: { value: 45, locked: true } });

        expect(mount.querySelectorAll(".sf-card")).toHaveLength(1);
        expect(mount.querySelectorAll(".sf-card header")).toHaveLength(0);
        expect(mount.querySelector(".sf-form").classList.contains("is-overrides")).toBe(true);
        expect(row(mount, "forwardSkipTime").hidden).toBe(false);
        expect(row(mount, "enableDoubleTapToSeek").hidden).toBe(true);
        expect(form.overridden()).toEqual(["forwardSkipTime"]);
    });

    test("offers suggested and locked, never free, and a way to stop overriding", () => {
        const { mount, form } = mountLevel({ forwardSkipTime: { value: 45, locked: true } });
        const states = [...row(mount, "forwardSkipTime").querySelectorAll(".sf-state button")].map((b) => b.dataset.state);
        expect(states).toEqual(["suggested", "locked"]);

        row(mount, "forwardSkipTime").querySelector(".sf-drop").click();

        expect(row(mount, "forwardSkipTime").hidden).toBe(true);
        expect(form.overridden()).toEqual([]);
        expect(form.toSettings()).not.toHaveProperty("forwardSkipTime");
        expect(form.dirtyCount()).toBe(1);
    });

    test("an override starts from what everyone gets, and says what that is", () => {
        const { mount, form } = mountLevel();

        form.set("forwardSkipTime", "suggested");

        expect(row(mount, "forwardSkipTime").hidden).toBe(false);
        expect(control(mount, "forwardSkipTime").value).toBe("30");
        expect(row(mount, "forwardSkipTime").querySelector(".sf-from").textContent).toBe("everyone gets 30");
        expect(form.toSettings().forwardSkipTime).toEqual({ value: 30, locked: false });
    });

    test("the hint reads a choice by its label, a toggle as on or off, and nothing when everyone gets the app's default", () => {
        const { mount, form } = mountLevel();
        form.set("defaultBitrate", "locked");
        form.set("subtitlesOnMute", "suggested");
        form.set("hiddenLibraries", "suggested");

        expect(row(mount, "defaultBitrate").querySelector(".sf-from").textContent).toBe("everyone gets 1 MB");
        expect(row(mount, "subtitlesOnMute").querySelector(".sf-from").textContent).toBe("everyone gets on");
        expect(row(mount, "hiddenLibraries").querySelector(".sf-from").textContent).toBe("everyone gets the app's default");
    });

    test("the settings that can still be added are the ones not overridden, with their category", () => {
        const { form } = mountLevel({ forwardSkipTime: { value: 45, locked: true } });

        const candidates = form.candidates();

        expect(candidates.map((c) => c.key)).not.toContain("forwardSkipTime");
        expect(candidates.find((c) => c.key === "defaultBitrate")).toEqual({
            key: "defaultBitrate", title: "Default playback quality", category: "Playback controls",
        });
        expect(candidates.map((c) => c.key)).not.toContain("home");
    });

    // Discard restores the states, and the list has to follow them: an override added
    // since the load goes back out of sight, a dropped one comes back.
    test("discarding puts the list back in step with what was loaded", () => {
        const { mount, form } = mountLevel({ forwardSkipTime: { value: 45, locked: true } });

        form.set("enableDoubleTapToSeek", "suggested");
        row(mount, "forwardSkipTime").querySelector(".sf-drop").click();
        expect(row(mount, "enableDoubleTapToSeek").hidden).toBe(false);
        expect(row(mount, "forwardSkipTime").hidden).toBe(true);

        form.reset();

        expect(row(mount, "enableDoubleTapToSeek").hidden).toBe(true);
        expect(row(mount, "forwardSkipTime").hidden).toBe(false);
        expect(form.overridden()).toEqual(["forwardSkipTime"]);
        expect(form.dirtyCount()).toBe(0);
    });

    test("a level saves exactly its overrides, and a setting it cannot draw passes through", () => {
        const { form } = mountLevel({ forwardSkipTime: { value: 45, locked: true }, somethingNewer: { value: 1, locked: false } });
        form.set("enableDoubleTapToSeek", "suggested");

        expect(form.toSettings()).toEqual({
            forwardSkipTime: { value: 45, locked: true },
            enableDoubleTapToSeek: { value: false, locked: false },
            somethingNewer: { value: 1, locked: false },
        });
    });
});

describe("what a level inherits", () => {
    const APP = { forwardSkipTime: { value: 30, locked: false }, subtitleSize: { value: 80, locked: false } };
    const SERVER = { forwardSkipTime: { value: 15, locked: false } };
    const GROUPS = [
        { name: "Kids", priority: 10, userIds: ["u1"], settings: { forwardSkipTime: { value: 45, locked: true } } },
        { name: "TVs", priority: 1, userIds: ["u1", "u2"], settings: { forwardSkipTime: { value: 20, locked: false }, subtitleSize: { value: 120, locked: false } } },
    ];

    test("the most specific level wins, and the rest falls through", () => {
        const merged = inherited(APP, SERVER);

        expect(merged.forwardSkipTime).toEqual({ value: 15, locked: false });
        expect(merged.subtitleSize).toEqual({ value: 80, locked: false });
    });

    test("a user's groups come in ascending priority, so the highest priority speaks last", () => {
        expect(groupsFor(GROUPS, "u1").map((g) => g.name)).toEqual(["TVs", "Kids"]);
        expect(groupsFor(GROUPS, "u2").map((g) => g.name)).toEqual(["TVs"]);
        expect(groupsFor(GROUPS, "nobody")).toEqual([]);

        const forU1 = inherited(APP, SERVER, ...groupsFor(GROUPS, "u1").map((g) => g.settings));
        expect(forU1.forwardSkipTime).toEqual({ value: 45, locked: true });
        expect(forU1.subtitleSize).toEqual({ value: 120, locked: false });
    });
});

// An address is the one setting that can be wrong in a way nobody notices: it saves, it
// looks right, and it shows up as an empty tab in the app days later. The server does
// the reaching, because it can see an internal address the browser never will.
describe("testing an address", () => {
    const PROBED = [
        ...FIELDS,
        field("marlinServerUrl", "Text", { category: "Plugins", group: "Marlin search", title: "Marlin server", probe: "Marlin", address: true }),
    ];

    const withProbe = (probe) => {
        const mount = document.createElement("div");
        document.body.appendChild(mount);
        return { mount, form: createForm(mount, { fields: PROBED, values: {}, defaults: DEFAULTS, cultures: CULTURES, probe }) };
    };

    const row = (mount, key) => mount.querySelector(`[data-key="${key}"]`);

    // The button's answer arrives through a promise chain, so a few turns of the
    // microtask queue rather than a count that has to be kept in step with its length.
    const flush = async () => { for (let i = 0; i < 8; i++) await Promise.resolve(); };

    test("only a field that declares a probe gets a button", () => {
        const { mount } = withProbe(() => Promise.resolve({ outcome: "Ok" }));

        expect([...row(mount, "marlinServerUrl").querySelectorAll("button")].some((b) => b.textContent === "Test")).toBe(true);
        expect([...row(mount, "jellyseerrServerUrl").querySelectorAll("button")].some((b) => b.textContent === "Test")).toBe(false);
    });

    test("no button at all when the page passed no way to ask", () => {
        const mount = document.createElement("div");
        document.body.appendChild(mount);
        createForm(mount, { fields: PROBED, values: {}, defaults: DEFAULTS, cultures: CULTURES });

        expect([...row(mount, "marlinServerUrl").querySelectorAll("button")].some((b) => b.textContent === "Test")).toBe(false);
    });

    test("the address on screen is what gets tried, not what was saved", async () => {
        const asked = [];
        const { mount } = withProbe((kind, address) => {
            asked.push([kind, address]);
            return Promise.resolve({ outcome: "Ok", version: "2.1.0" });
        });

        const marlin = row(mount, "marlinServerUrl");
        const input = marlin.querySelector("input");
        input.value = "  https://marlin.example.com  ";

        [...marlin.querySelectorAll("button")].find((b) => b.textContent === "Test").click();
        await flush();

        expect(asked).toEqual([["Marlin", "https://marlin.example.com"]]);
        expect(marlin.querySelector(".sf-said").textContent).toBe("Answered, running 2.1.0.");
        expect(marlin.querySelector(".sf-said").className).toContain("sf-said--ok");
    });

    test("a server that cannot be asked says so rather than staying on Asking", async () => {
        const { mount } = withProbe(() => Promise.reject(new Error("no")));

        const marlin = row(mount, "marlinServerUrl");
        [...marlin.querySelectorAll("button")].find((b) => b.textContent === "Test").click();
        await flush();

        expect(marlin.querySelector(".sf-said").textContent).toBe("The server could not be asked.");
        expect(marlin.querySelector(".sf-said").className).toContain("sf-said--no");
    });

    test("an empty field does not read as a broken one", async () => {
        const { mount } = withProbe(() => Promise.resolve({ outcome: "NotConfigured", detail: "Nothing is configured." }));

        const marlin = row(mount, "marlinServerUrl");
        [...marlin.querySelectorAll("button")].find((b) => b.textContent === "Test").click();
        await flush();

        expect(marlin.querySelector(".sf-said").textContent).toBe("Nothing is configured.");
        expect(marlin.querySelector(".sf-said").className).toContain("sf-said--quiet");
        expect(marlin.querySelector(".sf-said").className).not.toContain("sf-said--no");
    });

    test("pressing Locked keeps an answer about an address that did not move", async () => {
        const { mount } = withProbe(() => Promise.resolve({ outcome: "Ok", version: "2.1.0" }));

        const marlin = row(mount, "marlinServerUrl");
        marlin.querySelector('.sf-state button[data-state="suggested"]').click();
        const input = marlin.querySelector("input");
        input.value = "https://marlin.example.com";
        input.dispatchEvent(new Event("change", { bubbles: true }));
        [...marlin.querySelectorAll("button")].find((b) => b.textContent === "Test").click();
        await flush();

        marlin.querySelector('.sf-state button[data-state="locked"]').click();

        expect(marlin.querySelector(".sf-said").textContent).toBe("Answered, running 2.1.0.");
    });

    test("an answer that lands after the address changed is not shown against it", async () => {
        let answer;
        const { mount } = withProbe(() => new Promise((resolve) => { answer = resolve; }));

        const marlin = row(mount, "marlinServerUrl");
        marlin.querySelector('.sf-state button[data-state="suggested"]').click();
        const input = marlin.querySelector("input");
        input.value = "https://one.example.com";
        input.dispatchEvent(new Event("change", { bubbles: true }));
        [...marlin.querySelectorAll("button")].find((b) => b.textContent === "Test").click();
        await flush();

        // The address moves while the server is still thinking about the old one.
        input.value = "https://two.example.com";
        input.dispatchEvent(new Event("input", { bubbles: true }));
        input.dispatchEvent(new Event("change", { bubbles: true }));

        answer({ outcome: "Ok", version: "2.1.0" });
        await flush();

        expect(marlin.querySelector(".sf-said").textContent).toBe("");
    });

    test("a failure message goes when the address does, not only when someone types", async () => {
        const { mount, form } = withProbe(() => Promise.reject(new Error("no")));

        const marlin = row(mount, "marlinServerUrl");
        marlin.querySelector('.sf-state button[data-state="suggested"]').click();
        const input = marlin.querySelector("input");
        input.value = "https://one.example.com";
        input.dispatchEvent(new Event("change", { bubbles: true }));
        [...marlin.querySelectorAll("button")].find((b) => b.textContent === "Test").click();
        await flush();
        expect(marlin.querySelector(".sf-said").textContent).toBe("The server could not be asked.");

        form.reset();

        expect(marlin.querySelector(".sf-said").textContent).toBe("");
    });

    test("an answer about a free row's empty address goes when Discard empties it", async () => {
        const { mount, form } = withProbe(() => Promise.resolve({ outcome: "Ok", version: "2.1.0" }));

        const marlin = row(mount, "marlinServerUrl");
        marlin.querySelector('.sf-state button[data-state="suggested"]').click();
        const input = marlin.querySelector("input");
        input.value = "https://one.example.com";
        input.dispatchEvent(new Event("change", { bubbles: true }));
        [...marlin.querySelectorAll("button")].find((b) => b.textContent === "Test").click();
        await flush();

        // Back to free, where the value is undefined rather than a string.
        form.reset();

        expect(marlin.querySelector(".sf-said").textContent).toBe("");
    });

    test("a helper that throws before it returns a promise still frees the button", async () => {
        const { mount } = withProbe(() => { throw new TypeError("ApiClient is not ready"); });

        const marlin = row(mount, "marlinServerUrl");
        const test = [...marlin.querySelectorAll("button")].find((b) => b.textContent === "Test");
        test.click();
        await flush();

        expect(marlin.querySelector(".sf-said").textContent).toBe("The server could not be asked.");
        expect(test.disabled).toBe(false);
    });

    test("editing the address clears an answer that was about the old one", async () => {
        const { mount } = withProbe(() => Promise.resolve({ outcome: "Ok", version: "2.1.0" }));

        const marlin = row(mount, "marlinServerUrl");
        const input = marlin.querySelector("input");
        input.value = "https://marlin.example.com";
        [...marlin.querySelectorAll("button")].find((b) => b.textContent === "Test").click();
        await flush();
        expect(marlin.querySelector(".sf-said").textContent).not.toBe("");

        input.value = "https://other.example.com";
        input.dispatchEvent(new Event("input", { bubbles: true }));

        expect(marlin.querySelector(".sf-said").textContent).toBe("");
    });

    test("an address the server would refuse is marked on the field", () => {
        const { mount, form } = withProbe(() => Promise.resolve({ outcome: "Ok" }));

        const marlin = row(mount, "marlinServerUrl");
        marlin.querySelector('.sf-state button[data-state="suggested"]').click();
        const input = marlin.querySelector("input");

        for (const bad of ["192.168.1.5:3000", "marlin.example.com", "file:///etc/passwd", "   "]) {
            input.value = bad;
            input.dispatchEvent(new Event("change", { bubbles: true }));
            expect(form.invalid()).toContain("marlinServerUrl");
        }

        input.value = "https://marlin.example.com";
        input.dispatchEvent(new Event("change", { bubbles: true }));
        expect(form.invalid()).not.toContain("marlinServerUrl");
    });

    test("the form refuses exactly what the server refuses", () => {
        const { mount, form } = withProbe(() => Promise.resolve({ outcome: "Ok" }));

        const marlin = row(mount, "marlinServerUrl");
        marlin.querySelector('.sf-state button[data-state="suggested"]').click();
        const input = marlin.querySelector("input");

        // Every one of these is refused by Uri.TryCreate on the server. Checked against
        // .NET rather than assumed, since the browser's parser is looser in both
        // directions.
        const refused = [
            "192.168.1.5:3000",
            "marlin.example.com",
            "file:///etc/passwd",
            "http:/marlin.example.com",
            "http:marlin.example.com",
            "http://",
            "http://.",
            "http://..",
            "http://%41",
            "   ",
        ];

        for (const bad of refused) {
            input.value = bad;
            input.dispatchEvent(new Event("change", { bubbles: true }));
            expect(form.invalid()).toContain("marlinServerUrl");
        }

        for (const good of ["https://marlin.example.com", "http://10.0.0.1:3000/marlin"]) {
            input.value = good;
            input.dispatchEvent(new Event("change", { bubbles: true }));
            expect(form.invalid()).not.toContain("marlinServerUrl");
        }
    });

    test("what is saved is what was tested, without the space that came with the paste", () => {
        const { mount, form } = withProbe(() => Promise.resolve({ outcome: "Ok" }));

        const marlin = row(mount, "marlinServerUrl");
        marlin.querySelector('.sf-state button[data-state="suggested"]').click();
        const input = marlin.querySelector("input");
        input.value = "  https://marlin.example.com  ";
        input.dispatchEvent(new Event("change", { bubbles: true }));

        expect(form.toSettings().marlinServerUrl.value).toBe("https://marlin.example.com");
    });

    test("the answer is announced and each button says what it tests", () => {
        const { mount } = withProbe(() => Promise.resolve({ outcome: "Ok" }));

        const marlin = row(mount, "marlinServerUrl");
        expect(marlin.querySelector(".sf-said").getAttribute("role")).toBe("status");
        expect([...marlin.querySelectorAll("button")].find((b) => b.textContent === "Test").getAttribute("aria-label"))
            .toBe("Test Marlin server");
    });

    test("something serving HTTP with no signature is not read as confirmed", () => {
        expect(probeTone({ outcome: "Ok" })).toBe("sf-said--ok");
        expect(probeTone({ outcome: "Down" })).toBe("sf-said--no");
        // An outcome this page does not know is not a failure it can name.
        expect(probeTone({ outcome: "SomethingNewer" })).toBe("sf-said--quiet");
        expect(probeTone({ outcome: "Reachable" })).toBe("sf-said--maybe");
        // Without a detail it still has to read as something that answered.
        expect(probeText({ outcome: "Reachable" })).toBe("Something answered, and nothing there says what it is.");
        expect(probeText({ outcome: "Reachable", detail: "Answered with 200. Nothing there identifies the service." }))
            .toBe("Answered with 200. Nothing there identifies the service.");
    });

    test("discarding clears an answer about the address that was there", async () => {
        const { mount, form } = withProbe(() => Promise.resolve({ outcome: "Ok", version: "2.1.0" }));

        const marlin = row(mount, "marlinServerUrl");
        marlin.querySelector('.sf-state button[data-state="suggested"]').click();
        const input = marlin.querySelector("input");
        input.value = "https://marlin.example.com";
        input.dispatchEvent(new Event("change", { bubbles: true }));
        [...marlin.querySelectorAll("button")].find((b) => b.textContent === "Test").click();
        await flush();
        expect(marlin.querySelector(".sf-said").textContent).not.toBe("");

        form.reset();

        expect(marlin.querySelector(".sf-said").textContent).toBe("");
    });

    test("a setting that is an address with nothing to ask is still checked", () => {
        const mount = document.createElement("div");
        document.body.appendChild(mount);
        const fields = [
            ...FIELDS,
            field("webhookUrl", "Text", { category: "Plugins", group: "Other", title: "Webhook", address: true }),
        ];
        const form = createForm(mount, { fields, values: {}, defaults: DEFAULTS, cultures: CULTURES });

        const hook = mount.querySelector('[data-key="webhookUrl"]');
        hook.querySelector('.sf-state button[data-state="suggested"]').click();
        const input = hook.querySelector("input");
        input.value = "not an address";
        input.dispatchEvent(new Event("change", { bubbles: true }));

        expect(form.invalid()).toContain("webhookUrl");
        // And no Test button, since nothing declared a service at the other end.
        expect([...hook.querySelectorAll("button")].some((b) => b.textContent === "Test")).toBe(false);
    });

    test("only an address is trimmed, since a key can mean its edges", () => {
        const mount = document.createElement("div");
        document.body.appendChild(mount);
        const form = createForm(mount, { fields: PROBED, values: {}, defaults: DEFAULTS, cultures: CULTURES });

        const key = mount.querySelector('[data-key="jellyseerrApiKey"]');
        key.querySelector('.sf-state button[data-state="suggested"]').click();
        const secret = key.querySelector("input");
        secret.value = "  a-key ";
        secret.dispatchEvent(new Event("change", { bubbles: true }));

        const marlin = mount.querySelector('[data-key="marlinServerUrl"]');
        marlin.querySelector('.sf-state button[data-state="suggested"]').click();
        const address = marlin.querySelector("input");
        address.value = "  https://marlin.example.com  ";
        address.dispatchEvent(new Event("change", { bubbles: true }));

        const saved = form.toSettings();
        expect(saved.jellyseerrApiKey.value).toBe("  a-key ");
        expect(saved.marlinServerUrl.value).toBe("https://marlin.example.com");
    });

    test("editing while the server is thinking leaves the button usable", async () => {
        let answer;
        const { mount } = withProbe(() => new Promise((resolve) => { answer = resolve; }));

        const marlin = row(mount, "marlinServerUrl");
        marlin.querySelector('.sf-state button[data-state="suggested"]').click();
        const input = marlin.querySelector("input");
        input.value = "https://one.example.com";
        input.dispatchEvent(new Event("change", { bubbles: true }));
        const test = [...marlin.querySelectorAll("button")].find((b) => b.textContent === "Test");
        test.click();
        await flush();

        input.value = "https://two.example.com";
        input.dispatchEvent(new Event("input", { bubbles: true }));
        expect(test.disabled).toBe(false);

        answer({ outcome: "Ok", version: "2.1.0" });
        await flush();

        expect(test.disabled).toBe(false);
        expect(marlin.querySelector(".sf-said").textContent).toBe("");
    });

    test("discarding while the server is thinking drops the answer", async () => {
        let answer;
        const { mount, form } = withProbe(() => new Promise((resolve) => { answer = resolve; }));

        const marlin = row(mount, "marlinServerUrl");
        marlin.querySelector('.sf-state button[data-state="suggested"]').click();
        const input = marlin.querySelector("input");
        input.value = "https://one.example.com";
        input.dispatchEvent(new Event("change", { bubbles: true }));
        [...marlin.querySelectorAll("button")].find((b) => b.textContent === "Test").click();
        await flush();

        form.reset();
        answer({ outcome: "Ok", version: "2.1.0" });
        await flush();

        expect(marlin.querySelector(".sf-said").textContent).toBe("");
    });

    test("a bracketed authority still has to be an address", () => {
        // Unterminated, and a port out of range: both parse loosely enough to slip past
        // a check that stops at the bracket, and .NET refuses both.
        const { mount, form } = withProbe(() => Promise.resolve({ outcome: "Ok" }));

        const marlin = row(mount, "marlinServerUrl");
        marlin.querySelector('.sf-state button[data-state="suggested"]').click();
        const input = marlin.querySelector("input");

        for (const bad of ["http://[not-an-ipv6]", "http://[zzz]:80", "http://[]1", "http://[::1", "http://[::1]:99999"]) {
            input.value = bad;
            input.dispatchEvent(new Event("change", { bubbles: true }));
            expect(form.invalid()).toContain("marlinServerUrl");
        }
    });

    test("a refusal the route wrote is the one shown", () => {
        expect(askingFailed({ body: '"Say which service to try: Seerr, Marlin or Streamystats."' }))
            .toBe("Say which service to try: Seerr, Marlin or Streamystats.");
        expect(askingFailed({ body: '{"title":"One or more validation errors occurred."}' }))
            .toBe("The server could not be asked.");
        expect(askingFailed(new Error("network"))).toBe("The server could not be asked.");
    });

    test("a version is shown whole, since the server is what bounds it", () => {
        expect(probeText({ outcome: "Ok", version: "develop-68c5bc8c7d85" }))
            .toBe("Answered, running develop-68c5bc8c7d85.");
    });

    test("the form can point at the setting that is blocking the save", () => {
        const { mount, form } = withProbe(() => Promise.resolve({ outcome: "Ok" }));

        form.showCategory("Playback controls");

        const marlin = row(mount, "marlinServerUrl");
        marlin.querySelector('.sf-state button[data-state="suggested"]').click();
        const input = marlin.querySelector("input");
        input.value = "marlin.example.com";
        input.dispatchEvent(new Event("change", { bubbles: true }));

        // Named rather than moved to: the page owns its search box and its pills, and
        // this used to clear them behind its back.
        expect(form.firstProblem()).toEqual({ key: "marlinServerUrl", title: "Marlin server", category: "Plugins" });

        form.showCategory("Plugins");
        expect(form.reveal("marlinServerUrl")).toBe(true);
        expect(form.reveal("nothing-declared-this")).toBe(false);
    });

    test("nothing to point at when nothing is wrong", () => {
        const { form } = withProbe(() => Promise.resolve({ outcome: "Ok" }));

        expect(form.firstProblem()).toBe(null);
    });

    test("every outcome reads as a sentence", () => {
        expect(probeText({ outcome: "Ok", version: "2.1.0" })).toBe("Answered, running 2.1.0.");
        expect(probeText({ outcome: "Ok", detail: "Answered with 404." })).toBe("Answered with 404.");
        expect(probeText({ outcome: "NotConfigured" })).toBe("Nothing to try yet.");
        expect(probeText({ outcome: "NotAUrl", detail: "That is not an http address." })).toBe("That is not an http address.");
        expect(probeText({ outcome: "WrongService" })).toBe("Something answered, but not this service.");
        expect(probeText({ outcome: "Unreachable" })).toBe("Nothing answered at that address.");
        expect(probeText({ outcome: "Down" })).toBe("The service answered that it is not working.");
        // A page that has not caught up with a server that gained an outcome.
        expect(probeText({ outcome: "SomethingNewer" }))
            .toBe("The server gave an answer this page does not understand.");
        expect(probeText(null)).toBe("The server gave no answer.");
    });
});
