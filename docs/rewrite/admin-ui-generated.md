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
- **A manual pass on the beta**, LXC 132, real Jellyfin 12. The generated form is
  JS and only a browser connected to the dashboard exercises it. Build jf12,
  deploy to the beta with `autoUpdate` false as usual, open the plugin config
  page, and confirm: every declared simple setting has a control, a lock checkbox
  sits beside each lockable value, the two credential fields are masked, an enum
  and a string array render, and a save round trips through YAML with the values
  intact. The known unauthenticated `config/schema` is untouched here.

## Out of scope

- **P3.2** home section editor, hand written.
- **P3.3** group and user targeting screen, hand written.
- **P3.4** JSON import and export.
- **P3.5** the embedded-pages versus `jellyfin-plugin-pages` decision. This slice
  stays inside the embedded pages.
- **Grouped, collapsible category layout.** Flat first.
- **A json-editor theme beyond the emby classes already injected.** No new theme
  work.

## Delivery

Branch `refonte/p3-1-schema-form`, one pull request onto `develop`, squash, body
in the `Part of #114. Covers P3.1.` form the sisters use. The tracking pull
request is #121.
