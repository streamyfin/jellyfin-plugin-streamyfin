# Generated admin form

This is P3.1, the first slice of P3. The admin settings that today are hand
written HTML become a form generated from the schema the plugin already serves.
P3.2 (home sections), P3.3 (group and user targeting), P3.4 (JSON import and
export) and P3.5 (embedded pages versus `jellyfin-plugin-pages`) are out of scope
and named at the end.

## The drift this closes

Every simple setting on the Application page is written by hand in
`Pages/Application/index.html`: an emby input carrying `data-key-name` and
`data-prop-name`, a matching entry read back in `Pages/Application/index.js`. The
schema at `GET streamyfin/config/schema` is consulted only to fill the four enum
dropdowns. So declaring a setting in `Settings.cs` is not enough for an admin to
reach it: the form has to be edited by hand as well, per key, in two files.

P1.7 took the plugin from 43 to 92 declared settings. None of the new ones has a
form control. The hand written page is where the parity work stops being visible.

| | |
|---|---|
| Settings the plugin declares | 92 of 95 |
| Simple settings with a hand written control | a partial subset |
| Settings a generated form reaches | every declared key, with no per key HTML |

## What already exists

P3.1 finishes a mechanism that is already half built, it does not start one.

- **`json-editor` 2.15.2 is vendored and never imported.**
  `Pages/Libraries/json-editor.min.js` is the real 535 KB library. Nothing in
  `Pages/` references it.
- **The served schema is already shaped for it.**
  `SerializationHelper.GetJsonSchema<Config>()` runs NJsonSchema with
  `HTMLFormTypeMappers()`, which writes json-editor's own `options` extension onto
  each primitive: `format: checkbox` on booleans, and `inputAttrs` and
  `containerAttrs` carrying the emby classes `emby-input`, `emby-checkbox-label`,
  `inputContainer`. The form therefore renders with the dashboard's own input
  styling out of the box, not with json-editor's bare theme. `MarkSecrets` then
  adds `x-secret: true` on the credential properties, on the property and never on
  the shared `LockableOfString` definition that several plain URLs also point at.
- **The runtime is in place.** `Pages/shared.js` already loads the schema, the
  config and the defaults, exposes `getConfig` / `setConfig`, and saves by dumping
  the config to YAML with the vendored js-yaml and posting it to
  `streamyfin/config/yaml`.
- **The config shape.** `Config` is `{ notifications, settings, other }`. This
  form owns `settings` only; notifications and other keep their own pages.

The gap is the consumer. No code turns that schema into a form.

## What the form renders

One json-editor instance, rooted at the `Settings` subtree of the served schema,
with `config.settings` as its start value.

- **Lockable settings render as themselves.** `Lockable<T>` is a `$ref` to
  `LockableOfString`, `LockableOfBoolean` and friends, an object of `{ value,
  locked }`. json-editor resolves the `$ref` and renders the pair: the value
  control the setting already had, plus a `locked` checkbox. That is exactly the
  admin semantic, a default the admin can also pin, with no bespoke widget.
- **Non lockable settings render as a plain control.**
- **Secrets render masked.** `x-secret` maps to `format: password`. The admin
  receives the raw config, not the redacted view (P1.4 redacts only for non
  admins), so the real value is present in the form and a save round trips it. No
  redaction-clobber to guard against on this surface.
- **Enums render as selects.** They serialize as their string names. The two that
  need more than that keep their present behaviour, expressed in the schema rather
  than in the page: `Bitrate` offers a `Max` choice that maps to `null`, and the
  underscores in a bitrate label are cleaned up. This moves the logic that lives
  in `index.js` `setOptions` today onto the schema, as `enum_titles` or a type
  mapper, so the page holds none of it.
- **String arrays** (library ids and the like) render with json-editor's array
  editor.
- **Layout is flat, in declaration order.** `propertyOrder`, sourced from the
  order `SettingsSchema` reports, drives it. Collapsible category sections like
  today's `<details>` are a later refinement and would need a category per
  setting; they are not in this slice.

Advanced json-editor options are set explicitly rather than left to defaults:
`disable_edit_json` and `disable_properties` on (an admin edits values, not the
JSON tree or the property set), `no_additional_properties` on (the schema is the
contract), `required_by_default` off. The escape hatch for raw editing stays the
existing YAML page.

## The schema the form needs, and the test that pins it

The page stays generic. Everything specific to a setting travels in the schema,
built in C# from `SettingsSchema.Descriptors`, the single list P1.1 already reads
by reflection. The post-processing that `MarkSecrets` began grows to also carry,
per property:

- `title` from the `[Display(Name)]` and `description` from its `Description`,
  **only where NJsonSchema does not already emit them**. Which of the two it emits
  from `DisplayAttribute` is verified first, and the pass fills in only the gap,
  so the rich descriptions already written on the settings (the Seerr key warning,
  for one) reach the form.
- `format: password` alongside the existing `x-secret`, so a generic page needs no
  secret list of its own.
- `propertyOrder` from declaration order.
- the enum titles for `Bitrate`.

All of it is sourced from `SettingsSchema`, so a new key stays one property and
one attribute, and nothing here is edited to add a setting. A new xUnit class
asserts the served schema, for representative keys, carries the title, the
description, the `x-secret` and `format`, and the order, and that no declared
simple setting is missing a control. This is the part that carries real coverage:
the page is JS that only a browser exercises, the schema it consumes is C# that a
test can hold to account. It is the same move as `ApiSurfaceTests` in P1.6, the
promise becomes a mechanism.

## The page

`Pages/Application/index.html` loses its hand written `<details>` blocks of
simple-setting inputs and becomes a mount point plus the existing save button.
`Pages/Application/index.js` loses `setOptions` and the per element wiring and
gains: build the `Settings` root schema from what `shared.getJsonSchema()`
returns, instantiate json-editor with that schema and `shared.getConfig().settings`
as `startval`, and on change write the editor's value back through
`shared.setConfig`. Save stays `shared.saveConfig()`, unchanged: the editor's JSON
is dumped to YAML and posted to `config/yaml`. No server write route is added.

The `Settings` subtree is taken from the served `Config` schema on the client, so
`config/schema` is not changed. If that proves awkward against json-editor's
`$ref` resolution, the fallback is a `?root=settings` on the existing endpoint,
but the client path is tried first and is expected to hold.

## How it is validated

- **xUnit**, for the schema shaping described above. Runs on jf11 and jf12 in CI
  like the rest.
- **A pass on the beta**, LXC 132, real Jellyfin 12.0.0. The form is JS and only a
  browser connected to the dashboard exercises it.

Done on 2026-08-31 and it passes. The schema the deployed plugin serves carries all
four shapes, 92 settings, 80 of them with a description. On the page: every setting
reaches the admin, both credentials render masked, the quality dropdown reads Max
then 250KB through 8MB, and an edit round trips, toggled in the form, dumped to
YAML, stored by the server, read back changed. A save with no edit sends the config
back unchanged rather than growing it.

## What the dashboard pass found, and the unit tests could not

Four things only a real dashboard surfaced. They are written down because each one
looks like a detail and is not.

- **A setting the config never carried did not render at all.** With
  `required_by_default` left false, json-editor draws only the keys present in the
  start value, and `disable_properties` removes the button that would add the rest.
  On this server that was 20 settings of 92: the other 72 were unreachable, which is
  the opposite of what this part is for. The form asks for every setting.
- **json-editor ignores a `format` and an `enum_titles` when the type is a list.** A
  credential typed `["null","string"]` renders as a plain text box with the key in
  clear, and a nullable enum renders a type selector. Both are single types now,
  `"string"`, and the blank they use for "no value" is turned back into null on save.
  A test that only asserts `format: password` passes while the field renders in
  clear, so the tests pin the type as well.
- **A save compared against the wrong baseline sent every setting.** The editor fills
  in a default for each key the config was missing, so comparing its output against
  what it was *seeded* with marked those keys as changed and wrote all 92. The
  comparison is against the editor's own first value.
- **`defaultAudioLanguage` and `defaultSubtitleLanguage` cannot round trip**, and
  that predates this part. See below.

## A setting that cannot be saved, found on the way

`LanguagePreference` declares `ThreeLetterISOLanguageName` and `DisplayName` in
PascalCase, alone among the settings types, because the app types both settings as
the SDK's `CultureDto` and matches on that name. The plugin's YAML reader uses the
camel case convention, so it rejects what its own schema describes:

```
Property 'ThreeLetterISOLanguageName' not found on type
'...Configuration.Settings.LanguagePreference'
```

So an administrator cannot set a default audio or subtitle language today. The hand
written page never offered the two, which is why it went unnoticed; the generated
form offers them, and a save carrying one fails. Nothing here works around it: a
save only sends what the admin touched, so the two stay out of the way until someone
edits them. Fixing it means making the reader accept the name the app needs, and it
wants its own change and its own test rather than being folded in here.

## Out of scope

- **P3.2** home section editor, hand written.
- **P3.3** group and user targeting screen, hand written.
- **P3.4** JSON import and export.
- **P3.5** the embedded-pages versus `jellyfin-plugin-pages` decision. This slice
  stays inside the embedded pages.
- **Grouped, collapsible category layout.** Flat first.
- **A json-editor theme beyond the emby classes already injected.** No new theme
  work.

## Why there is no "platforms" field on a setting

Not every setting applies everywhere, and the obvious next step after grouping was
to record which platforms each one reaches and show it in the form. It was
investigated and **deliberately dropped**. The reason is written here so nobody
spends another afternoon rediscovering it.

There are two different questions, and they give different answers:

1. **Where can a user change it?** Derivable, by following which of the app's
   settings pages exposes the key. It gives a clean answer for 84 of the 95 keys.
2. **Where does it take effect?** What an administrator actually needs, since they
   are deciding what to impose on a fleet.

They do not agree, and the first one is the misleading one:

| Setting | Offered on | Read by |
|---|---|---|
| `hiddenLibraries` | mobile pages only | `Home.tv.tsx`, `TVLibraries.tsx` |
| `defaultBitrate` | mobile pages only | `ItemContent.tv.tsx` |
| `streamyStatsMovieRecommendations` | mobile pages only | `Home.tv.tsx` |

A badge reading "mobile only" on any of those would be a lie an administrator has
no way to check, which is worse than saying nothing at all.

The second question is not derivable either. Most settings are read by shared
player and provider files, `buildNativePlayerConfig.ts`, `useVideoNavigation.ts`,
`InactivityProvider.tsx`, whose platform is a runtime property rather than
something the source states. And the derivation that looked cleanest still
produced a wrong answer: it called `preferedLanguage` TV only, when it is the
app's language setting and `_layout.tsx` and `AppLanguageSelector.tsx` read it.

**What is done instead**: the settings that are verifiably platform specific say
so in their own description, in prose, one at a time. Only a handful qualify, and
each was checked rather than derived:

- `showHomeBackdrop`, `showSeriesPosterOnEpisode` and `tvThemeMusicEnabled`, where
  nothing outside a `.tv.*` file or `useTVThemeMusic.ts` reads them.
- `videoPlayer` and the two native player toggles, which #138 already describes,
  including that `Native` is a phone and tablet value and that a TV chooses
  through the two toggles instead.

If a future setting is genuinely platform specific, the answer is a sentence in
its `[Display]` description, not a field that claims to know for all 95.

## Delivery

Branch `refonte/p3-1-schema-form`, one pull request onto `develop`, squash, body
in the `Part of #114. Covers P3.1.` form the sisters use. The tracking pull
request is #121.
