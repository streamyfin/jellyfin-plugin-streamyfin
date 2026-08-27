# Settings parity

The plugin exists so an administrator can decide, propose or impose what the app
does. It can only do that for a setting it declares: an undeclared key resolves
`locked` to `undefined` in the app, so the lock never fires, and no value is ever
pushed. That is the finding behind [#109](https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/109),
recorded in [pull-request-triage.md](pull-request-triage.md).

Measured on `develop` and on the app's `develop`, 2026-08-27:

| | |
|---|---|
| Settings the app reads | 95 |
| Settings the plugin declares | 43 |
| In common | 43 |
| **In the app, undeclared** | **52** |

These come from `AppSettingsManifest.json`, which is generated from the app's
`utils/atoms/settings.ts` rather than counted by hand. Two hand counts before it
were both wrong, in opposite directions.

So more than half of what the app offers is outside an administrator's reach:
every subtitle appearance control, the player gestures, the mpv tuning, the TV
options, the choice of video player. This document is the decision about those 52
and the mechanism that stops the gap reopening.

Two of the 52 are declared in `Settings.cs` but commented out, with a note saying
`CultureDto` has no parameterless constructor so the schema generator fails on it.
`defaultAudioLanguage` and `defaultSubtitleLanguage` therefore count as
undeclared, because a commented property is not one.

It is part P1 work finishing rather than a new part: P1.1 built `SettingsSchema`
to read `Settings.cs` by reflection, and P1.3 resolves every key that schema
reports. Neither holds a list of its own. Declaring a property is therefore the
whole of the work: targeting, locking, redaction and the generated forms of P3
all pick it up with no further change.

## What gets declared

**50 of the 52 are decided as declarable.** Two are not. One of the 50,
`downloadQuality`, needs a matching app change before it can land, for the reason
given under the type rules below.

`playbackSpeedPerMedia` and `playbackSpeedPerShow` stay out. They are not
settings. They are `Record<string, number>` maps the player writes by itself,
keyed by item and by series id, so there is nothing an administrator could
usefully put in them. Declaring them would also put a field nobody can fill into
the generated admin forms of P3.

Six of the 50 were weighed rather than waved through, and are declared with the
caveat written next to them in `examples/full.yml`:

- **The five mpv keys** (`mpvCacheEnabled`, `mpvCacheSeconds`, `mpvDemuxerMaxBytes`,
  `mpvDemuxerMaxBackBytes`, `mpvVoDriver`) and **`deviceProfile`** describe what a
  device can do, not what a user prefers. An administrator running a homogeneous
  fleet has a real reason to fix them; one who locks a value chosen for a phone
  also applies it to a Shield. Declared, with the warning stated.
- **`sentryEnabled`** lets an administrator turn crash reporting off for everyone,
  which is legitimate. The same key lets them turn it on, which is a consent
  taken on someone's behalf. Declared, and the warning says so.
- **`openSubtitlesApiKey`** follows `jellyseerrApiKey`, which is already declared
  and already carries `[Secret]`. An administrator can supply one key for
  everybody. In exchange, a key the user paid for is a value the administrator
  can overwrite.

## The rules a declaration follows

**The name is the app's key, character for character.** This is the whole of the
mistake in #109: the plugin declared `autoSubtitlesOnMute` while the app read
`subtitlesOnMute`, so two keys nothing reads shipped, and the lock they existed
to enable still did nothing.

**The default matches the app's, or there is no default.** An unlocked plugin
value is applied exactly once as a default, through `pendingPluginDefaults` and
the `PLUGIN_APPLIED_DEFAULTS` registry. A default that disagrees with the app
therefore does not sit there harmlessly: it silently flips the setting for every
user who has not already chosen one. Across 50 keys at once, care is not a
mechanism, which is why the test below exists.

Writing the manifest found five keys where the plugin disagreed with the app, and
they were not theoretical: `PluginConfiguration()` fills its config from
`DefaultSettings()` on first start, `GET config` serves it, and the app applies an
unlocked value once. Installing the plugin and configuring nothing is enough.

| Key | Plugin | App | What it does to a user who never chose |
|---|---|---|---|
| `rememberAudioSelections` | `false` | `true` | turns off remembering the audio track |
| `rememberSubtitleSelections` | `false` | `true` | turns off remembering the subtitle track |
| `rewindSkipTime` | `15` | `10` | rewinds 15 seconds instead of 10 |
| `subtitleSize` | `80`, normalised to `0.8` | `1.0` | subtitles at 80 per cent of the intended size |

Those four predate the rewrite and are corrected to the app's values here.
Nothing changes for an existing device: `PLUGIN_APPLIED_DEFAULTS` has already
recorded the old value, so only a device that has never seen one gets the new.

The fifth was `subtitlesOnMute`. The plugin's `true` was never a mistake, it
matched the app branch of [#1900](https://github.com/streamyfin/streamyfin/pull/1900),
which #109 was deliberately aligned with, while the app's published default was
still `false`. That branch merged on 2026-08-27, so the app defaults to `true`
too and the written exception the test carried for it is gone.

**A setting whose app default varies by platform is declared without a default.**
Two of them do:

```ts
mpvDemuxerMaxBytes:     Platform.isTV && Platform.OS === "android" ? 75 : 150
mpvDemuxerMaxBackBytes: Platform.isTV && Platform.OS === "android" ? 30 : 50
```

There is no single value to declare. Putting either number in `DefaultConfig()`
would push it to every device and flatten the distinction the app makes on
purpose, so Android TV would inherit a phone's memory budget. The property is
declared, so an administrator can still set and lock it deliberately, and the
plugin proposes nothing. `videoPlayer`, `preferedLanguage` and
`openSubtitlesApiKey` take the same treatment for the simpler reason that the app
has no default for them either.

**The type is the app's type, and it has to survive the round trip.** Most of the
50 are booleans, numbers and strings, and land on `Lockable<bool>`,
`Lockable<int>` and `Lockable<string>` unchanged. Two shapes need care:

- **Enumerations.** `audioTranscodeMode`, `mpvCacheEnabled`, `mpvVoDriver`,
  `tvTypographyScale`, `deviceProfile`, `subtitleAlignX` and `subtitleAlignY`
  each need a C# enum whose member names are the strings the app compares
  against. `inactivityTimeout` and `videoPlayer` are enums too, but the app
  compares them as numbers, so they join `OrientationLock`, `Bitrate` and
  `SubtitlePlaybackMode` in the number converters `SerializationHelper`
  registers, five in all. `Configuration/Settings/Enums.cs` holds both patterns.
  Anything new is written as its member name unless it is added to that list.
- **`downloadQuality` is the one that does not fit.** The app types it as
  `DownloadOption`, which is `{ label, value }`. The generic fallback in
  `normalizePluginValue` only rebuilds `{ key, value }` objects, so a value
  declared as-is arrives in a shape the app cannot read. Either the plugin
  declares the scalar `DownloadQuality` and the app gains a normalizer case, or
  the app's `DownloadOption` gains a `key`. That is an app-side change, so it is
  the one key in this part that cannot land alone.

- **The two language keys need a type of their own.** `defaultAudioLanguage` and
  `defaultSubtitleLanguage` are `CultureDto | null` in the app, and the existing
  commented-out declaration says why they were left alone: Jellyfin's `CultureDto`
  has no parameterless constructor, so the schema generator fails on it. The app
  reads exactly two of its fields, `ThreeLetterISOLanguageName` and `DisplayName`,
  so the plugin declares its own small type carrying those two rather than
  borrowing Jellyfin's.

Three keys are plain arrays, `hiddenHomeHeroSections`, `hiddenHomeHeroMediaTypes`
and `mediaListCollectionIds`, and follow `Home.sections`, which is already an
array property.

## The manifest, and the test that reads it

`Jellyfin.Plugin.Streamyfin.Tests/AppSettingsManifest.json` lists what the app
reads: every key, its type, and its default, with an explicit marker for the keys
that have none. It is generated from the app's `utils/atoms/settings.ts`,
committed, and embedded in the test assembly rather than copied to the output
directory, so reading it does not depend on which directory `dotnet test` was
invoked from. It is the same device as `ApiSurfaceTests._legacyRoutes`, where a
checked-in list turns a promise into something a build can fail on, and editing
the list is the deliberate act.

Three rules read it, and two further tests refuse an excuse that has outlived
either the setting it names or the reason it was written for.

1. **Every key in the manifest is either declared in `Settings.cs` or named in an
   explicit `NotDeclared` set with its reason.** Silence is not an option: a key
   nobody decided about fails the build.
2. **Every declared key's default equals the manifest's**, or the manifest marks
   it as having none. This is the rule that 47 keys at once cannot be trusted to
   follow by hand.
3. **Every key declared in `Settings.cs` appears in the manifest.** This is the
   one that catches #109. A property named for a key the app does not read fails
   the day it is written, instead of shipping and doing nothing.

### What it does not catch

The test compares the plugin against the manifest. It cannot see the app. A key
added to the app's `settingsAtom` is invisible here until the manifest is
regenerated, so this closes the gap and does not keep it closed by itself.

Closing it properly needs the manifest generated on the app's side and published,
which is app work and belongs to its own part. Until then, regenerating the
manifest is a step in reviewing any app pull request that touches
`utils/atoms/settings.ts`, and the CodeRabbit instruction below is what makes
that visible rather than remembered.

### The review instruction

`.coderabbit.yaml` already configures the base branches this repository reviews.
It gains a path instruction on `Configuration/Settings/Settings.cs` restating the
three rules above, so a pull request that adds a property gets told about the
manifest and the default at review time, before the build runs. The test is what
decides; the instruction is what explains.

## Delivery

One pull request. The 47 declarations are mechanical once the manifest and the
test are in place, and splitting them by family would mean a half-populated
manifest in every intermediate commit, which is the state the test exists to
forbid.

`downloadQuality` is the exception called out above. Its app-side half has to
land first, so it stays in `NotDeclared` with that as its written reason, and
moves across in the pull request that carries the app change. The manifest says
what is true rather than pretending, which is the whole point of the second
assertion.
