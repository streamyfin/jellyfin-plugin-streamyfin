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
given under the type rules below. These are the first study's numbers, on the 52
settings the app had then; the manifest records 95 now, and the keys taken out since are
under "Six keys left again" below.

`playbackSpeedPerMedia` and `playbackSpeedPerShow` stay out. They are not
settings. They are `Record<string, number>` maps the player writes by itself,
keyed by item and by series id, so there is nothing an administrator could
usefully put in them. Declaring them would also put a field nobody can fill into
the generated admin forms of P3.

Seven were weighed rather than waved through, and are declared with the caveat written
next to them in `examples/full.yml`:

- **The five mpv keys** (`mpvCacheEnabled`, `mpvCacheSeconds`, `mpvDemuxerMaxBytes`,
  `mpvDemuxerMaxBackBytes`, `mpvVoDriver`) describe what a device can do, not what a
  user prefers. An administrator running a homogeneous
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
plugin proposes nothing. `preferedLanguage` and `openSubtitlesApiKey` take the same
treatment for the simpler reason that the app has no default for them either.

**The type is the app's type, and it has to survive the round trip.** Most of the
50 are booleans, numbers and strings, and land on `Lockable<bool>`,
`Lockable<int>` and `Lockable<string>` unchanged. Two shapes need care:

- **Enumerations.** `audioTranscodeMode`, `mpvCacheEnabled`, `mpvVoDriver`,
  `tvTypographyScale`, `subtitleAlignX` and `subtitleAlignY` each need a C# enum
  whose member names are the strings the app compares against. `inactivityTimeout`
  is an enum too, but the app compares it as a number, so it joins `OrientationLock`
  and `Bitrate` in the number converters `SerializationHelper` registers. `SubtitlePlaybackMode` keeps
  one for storage only: the regenerated manifest showed the app compares the SDK's
  strings for it (below), so what the app receives carries the member name, while
  what the plugin stores keeps the number every earlier build reads.
  `Configuration/Settings/Enums.cs` holds both patterns. Anything new is written as
  its member name unless it is added to that list.
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

Two keys are plain arrays, `hiddenHomeHeroSections` and `hiddenHomeHeroMediaTypes`,
and follow `Home.sections`, which is already an array property.

**Six keys left again.** `deviceProfile`, `mediaListCollectionIds`, `usePopularPlugin`
and `showHomeTitles` were declared from the app's `Settings` type, but the app reads none
of them: nothing has read `deviceProfile` since the app built its device profile from the
active player, the other three lost their last reader with the large carousel, or never
had one. `videoPlayer` picks the engine and the controls for every platform at once, so a
value an administrator set for Android TV, ExoPlayer for HDR, also moved every iPhone
off the native controls. `autoPlayEpisodeCount` is no setting: it is the counter the player
keeps against the maximum, and a locked value would stop auto play for good or never. The
six are in `NotDeclared` with the reasons, and the two native player switches, which each
name one platform, stay. None of the six was in 0.68.1, so only an unstable build ever
stored one; a stored configuration that holds one still loads, without it.

## The manifest, and the test that reads it

`Jellyfin.Plugin.Streamyfin/AppSettingsManifest.json` lists what the app
reads: every key, its type, and its default, with an explicit marker for the keys
that have none. It is generated from the app's `utils/atoms/settings.ts`,
committed, and embedded in the test assembly rather than copied to the output
directory, so reading it does not depend on which directory `dotnet test` was
invoked from. It is the same device as `ApiSurfaceTests._legacyRoutes`, where a
checked-in list turns a promise into something a build can fail on, and editing
the list is the deliberate act.

`scripts/app-settings-manifest.js` writes it, from a checkout of the app with its
`node_modules`:

```sh
bun scripts/app-settings-manifest.js ../streamyfin
```

The script that wrote the first manifest was not kept, and regenerating that
manifest from the app of the same day showed what it had guessed. It recorded
`downloadQuality` as having no default because it could not read
`DownloadOptions[0]`, and `subtitleMode` as `0` where the app holds the SDK's
string `"Default"`. The new one reads `settings.ts` with the TypeScript compiler.
It follows the file's own constants and enums, reads a value from another module of
the app in that module's source, loads one from a package out of the checkout, and
runs an expression built only from literals, such as the sorted `BITRATES`, in a
context holding nothing else. Anything else stops it rather than become a guess. The
one value it cannot work out, the default orientation, which the app's wrapper picks
by platform, is written down by hand and checked against both of its sources on every
run. The three keys `normalizePluginValue` reshapes keep their wire form, and each
run sends that form through the app's own `normalizePluginValue` and stops unless it
comes back as the app's default.

The second guess hid a bug. The plugin wrote `SubtitlePlaybackMode` as a number and
the app compares the SDK's strings. The unlocked default reached every user of the
server once, as `0`, which replaced the mode they had and left the settings screen
with none selected, and a locked mode did not take effect.

The plugin sends the member name to the app now, and the parity test compares the
default like any other. It keeps storing the number: an earlier build reads the stored
mode with a number converter only and throws on a name, so a rollback would have served
an empty configuration to everyone. A device that recorded `0` sees `"Default"` as a new default and takes it
once, which also replaces a mode its user picked since. An app migration rewriting the
stored `0` and its record would have spared that, and was weighed and left out: it is
one change of a setting most people never touched, against a release of both sides.

**A setting the app reads under more than one name.** The app renamed the three
Seerr settings to `seerrServerUrl`, `seerrApiKey` and `autoLoginSeerr`. It reads
the `seerr` block first and the jellyseerr keys after it, which every earlier copy
of the app reads and the plugin still sends. The manifest lists those other names
as `wireNames`, and the script finds them by running the app's own
`readIntegrationBlocks` rather than by copying it. A setting counts as declared
under any of its names, and its default is compared through the name the plugin
declares it under, flat or in a block. A block the plugin serves must carry every
field the app reads from it, filled in what the plugin writes out, and nothing the
app does not read. The excuse lists stay keyed by the app's own names, and one
written under another name is reported as stale.

The day the app stops reading the jellyseerr keys, they leave the manifest and a
plugin still declaring them fails the third rule below. That is not the day they can
go: copies of the app from before the block read nothing else. The answer then is an
entry in `KeptForAppsInTheField`, which dies once the plugin stops declaring the key
or the app reads it again.

**A setting the app picks from a list of its own.** The app language is a string the
app's pickers choose from `APP_LANGUAGES` in `i18n.ts`, and the Application tab offers
the same list rather than free text, so the plugin keeps a copy of it, `AppLanguages`.
The manifest records the app's list as the setting's `options`, read from `i18n.ts` the
way a default is, and the run stops if either picker no longer imports the list or no
longer writes the setting. It is sorted by value, because the app sorts by label in each
device's own collation, which is no one order. Two tests read it beside the rules below:
the plugin offers the app's list value for value and label for label, and offers no list
the manifest does not record.

The hero carousel's two filters, `hiddenHomeHeroSections` and `hiddenHomeHeroMediaTypes`,
come the same way from another kind of list: a record in `HomeHeroCarousel.tsx`
(`SECTION_LABEL_KEYS`, `MEDIA_LABEL_KEYS`) from each value to the translation key of its
label, which the generator reads with `translated` and labels with the app's English from
`translations/en.json`. A run stops when that file has no string for a key, or when the
carousel no longer writes the setting. The plugin's copies are `HomeHeroSections` and
`HomeHeroMediaTypes`, and the schema the Yaml tab reads names their values, which the
form shows as boxes under the labels.

Three rules read it. The rest of the tests in the file refuse an excuse that has
outlived either the setting it names or the reason it was written for, from
whichever side moved: the app dropping a key, the app catching up, or the plugin
declaring the setting an entry said it would not.

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
